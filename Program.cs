using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using BookNotifier.Integrations.GoodReads;
using BookNotifier.Integrations.Literotica;
using BookNotifier.Integrations.ScribbleHub;
using BookNotifier.Services;

namespace BookNotifier
{
	internal class Program
	{
		private static Task JitterAsync(int baseMs, int minJitterMs = -500, int maxJitterMs = 500) => Task.Delay(Math.Max(0, baseMs + Random.Shared.Next(minJitterMs, maxJitterMs)));
		public static async Task Main(string[] args)
		{
			CultureInfo ci = new("en-CA");
			Thread.CurrentThread.CurrentCulture = ci;
			Thread.CurrentThread.CurrentUICulture = ci;

			AppDomain.CurrentDomain.UnhandledException += (_, f) => LogError(f.ExceptionObject.ToString() ?? "Unhandled exception");
			TaskScheduler.UnobservedTaskException += (_, ef) => LogError(ef.Exception.Message);

			_ = new EnvService();

			Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data"));

			string[] notifiers = (Environment.GetEnvironmentVariable("NOTIFIER")
				?? throw new InvalidOperationException("Missing NOTIFIER environment variable."))
				.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Select(x => x.ToLowerInvariant())
				.Distinct()
				.ToArray();

			if (notifiers.Length == 0)
				throw new InvalidOperationException("NOTIFIER is empty. Expected one or more of: goodreads, scribblehub, literotica.");

			Log($"Starting notifiers: {string.Join(", ", notifiers)}");

			IEnumerable<Task> notifierTasks = notifiers.Select(notifier => notifier switch
			{
				"goodreads" => RunLoopAsync("goodreads", GetRecheckMs("GOODREADS"), RunGoodReadsAsync),
				"scribblehub" => RunLoopAsync("scribblehub", GetRecheckMs("SCRIBBLEHUB"), RunScribbleHubAsync),
				"literotica" => RunLoopAsync("literotica", GetRecheckMs("LITEROTICA"), RunLiteroticaAsync),
				_ => throw new InvalidOperationException(
					$"Unknown notifier '{notifier}'. Expected one or more of: goodreads, scribblehub, literotica.")
			});

			await Task.WhenAll(notifierTasks);
		}

		[SuppressMessage("ReSharper", "FunctionNeverReturns")]
		private static async Task RunLoopAsync(string name, long recheckMs, Func<Task> action)
		{
			while (true)
			{
				try
				{
					await action();
					Log($"[{name}] Check complete.");
				}
				catch (Exception ex)
				{
					LogError($"[{name}] Error: {ex.Message}");
				}

				Log($"[{name}] Waiting {recheckMs}ms...");
				await Task.Delay((int)recheckMs);
			}
		}

		private static long GetRecheckMs(string prefix)
		{
			string key = $"{prefix}_RECHECK_MS";
			string raw = (Environment.GetEnvironmentVariable(key)
				?? throw new InvalidOperationException($"Missing {key} environment variable."))
				.Replace("_", "")
				.Replace(" ", "");

			return !long.TryParse(raw, out long ms)
				? throw new InvalidOperationException($"Failed to parse {key}.")
				: ms;
		}

		private static async Task RunGoodReadsAsync()
		{
			using GoodReadsClient sdk = new();

			IReadOnlyList<GoodReadsBookDetails> readingListData =
				await sdk.GetReadingListBooksAsync(
					Environment.GetEnvironmentVariable("USER_ID")
						?? throw new InvalidOperationException("Missing USER_ID environment variable."),
					Environment.GetEnvironmentVariable("SHELF_TAG")
						?? throw new InvalidOperationException("Missing SHELF_TAG environment variable.")
				);

			Dictionary<string, List<GoodReadsBook>> authorBooks = [];

			foreach (GoodReadsAuthor author in readingListData.Select(x => x.Author).DistinctBy(x => x.Id))
			{
				List<GoodReadsBook> books = await sdk.GetAuthorsBooks(author.Url);
				authorBooks[author.Name] = books;
			}

			await sdk.RunAsync(readingListData, authorBooks);
		}

		private static async Task RunScribbleHubAsync()
		{
			using ScribbleClient api = new();

			try
			{
				await api.Login(
					Environment.GetEnvironmentVariable("USERNAME")
						?? throw new InvalidOperationException("Missing USERNAME environment variable."),
					Environment.GetEnvironmentVariable("PASSWORD")
						?? throw new InvalidOperationException("Missing PASSWORD environment variable.")
				);
			}
			catch
			{
				api.SetCookies(
					Environment.GetEnvironmentVariable("PRESET_COOKIE")
						?? throw new InvalidOperationException("Login failed and PRESET_COOKIE is missing.")
				);
			}

			await JitterAsync(1500);

			List<ScribbleSaveBookRoot> currentBooks = await FileStoreService.LoadScribbleHubAsync();
			List<ScribbleReadingListStory> readingData = await api.GetReadingList();
			await FileStoreService.SaveScribbleHubAsync(readingData);

			foreach (ScribbleReadingListStory story in readingData)
			{
				ScribbleSaveBookRoot? cachedStory = currentBooks.FirstOrDefault(x => x.Id == story.Id);

				if (cachedStory is null)
				{
					Log($"[scribblehub] New story: {story.Name} -> {story.Chapters.Count} chapters");
					await NotificationService.SendNewScribbleStoryAsync(story.Name, story.Link);
					continue;
				}

				ScribbleChapter latestCurrentChapter = story.Chapters[^1];
				ScribbleSaveChapter latestCachedChapter = cachedStory.Chapters[^1];

				if (latestCurrentChapter.Id == latestCachedChapter.Id) continue;

				Log($"[scribblehub] New chapter: {story.Name} -> {latestCurrentChapter.Title}");
				await NotificationService.SendNewScribbleChapterAsync(story.Name, story.Link, latestCurrentChapter.Title, latestCurrentChapter.Link);
			}
		}

		private static Task RunLiteroticaAsync()
		{
			string username = Environment.GetEnvironmentVariable("LIT_USERNAME")
			                  ?? throw new InvalidOperationException("Missing LIT_USERNAME environment variable.");

			string password = Environment.GetEnvironmentVariable("LIT_PASSWORD")
			                  ?? throw new InvalidOperationException("Missing LIT_PASSWORD environment variable.");

			return new LiteroticaClient(username, password).RunAsync();
		}

	}

	internal class EnvService
	{
		public IReadOnlyDictionary<string, string> Variables { get; private set; }

		internal EnvService()
		{
			Dictionary<string, string> vars = [];
			string envPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");

			if (!File.Exists(envPath)) { Variables = vars; return; }

			foreach (string line in File.ReadAllLines(envPath))
			{
				if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;

				string[] parts = line.Split('=', 2);
				if (parts.Length != 2) continue;

				string key = parts[0].Trim();
				if (string.IsNullOrEmpty(key)) continue;

				string value = parts[1].Trim().Trim('"').Trim('\'');
				vars[key] = value;

				if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
					Environment.SetEnvironmentVariable(key, value);
			}

			Variables = vars;
		}
	}
}
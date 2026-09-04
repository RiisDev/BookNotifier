using BookNotifier.Integrations.Ao3;
using BookNotifier.Integrations.GoodReads;
using BookNotifier.Integrations.Literotica;
using BookNotifier.Integrations.RoyalRoad;
using BookNotifier.Integrations.ScribbleHub;
using BookNotifier.Services;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace BookNotifier
{
	internal class Program
	{
		// Declare first or else flareclient will not register the env
		private static readonly EnvService Env = new();

		public static FlareSolverClient FlareClient = new();
		private static bool RunOnce { get; set; }
		public static bool IgnorePost { get; set; }

		public static async Task Main(string[] args)
		{
			CultureInfo ci = new("en-CA");
			Thread.CurrentThread.CurrentCulture = ci;
			Thread.CurrentThread.CurrentUICulture = ci;

			AppDomain.CurrentDomain.UnhandledException += (_, f) => LogError(f.ExceptionObject.ToString() ?? "Unhandled exception");
			TaskScheduler.UnobservedTaskException += (_, ef) => LogError(ef.Exception.Message);

			RunOnce = args.Contains("--runonce", StringComparer.OrdinalIgnoreCase);
			IgnorePost = args.Contains("--ignore-post", StringComparer.OrdinalIgnoreCase);

			Log($"Running Once: {RunOnce}");
			Log($"Ignoring Discord Post: {IgnorePost}");
			Log($"Env Vars Found: {Env.Variables.Count}");
			
			Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data"));

			string[] notifiers = (Environment.GetEnvironmentVariable("NOTIFIER")
				?? throw new InvalidOperationException("Missing NOTIFIER environment variable."))
				.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Select(x => x.ToLowerInvariant())
				.Distinct()
				.ToArray();

			if (notifiers.Length == 0)
				throw new InvalidOperationException("NOTIFIER is empty. Expected one or more of: goodreads, scribblehub, literotica, royalroad, ao3.");

			Log($"Starting notifiers: {string.Join(", ", notifiers)}");

			IEnumerable<Task> notifierTasks = notifiers.Select(notifier => notifier switch
			{
				"goodreads" => RunLoopAsync("goodreads", GetRecheckMs("GOODREADS"), RunGoodReadsAsync),
				"scribblehub" => RunLoopAsync("scribblehub", GetRecheckMs("SCRIBBLEHUB"), RunScribbleHubAsync),
				"literotica" => RunLoopAsync("literotica", GetRecheckMs("LITEROTICA"), RunLiteroticaAsync),
				"royalroad" => RunLoopAsync("royalroad", GetRecheckMs("ROYALROAD"), RunRoyalRoadAsync),
				"ao3" => RunLoopAsync("ao3", GetRecheckMs("AO3"), RunAo3Async),
				_ => throw new InvalidOperationException(
					$"Unknown notifier '{notifier}'. Expected one or more of: goodreads, scribblehub, literotica, royalroad, ao3.")
			});

			await Task.WhenAll(notifierTasks);

			if (RunOnce) Environment.Exit(0);
		}

		[SuppressMessage("ReSharper", "FunctionNeverReturns")]
		private static async Task RunLoopAsync(string name, long recheckMs, Func<Task> action)
		{
			while (true)
			{
				try
				{
					Log($"[{name}] Running...");
					await action();
					Log($"[{name}] Check complete.");
				}
				catch (Exception ex)
				{
					LogError($"[{name}] Error: {ex.Message}");
				}

				if (RunOnce) break;

				Log($"[{name}] Waiting {recheckMs}ms...");
				await Task.Delay(TimeSpan.FromMilliseconds(recheckMs));
			}
		}

		private static long GetRecheckMs(string prefix)
		{
			if (RunOnce) return long.MaxValue;

			string key = $"{prefix}_RECHECK_MS";
			string raw = (Environment.GetEnvironmentVariable(key)
				?? throw new InvalidOperationException($"Missing {key} environment variable."))
				.Replace("_", "")
				.Replace(" ", "");

			return !long.TryParse(raw, out long ms)
				? throw new InvalidOperationException($"Failed to parse {key}.")
				: ms;
		}

		private static async Task RunAo3Async()
		{
			string username = Environment.GetEnvironmentVariable("AO3_USERNAME") ?? throw new InvalidOperationException("Missing AO3_USERNAME environment variable");
			string pseudoname = Environment.GetEnvironmentVariable("AO3_PSEUDO") ?? username;

			Ao3Client ao3Client = new(username, pseudoname);

			await ao3Client.RunCheck();
		}

		private static async Task RunGoodReadsAsync()
		{
			using GoodReadsClient sdk = new();

			IReadOnlyList<GoodReadsBookDetails> readingListData =
				await sdk.GetReadingListBooksAsync(
					Environment.GetEnvironmentVariable("GOODREADS_USER_ID")
						?? throw new InvalidOperationException("Missing GOODREADS_USER_ID environment variable."),
					Environment.GetEnvironmentVariable("GOODREADS_SHELF_TAG")
						?? throw new InvalidOperationException("Missing GOODREADS_SHELF_TAG environment variable.")
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
			string userId = Environment.GetEnvironmentVariable("SCRIBBLEHUB_USERID") ?? throw new InvalidOperationException("Missing required USERID environment variable");
			
			ScribbleClient api = new(userId);

			await api.RunCheck();
		}

		private static Task RunLiteroticaAsync()
		{
			string username = Environment.GetEnvironmentVariable("LITEROTICA_USERNAME") ?? throw new InvalidOperationException("Missing LITEROTICA_USERNAME environment variable.");
			string password = Environment.GetEnvironmentVariable("LITEROTICA_PASSWORD") ?? throw new InvalidOperationException("Missing LITEROTICA_PASSWORD environment variable.");

			return new LiteroticaClient(username, password).RunAsync();
		}

		private static async Task RunRoyalRoadAsync()
		{
			string userId = Environment.GetEnvironmentVariable("ROYALROAD_USERID") ?? throw new InvalidOperationException("Missing ROYALROAD_USERID environment variable.");
			if (!int.TryParse(userId, out int userIdOut)) throw new InvalidOperationException($"Failed to parse {userId} as int");
			using RoyalRoadClient client = new(userIdOut);
			await client.RunAsync();
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
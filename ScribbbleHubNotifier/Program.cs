using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScribbleHubNotifier.Classes;

namespace ScribbleHubNotifier
{
	internal class Program
	{
		public record Chapter(
			[property: JsonPropertyName("Title")] string Title,
			[property: JsonPropertyName("Link")] string Link,
			[property: JsonPropertyName("Id")] string Id
		);

		public record BookRoot(
			[property: JsonPropertyName("Name")] string Name,
			[property: JsonPropertyName("Link")] string Link,
			[property: JsonPropertyName("Id")] string Id,
			[property: JsonPropertyName("Chapters")] IReadOnlyList<Chapter> Chapters
		);


		static async Task Main(string[] args)
		{
			Log("Hello, World!");

			CultureInfo ci = new ("en-CA");
			Thread.CurrentThread.CurrentCulture = ci;
			Thread.CurrentThread.CurrentUICulture = ci;

			AppDomain.CurrentDomain.UnhandledException += (_, f) => LogError(f.ExceptionObject.ToString() ?? "");
			TaskScheduler.UnobservedTaskException += (_, ef) => LogError(ef.Exception.Message);

			try
			{
				ScribbleApi api = new(); 
				_ = new EnvService();

				try
				{
					await api.Login(
						Environment.GetEnvironmentVariable("USERNAME") ?? throw new InvalidOperationException("Missing username variable"),
						Environment.GetEnvironmentVariable("PASSWORD") ?? throw new InvalidOperationException("Missing password variable")
					);
				}
				catch (Exception) { api.SetCookies(Environment.GetEnvironmentVariable("PRESET_COOKIE") ?? throw new InvalidOperationException("Missing preset cookie, cannot continue")); }

#if DEBUG
				_ = await api.GetReadingList();
#endif

				Directory.CreateDirectory("data");

				if (!File.Exists("data/books.json"))
					File.Create("data/books.json").Close();

				try
				{
					Log("Running check...");

					List<BookRoot> currentBooks;

					try { currentBooks = JsonSerializer.Deserialize<List<BookRoot>>(await File.ReadAllTextAsync("data/books.json")) ?? []; }
					catch { currentBooks = []; }

					List<ReadingListStory> readingData = await api.GetReadingList();
					await File.WriteAllTextAsync("data/books.json", JsonSerializer.Serialize(readingData, JsonOptions));

					foreach (ReadingListStory story in readingData)
					{
						BookRoot? cachedStory = currentBooks.FirstOrDefault(x => x.Id == story.Id);
						if (cachedStory is null)
						{
							Log($"New book detected {story.Name} -> {story.Chapters.Count} total chapters");
							await Discord.SendDiscordWebhookAsync(story, true);
							continue;
						}

						Chapter latestCachedChapter = cachedStory.Chapters[^1];
						Classes.Chapter latestCurrentChapter = story.Chapters[^1];

						if (latestCurrentChapter.Id == latestCachedChapter.Id) continue;

						Log($"New chapter of {story.Name} -> {latestCurrentChapter.Title} -> {story.Chapters.Count} total chapters");
						await Discord.SendDiscordWebhookAsync(story);
					}
				}
				catch (Exception ex)
				{
					LogError(ex.Message, ex.StackTrace!, ex.Source!);
				}
			}
			catch (Exception ex)
			{
				LogError(ex.Message, ex.Source!, ex.StackTrace);
			}
		}
		
	}

	internal class EnvService
	{
		public IReadOnlyDictionary<string, string> Variables { get; private set; }

		internal EnvService()
		{
			Variables = new Dictionary<string, string>();
			Dictionary<string, string> vars = [];
			string envPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");
			if (!File.Exists(envPath))
			{
				Variables = vars;
				return;
			}

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

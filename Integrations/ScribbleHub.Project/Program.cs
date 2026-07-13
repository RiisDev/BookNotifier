using BookNotifier.Integrations;
using BookNotifier.Services;

namespace ScribbleHub.Project
{
	internal class Program
	{
		public static readonly FlareSolverClient FlareClient = new();
		public static readonly string SessionId = $"booknot-scribblehub-{Guid.NewGuid():N}";

		static async Task Main(string[] __)
		{
#if DEBUG
			_ = new EnvService();
#endif

			Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data"));

			string userId = Environment.GetEnvironmentVariable("SCRIBBLEHUB_USERID") ?? throw new InvalidOperationException("Missing required USERID environment variable");
			
			ScribbleClient api = new(userId);

			try
			{
				await FlareClient.InitiateSession(SessionId);

				Log("Reading scribble data...");
				List<ScribbleSaveBookRoot> currentBooks = await FileStoreService.LoadScribbleHubAsync();
				Log($"Found: {currentBooks.Count} cached books");

				Log("Fetching new scribble data...");
				List<ScribbleReadingListStory> readingData = await api.GetReadingList();
				Log($"Found: {readingData.Count} new books");
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
					await NotificationService.SendNewScribbleChapterAsync(story.Name, story.Link,
						latestCurrentChapter.Title, latestCurrentChapter.Link);
				}
			}
			catch (Exception ex)
			{
				Log($"An unknown error has occured during runtime: {ex}");
			}
			finally
			{
				await FlareClient.DestroySessionAsync(SessionId);
			}

		}
	}


#if DEBUG
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
#endif
}

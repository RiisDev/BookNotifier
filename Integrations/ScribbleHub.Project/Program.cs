using BookNotifier.Integrations;
using BookNotifier.Services;

namespace ScribbleHub.Project
{
	internal class Program
	{
		static async Task Main(string[] _)
		{
			using ScribbleClient api = new();

			try
			{
				await api.Login(
					Environment.GetEnvironmentVariable("SCRIBBLEHUB_USERNAME")
					?? throw new InvalidOperationException("Missing SCRIBBLEHUB_USERNAME environment variable."),
					Environment.GetEnvironmentVariable("SCRIBBLEHUB_PASSWORD")
					?? throw new InvalidOperationException("Missing SCRIBBLEHUB_PASSWORD environment variable.")
				);
			}
			catch
			{
				api.SetCookies(
					Environment.GetEnvironmentVariable("SCRIBBLEHUB_PRESET_COOKIE")
					?? throw new InvalidOperationException("Login failed and SCRIBBLEHUB_PRESET_COOKIE is missing.")
				);
			}

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
	}
}

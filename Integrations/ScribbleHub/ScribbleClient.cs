using BookNotifier.Services;
using BookNotifier.Utilities;
using System.Text.RegularExpressions;

namespace BookNotifier.Integrations.ScribbleHub
{
	public class ScribbleClient(string userId)
	{
		public readonly string SessionId = $"booknot-scribblehub-{Guid.NewGuid():N}";

		public async Task RunCheck()
		{
			try
			{
				await Program.FlareClient.InitiateSession(SessionId);

				await Program.FlareClient.GetSolver("https://www.scribblehub.com/", SessionId);

				Log("Reading scribble data...");
				List<ScribbleSaveBookRoot> currentBooks = await FileStoreService.LoadScribbleHubAsync();
				Log($"Found: {currentBooks.Count} cached books");

				Log("Fetching new scribble data...");
				List<ScribbleReadingListStory> readingData = await GetReadingList(currentBooks);
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
					await NotificationService.SendNewScribbleChapterAsync(story.Name, story.Link, latestCurrentChapter.Title, latestCurrentChapter.Link);
				}
			}
			catch (Exception ex)
			{
				Log($"An unknown error has occured during runtime: {ex}");
			}
			finally
			{
				await Program.FlareClient.DestroySessionAsync(SessionId);
			}
		}

		public async Task<List<ScribbleReadingListStory>> GetReadingList(List<ScribbleSaveBookRoot> currentCache)
		{
			List<ScribbleReadingListStory> storyReturn = [];

			Log("Grabbing ReadingList");
			(string responseData, _, _) = await Program.FlareClient.PostSolver("https://www.scribblehub.com/wp-admin/admin-ajax.php", SessionId, [
				new KeyValuePair<string, string>("action", "wi_profilerl"),
				new KeyValuePair<string, string>("intAuthorID", userId),
				new KeyValuePair<string, string>("isMobile", ""),
				new KeyValuePair<string, string>("str_isapp", "0"),
			]);

			if (responseData.Contains("need to log in before you can access this page", StringComparison.InvariantCultureIgnoreCase))
				throw new InvalidOperationException("User is not logged in");

			HtmlDocument document = HtmlDocument.Parse(responseData);
			IEnumerable<HtmlElement> stories = document.QuerySelectorAll("div[class*=title] a");

			foreach (HtmlElement story in stories)
			{
				HtmlNode gridParent = story.Parent?.Parent ?? throw new InvalidOperationException("Failed to find grid parent.");
				if (gridParent is not HtmlElement gridElement) throw new InvalidOperationException("Failed to convert back to htmlElement.");

				string title = story.InnerText;
				string storyLink = story.GetAttribute("href")!;
				string storyId = Regex.Match(storyLink, @"series\/(\d+)\/", RegexOptions.Compiled | RegexOptions.Singleline).Groups[1].Value.Trim();
				string chapterId = gridElement.QuerySelector("span[last]")?.GetAttribute("last") ?? throw new InvalidOperationException("Failed to get last_id");
				string chapterName = gridElement.QuerySelector("[class=rl_i_status]")?.Children.LastOrDefault()?.InnerText ?? "";

				if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(chapterId) || string.IsNullOrEmpty(chapterName))
				{
					LogError($"One or more fields are empty -> {title} | {chapterName} | {chapterId}");
					continue;
				}

				title = title.HtmlDecode();
				chapterName = chapterName.HtmlDecode();

				Log($"Found Story: {title} -> {chapterName}");

				if (currentCache.TryFind(x => x.Id == storyId, out ScribbleSaveBookRoot? data))
				{
					if (data is not null && data.Chapters.TryFind(x => x.Id == chapterId, out _))
					{
						Log($"Story cache for {title} already contains latest chapter, skipping lookup...");
						storyReturn.Add(new ScribbleReadingListStory(title,
							storyLink,
							storyId,
							data.Chapters.Select(chap => new ScribbleChapter(chap.Title,
									chap.Link,
									chap.Id))
								.ToList()));
						continue;
					}
				}

				storyReturn.Add(new ScribbleReadingListStory(title, storyLink, storyId, []));
			}

			await Task.Delay(500);

			foreach (ScribbleReadingListStory story in storyReturn)
			{
				story.Chapters.AddRange(await GetBookToc(story.Id));
				story.Chapters.Reverse();
				await Task.Delay(500);
			}

			return storyReturn;
		}

		public async Task<List<ScribbleChapter>> GetBookToc(string bookId)
		{
			(string responseData, int status, _) = await Program.FlareClient.PostSolver("https://www.scribblehub.com/wp-admin/admin-ajax.php", SessionId, [
				new KeyValuePair<string, string>("action", "wi_getreleases_pagination"),
				new KeyValuePair<string, string>("mypostid", bookId),
				new KeyValuePair<string, string>("pagenum", "-1")
			]);
			Log($"Book TOC Status -> ({status})");

			MatchCollection chapterMatches = Regex.Matches(responseData, "<a\\b[^>]*href=\"([^\"]+)\"[^>]*>([^<]+)<\\/a>");
			List<ScribbleChapter> chapters = [];

			foreach (Match match in chapterMatches)
			{
				string title = match.Groups[2].Value.Trim().HtmlDecode();
				string link = match.Groups[1].Value.Trim().HtmlDecode();
				string id = link[link.Trim('/').LastIndexOf('/')..].Trim('/');

				chapters.Add(new ScribbleChapter(title, link, id));
			}

			return chapters;
		}

	}
}

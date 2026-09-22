using BookNotifier.Services;
using BookNotifier.Utilities;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BookNotifier.Integrations.ScribbleHub
{
	public partial class ScribbleClient(string userId)
	{
		public readonly string SessionId = $"booknot-scribblehub-{Guid.NewGuid():N}";

		[GeneratedRegex(@"(\d+)\s*(sec|min|hour|day|week|month|year)s?\s*ago", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
		private static partial Regex RelativeDateRegex();

		internal static DateTime? ParseScribbleDate(string raw)
		{
			raw = raw.Trim();

			if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime absolute))
				return absolute;

			if (raw.Equals("yesterday", StringComparison.OrdinalIgnoreCase))
				return DateTime.UtcNow.AddDays(-1);

			Match match = RelativeDateRegex().Match(raw);
			if (!match.Success) return null;

			int amount = int.Parse(match.Groups[1].Value);
			TimeSpan span = match.Groups[2].Value.ToLowerInvariant() switch
			{
				"sec" => TimeSpan.FromSeconds(amount),
				"min" => TimeSpan.FromMinutes(amount),
				"hour" => TimeSpan.FromHours(amount),
				"day" => TimeSpan.FromDays(amount),
				"week" => TimeSpan.FromDays(amount * 7),
				"month" => TimeSpan.FromDays(amount * 30),
				"year" => TimeSpan.FromDays(amount * 365),
				_ => TimeSpan.Zero
			};

			return DateTime.UtcNow - span;
		}

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

				HashSet<string> fetchedStoryIds = readingData.Select(static s => s.Id).ToHashSet();

				foreach (ScribbleSaveBookRoot missing in currentBooks.Where(cb => !fetchedStoryIds.Contains(cb.Id)))
				{
					LogError($"[scribblehub] '{missing.Name}' missing from this fetch, keeping cached entry.");
					readingData.Add(new ScribbleReadingListStory(missing.Name, missing.Link, missing.Id,
						missing.Chapters.Select(static c => new ScribbleChapter(c.Title, c.Link, c.Id, c.ReleasedAt)).ToList(),
						missing.CoverUrl, missing.Status));
				}

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

					ScribbleChapter? latestCurrentChapter = story.Chapters.Count > 0 ? story.Chapters[^1] : null;
					ScribbleSaveChapter? latestCachedChapter = cachedStory.Chapters.Count > 0 ? cachedStory.Chapters[^1] : null;

					if (latestCurrentChapter is null)
					{
						LogError($"[scribblehub] No chapters parsed for {story.Name}, skipping chapter check.");
						continue;
					}

					if (latestCachedChapter is not null && latestCurrentChapter.Id == latestCachedChapter.Id) continue;

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
				string? coverUrl = (gridElement.Parent as HtmlElement)?.QuerySelector("div.rl_i_image img")?.GetAttribute("src");

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
									chap.Id,
									chap.ReleasedAt))
								.ToList(),
							coverUrl ?? data.CoverUrl,
							DetermineStatus(data.Chapters.Select(c => c.ReleasedAt).ToList())));
						continue;
					}
				}

				storyReturn.Add(new ScribbleReadingListStory(title, storyLink, storyId, [], coverUrl));
			}

			await Task.Delay(500);

			List<ScribbleReadingListStory> needsToc = storyReturn.Where(story => story.Chapters.Count <= 0).ToList();

			foreach (ScribbleReadingListStory story in needsToc)
			{
				List<ScribbleChapter> chapters = await GetBookToc(story.Id);
				chapters.Reverse();
				story.Chapters.AddRange(chapters);
				storyReturn[storyReturn.IndexOf(story)] = story with { Status = DetermineStatus(chapters.Select(c => c.ReleasedAt).ToList()) };
				await Task.Delay(500);
			}

			return storyReturn;
		}

		private static string DetermineStatus(List<DateTime?> releaseDates)
		{
			DateTime? latest = releaseDates.Where(d => d.HasValue).Max();
			if (latest is null) return "unknown";
			return DateTime.UtcNow - latest.Value <= TimeSpan.FromDays(14) ? "ongoing" : "hiatus";
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
			MatchCollection dateMatches = Regex.Matches(responseData, "class=\"fic_date_pub\"\\s+title=\"([^\"]+)\"");
			List<ScribbleChapter> chapters = [];

			for (int i = 0; i < chapterMatches.Count; i++)
			{
				Match match = chapterMatches[i];
				string title = match.Groups[2].Value.Trim().HtmlDecode();
				string link = match.Groups[1].Value.Trim().HtmlDecode();
				string id = link[link.Trim('/').LastIndexOf('/')..].Trim('/');
				DateTime? releasedAt = i < dateMatches.Count ? ParseScribbleDate(dateMatches[i].Groups[1].Value.HtmlDecode()) : null;

				chapters.Add(new ScribbleChapter(title, link, id, releasedAt));
			}

			return chapters;
		}

	}
}

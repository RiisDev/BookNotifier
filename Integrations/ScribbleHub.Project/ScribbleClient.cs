using System.Text.RegularExpressions;
using BookNotifier.Integrations;
using BookNotifier.Utilities;

namespace ScribbleHub.Project
{
	public class ScribbleClient(string userId)
	{
		public async Task<List<ScribbleReadingListStory>> GetReadingList()
		{
			List<ScribbleReadingListStory> storyReturn = [];

			Log("Grabbing ReadingList");
			(string responseData, _, _) = await Program.FlareClient.PostSolver("https://www.scribblehub.com/wp-admin/admin-ajax.php", Program.SessionId, [
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

				Log($"Found Story: {title.HtmlDecode()} -> {chapterName.HtmlDecode()}");
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
			(string responseData, int status, _) = await Program.FlareClient.PostSolver("https://www.scribblehub.com/wp-admin/admin-ajax.php", Program.SessionId, [
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

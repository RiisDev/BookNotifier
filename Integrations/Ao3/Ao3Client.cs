using System.Text;
using System.Text.RegularExpressions;
using BookNotifier.Services;

namespace BookNotifier.Integrations.Ao3
{
	public partial class Ao3Client(string username, string pseudoName)
	{
		private readonly string _sessionId = $"booknot-ao3-{Guid.NewGuid():N}";

		public async Task RunCheck()
		{
			try
			{
				Log("Creating flaresolver session...");
				await Program.FlareClient.InitiateSession(_sessionId);

				Log($"Fetching bookmarks for {username} (pseudo {pseudoName})...");

				List<Ao3ExistingWorkEntries> existing = await FileStoreService.LoadAo3Async();
				bool firstRun = existing.Count == 0;
				
				List<Ao3WorkEntry> bookmarks = await GetBookmarkedBooksAsync();
				await FileStoreService.SaveAo3Async(bookmarks);

				if (firstRun) return;

				// Don't fix R, some entries have no chapters as a singleshot, and will throw a null error if indexed directly.
				Dictionary<string, string> existingStoryChapter = existing.ToDictionary(x => x.WorkId, r => r.Chapters.LastOrDefault()?.Url ?? "");

				foreach (Ao3WorkEntry newEntry in bookmarks)
				{
					if (existingStoryChapter.TryGetValue(newEntry.WorkId, out string? cachedLastUrl))
					{
						string latestChapterUrl = newEntry.Chapters.LastOrDefault()?.Url.Trim() ?? "";
						string latestChapterTitle = newEntry.Chapters.LastOrDefault()?.Title.Trim() ?? "";

						if (!latestChapterUrl.Contains("/works/"))
						{
							latestChapterUrl = latestChapterTitle.Replace("https://archiveofourown.org/", $"https://archiveofourown.org/works/{newEntry.WorkId}/chapters/");
						}

						if (StringComparer.Ordinal.Equals(latestChapterUrl, cachedLastUrl)) continue;

						await NotificationService.SendNewAo3ChapterAsync(newEntry.Title, newEntry.Url, latestChapterTitle, latestChapterUrl);
					}
					else
					{
						await NotificationService.SendNewAo3FictionAsync(newEntry.Title, newEntry.Url);
					}
				}
			}
			finally
			{
				await Program.FlareClient.DestroySessionAsync(_sessionId);
			}
		}

		private async Task<List<Ao3WorkEntry>> GetBookmarkedBooksAsync()
		{
			List<Ao3WorkEntry> entries = [];

			List<string> bookmarks = await GetBookmarkedUrlsAsync();
			
			Log($"Found {bookmarks.Count} bookmarks, fetching metadata..."); 

			TimeSpan estimated = TimeSpan.FromSeconds(bookmarks.Count * 4);
			Log($"Estimated: {estimated:mm\\:ss}");

			foreach (string bookmark in bookmarks)
			{
				Ao3WorkEntry? bookEntry = await GetEntryDetailsAsync(bookmark);
				if (bookEntry is null) continue;
				entries.Add(bookEntry);
			}

			return entries;
		}

		private async Task<List<string>> GetBookmarkedUrlsAsync(int page = 1)
		{
			List<string> favourites = [];

			(string responseContent, int statusCode, _) = await Program.FlareClient.GetSolver($"https://archiveofourown.org/bookmarks?commit=Sort+and+Filter&bookmark_search%5Bsort_column%5D=bookmarkable_date&pseud_id={pseudoName}&user_id={username}&page={page}", _sessionId);

			if (statusCode != 200)
			{
				LogError($"Failed to get favourites ({statusCode})");
				return favourites;
			}

			MatchCollection bookUrlsMatches = GetWorksRegex().Matches(responseContent);

			foreach (Match match in bookUrlsMatches)
				favourites.Add($"https://archiveofourown.org{match.Groups[1].Value}");

			if (page != 1) return favourites;

			MatchCollection pageCountMatch = GetPageCount().Matches(responseContent);

			// Take only 3 pages of bookmarks, should still good enough since we already sort by most recently updated.
			HashSet<string> otherPages = pageCountMatch.Select(static m => m.Groups[1].Value).Take(2).ToHashSet();

			foreach (string pageNumber in otherPages)
			{
				favourites.AddRange(await GetBookmarkedUrlsAsync(int.Parse(pageNumber)));
			}

			return favourites;
		}

		private async Task<Ao3WorkEntry?> GetEntryDetailsAsync(string url)
		{
			(string responseContent, int statusCode, _) = await Program.FlareClient.GetSolver($"{url}?view_adult=true", _sessionId);

			if (statusCode != 200)
			{
				LogError($"Failed to book data ({statusCode}) for {url}");
				return null;
			}

			if (!url.Contains('/'))
			{
				LogError($"{url} is somehow missing '/'");
				return null;
			}

			string workId = url[(url.LastIndexOf('/') + 1)..];

			Match titleMatch = GetTitleRegex().Match(responseContent);

			if (!titleMatch.Success)
			{
				LogError($"Failed to get title match on {Convert.ToBase64String(Encoding.UTF8.GetBytes(responseContent))}");
				return null;
			}

			Match authorMatch = GetAuthorRegex().Match(responseContent);

			string author;

			if (!authorMatch.Success)
			{
				LogError($"Failed to get author match on {Convert.ToBase64String(Encoding.UTF8.GetBytes(responseContent))}");
				author = "Anonymous";
			}
			else author = authorMatch.Groups[1].Value.Trim();

			MatchCollection chaptersMatch = GetChaptersRegex().Matches(responseContent);

			List<Ao3Chapter> chapters = [];

			foreach (Match match in chaptersMatch)
			{
				chapters.Add(new Ao3Chapter(match.Groups[2].Value.Trim(), $"https://archiveofourown.org/{match.Groups[1].Value.Trim()}"));
			}

			return new Ao3WorkEntry(
				WorkId: workId,
				Title: titleMatch.Groups[1].Value.Trim(),
				Url: url,
				Author: author,
				Chapters: chapters
			);
		}

		[GeneratedRegex("href=\"(\\/works\\/\\d+)\">", RegexOptions.Compiled)]
		private partial Regex GetWorksRegex();

		[GeneratedRegex("href=\"[^\"]*page=(\\d+)\">(\\d+)</a>(?=.*?<li><span class=\"gap\")", RegexOptions.Compiled)]
		private partial Regex GetPageCount();

		[GeneratedRegex("<h2\\s+class=\"title\\s+heading\">\\s*(.*?)\\s*</h2>", RegexOptions.Compiled)]
		private partial Regex GetTitleRegex();

		[GeneratedRegex("<a\\s+rel=\"author\"[^>]*>(.*?)</a>", RegexOptions.Compiled)]
		private partial Regex GetAuthorRegex();

		[GeneratedRegex("<option(?:\\s+selected=\"selected\")?\\s+value=\"(\\d+)\">(.*?)</option>", RegexOptions.Compiled)]
		private partial Regex GetChaptersRegex();
	}
}

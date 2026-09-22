using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using BookNotifier.Services;
using BookNotifier.Utilities;

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

				// A work missing from this fetch is more likely a failed/empty request than a real
				// unbookmark — keep the cached entry instead of silently dropping it.
				HashSet<string> fetchedWorkIds = bookmarks.Select(static b => b.WorkId).ToHashSet();

				foreach (Ao3ExistingWorkEntries missing in existing.Where(e => !fetchedWorkIds.Contains(e.WorkId)))
				{
					LogError($"[ao3] '{missing.Title}' missing from this fetch, keeping cached entry.");
					bookmarks.Add(new Ao3WorkEntry(
						missing.WorkId,
						missing.Title,
						missing.Url,
						missing.Author,
						missing.Chapters.Select(static c => new Ao3Chapter(c.Title, c.Url, c.ReleasedAt)).ToList(),
						missing.Status));
				}

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
						string chapterFullUrl = latestChapterUrl;

						if (!latestChapterUrl.Contains("/works/"))
						{
							chapterFullUrl = latestChapterUrl.Replace("https://archiveofourown.org/", $"https://archiveofourown.org/works/{newEntry.WorkId}/chapters/");
						}

						if (StringComparer.Ordinal.Equals(latestChapterUrl, cachedLastUrl)) continue;
						if (StringComparer.Ordinal.Equals(chapterFullUrl, cachedLastUrl)) continue;

						Log($"New Chapter Detected: {newEntry.Title}");
						await NotificationService.SendNewAo3ChapterAsync(newEntry.Title, newEntry.Url, latestChapterTitle, chapterFullUrl);
					}
					else
					{
						Log($"New Fiction Detected: {newEntry.Title}");
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

			Stopwatch stopwatch = Stopwatch.StartNew();

			for (int i = 0; i < bookmarks.Count; i++)
			{
				Ao3WorkEntry? bookEntry = await GetEntryDetailsAsync(bookmarks[i]);
				if (bookEntry is not null) entries.Add(bookEntry);

				if (i == 0)
				{
					TimeSpan estimated = stopwatch.Elapsed * bookmarks.Count;
					Log($"First request took {stopwatch.Elapsed.TotalSeconds:F1}s, estimated total: {estimated:mm\\:ss}");
				}
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
			if (!url.Contains('/'))
			{
				LogError($"{url} is somehow missing '/'");
				return null;
			}

			string workId = url[(url.LastIndexOf('/') + 1)..];

			(string responseContent, int statusCode, _) = await Program.FlareClient.GetSolver($"{url}/navigate?view_adult=true", _sessionId);

			if (statusCode != 200)
			{
				LogError($"Failed to get navigate page ({statusCode}) for {url}");
				return null;
			}

			Match titleMatch = GetTitleAndAuthorRegex().Match(responseContent);

			if (!titleMatch.Success)
			{
				LogError($"Failed to get title match on {responseContent.ToBase64()}");
				return null;
			}

			string author = titleMatch.Groups[2].Success ? titleMatch.Groups[2].Value.Trim() : "Anonymous";

			MatchCollection chaptersMatch = GetChaptersRegex().Matches(responseContent);

			List<Ao3Chapter> chapters = [];

			foreach (Match match in chaptersMatch)
			{
				string chapterUrl = $"https://archiveofourown.org{match.Groups[1].Value.Trim()}";
				string chapterTitle = match.Groups[2].Value.Trim();
				DateTime? releasedAt = DateTime.TryParse(match.Groups[3].Value.Trim(), CultureInfo.InvariantCulture,
					DateTimeStyles.None, out DateTime parsed) ? parsed : null;

				chapters.Add(new Ao3Chapter(chapterTitle, chapterUrl, releasedAt));
			}

			string status = DetermineStatus(chapters.Select(c => c.ReleasedAt).ToList());

			return new Ao3WorkEntry(
				WorkId: workId,
				Title: titleMatch.Groups[1].Value.Trim(),
				Url: url,
				Author: author,
				Chapters: chapters,
				Status: status
			);
		}

		// AO3 has no explicit ongoing/hiatus flag on the navigate page — approximate it from recency:
		// nothing posted in 2 weeks is treated as on hiatus.
		private static string DetermineStatus(List<DateTime?> releaseDates)
		{
			DateTime? latest = releaseDates.Where(d => d.HasValue).Max();
			if (latest is null) return "unknown";
			return DateTime.UtcNow - latest.Value <= TimeSpan.FromDays(14) ? "ongoing" : "hiatus";
		}

		[GeneratedRegex("href=\"(\\/works\\/\\d+)\">", RegexOptions.Compiled)]
		private partial Regex GetWorksRegex();

		[GeneratedRegex("href=\"[^\"]*page=(\\d+)\">(\\d+)</a>(?=.*?<li><span class=\"gap\")", RegexOptions.Compiled)]
		private partial Regex GetPageCount();

		// Matches: <a href="/works/123">Title</a> by <a rel="author" href="...">Author</a> — author group is
		// optional since anonymous/orphaned works omit the "by <a rel=author>" part entirely.
		[GeneratedRegex("<a\\s+href=\"\\/works\\/\\d+\">\\s*(.*?)\\s*<\\/a>(?:\\s*by\\s*<a\\s+rel=\"author\"[^>]*>\\s*(.*?)\\s*<\\/a>)?", RegexOptions.Compiled)]
		private partial Regex GetTitleAndAuthorRegex();

		[GeneratedRegex("<li><a\\s+href=\"(\\/works\\/\\d+\\/chapters\\/\\d+)\">\\s*(?:\\d+\\.\\s*)?(.*?)\\s*<\\/a>\\s*<span class=\"datetime\">\\(([\\d-]+)\\)<\\/span>", RegexOptions.Compiled)]
		private partial Regex GetChaptersRegex();
	}
}

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BookNotifier.Services;

namespace BookNotifier.Integrations.RoyalRoad
{
	internal partial class RoyalRoadClient(int userId) : IDisposable
	{
		private record RoyalRoadBook(string Title, string Url, List<RoyalRoadChapter> Chapters);

		private record RoyalRoadChapter(
			[property: JsonPropertyName("title")] string Title,
			[property: JsonPropertyName("isUnlocked")] bool? IsUnlocked,
			[property: JsonPropertyName("url")] string Url
		);

		private static readonly HttpClient Client = new(new HttpClientHandler
		{
			AllowAutoRedirect = true,
			AutomaticDecompression = DecompressionMethods.All,
			CookieContainer = new CookieContainer(),
			UseCookies = true
		})
		{
			DefaultRequestHeaders =
			{
				{
					"User-Agent",
					"Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36"
				}
			},
			Timeout = TimeSpan.FromSeconds(15)
		};

		public void Dispose()
		{
			GC.SuppressFinalize(this);
			Client.Dispose();
		}

		public async Task RunAsync()
		{
			Log($"[royalroad] Fetching Books for {userId}...");
			List<RoyalRoadBook> currentBooks = await FetchAllFavouritesAsync();
			Log($"[royalroad] Grabbed {currentBooks.Count} books...");

			List<RoyalRoadKnownFiction> knownFictions = await FileStoreService.LoadRoyalRoadAsync();
			
			bool isFirstRun = knownFictions.Count == 0;

			Log($"[royalroad] Cached books: {knownFictions.Count}, IsFirstRun: {isFirstRun}");

			Dictionary<string, RoyalRoadKnownFiction> knownByUrl = knownFictions.ToDictionary(static f => f.Url, StringComparer.OrdinalIgnoreCase);

			List<RoyalRoadKnownFiction> updatedFictions = [];

			foreach (RoyalRoadBook book in currentBooks)
			{
				string normalizedUrl = NormalizeUrl(book.Url);

				List<RoyalRoadKnownChapter> currentChapters = book.Chapters
					.Where(static c => c.IsUnlocked != false)
					.Select(static c => new RoyalRoadKnownChapter
					{
						Title = c.Title.Trim(),
						Url = $"https://www.royalroad.com{c.Url}"
					})
					.ToList();

				if (!knownByUrl.TryGetValue(normalizedUrl, out RoyalRoadKnownFiction? knownFiction))
				{
					// Brand-new fiction
					if (!isFirstRun)
					{
						Log($"[royalroad] New Book Detected: {book.Title}");
						await NotificationService.SendNewRoyalRoadFictionAsync(book.Title, normalizedUrl);
					}

					updatedFictions.Add(new RoyalRoadKnownFiction
					{
						Title = book.Title.Trim(),
						Url = normalizedUrl,
						Chapters = currentChapters
					});

					continue;
				}

				// Known fiction — diff chapters
				HashSet<string> knownChapterUrls = knownFiction.Chapters
					.Select(static c => c.Url)
					.ToHashSet(StringComparer.OrdinalIgnoreCase);

				List<RoyalRoadKnownChapter> newChapters = currentChapters
					.Where(c => !knownChapterUrls.Contains(c.Url))
					.ToList();

				foreach (RoyalRoadKnownChapter chapter in newChapters.Where(_ => !isFirstRun))
				{
					Log($"[royalroad] New chapter released: {chapter.Title}");
					await NotificationService.SendNewRoyalRoadChapterAsync(book.Title, normalizedUrl, chapter.Title, chapter.Url);
				}

				updatedFictions.Add(knownFiction with
				{
					Title = book.Title.Trim(),
					Chapters = currentChapters
				});
			}

			await FileStoreService.SaveRoyalRoadAsync(updatedFictions);
		}
		
		private async Task<List<RoyalRoadBook>> FetchAllFavouritesAsync()
		{
			List<string> favouriteUrls = await GetFavouriteUrlsAsync();

			List<RoyalRoadBook> books = [];

			foreach (string url in favouriteUrls)
			{
				RoyalRoadBook? book = await GetBookAsync(url);

				if (book is not null)
					books.Add(book);
			}

			return books;
		}

		private async Task<List<string>> GetFavouriteUrlsAsync(int page = 1)
		{
			List<string> favourites = [];

			using HttpRequestMessage request = new(HttpMethod.Get, $"https://www.royalroad.com/profile/{userId}/favorites?page={page}");
			using HttpResponseMessage response = await Client.SendAsync(request);

			string responseContent = await response.Content.ReadAsStringAsync();

			if (!response.IsSuccessStatusCode)
			{
				LogError($"[RoyalRoad] Failed to get favourites ({response.StatusCode}): {Convert.ToBase64String(Encoding.UTF8.GetBytes(responseContent))}");
				return favourites;
			}

			MatchCollection bookMatches = BookFavouriteRegex().Matches(responseContent);

			foreach (Match match in bookMatches)
				favourites.Add($"https://www.royalroad.com{match.Groups[1].Value}");

			if (page != 1) return favourites;

			MatchCollection paginationMatches = FavouritePageRegex().Matches(responseContent);

			HashSet<string> otherPages = paginationMatches
				.Select(static m => m.Value)
				.Skip(1)
				.ToHashSet();

			foreach (string pageNumber in otherPages.Select(pageUrl => pageUrl[(pageUrl.IndexOf('=') + 1)..]))
			{
				List<string> pageResults = await GetFavouriteUrlsAsync(int.Parse(pageNumber));

				favourites.AddRange(pageResults);
			}

			return favourites;
		}

		private async Task<RoyalRoadBook?> GetBookAsync(string url)
		{
			using HttpRequestMessage request = new(HttpMethod.Get, url);
			using HttpResponseMessage response = await Client.SendAsync(request);

			string responseContent = await response.Content.ReadAsStringAsync();

			if (!response.IsSuccessStatusCode)
			{
				LogError($"[RoyalRoad] Failed to get book ({response.StatusCode}): {url}");
				return null;
			}

			Match titleMatch = BookTitleRegex().Match(responseContent);

			if (!titleMatch.Success)
			{
				LogError($"[RoyalRoad] Failed to parse title for: {url}");
				return null;
			}

			Match chaptersMatch = ChapterRegex().Match(responseContent);

			if (!chaptersMatch.Success)
			{
				LogError($"[RoyalRoad] Failed to parse chapters for: {url}");
				return null;
			}

			List<RoyalRoadChapter>? chapters = JsonSerializer.Deserialize<List<RoyalRoadChapter>>(chaptersMatch.Groups[1].Value);

			if (chapters is null)
			{
				LogError($"[RoyalRoad] Failed to deserialize chapters for: {url}");
				return null;
			}

			string title = titleMatch.Groups[1].Value.Trim();

			return new RoyalRoadBook(title, url, chapters);
		}
		
		private static string NormalizeUrl(string url)
		{
			Uri uri = new(url);
			return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
		}

		[GeneratedRegex("""<a class="btn btn-default btn-outline" href="(.+)">View Page<\/a>""", RegexOptions.Compiled)]
		private partial Regex BookFavouriteRegex();

		[GeneratedRegex("""window\.chapters\s*=\s*(\[.*?\]);""", RegexOptions.Compiled)]
		private partial Regex ChapterRegex();

		[GeneratedRegex("""<meta\s+name="twitter:title"\s+content="([^"]+)""", RegexOptions.Compiled)]
		private partial Regex BookTitleRegex();

		[GeneratedRegex("""\/profile\/\d+\/favorites\?page=\d+""", RegexOptions.Compiled)]
		private partial Regex FavouritePageRegex();
	}
}
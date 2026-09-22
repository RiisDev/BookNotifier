using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.ServiceModel.Syndication;
using System.Text.RegularExpressions;
using System.Xml;
using BookNotifier.Services;
using BookNotifier.Utilities;

namespace BookNotifier.Integrations.GoodReads
{
	public class GoodReadsClient : IDisposable
	{


		private static readonly Uri BaseUri = new("https://www.goodreads.com");

		private static string NormalizeUrl(string href) => new Uri(BaseUri, href).GetLeftPart(UriPartial.Path);

		private readonly HttpClient _client = ScraperHttpClient.Create(
			"Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:141.0) Gecko/20100101 Firefox/141.0 GoodReadsWatcher/1.0",
			TimeSpan.FromSeconds(15));

		public void Dispose()
		{
			GC.SuppressFinalize(this);
			_client.Dispose();
		}

		public async Task<IReadOnlyList<GoodReadsBookDetails>> GetReadingListBooksAsync(string userId, string? shelf = null)
		{
			Log($"[goodreads] Getting reading list for: {userId}...");
			string url = $"https://www.goodreads.com/review/list_rss/{userId}" + $"{(string.IsNullOrWhiteSpace(shelf) ? string.Empty : $"?shelf={shelf}")}";

			string xmlContent = await GetStringWithRetryAsync(url);

			using XmlReader reader = XmlReader.Create(new StringReader(xmlContent));

			SyndicationFeed feed = SyndicationFeed.Load(reader);

			Uri[] bookUrls =
			[
				..
				from SyndicationItem item in feed.Items
				let doc = HtmlDocument.Parse(item.Summary.Text)
				let href = doc
					.GetElementsByTagName("a")
					.FirstOrDefault()
					?.GetAttribute("href")
				where Uri.IsWellFormedUriString(href, UriKind.Absolute)
				select new Uri(href!)
			];

			Log($"[goodreads] Found {bookUrls.Length} book(s) urls...");

			GoodReadsBookDetails?[] results = await Task.WhenAll(bookUrls.Select(async url =>
			{
				try
				{
					return await GetBookDetailsAsync(url);
				}
				catch (Exception ex)
				{
					LogError($"[goodreads] Failed to parse book details for {url}: {ex.Message}");
					return null;
				}
			}));

			GoodReadsBookDetails[] books = results.Where(static b => b is not null).ToArray()!;

			Log($"[goodreads] Grabbed {books.Length} book(s) data...");

			return books.AsReadOnly();
		}

		public async Task<GoodReadsBookDetails> GetBookDetailsAsync(Uri url)
		{
			string html = await GetStringWithRetryAsync(url);

			HtmlDocument doc = HtmlDocument.Parse(html);

			HtmlElement? seriesElement = doc.GetElementsByTagName("h3")
				.FirstOrDefault(static element =>
					element.GetAttribute("aria-label")?.Contains("series") == true);

			HtmlElement? seriesAnchor = seriesElement?
				.GetElementsByTagName("a")
				.FirstOrDefault();

			GoodReadsSeries? series = null;

			if (seriesAnchor is not null)
			{
				series = new GoodReadsSeries
				{
					Id = Guid.NewGuid(),
					Name = seriesAnchor.TextContent.Trim(),
					Url = new Uri(seriesAnchor.GetAttribute("href") ?? string.Empty)
				};
			}

			HtmlElement? authorAnchor = doc.GetElementsByTagName("a")
				.FirstOrDefault(static element =>
					element.GetAttribute("class")?.Contains("ContributorLink") == true);

			HtmlElement? authorNameSpan = authorAnchor?
				.GetElementsByTagName("span")
				.FirstOrDefault(static element =>
					element.GetAttribute("data-testid") == "name");

			if (authorAnchor is null || authorNameSpan is null)
			{
				throw new InvalidOperationException("Could not parse author.");
			}

			GoodReadsAuthor author = new()
			{
				Id = Guid.NewGuid(),
				Name = authorNameSpan.TextContent.Trim(),
				Url = new Uri(authorAnchor.GetAttribute("href") ?? string.Empty)
			};

			HtmlElement? titleElement = doc.GetElementsByTagName("h1")
				.FirstOrDefault(static element =>
					element.GetAttribute("data-testid") == "bookTitle");

			string title = titleElement?.TextContent.Trim()
				?? url.Segments.Last();

			HtmlElement? publicationElement = doc.QuerySelector("[data-testid=publicationInfo]");
			DateTime? publishedAt = ParsePublicationInfo(publicationElement?.TextContent);

			string? coverUrl = doc.QuerySelector("div.BookCover__image img")?.GetAttribute("src");

			GoodReadsBook book = new()
			{
				Id = Guid.NewGuid(),
				Title = title,
				Url = url,
				AuthorId = author.Id,
				SeriesId = series?.Id,
				PublishedAt = publishedAt,
				CoverUrl = coverUrl
			};

			GoodReadsBookDetails details = new()
			{
				Book = book,
				Author = author,
				Series = series
			};

			if (series is not null)
			{
				details = await GetBookSeriesDetails(details);
			}

			return details;
		}

		public async Task<GoodReadsBookDetails> GetBookSeriesDetails(GoodReadsBookDetails details)
		{
			Log($"[goodreads] {details.Book.Title} Series detected, grabbing series info...");
			if (details.Series is null)
			{
				return details;
			}

			string html = await GetStringWithRetryAsync(details.Series.Url);

			HtmlDocument doc = HtmlDocument.Parse(html);

			List<GoodReadsSeriesBook> books = [];

			HashSet<string> seen = [];

			IEnumerable<HtmlElement> anchors = doc.All
				.Where(x => x.HasAttribute("href"))
				.Where(x => x.GetAttribute("href")!.Contains("/book/show"));

			foreach (HtmlElement anchor in anchors)
			{
				string? href = anchor.GetAttribute("href");

				if (string.IsNullOrWhiteSpace(href)) continue;
				if (!href.Contains("/book/show")) continue;

				string normalizedUrl = NormalizeUrl(href);

				if (!seen.Add(normalizedUrl)) continue;

				HtmlElement coverImg = anchor.Children.First();
				string title = coverImg.GetAttribute("alt") ?? "N/A";

				if (string.IsNullOrWhiteSpace(title)) continue;
				if (title.Length < 2) continue;

				books.Add(new GoodReadsSeriesBook
				{
					Title = title,
					Url = new Uri(normalizedUrl),
					Position = books.Count + 1,
					CoverUrl = coverImg.GetAttribute("src")
				});
			}

			books = books
				.Where(static book =>
					!book.Title.Contains("See full series") &&
					!book.Title.Contains("More books"))
				.ToList();

			GoodReadsSeries updatedSeries = details.Series with
			{
				Books = books.AsReadOnly()
			};

			return details with
			{
				Series = updatedSeries
			};
		}

		public async Task<List<GoodReadsBook>> GetAuthorsBooks(Uri authorUrl)
		{
			string html = await GetStringWithRetryAsync(authorUrl);

			HtmlDocument doc = HtmlDocument.Parse(html);

			List<GoodReadsBook> books = [];

			HashSet<string> seen = [];

			IEnumerable<HtmlElement> rows = doc.All
				.Where(static element =>
					element.TagName.Equals("tr", StringComparison.OrdinalIgnoreCase))
				.Where(static element =>
					element.GetAttribute("itemtype")
						== "http://schema.org/Book");

			foreach (HtmlElement row in rows)
			{
				HtmlElement? bookAnchor = row
					.GetElementsByTagName("a")
					.FirstOrDefault(static element =>
						element.GetAttribute("class")
							?.Contains("bookTitle") == true);

				if (bookAnchor is null)
				{
					continue;
				}

				string? href = bookAnchor.GetAttribute("href");

				if (string.IsNullOrWhiteSpace(href))
				{
					continue;
				}

				string normalizedUrl = NormalizeUrl(href);

				if (!seen.Add(normalizedUrl))
				{
					continue;
				}

				string title =
					bookAnchor.TextContent.Trim();

				HtmlElement? image = row
					.GetElementsByTagName("img")
					.FirstOrDefault();

				if (string.IsNullOrWhiteSpace(title))
				{
					title =
						image?.GetAttribute("alt")
						?? "Unknown";
				}

				string? coverUrl = image?.GetAttribute("src");

				HtmlElement? authorAnchor = row
					.GetElementsByTagName("a")
					.FirstOrDefault(static element =>
						element.GetAttribute("class")
							?.Contains("authorName") == true);

				string authorName =
					authorAnchor?.TextContent.Trim()
					?? "Unknown";

				string? authorHref =
					authorAnchor?.GetAttribute("href");

				GoodReadsAuthor author = new()
				{
					Id = Guid.NewGuid(),
					Name = authorName,
					Url = new Uri(authorHref ?? authorUrl.ToString())
				};

				GoodReadsBook book = new()
				{
					Id = Guid.NewGuid(),
					Title = title,
					Url = new Uri(normalizedUrl),
					AuthorId = author.Id,
					CoverUrl = coverUrl
				};

				books.Add(book);
			}

			return books;
		}

		internal static DateTime? ParsePublicationInfo(string? text)
		{
			if (string.IsNullOrWhiteSpace(text)) return null;

			string dateText = Regex.Replace(text, "^.*?published\\s+", "", RegexOptions.IgnoreCase).Trim();

			return DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed)
				? parsed
				: null;
		}

		private async Task<string> GetStringWithRetryAsync(Uri url, int maxRetries = 5,
			CancellationToken cancellationToken = default) =>
			await GetStringWithRetryAsync(url.AbsoluteUri, maxRetries, cancellationToken);

		private async Task<string> GetStringWithRetryAsync(string url, int maxRetries = 5, CancellationToken cancellationToken = default)
		{
			(string content, HttpStatusCode statusCode) = await HttpRetry.GetWithRetryAsync(_client, url, maxRetries, cancellationToken);

			if (!HttpRetry.IsSuccess(statusCode))
				throw new HttpRequestException($"Request failed with status code {(int)statusCode} for {url}");

			return content;
		}

		public async Task RunAsync(IReadOnlyList<GoodReadsBookDetails> readingListData, Dictionary<string, List<GoodReadsBook>> authorBooks)
		{
			List<GoodReadsKnownBook> knownBookRecords = await FileStoreService.LoadGoodReadsKnownBookRecordsAsync();
			HashSet<string> knownBooks = knownBookRecords.Select(FileStoreService.CreateGoodReadsKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

			bool isFirstRun = knownBooks.Count == 0;

			List<GoodReadsKnownBook> updatedKnownBooks = [];
			HashSet<string> processedAuthors = new(StringComparer.OrdinalIgnoreCase);

			foreach (GoodReadsBookDetails details in readingListData)
			{
				GoodReadsAuthor author = details.Author;
				processedAuthors.Add(author.Name);

				if (!authorBooks.TryGetValue(author.Name, out List<GoodReadsBook>? books))
				{
					continue;
				}

				List<GoodReadsKnownBook> existingAuthorBooks = knownBookRecords
					.Where(kb => kb.AuthorName == author.Name && kb.SeriesName is null)
					.ToList();

				// An author fetch returning 0 books is more likely a failed/empty request than the
				// author actually having nothing published — keep the cached entries instead of
				// silently dropping them.
				if (books.Count == 0 && existingAuthorBooks.Count > 0)
				{
					LogError($"[goodreads] Fetched 0 books for {author.Name} but {existingAuthorBooks.Count} are cached, keeping cache.");
					updatedKnownBooks.AddRange(existingAuthorBooks);
					continue;
				}

				List<GoodReadsKnownBook> authorKnownBooks = books
					.Select(book => new GoodReadsKnownBook
					{
						Title = book.Title,
						AuthorName = author.Name,
						Url = book.Url.ToString(),
						SeriesName = null,
						SeriesPosition = null,
						CoverUrl = book.CoverUrl
					})
					.ToList();

				List<GoodReadsKnownBook> seriesKnownBooks = details.Series?.Books
					.Select(seriesBook => new GoodReadsKnownBook
					{
						Title = seriesBook.Title,
						AuthorName = author.Name,
						Url = seriesBook.Url.ToString(),
						SeriesName = details.Series.Name,
						SeriesPosition = seriesBook.Position,
						CoverUrl = seriesBook.CoverUrl
					})
					.ToList() ?? [];

				if (details.Series is not null && seriesKnownBooks.Count == 0)
				{
					List<GoodReadsKnownBook> existingSeriesBooks = knownBookRecords
						.Where(kb => kb.SeriesName == details.Series.Name)
						.ToList();

					if (existingSeriesBooks.Count > 0)
					{
						LogError($"[goodreads] Series '{details.Series.Name}' returned 0 books but {existingSeriesBooks.Count} are cached, keeping cache.");
						seriesKnownBooks = existingSeriesBooks;
					}
				}

				bool isNewAuthor = authorKnownBooks.Concat(seriesKnownBooks).All(kb => !knownBooks.Contains(FileStoreService.CreateGoodReadsKey(kb)));

				if (isNewAuthor)
				{
					if (!isFirstRun)
					{
						await NotificationService.SendNewGoodReadsAuthorAddedAsync(author.Name, author.Url.ToString());
					}

					foreach (GoodReadsKnownBook knownBook in authorKnownBooks.Concat(seriesKnownBooks))
					{
						knownBooks.Add(FileStoreService.CreateGoodReadsKey(knownBook));
						updatedKnownBooks.Add(knownBook);
					}

					continue;
				}

				foreach (GoodReadsKnownBook knownBook in authorKnownBooks)
				{
					string key = FileStoreService.CreateGoodReadsKey(knownBook);

					if (!knownBooks.Contains(key))
					{
						if (!isFirstRun)
							await NotificationService.SendNewGoodReadsAuthorBookAsync(author.Name, knownBook.Title, knownBook.Url);

						knownBooks.Add(key);
					}

					updatedKnownBooks.Add(knownBook);
				}

				if (details.Series is not null && seriesKnownBooks.Count > 0)
				{
					bool isNewSeries = seriesKnownBooks.All(kb => !knownBooks.Contains(FileStoreService.CreateGoodReadsKey(kb)));

					if (isNewSeries)
					{
						if (!isFirstRun)
						{
							await NotificationService.SendNewGoodReadsSeriesDetectedAsync(author.Name, details.Series.Name, details.Series.Url.ToString());
						}

						foreach (GoodReadsKnownBook knownSeriesBook in seriesKnownBooks)
						{
							knownBooks.Add(FileStoreService.CreateGoodReadsKey(knownSeriesBook));
							updatedKnownBooks.Add(knownSeriesBook);
						}
					}
					else
					{
						foreach (GoodReadsKnownBook knownSeriesBook in seriesKnownBooks)
						{
							string key = FileStoreService.CreateGoodReadsKey(knownSeriesBook);

							if (!knownBooks.Contains(key))
							{
								if (!isFirstRun)
									await NotificationService.SendNewGoodReadsSeriesBookAsync(
										author.Name,
										knownSeriesBook.Title,
										knownSeriesBook.Url,
										details.Series.Name,
										knownSeriesBook.SeriesPosition?.ToString() ?? ""
									);

								knownBooks.Add(key);
							}

							updatedKnownBooks.Add(knownSeriesBook);
						}
					}
				}
			}

			// An author missing entirely from this run's reading list is more likely a failed/empty
			// request than every one of their books being un-shelved — keep the cached entries.
			foreach (GoodReadsKnownBook missing in knownBookRecords.Where(kb => !processedAuthors.Contains(kb.AuthorName)))
			{
				LogError($"[goodreads] Author '{missing.AuthorName}' missing from this run's reading list, keeping cached entry.");
				updatedKnownBooks.Add(missing);
			}

			await FileStoreService.SaveGoodReadsKnownBooksAsync(updatedKnownBooks);
		}
	}

}


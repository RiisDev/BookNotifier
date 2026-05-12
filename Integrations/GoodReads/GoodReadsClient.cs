using System.Diagnostics;
using System.Net;
using System.ServiceModel.Syndication;
using System.Xml;
using BookNotifier.Services;
using BookNotifier.Utilities;

namespace BookNotifier.Integrations.GoodReads
{
	public class GoodReadsClient : IDisposable
	{


		private readonly HttpClient _client = new(new HttpClientHandler
		{
			AllowAutoRedirect = true,
			AutomaticDecompression = DecompressionMethods.All,
			UseCookies = true
		})
		{
			DefaultRequestHeaders =
			{
				{
					"User-Agent",
					"Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:141.0) Gecko/20100101 Firefox/141.0 GoodReadsWatcher/1.0"
				}
			},
			Timeout = TimeSpan.FromSeconds(15)
		};

		public void Dispose()
		{
			GC.SuppressFinalize(this);
			_client.Dispose();
		}

		public async Task<IReadOnlyList<GoodReadsBookDetails>> GetReadingListBooksAsync(long userId, string? shelf = null) => await GetReadingListBooksAsync(userId.ToString(), shelf);

		public async Task<IReadOnlyList<GoodReadsBookDetails>> GetReadingListBooksAsync(string userId, string? shelf = null)
		{
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

			GoodReadsBookDetails[] books = await Task.WhenAll(bookUrls.Select(GetBookDetailsAsync));

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

			GoodReadsBook book = new()
			{
				Id = Guid.NewGuid(),
				Title = title,
				Url = url,
				AuthorId = author.Id,
				SeriesId = series?.Id
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
			if (details.Series is null)
			{
				return details;
			}

			string html = await GetStringWithRetryAsync(details.Series.Url);

			HtmlDocument doc = HtmlDocument.Parse(html);

			List<GoodReadsSeriesBook> books = [];

			HashSet<string> seen = [];

			Uri baseUri = new("https://www.goodreads.com");

			IEnumerable<HtmlElement> anchors = doc.All
				.Where(x => x.HasAttribute("href"))
				.Where(x => x.GetAttribute("href")!.Contains("/book/show"));

			foreach (HtmlElement anchor in anchors)
			{
				string? href = anchor.GetAttribute("href");

				if (string.IsNullOrWhiteSpace(href)) continue;
				if (!href.Contains("/book/show")) continue;

				Uri absoluteUrl = new(baseUri, href);

				string normalizedUrl = absoluteUrl.GetLeftPart(UriPartial.Path);

				if (!seen.Add(normalizedUrl)) continue;

				string title = anchor.Children.First().GetAttribute("alt") ?? "N/A";

				if (string.IsNullOrWhiteSpace(title)) continue;
				if (title.Length < 2) continue;

				books.Add(new GoodReadsSeriesBook
				{
					Title = title,
					Url = new Uri(normalizedUrl),
					Position = books.Count + 1
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

			Uri baseUri = new("https://www.goodreads.com");

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

				Uri bookUrl = new(baseUri, href);

				string normalizedUrl =
					bookUrl.GetLeftPart(UriPartial.Path);

				if (!seen.Add(normalizedUrl))
				{
					continue;
				}

				string title =
					bookAnchor.TextContent.Trim();

				if (string.IsNullOrWhiteSpace(title))
				{
					HtmlElement? image = row
						.GetElementsByTagName("img")
						.FirstOrDefault();

					title =
						image?.GetAttribute("alt")
						?? "Unknown";
				}

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
					AuthorId = author.Id
				};

				books.Add(book);
			}

			return books;
		}

		private async Task<string> GetStringWithRetryAsync(Uri url, int maxRetries = 5,
			CancellationToken cancellationToken = default) =>
			await GetStringWithRetryAsync(url.AbsoluteUri, maxRetries, cancellationToken);

		private async Task<string> GetStringWithRetryAsync(string url, int maxRetries = 5, CancellationToken cancellationToken = default)
		{
			for (int attempt = 1; ; attempt++)
			{
				try
				{
					using HttpResponseMessage response =
						await _client.GetAsync(url, cancellationToken);

					if (response.StatusCode is HttpStatusCode.ServiceUnavailable or (HttpStatusCode)429)
					{
						if (attempt >= maxRetries)
						{
							throw new HttpRequestException(
								$"Request failed with status code {(int)response.StatusCode} after {maxRetries} attempts.");
						}

						TimeSpan delay =
							TimeSpan.FromSeconds(Math.Pow(2, attempt));

						Debug.WriteLine(
							$"Retry {attempt}/{maxRetries} for {url} due to {(int)response.StatusCode}. Waiting {delay.TotalSeconds}s");

						await Task.Delay(delay, cancellationToken);

						continue;
					}

					response.EnsureSuccessStatusCode();

					return await response.Content.ReadAsStringAsync(cancellationToken);
				}
				catch (HttpRequestException) when (attempt < maxRetries)
				{
					TimeSpan delay =
						TimeSpan.FromSeconds(Math.Pow(2, attempt));

					await Task.Delay(delay, cancellationToken);
				}
				catch (TaskCanceledException) when (attempt < maxRetries)
				{
					TimeSpan delay =
						TimeSpan.FromSeconds(Math.Pow(2, attempt));

					await Task.Delay(delay, cancellationToken);
				}
			}
		}

		public async Task RunAsync(IReadOnlyList<GoodReadsBookDetails> readingListData, Dictionary<string, List<GoodReadsBook>> authorBooks)
		{
			HashSet<string> knownBooks = await FileStoreService.LoadGoodReadsKnownBooksAsync();

			bool isFirstRun = knownBooks.Count == 0;

			List<GoodReadsKnownBook> updatedKnownBooks = [];

			foreach (GoodReadsBookDetails details in readingListData)
			{
				GoodReadsAuthor author = details.Author;

				if (!authorBooks.TryGetValue(author.Name, out List<GoodReadsBook>? books))
				{
					continue;
				}


				foreach (GoodReadsBook book in books)
				{
					GoodReadsKnownBook knownBook = new()
					{
						Title = book.Title,
						AuthorName = author.Name,
						Url = book.Url.ToString(),
						SeriesName = null,
						SeriesPosition = null
					};

					string key = FileStoreService.CreateGoodReadsKey(knownBook);

					if (!knownBooks.Contains(key))
					{
						if (!isFirstRun)
							await NotificationService.SendNewGoodReadsAuthorBookAsync(author.Name, book.Title, book.Url.ToString());

						knownBooks.Add(key);
					}

					updatedKnownBooks.Add(knownBook);
				}
				
				if (details.Series is not null)
				{
					foreach (GoodReadsSeriesBook seriesBook in details.Series.Books)
					{
						GoodReadsKnownBook knownSeriesBook = new()
						{
							Title = seriesBook.Title,
							AuthorName = author.Name,
							Url = seriesBook.Url.ToString(),
							SeriesName = details.Series.Name,
							SeriesPosition = seriesBook.Position
						};

						string key = FileStoreService.CreateGoodReadsKey(knownSeriesBook);

						if (!knownBooks.Contains(key))
						{
							if (!isFirstRun)
								await NotificationService.SendNewGoodReadsSeriesBookAsync(
									author.Name,
									seriesBook.Title,
									seriesBook.Url.ToString(),
									details.Series.Name,
									seriesBook.Position.ToString()
								);

							knownBooks.Add(key);
						}

						updatedKnownBooks.Add(knownSeriesBook);
					}
				}
			}

			await FileStoreService.SaveGoodReadsKnownBooksAsync(updatedKnownBooks);
		}
	}

}


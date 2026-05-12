using GoodReadsWatcher.MainSystems;

namespace GoodReadsWatcher.Util
{
	public static class BookWatcher
	{
		public static async Task RunAsync(IReadOnlyList<BookDetails> readingListData, Dictionary<string, List<Book>> authorBooks)
		{
			HashSet<string> knownBooks = await FileStore.LoadKnownBooksAsync();

			bool isFirstRun = knownBooks.Count == 0;

			List<KnownBook> updatedKnownBooks = [];

			foreach (BookDetails details in readingListData)
			{
				Author author = details.Author;

				if (!authorBooks.TryGetValue(author.Name, out List<Book>? books))
				{
					continue;
				}

				// ----------------------------------------
				// Check all author books
				// ----------------------------------------

				foreach (Book book in books)
				{
					KnownBook knownBook = new()
					{
						Title = book.Title,
						AuthorName = author.Name,
						Url = book.Url.ToString(),
						SeriesName = null,
						SeriesPosition = null
					};

					string key = FileStore.CreateKey(knownBook);

					if (!knownBooks.Contains(key))
					{
						if (!isFirstRun)
							await NotificationService.SendNewAuthorBookAsync(author, book);

						knownBooks.Add(key);
					}

					updatedKnownBooks.Add(knownBook);
				}

				// ----------------------------------------
				// Check series books
				// ----------------------------------------

				if (details.Series is not null)
				{
					foreach (SeriesBook seriesBook in details.Series.Books)
					{
						KnownBook knownSeriesBook = new()
						{
							Title = seriesBook.Title,
							AuthorName = author.Name,
							Url = seriesBook.Url.ToString(),
							SeriesName = details.Series.Name,
							SeriesPosition = seriesBook.Position
						};

						string key =
							FileStore.CreateKey(knownSeriesBook);

						if (!knownBooks.Contains(key))
						{
							if (!isFirstRun)
								await NotificationService.SendNewSeriesBookAsync(details, seriesBook);

							knownBooks.Add(key);
						}

						updatedKnownBooks.Add(knownSeriesBook);
					}
				}
			}

			await FileStore.SaveKnownBooksAsync(updatedKnownBooks);
		}
	}
}

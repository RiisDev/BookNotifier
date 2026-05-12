using GoodReadsWatcher.MainSystems;
using System.Text.Json;

namespace GoodReadsWatcher.Util
{
	public static class FileStore
	{
		private static readonly JsonSerializerOptions JsonOptions = new()
		{
			WriteIndented = true
		};

		private static string KnownBooksFile => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "known-books.json");

		public static async Task<HashSet<string>> LoadKnownBooksAsync()
		{
			if (!File.Exists(KnownBooksFile))
			{
				return [];
			}

			string json = await File.ReadAllTextAsync(KnownBooksFile);

			List<KnownBook>? books = JsonSerializer.Deserialize<List<KnownBook>>(json);

			if (books is null)
			{
				return [];
			}

			HashSet<string> keys = books
				.Select(CreateKey)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			return keys;
		}

		public static async Task SaveKnownBooksAsync(IEnumerable<KnownBook> books)
		{
			List<KnownBook> distinctBooks = books
				.GroupBy(CreateKey)
				.Select(group => group.First())
				.OrderBy(x => x.AuthorName)
				.ThenBy(x => x.Title)
				.ToList();

			string json = JsonSerializer.Serialize(distinctBooks, JsonOptions);

			await File.WriteAllTextAsync(KnownBooksFile, json);
		}

		public static string CreateKey(KnownBook book)
		{
			return $"{book.AuthorName}|{book.Title}";
		}
	}

}

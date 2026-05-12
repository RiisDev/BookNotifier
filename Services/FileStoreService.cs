using System.Text.Encodings.Web;
using System.Text.Json;
using BookNotifier.Integrations.GoodReads;
using BookNotifier.Integrations.Literotica;
using BookNotifier.Integrations.ScribbleHub;

namespace BookNotifier.Services
{
	public static class FileStoreService
	{
		private static readonly JsonSerializerOptions JsonOptions = new()
		{
			AllowTrailingCommas = true,
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
			WriteIndented = true,
			IndentCharacter = '\t',
			IndentSize = 1
		};

		private static string DataDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");

		private static string FilePath(string filename) => Path.Combine(DataDir, filename);

		private static async Task<T> ReadAsync<T>(string filename, T fallback) where T : class
		{
			string path = FilePath(filename);

			if (!File.Exists(path))
				return fallback;

			try
			{
				string json = await File.ReadAllTextAsync(path);
				return JsonSerializer.Deserialize<T>(json) ?? fallback;
			}
			catch
			{
				return fallback;
			}
		}

		private static async Task WriteAsync<T>(string filename, T data)
		{
			Directory.CreateDirectory(DataDir);
			await File.WriteAllTextAsync(FilePath(filename), JsonSerializer.Serialize(data, JsonOptions));
		}

		public static async Task<HashSet<string>> LoadGoodReadsKnownBooksAsync()
		{
			List<GoodReadsKnownBook> books = await ReadAsync<List<GoodReadsKnownBook>>("goodreads.json", []);

			return books
				.Select(CreateGoodReadsKey)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
		}

		public static Task SaveGoodReadsKnownBooksAsync(IEnumerable<GoodReadsKnownBook> books)
		{
			List<GoodReadsKnownBook> distinct = books
				.GroupBy(CreateGoodReadsKey)
				.Select(g => g.First())
				.OrderBy(x => x.AuthorName)
				.ThenBy(x => x.Title)
				.ToList();

			return WriteAsync("goodreads.json", distinct);
		}

		public static string CreateGoodReadsKey(GoodReadsKnownBook book) => $"{book.AuthorName}|{book.Title}";

		public static Task<List<ScribbleSaveBookRoot>> LoadScribbleHubAsync() => ReadAsync<List<ScribbleSaveBookRoot>>("scribblehub.json", []);

		public static Task SaveScribbleHubAsync(IEnumerable<ScribbleReadingListStory> stories)
		{
			List<ScribbleSaveBookRoot> toSave = stories
				.Select(s => new ScribbleSaveBookRoot(
					s.Name,
					s.Link,
					s.Id,
					s.Chapters
						.Select(c => new ScribbleSaveChapter(c.Title, c.Link, c.Id))
						.ToList()
				))
				.ToList();

			return WriteAsync("scribblehub.json", toSave);
		}

		public static Task<LiteroticaKnownData> LoadLiteroticaAsync() => ReadAsync("literotica.json", new LiteroticaKnownData { Authors = [], Stories = [] });

		public static Task SaveLiteroticaAsync(LiteroticaKnownData data) => WriteAsync("literotica.json", data);

		public static string CreateLiteroticaStoryKey(string? id, string? title) => $"{id}||{title}";
	}
}
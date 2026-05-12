using GoodReadsWatcher.MainSystems;
using GoodReadsWatcher.Util;

namespace GoodReadsWatcher
{
	internal class Program
	{
		public static async Task Main()
		{
			Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data"));

			_ = new EnvService();

			IReadOnlyList<BookDetails> readingListData =
				await GoodReadsSdk.GetReadingListBooksAsync(
					Environment.GetEnvironmentVariable("USER_ID")
					?? throw new InvalidOperationException(
						"Missing USER_ID environment variable."),

					Environment.GetEnvironmentVariable("SHELF_TAG")
					?? throw new InvalidOperationException(
						"Missing SHELF_TAG environment variable.")
				);

			Dictionary<string, List<Book>> authorBooks = [];

			foreach (Author author in readingListData.Select(x => x.Author).DistinctBy(x => x.Id))
			{
				List<Book> books = await GoodReadsSdk.GetAuthorsBooks(author.Url);
				authorBooks[author.Name] = books;
			}

			await BookWatcher.RunAsync(
				readingListData,
				authorBooks
			);

			Console.WriteLine("Finished checking for new books.");
		}

		internal class EnvService
		{
			public IReadOnlyDictionary<string, string> Variables { get; private set; }

			internal EnvService()
			{
				Variables = new Dictionary<string, string>();
				Dictionary<string, string> vars = [];
				string envPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");
				if (!File.Exists(envPath))
				{
					Variables = vars;
					return;
				}

				foreach (string line in File.ReadAllLines(envPath))
				{
					if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
					string[] parts = line.Split('=', 2);
					if (parts.Length != 2) continue;
					string key = parts[0].Trim();
					if (string.IsNullOrEmpty(key)) continue;
					string value = parts[1].Trim().Trim('"').Trim('\'');
					vars[key] = value;
					if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
						Environment.SetEnvironmentVariable(key, value);
				}

				Variables = vars;
			}
		}
	}
}

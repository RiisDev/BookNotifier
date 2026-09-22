using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BookNotifier.Services;
using LiteroticaApi.Api;
using LiteroticaApi.AuthClientData;
using LiteroticaApi.DataObjects;

namespace BookNotifier.Integrations.Literotica
{
	internal class LiteroticaClient : IDisposable
	{
		public static string ComputeMd5Hash(string input)
		{
			byte[] inputBytes = Encoding.UTF8.GetBytes(input);
			byte[] hashBytes = MD5.HashData(inputBytes);
			StringBuilder sb = new();
			foreach (byte b in hashBytes) sb.Append(b.ToString("x2"));
			return sb.ToString();
		}

		private readonly AuthClient _authClient = new();
		private readonly string _username;
		private readonly string _password;

		public LiteroticaClient(string username, string password)
		{
			_username = username;
			_password = password;

			string? versionData = typeof(Program).Assembly
				.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
				?.InformationalVersion;

			Log($"[literotica] LiteroticaSdk Version: {ComputeMd5Hash(versionData ?? "")}");
		}

		public void Dispose()
		{
			GC.SuppressFinalize(this);
			(_authClient as IDisposable)?.Dispose();
		}

		// Called once per cycle by RunLoopAsync in Program.cs
		public async Task RunAsync()
		{
			Log($"[literotica] Logging into {_username}...");
			bool loggedIn = await _authClient.LoginAsync(_username, _password);

			if (!loggedIn)
				throw new InvalidOperationException("Failed to log in to Literotica.");

			await RunWatcher();
		}

		private async Task RunWatcher()
		{
			Log("[literotica] Running watcher...");

			IReadOnlyList<Author> favoriteAuthors = await GetAllFavoriteAuthorsAsync();
			if (favoriteAuthors.Count <= 0)
			{
				Log("[literotica] Failed to retrieve favourite authors, skipping watcher run.");
				return;
			}

			HashSet<string> currentAuthorUsernames = favoriteAuthors
				.Select(static a => a.Username.ToString())
				.Where(static u => !string.IsNullOrWhiteSpace(u))
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			LiteroticaKnownData known = await FileStoreService.LoadLiteroticaAsync();
			bool isFirstRun = known.Works.Count == 0 && known.Authors.Count == 0;
			Dictionary<string, LiteroticaKnownWork> knownWorksByKey = known.Works
				.ToDictionary(static w => WorkKey(w.Author, w.Title), StringComparer.OrdinalIgnoreCase);

			foreach (string newAuthorUsername in currentAuthorUsernames.Except(known.Authors, StringComparer.OrdinalIgnoreCase))
			{
				await Task.Delay(Random.Shared.Next(1000, 3001));

				Author? author = await AuthorsApi.GetAuthorByUsernameAsync(newAuthorUsername);
				if (author is null)
				{
					Log($"[literotica] Failed to retrieve author data for {newAuthorUsername}, skipping.");
					continue;
				}

				if (isFirstRun) continue;

				Log($"[literotica] New author favourited: {newAuthorUsername}");
				await NotificationService.SendNewLitAuthorAsync(
					newAuthorUsername,
					$"https://www.literotica.com/authors/{newAuthorUsername}",
					author.StoriesCount ?? 0);
			}

			List<LiteroticaKnownWork> updatedWorks = [];
			HashSet<string> touchedWorkKeys = [];

			foreach (string authorUsername in currentAuthorUsernames)
			{
				await Task.Delay(Random.Shared.Next(1000, 3001));

				IReadOnlyList<StoryDatum> works;
				try
				{
					works = await AuthorsApi.GetAllWorksAsync(authorUsername);
				}
				catch (Exception ex)
				{
					LogError($"[literotica] Failed to retrieve works for {authorUsername}, skipping. {ex.Message}");
					continue;
				}

				foreach (StoryDatum work in works)
				{
					string workKey = WorkKey(authorUsername, work.Title);
					touchedWorkKeys.Add(workKey);

					knownWorksByKey.TryGetValue(workKey, out LiteroticaKnownWork? existingWork);
					Dictionary<string, LiteroticaKnownChapter> existingChaptersByUrl = existingWork?.Chapters
						.ToDictionary(static c => c.Url, StringComparer.OrdinalIgnoreCase)
						?? [];

					// A series has one chapter per part; a standalone story is a single chapter of itself.
					IEnumerable<(string Title, long? Id, string Url, string DateApprove)> parts = work.Parts is { Count: > 0 }
						? work.Parts.Select(static p => (p.Title, p.Id, p.Url, p.DateApprove))
						: [(work.Title, work.Id, work.Url, work.DateApprove)];

					List<LiteroticaKnownChapter> chapters = [];
					foreach ((string title, long? id, string url, string dateApprove) in parts)
					{
						string chapterUrl = $"https://www.literotica.com/s/{(id is null ? url : id)}";

						if (existingChaptersByUrl.TryGetValue(chapterUrl, out LiteroticaKnownChapter? existingChapter))
						{
							chapters.Add(existingChapter);
							continue;
						}

						DateTime publishedAt = DateTime.TryParseExact(dateApprove, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed)
							? parsed
							: DateTime.UtcNow;

						chapters.Add(new LiteroticaKnownChapter(title, chapterUrl, publishedAt));

						if (isFirstRun) continue;

						Log($"[literotica] New story found: {title} by {authorUsername}");
						await NotificationService.SendNewLitStoryAsync(authorUsername, title, chapterUrl);
					}

					updatedWorks.Add(new LiteroticaKnownWork(work.Title, work.Url, authorUsername, chapters));
				}
			}

			// Carry over works for authors that failed to fetch or were unfollowed this cycle.
			updatedWorks.AddRange(known.Works.Where(w => !touchedWorkKeys.Contains(WorkKey(w.Author, w.Title))));

			// "hiatus" per author = no chapter published in the last month.
			DateTime staleThreshold = DateTime.UtcNow.AddDays(-30);
			Dictionary<string, DateTime> latestByAuthor = updatedWorks
				.GroupBy(static w => w.Author, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(static g => g.Key, static g => g.Max(w => w.Chapters.Max(c => c.PublishedAt)), StringComparer.OrdinalIgnoreCase);

			updatedWorks = updatedWorks
				.Select(w => w with
				{
					Status = latestByAuthor.TryGetValue(w.Author, out DateTime latest) && latest >= staleThreshold
						? "ongoing"
						: "hiatus"
				})
				.ToList();

			await FileStoreService.SaveLiteroticaAsync(new LiteroticaKnownData
			{
				Authors = currentAuthorUsernames,
				Works = updatedWorks
			});

			Log("[literotica] Successfully ran watcher!");
		}

		private static string WorkKey(string author, string title) => $"{author}|{title}";

		private async Task<IReadOnlyList<Author>> GetAllFavoriteAuthorsAsync()
		{
			List<Author> authors = [];

			FavouriteAuthor? page = await UsersApi.GetFavoriteAuthorsAsync(_username, 1, 200);
			if (page?.Data is null) return authors;

			authors.AddRange(page.Data);

			for (int pageNumber = 2; pageNumber <= (page.LastPage ?? 1); pageNumber++)
			{
				FavouriteAuthor? nextPage = await UsersApi.GetFavoriteAuthorsAsync(_username, pageNumber, 200);
				if (nextPage?.Data != null) authors.AddRange(nextPage.Data);
			}

			return authors;
		}
	}
}
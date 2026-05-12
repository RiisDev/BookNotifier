using System.Net;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using LiteroticaApi.Api;
using LiteroticaApi.AuthClientData;
using LiteroticaApi.AuthClientData.DataObjects;
using LiteroticaApi.DataObjects;
using Activity = LiteroticaApi.AuthClientData.DataObjects.Activity;

namespace LiteroticaWatcher
{
    internal class LitParser
    {
        public static string ComputeMd5Hash(string input)
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
            using MD5 md5 = MD5.Create();
            byte[] hashBytes = md5.ComputeHash(inputBytes);
            StringBuilder sb = new();
            foreach (byte b in hashBytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static readonly Lock LogLock = new();

        public static void Log(string message, [CallerMemberName] string caller = "")
        {
            lock (LogLock)
            {
                string data = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [{caller}] {message}";
                Console.WriteLine(data);
                File.AppendAllText("data/log.txt", data + "\n");
            }
        }

        public static readonly JsonSerializerOptions Serializer = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            IndentSize = 1,
            IndentCharacter = '\t',
            WriteIndented = true
        };

        private static readonly HttpClient Client = new(new HttpClientHandler
        {
	        AllowAutoRedirect = true,
	        AutomaticDecompression = DecompressionMethods.All,
	        UseCookies = true
        })
        {
	        DefaultRequestHeaders = { { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:141.0) Gecko/20100101 Firefox/141.0 RiisDevLiteroticaWatcher/1" } },
	        Timeout = TimeSpan.FromSeconds(15),
	        BaseAddress = new Uri("https://www.literotica.com", UriKind.Absolute)
        };

		private static readonly AuthClient AuthClient = new();
		public string Username;
		public string Password;

		public LitParser(string username, string password, long recheckMs)
        {
            Username = username;
            Password = password;

			string? versionData = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            Console.WriteLine($"Version: {ComputeMd5Hash(versionData ?? "")}");
			
            Directory.CreateDirectory("data");

            if (!File.Exists("data/books.txt"))
                 File.Create("data/books.txt").Close();
            if (!File.Exists("data/authors.txt"))
                File.Create("data/authors.txt").Close();

			_ = Task.Run(async () =>
			{
				Log("Logging in...");
				bool loggedIn = await AuthClient.LoginAsync(username, password);

                if (!loggedIn)
                {
                    Log("Failed to log in, exiting.");
                    return;
				}
                
                try { await RunWatcher(); }
                catch (Exception ex) { Log($"Failed to run watcher: {ex}"); }

				using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(recheckMs));
                
                while (await timer.WaitForNextTickAsync())
                {
                    try { await RunWatcher(); }
                    catch (Exception ex) { Log($"Failed to run watcher: {ex}"); }
                }
            });
        }

        
       
        private async Task RunWatcher()
        {
            Log("Running watcher...");

            Activity? activityData = await AuthClient.Activity.GetFollowedAuthorsActivityAsync(50);
            if (activityData is null || activityData.Data?.Count <= 0)
            {
                Log("Failed to retrieve activity data, skipping watcher run.");
                await AuthClient.LoginAsync(Username, Password);
                return;
			}
            IEnumerable<ActivityData>? storyActivityData = activityData.Data?
	            .Where(x => x.Action == "published-story")
	            .Take(5);
			
			Activity? myActivity = await AuthClient.Activity.GetLocalActivityAsync(50);
            if (myActivity is null || myActivity.Data?.Count <= 0)
			{
                Log("Failed to retrieve my activity data, skipping watcher run.");
                await AuthClient.LoginAsync(Username, Password);
				return;
			}
			IEnumerable<ActivityData>? followedData = myActivity.Data?
				.Where(x => x.Action.Contains("favorited-author"))
				.Take(5);
			
            if (storyActivityData is null || followedData is null) { await AuthClient.LoginAsync(Username, Password); Log("No new activity found, skipping watcher run."); return; }

            string[] oldAuthors = (await File.ReadAllLinesAsync("data/authors.txt")).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
			string[] oldStories = (await File.ReadAllLinesAsync("data/books.txt")).Where(x=>!string.IsNullOrWhiteSpace(x)).ToArray();

            IEnumerable<ActivityData> storyActivities = storyActivityData as ActivityData[] ?? storyActivityData.ToArray();
            foreach (ActivityData storyActivity in storyActivities)
            {
                string storyLine = $"{storyActivity.What.Story?.Id}||{storyActivity.What.Story?.Title}";
                if (oldStories.Contains(storyLine)) continue;

                Log($"New story found: {storyActivity.What.Story?.Title} by {storyActivity.Who.Username}");

                Author? author = await AuthorsApi.GetAuthorByUsernameAsync(storyActivity.Who.Username);
                if (author is null) { Log($"Failed to retrieve author data for {storyActivity.Who.Username}, skipping story notification."); continue; }

				await SendStoryUpdate(author, storyActivity.What.Story!);
			}

            IEnumerable<ActivityData> followedActivities = followedData as ActivityData[] ?? followedData.ToArray();
            foreach (ActivityData followedActivity in followedActivities)
            {
	            if (followedActivity.Whom is null) { Log($"Whom is null for {followedActivity.When}"); continue; }
                if (oldAuthors.Contains(followedActivity.Whom.Username)) continue;

                Log($"New author followed: {followedActivity.Whom.Username}");
                Author? author = await AuthorsApi.GetAuthorByUsernameAsync(followedActivity.Whom.Username);
                if (author is null) { Log($"Failed to retrieve author data for {followedActivity.Whom.Username}, skipping author notification."); continue; }
                await SendNewAuthor(author);
			}

			await File.WriteAllLinesAsync("data/authors.txt", followedActivities.Select(x=> x.Whom?.Username)!);
            await File.WriteAllLinesAsync("data/books.txt", storyActivities.Select(x => $"{x.What.Story?.Id}||{x.What.Story?.Title}"));

			Log("Successfully ran watcher!");
        }
		
        private async Task SendStoryUpdate(Author author, Story story) => await SendDiscordWebhookAsync(author, story);

        private async Task SendNewAuthor(Author author) => await SendDiscordWebhookAsync(author);

        private async Task SendDiscordWebhookAsync(Author author, Story? story = null)
        {
            string? webhook = Environment.GetEnvironmentVariable("WEBHOOK");
            if (string.IsNullOrEmpty(webhook))
            {
                Log("No webhook configured, skipping notification.");
                return;
            }

            int colorCode = 5814783;
            string message = "";
            string titleData = "";

            if (story is not null)
            {
	            colorCode = 1044502;
	            titleData = "New Story Published!";
	            message = $"**{story.Title}** by *{author.Username}*\n\n(https://wwww.literotica.com/s/{(story.Id is null ? story.Url : story.Id)})";
			}
            else if (story is null)
            {
                colorCode = 15762959;
                titleData = "New author added to watch!";
                message = $"**{author.Username}** with __**{author.StoriesAndSeriesCount}**__ stories\n\n(https://www.literotica.com/authors/{author.Username})";
            }

            Log($"{titleData} | {message}");

            var payload = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = titleData,
                        description = message,
                        color = colorCode,
                        thumbnail = new { url = "https://www.google.com/s2/favicons?domain=literotica.com&sz=48" }
                    }
                },
                username = "Book Notifier",
                avatar_url = "https://www.google.com/s2/favicons?domain=literotica.com&sz=48"
			};

            StringContent jsonContent = new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            HttpResponseMessage response = await Client.PostAsync(webhook, jsonContent);

            Log(response.IsSuccessStatusCode
                ? $"Successfully sent webhook for {author.Username} - {titleData}"
                : $"Failed to send webhook for {author.Username} - {titleData}: {response.StatusCode}");
            Log(await response.Content.ReadAsStringAsync());
        }
    }
}

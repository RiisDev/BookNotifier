using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BookNotifier.Services;
using LiteroticaApi.Api;
using LiteroticaApi.AuthClientData;
using LiteroticaApi.AuthClientData.DataObjects;
using LiteroticaApi.DataObjects;
using Activity = LiteroticaApi.AuthClientData.DataObjects.Activity;

namespace BookNotifier.Integrations.Literotica
{
	internal class LiteroticaClient
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

			Log($"LiteroticaSdk Version: {ComputeMd5Hash(versionData ?? "")}");
		}

		// Called once per cycle by RunLoopAsync in Program.cs
		public async Task RunAsync()
		{
			Log("Logging in...");
			bool loggedIn = await _authClient.LoginAsync(_username, _password);

			if (!loggedIn)
				throw new InvalidOperationException("Failed to log in to Literotica.");

			await RunWatcher();
		}

		private async Task RunWatcher()
		{
			Log("Running watcher...");

			Activity? activityData = await _authClient.Activity.GetFollowedAuthorsActivityAsync(50);
			if (activityData is null || activityData.Data.Count <= 0)
			{
				Log("Failed to retrieve activity data, skipping watcher run.");
				return;
			}

			ActivityData[] storyActivities = activityData.Data
				.Where(x => x.Action == "published-story")
				.Take(5)
				.ToArray();

			Activity? myActivity = await _authClient.Activity.GetLocalActivityAsync(50);
			if (myActivity is null || myActivity.Data.Count <= 0)
			{
				Log("Failed to retrieve my activity data, skipping watcher run.");
				return;
			}

			ActivityData[] followedActivities = myActivity.Data
				.Where(x => x.Action.Contains("favorited-author"))
				.Take(5)
				.ToArray();

			LiteroticaKnownData known = await FileStoreService.LoadLiteroticaAsync();

			foreach (ActivityData storyActivity in storyActivities)
			{
				string storyKey = FileStoreService.CreateLiteroticaStoryKey(
					storyActivity.What.Story?.Id.ToString(),
					storyActivity.What.Story?.Title);

				if (known.Stories.Contains(storyKey)) continue;

				Log($"New story found: {storyActivity.What.Story?.Title} by {storyActivity.Who.Username}");

				Author? author = await AuthorsApi.GetAuthorByUsernameAsync(storyActivity.Who.Username);
				if (author is null)
				{
					Log($"Failed to retrieve author data for {storyActivity.Who.Username}, skipping.");
					continue;
				}

				if (storyActivity.What.Story is null)
				{
					LogError("Story is null after null check, skipping.");
					continue;
				}

				await NotificationService.SendNewLitStoryAsync(author.Username,
					storyActivity.What.Story.Title,
					$"https://wwww.literotica.com/s/{(storyActivity.What.Story.Id is null ? storyActivity.What.Story.Url : storyActivity.What.Story.Id)}");
			}

			foreach (ActivityData followedActivity in followedActivities)
			{
				if (followedActivity.Whom is null)
				{
					Log($"Whom is null for activity at {followedActivity.When}, skipping.");
					continue;
				}

				if (known.Authors.Contains(followedActivity.Whom.Username)) continue;

				Log($"New author followed: {followedActivity.Whom.Username}");

				Author? author = await AuthorsApi.GetAuthorByUsernameAsync(followedActivity.Whom.Username);
				if (author is null)
				{
					Log($"Failed to retrieve author data for {followedActivity.Whom.Username}, skipping.");
					continue;
				}

				await NotificationService.SendNewLitAuthorAsync(
					author.Username,
					$"https://www.literotica.com/authors/{author.Username}",
					author.StoriesCount ?? 0);
			}

			await FileStoreService.SaveLiteroticaAsync(new LiteroticaKnownData
			{
				Authors = followedActivities
					.Select(x => x.Whom?.Username)
					.Where(x => !string.IsNullOrWhiteSpace(x))
					.ToHashSet()!,

				Stories = storyActivities
					.Select(x => FileStoreService.CreateLiteroticaStoryKey(
						x.What.Story?.Id.ToString(),
						x.What.Story?.Title))
					.ToHashSet()
			});

			Log("Successfully ran watcher!");
		}
	}
}
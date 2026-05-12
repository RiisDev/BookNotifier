using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BookNotifier.Services
{
	public enum NotificationEvent
	{
		// GoodReads
		NewGoodReadsAuthorBook,
		NewGoodReadsSeriesBook,

		// ScribbleHub
		NewScribbleStory,
		NewScribbleChapter,

		// Literotica
		NewLitStory,
		NewLitAuthor,
	}

	public record NotificationPayload
	{
		public required NotificationEvent Event { get; init; }
		public required string Title { get; init; }
		public required string Author { get; init; }
		public required string Url { get; init; }

		public string? SeriesName { get; init; }
		public string? SeriesPosition { get; init; }
		public string? ChapterTitle { get; init; }
		public string? ChapterUrl { get; init; }

		public DateTimeOffset DetectedAtUtc { get; init; } = DateTimeOffset.UtcNow;
	}

	public static class NotificationService
	{
		private static readonly HttpClient Client = new()
		{
			DefaultRequestHeaders =
			{
				Accept = { new MediaTypeWithQualityHeaderValue("application/json") }
			}
		};


		// GoodReads
		public static Task SendNewGoodReadsAuthorBookAsync(string author, string title, string url) =>
			SendAsync(new NotificationPayload
			{
				Event = NotificationEvent.NewGoodReadsAuthorBook,
				Title = title,
				Author = author,
				Url = url
			});

		public static Task SendNewGoodReadsSeriesBookAsync(string author, string title, string url, string seriesName, string seriesPosition) =>
			SendAsync(new NotificationPayload
			{
				Event = NotificationEvent.NewGoodReadsSeriesBook,
				Title = title,
				Author = author,
				Url = url,
				SeriesName = seriesName,
				SeriesPosition = seriesPosition
			});

		// ScribbleHub
		public static Task SendNewScribbleStoryAsync(string name, string url) =>
			SendAsync(new NotificationPayload
			{
				Event = NotificationEvent.NewScribbleStory,
				Title = name,
				Author = string.Empty,
				Url = url
			});

		public static Task SendNewScribbleChapterAsync(string storyName, string storyUrl, string chapterTitle, string chapterUrl) =>
			SendAsync(new NotificationPayload
			{
				Event = NotificationEvent.NewScribbleChapter,
				Title = storyName,
				Author = string.Empty,
				Url = storyUrl,
				ChapterTitle = chapterTitle,
				ChapterUrl = chapterUrl
			});

		// Literotica
		public static Task SendNewLitStoryAsync(string authorUsername, string storyTitle, string storyUrl) =>
			SendAsync(new NotificationPayload
			{
				Event = NotificationEvent.NewLitStory,
				Title = storyTitle,
				Author = authorUsername,
				Url = storyUrl
			});

		public static Task SendNewLitAuthorAsync(string authorUsername, string authorUrl, int storyCount) =>
			SendAsync(new NotificationPayload
			{
				Event = NotificationEvent.NewLitAuthor,
				Title = $"{storyCount} stories",
				Author = authorUsername,
				Url = authorUrl
			});


		private static async Task SendAsync(NotificationPayload payload)
		{
			string? webhook = Environment.GetEnvironmentVariable("WEBHOOK");

			if (string.IsNullOrWhiteSpace(webhook))
			{
				Log("[notification] No webhook configured, skipping.");
				return;
			}

			(int color, string embedTitle, string description) = BuildEmbed(payload);
			(string avatarUrl, string botUsername) = GetPlatformMeta(payload.Event);

			object discordPayload = new
			{
				username = botUsername,
				avatar_url = avatarUrl,
				embeds = new[]
				{
					new
					{
						title       = embedTitle,
						description,
						color,
						timestamp   = payload.DetectedAtUtc,
						thumbnail   = new { url = avatarUrl }
					}
				}
			};

			StringContent content = new(
				JsonSerializer.Serialize(discordPayload),
				Encoding.UTF8,
				"application/json");

			HttpResponseMessage response = await Client.PostAsync(webhook, content);

			Log(response.IsSuccessStatusCode
				? $"[notification] Sent: {payload.Event} | {payload.Title}"
				: $"[notification] Failed: {payload.Event} | {payload.Title} | {response.StatusCode}");
		}

		// ----------------------------------------
		// Embed builder
		// ----------------------------------------

		private static (int Color, string EmbedTitle, string Description) BuildEmbed(NotificationPayload payload) =>
			payload.Event switch
			{
				NotificationEvent.NewGoodReadsAuthorBook => (
					15762959,
					"New Author Release!",
					$"""
					**{payload.Title}**
					by *{payload.Author}*

					({payload.Url})
					"""
				),

				NotificationEvent.NewGoodReadsSeriesBook => (
					5814783,
					"New Series Book Released!",
					$"""
					**{payload.Title}**
					by *{payload.Author}*

					Series: {payload.SeriesName}
					Position: #{payload.SeriesPosition}

					({payload.Url})
					"""
				),

				NotificationEvent.NewScribbleStory => (
					1044502,
					"New Story Detected!",
					$"""
					**{payload.Title}**

					({payload.Url})
					"""
				),

				NotificationEvent.NewScribbleChapter => (
					15762959,
					"New Chapter Published!",
					$"""
					**{payload.Title}** has a new chapter!

					**{payload.ChapterTitle}**

					({payload.ChapterUrl})
					"""
				),

				NotificationEvent.NewLitStory => (
					1044502,
					"New Story Published!",
					$"""
					**{payload.Title}**
					by *{payload.Author}*

					({payload.Url})
					"""
				),

				NotificationEvent.NewLitAuthor => (
					15762959,
					"New Author Added to Watch!",
					$"""
					**{payload.Author}** with __{payload.Title}__

					({payload.Url})
					"""
				),

				_ => (0, "Book Notification", $"**{payload.Title}** by *{payload.Author}*\n\n({payload.Url})")
			};

		// ----------------------------------------
		// Platform metadata
		// ----------------------------------------

		private static (string AvatarUrl, string BotUsername) GetPlatformMeta(NotificationEvent @event) =>
			@event switch
			{
				NotificationEvent.NewGoodReadsAuthorBook or
				NotificationEvent.NewGoodReadsSeriesBook =>
					(
						"https://www.google.com/s2/favicons?domain=goodreads.com&sz=48",
						"GoodReads - Book Notifier"
					),

				NotificationEvent.NewScribbleStory or
				NotificationEvent.NewScribbleChapter =>
					(
						"https://api.irisapp.ca/images/scribbble.png",
						"ScribbleHub - Book Notifier"
					),

				NotificationEvent.NewLitStory or
				NotificationEvent.NewLitAuthor =>
					(
						"https://www.google.com/s2/favicons?domain=literotica.com&sz=48",
						"Literotica - Book Notifier"
					),

				_ => ("", "Book Notifier")
			};
	}
}
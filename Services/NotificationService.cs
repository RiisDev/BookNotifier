using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BookNotifier.Services
{
	public enum NotificationEvent
	{
		FlareSolverError,

		// RoyalRoad
		NewRoyalRoadFiction,
		NewRoyalRoadChapter,

		// GoodReads
		NewGoodReadsAuthor,
		NewGoodReadsSeries,
		NewGoodReadsAuthorBook,
		NewGoodReadsSeriesBook,

		// ScribbleHub
		NewScribbleStory,
		NewScribbleChapter,

		// Literotica
		NewLitStory,
		NewLitAuthor,

		// Ao3
		NewAo3Story,
		NewAo3Chapter,
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

		public static Task SendFlareSolverError(string message) =>
			SendAsync(new NotificationPayload
			{
				Event = NotificationEvent.FlareSolverError,
				Title = "FlareSolver Encountered an Error",
				Author = $"Error: {message}",
				Url = ""
			});

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

		public static Task SendNewGoodReadsAuthorAddedAsync(string author, string url) =>
			SendAsync(new NotificationPayload
			{
				Event = NotificationEvent.NewGoodReadsAuthor,
				Title = "",
				Author = author,
				Url = url
			});

		public static Task SendNewGoodReadsSeriesDetectedAsync(string author, string seriesName, string url) =>
			SendAsync(new NotificationPayload
			{
				Event = NotificationEvent.NewGoodReadsSeries,
				Title = "",
				Author = author,
				Url = url,
				SeriesName = seriesName
			});

		// RoyalRoad
		public static Task SendNewRoyalRoadFictionAsync(string title, string url) =>
			SendAsync(new NotificationPayload
			{
				Event = NotificationEvent.NewRoyalRoadFiction,
				Title = title,
				Author = string.Empty,
				Url = url
			});

		public static Task SendNewRoyalRoadChapterAsync(string fictionTitle, string fictionUrl, string chapterTitle, string chapterUrl) =>
			SendAsync(new NotificationPayload
			{
				Event = NotificationEvent.NewRoyalRoadChapter,
				Title = fictionTitle,
				Author = string.Empty,
				Url = fictionUrl,
				ChapterTitle = chapterTitle,
				ChapterUrl = chapterUrl
			});


		// Ao3
		public static Task SendNewAo3FictionAsync(string title, string url) =>
			SendAsync(new NotificationPayload
			{
				Event = NotificationEvent.NewAo3Story,
				Title = title,
				Author = string.Empty,
				Url = url
			});

		public static Task SendNewAo3ChapterAsync(string fictionTitle, string fictionUrl, string chapterTitle, string chapterUrl) =>
			SendAsync(new NotificationPayload
			{
				Event = NotificationEvent.NewAo3Chapter,
				Title = fictionTitle,
				Author = string.Empty,
				Url = fictionUrl,
				ChapterTitle = chapterTitle,
				ChapterUrl = chapterUrl
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

		public static Task SendNewLitAuthorAsync(string authorUsername, string authorUrl, long storyCount) =>
			SendAsync(new NotificationPayload
			{
				Event = NotificationEvent.NewLitAuthor,
				Title = $"{storyCount} stories",
				Author = authorUsername,
				Url = authorUrl
			});


		private static async Task SendAsync(NotificationPayload payload)
		{
			if (Program.IgnorePost) return;

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
						title       = embedTitle.Trim(),
						description = description.Trim(),
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
				NotificationEvent.FlareSolverError => (
					123,
					"FlareSolver Error",
					$"""
					 **{payload.Title}**
					 ``{payload.Author}``
					 """
				),

				NotificationEvent.NewGoodReadsAuthor => (
					15762959,
					"New Author Found!",
					$"""
					 *{payload.Author}*

					 ({payload.Url})
					 """
				),

				NotificationEvent.NewGoodReadsSeries => (
					5814783,
					"New Series Found!",
					$"""
					 **{payload.Title}**
					 by *{payload.Author}*

					 Series: {payload.SeriesName}

					 ({payload.Url})
					 """
				),

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

				NotificationEvent.NewRoyalRoadFiction => (
					1752220,
					"New Fiction Added to Favourites!",
					$"""
					 **{payload.Title}**

					 ({payload.Url})
					 """
				),

				NotificationEvent.NewRoyalRoadChapter => (
					16750848,
					"New Chapter Published!",
					$"""
					 **{payload.Title}** has a new chapter!

					 **{payload.ChapterTitle}**

					 ({payload.ChapterUrl})
					 """
				),


				NotificationEvent.NewAo3Story => (
					1752220,
					"New work detected in bookmarks!!",
					$"""
					 **{payload.Title}**

					 ({payload.Url})
					 """
				),

				NotificationEvent.NewAo3Chapter => (
					16750848,
					"New Chapter Published!",
					$"""
					 **{payload.Title}** has a new chapter!

					 **{payload.ChapterTitle}**

					 ({payload.ChapterUrl})
					 """
				),


				_ => (0, "Book Notification", $"**{payload.Title}** by *{payload.Author}*\n\n({payload.Url})")
			};


		private static (string AvatarUrl, string BotUsername) GetPlatformMeta(NotificationEvent @event) =>
			@event switch
			{
				NotificationEvent.FlareSolverError => ("https://www.google.com/s2/favicons?domain=flaresolverr.com&sz=48", "FlareSolver"),

				NotificationEvent.NewAo3Chapter or
					NotificationEvent.NewAo3Story =>
					(
						"https://api.irisapp.ca/images/ao3-logo.png",
						"Ao3 - Book Notifier"
					),


				NotificationEvent.NewRoyalRoadFiction or
					NotificationEvent.NewRoyalRoadChapter =>
					(
						"https://www.google.com/s2/favicons?domain=royalroad.com&sz=48",
						"Royal Road - Book Notifier"
					),


				NotificationEvent.NewGoodReadsAuthorBook or
				NotificationEvent.NewGoodReadsAuthor or
				NotificationEvent.NewGoodReadsSeries or
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
using System.Text;
using System.Text.Json;

namespace GoodReadsWatcher.MainSystems
{
	public static class NotificationService
	{
		private static readonly HttpClient Client = new();

		public static async Task SendNewSeriesBookAsync(BookDetails details, SeriesBook seriesBook)
		{
			NotificationPayload payload = new()
			{
				Type = "NEW_SERIES_BOOK",
				Title = seriesBook.Title,
				Author = details.Author.Name,
				Url = seriesBook.Url.ToString(),
				Series = details.Series?.Name,
				Position = seriesBook.Position,
				DetectedAtUtc = DateTimeOffset.UtcNow
			};

			await SendPostRequestAsync(payload);
		}

		public static async Task SendNewAuthorBookAsync(Author author, Book book)
		{
			NotificationPayload payload = new()
			{
				Type = "NEW_AUTHOR_BOOK",
				Title = book.Title,
				Author = author.Name,
				Url = book.Url.ToString(),
				Series = null,
				Position = null,
				DetectedAtUtc = DateTimeOffset.UtcNow
			};

			await SendPostRequestAsync(payload);
		}

		private static async Task SendPostRequestAsync(
			NotificationPayload payload)
		{
			string? webhook = Environment.GetEnvironmentVariable("WEBHOOK");

			if (string.IsNullOrWhiteSpace(webhook))
			{
				Log("No webhook configured, skipping notification.");
				return;
			}

			int colorCode;
			string titleData;
			string message;

			switch (payload.Type)
			{
				case "NEW_SERIES_BOOK":
				{
					colorCode = 5814783;

					titleData = "New Series Book Released!";

					message =
						$"""
						 **{payload.Title}**
						 by *{payload.Author}*

						 Series: {payload.Series}
						 Position: #{payload.Position}

						 ({payload.Url})
						 """;

					break;
				}

				case "NEW_AUTHOR_BOOK":
				{
					colorCode = 15762959;

					titleData = "New Author Release!";

					message =
						$"""
						 **{payload.Title}**
						 by *{payload.Author}*

						 ({payload.Url})
						 """;

					break;
				}

				default:
				{
					colorCode = 0;

					titleData = "Book Notification";

					message =
						$"""
						 **{payload.Title}**
						 by *{payload.Author}*

						 ({payload.Url})
						 """;

					break;
				}
			}

			Log($"{titleData} | {payload.Title}");

			object discordPayload = new
			{
				embeds = new[]
				{
					new
					{
						title = titleData,
						description = message,
						color = colorCode,
						timestamp = payload.DetectedAtUtc,

						thumbnail = new
						{
							url = "https://www.google.com/s2/favicons?domain=goodreads.com&sz=48"
						}
					}
				},

				username = "GoodReads - Book Notifier",

				avatar_url =
					"https://www.google.com/s2/favicons?domain=goodreads.com&sz=48"
			};

			string json =
				JsonSerializer.Serialize(discordPayload);

			StringContent jsonContent = new(
				json,
				Encoding.UTF8,
				"application/json");

			HttpResponseMessage response =
				await Client.PostAsync(
					webhook,
					jsonContent);

			Log(
				response.IsSuccessStatusCode
					? $"""
					   Successfully sent webhook:
					   {payload.Type} | {payload.Title}
					   """
					: $"""
					   Failed webhook:
					   {payload.Type} | {payload.Title}
					   Status: {response.StatusCode}
					   """);

			string responseContent =
				await response.Content.ReadAsStringAsync();

			Log(responseContent);
		}

		private static void Log(string message)
		{
			Console.WriteLine(
				$"[{DateTimeOffset.UtcNow:u}] {message}");
		}
	}
}
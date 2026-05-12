using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ScribbleHubNotifier.Classes
{
	public static class Discord
	{
		private static readonly CookieContainer Cookies = new();

		private static readonly HttpClient DiscordClient = new(new HttpClientHandler
		{
			AllowAutoRedirect = true,
			AutomaticDecompression = DecompressionMethods.All,
			CookieContainer = Cookies,
			UseCookies = true
		})
		{
			DefaultRequestHeaders =
			{
				{
					"User-Agent",
					"Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:141.0) Gecko/20100101 Firefox/141.0 ScribbleHubNotifier/1.0"
				}
			},
			Timeout = TimeSpan.FromSeconds(15)
		};

		public static async Task SendDiscordWebhookAsync(ReadingListStory story, bool? newStory = false)
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

			if (newStory.HasValue && newStory.Value)
			{
				colorCode = 1044502;
				titleData = "New Story Detected!";
				message = $"**{story.Name}**\n\n({story.Link})";
			}
			else
			{
				colorCode = 15762959;
				titleData = "New chapter published!";
				message = $"**{story.Name}** has a new chapter!\n\n**{story.Chapters[^1].Title}**\n\n({story.Chapters[^1].Link})";
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
						thumbnail = new { url = "https://api.irisapp.ca/images/scribbble.png" }
					}
				},
				username = "Book Notifier",
				avatar_url = "https://api.irisapp.ca/images/scribbble.png"
			};

			StringContent jsonContent = new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
			HttpResponseMessage response = await DiscordClient.PostAsync(webhook, jsonContent);

			Log(response.IsSuccessStatusCode
				? $"Successfully sent webhook for {story.Name} - {titleData}"
				: $"Failed to send webhook for {story.Name} - {titleData}: {response.StatusCode}");
			Log(await response.Content.ReadAsStringAsync());
		}
	}
}

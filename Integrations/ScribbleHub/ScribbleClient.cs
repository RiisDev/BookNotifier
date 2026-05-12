using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BookNotifier.Utilities;

namespace BookNotifier.Integrations.ScribbleHub
{
	public class ScribbleClient : IDisposable
	{
		private string _cookieString = "";

		private readonly HttpClient _client = new(new HttpClientHandler
		{
			AllowAutoRedirect = true,
			AutomaticDecompression = DecompressionMethods.All,
			UseCookies = true
		})
		{
			DefaultRequestHeaders =
			{
				{
					"User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:141.0) Gecko/20100101 Firefox/141.0 ScribbleHubNotifier/1.0"
				}
			},
			Timeout = TimeSpan.FromSeconds(15)
		};

		public void Dispose()
		{
			GC.SuppressFinalize(this);
			_client.Dispose();
		}

		public void SetCookies(string cookieString)
		{
			Log("Setting cookies...");
			_cookieString = cookieString;
		}

		public async Task Login(string username, string password, string? referralUrl = "https://www.scribblehub.com/reading-list/")
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(username);
			ArgumentException.ThrowIfNullOrWhiteSpace(password);
			ArgumentException.ThrowIfNullOrWhiteSpace(referralUrl);

			await _client.GetAsync("https://www.scribblehub.com/login/");
			using HttpRequestMessage request = new(HttpMethod.Post, new Uri("https://www.scribblehub.com/login/"));

			request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
			request.Headers.TryAddWithoutValidation("sec-ch-ua", "\"Chromium\";v=\"146\", \"Not-A.Brand\";v=\"24\", \"Brave\";v=\"146\"");
			request.Headers.TryAddWithoutValidation("sec-ch-ua-arch", "\"x86\"");
			request.Headers.TryAddWithoutValidation("sec-ch-ua-bitness", "\"64\"");
			request.Headers.TryAddWithoutValidation("sec-ch-ua-full-version-list", "\"Chromium\";v=\"146.0.0.0\", \"Not-A.Brand\";v=\"24.0.0.0\", \"Brave\";v=\"146.0.0.0\"");
			request.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
			request.Headers.TryAddWithoutValidation("sec-ch-ua-model", "\"\"");
			request.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
			request.Headers.TryAddWithoutValidation("sec-ch-ua-platform-version", "\"19.0.0\"");

			request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
			{
				{ "reg_username", username },
				{ "reg_password", password },
				{ "chk_rememberme", "1" }, // "1" to check the box
				{ "referral", referralUrl }
			});

			HttpResponseMessage response = await _client.SendAsync(request);
			string responseContent = await response.Content.ReadAsStringAsync();
			string headersData = JsonSerializer.Serialize(response.Headers);
			Log($"Login Cookies -> {headersData}");
			Log($"Login Status -> {response.StatusCode}");
			Log($"Login Content: {Convert.ToBase64String(Encoding.UTF8.GetBytes(responseContent))}");
			
			response.EnsureSuccessStatusCode();
			
			if (
				responseContent.Contains("An error with Google reCAPTCHA has occurred. Please try again.") ||
				!headersData.Contains("Set-Cookie")
			) throw new InvalidOperationException("Captcha hit, cannot continue");
		}

		public async Task<List<ScribbleReadingListStory>> GetReadingList()
		{
			List<ScribbleReadingListStory> storyReturn = [];

			HttpRequestMessage request = new(HttpMethod.Get, "https://www.scribblehub.com/reading-list/");
			if (!string.IsNullOrEmpty(_cookieString)) request.Headers.TryAddWithoutValidation("Cookie", _cookieString);
			HttpResponseMessage response = await _client.SendAsync(request);

			Log($"Reading List Status -> ({response.StatusCode})");
			response.EnsureSuccessStatusCode();
			string data = await response.Content.ReadAsStringAsync();

			if (data.Contains("need to log in before you can access this page", StringComparison.InvariantCultureIgnoreCase))
				throw new InvalidOperationException("User is not logged in");

			HtmlDocument document = HtmlDocument.Parse(data);
			IEnumerable<HtmlElement> stories = document.QuerySelectorAll("div[title] a");

			foreach (HtmlElement story in stories)
			{
				HtmlNode gridParent = story.Parent?.Parent ?? throw new InvalidOperationException("Failed to find grid parent.");
				if (gridParent is not HtmlElement gridElement) throw new InvalidOperationException("Failed to convert back to htmlElement.");

				HtmlElement latestSpan = gridElement.QuerySelector("span[last]") ?? throw new InvalidOperationException("Failed to find last span.");

				string title = story.InnerText;
				string storyLink = story.GetAttribute("href")!;
				string storyId = Regex.Match(storyLink, @"series\/(\d+)\/", RegexOptions.Compiled | RegexOptions.Singleline).Groups[1].Value.Trim();
				string chapterId = latestSpan.GetAttribute("last") ?? throw new InvalidOperationException("Failed to get last_id");
				string chapterName = latestSpan.InnerText;

				if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(chapterId) || string.IsNullOrEmpty(chapterName))
				{
					LogError($"One or more fields are empty -> {title} | {chapterName} | {chapterId}");
					continue;
				}

				Log($"Found Story: {title.HtmlDecode()} -> {chapterName.HtmlDecode()}");
				storyReturn.Add(new ScribbleReadingListStory(title, storyLink, storyId, []));
			}

			foreach (ScribbleReadingListStory story in storyReturn)
			{
				story.Chapters.AddRange(await GetBookToc(story.Id));
				story.Chapters.Reverse();
			}

			return storyReturn;
		}

		public async Task<List<ScribbleChapter>> GetBookToc(string bookId)
		{
			
			using HttpRequestMessage request = new(HttpMethod.Post, new Uri("https://www.scribblehub.com/wp-admin/admin-ajax.php"));
			Dictionary<string, string> formData = new()
			{
				{"action", "wi_gettocchp"},
				{"strSID", bookId},
				{"strFic", "read"}
			};
			request.Content = new FormUrlEncodedContent(formData);

			HttpResponseMessage response = await _client.SendAsync(request);
			string responseContent = await response.Content.ReadAsStringAsync();
			Log($"Book TOC Status -> ({response.StatusCode})");
			response.EnsureSuccessStatusCode();

			MatchCollection chapterMatches = Regex.Matches(responseContent, "title=\"([^\"]+)\"[^>]*href=\"([^\"]+)\"");
			List<ScribbleChapter> chapters = [];

			foreach (Match match in chapterMatches)
			{
				string title = match.Groups[1].Value.Trim().HtmlDecode();
				string link = match.Groups[2].Value.Trim().HtmlDecode();
				string id = link[link.Trim('/').LastIndexOf('/')..].Trim('/');

				chapters.Add(new ScribbleChapter(title, link, id));
			}

			return chapters;
		}
	}
}

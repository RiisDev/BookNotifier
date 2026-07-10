using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BookNotifier.Integrations;
using BookNotifier.Utilities;

namespace ScribbleHub.Project
{
	public class ScribbleClient(string flareSolver, string userId) : IDisposable
	{
		private string _cloudflareCookie = "";
		private readonly string _sessionId = $"booknot-scribblehub-{Guid.NewGuid():N}";
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
					"User-Agent", "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36"
				},
				{
					"Accept-Language", "en-US,en;q=0.9"
				}
			}
		};

		public void Dispose()
		{
			GC.SuppressFinalize(this);
			_client.Dispose();
		}

		public async Task InitiateSession()
		{
			StringContent body = new(
				JsonSerializer.Serialize(new { cmd = "sessions.create", session = _sessionId }),
				Encoding.UTF8,
				"application/json"
			);
			await _client.PostAsync(flareSolver, body);
		}

		public async Task DestroySessionAsync()
		{
			StringContent body = new(
				JsonSerializer.Serialize(new { cmd = "sessions.destroy", session = _sessionId }),
				Encoding.UTF8,
				"application/json"
			);
			await _client.PostAsync(flareSolver, body);
		}

		private async Task<string> PostSolver(string url, IEnumerable<KeyValuePair<string, string>>? postData = null)
		{
			StringContent body = new(
				JsonSerializer.Serialize(new
				{
					cmd = "request.post",
					url,
					session = _sessionId, 
					maxTimeout = 60000,
					postData = postData is null ? null : await new FormUrlEncodedContent(postData).ReadAsStringAsync()
				}),
				Encoding.UTF8,
				"application/json"
			);
			
			HttpResponseMessage response = await _client.PostAsync(flareSolver, body);
			string json = await response.Content.ReadAsStringAsync();

			if (json.Contains("error"))
			{
				using JsonDocument doc = JsonDocument.Parse(json);
				throw new InvalidOperationException($"FlareSolver encountered an error: {doc.RootElement.GetProperty("message").GetString()}");
			}

			FlareSolver? solverData = JsonSerializer.Deserialize<FlareSolver>(json);

			_cloudflareCookie = solverData?.Solution.Cookies.FirstOrDefault(x => x.Name == "cf_clearance")?.Value ?? "";

			Log($"[POST] ({solverData?.Solution.Status}) {url}\n[RETURN] {json}");
			return solverData?.Solution.Content ?? "";
		}

		
		public async Task<List<ScribbleReadingListStory>> GetReadingList()
		{
			List<ScribbleReadingListStory> storyReturn = [];

			string data = await PostSolver("https://www.scribblehub.com/wp-admin/admin-ajax.php", [
				new KeyValuePair<string, string>("action", "wi_profilerl"),
				new KeyValuePair<string, string>("intAuthorID", userId),
				new KeyValuePair<string, string>("isMobile", ""),
				new KeyValuePair<string, string>("str_isapp", "0"),
			]);

			if (data.Contains("need to log in before you can access this page", StringComparison.InvariantCultureIgnoreCase))
				throw new InvalidOperationException("User is not logged in");

			HtmlDocument document = HtmlDocument.Parse(data);
			IEnumerable<HtmlElement> stories = document.QuerySelectorAll("div[class*=title] a");
			
			foreach (HtmlElement story in stories)
			{
				HtmlNode gridParent = story.Parent?.Parent ?? throw new InvalidOperationException("Failed to find grid parent.");
				if (gridParent is not HtmlElement gridElement) throw new InvalidOperationException("Failed to convert back to htmlElement.");

				string title = story.InnerText;
				string storyLink = story.GetAttribute("href")!;
				string storyId = Regex.Match(storyLink, @"series\/(\d+)\/", RegexOptions.Compiled | RegexOptions.Singleline).Groups[1].Value.Trim();
				string chapterId = gridElement.QuerySelector("span[last]")?.GetAttribute("last") ?? throw new InvalidOperationException("Failed to get last_id");
				string chapterName = gridElement.QuerySelector("[class=rl_i_status]")?.Children.LastOrDefault()?.InnerText ?? "";

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
			request.Headers.TryAddWithoutValidation("Cookie", $"cf_clearance={_cloudflareCookie}");

			HttpResponseMessage response = await _client.SendAsync(request);
			string responseContent = await response.Content.ReadAsStringAsync();
			Log($"Book TOC Status -> ({response.StatusCode})");

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

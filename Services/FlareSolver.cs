using BookNotifier.Integrations;
using System.Net;
using System.Text;
using System.Text.Json;

namespace BookNotifier.Services
{
	public class FlareSolverClient
	{
		private readonly string _flareSolver = Environment.GetEnvironmentVariable("FLARESOLVER_URL") ?? "";

		private readonly HttpClient _flareSolverClient = new(new HttpClientHandler
		{
			AllowAutoRedirect = true,
			AutomaticDecompression = DecompressionMethods.All,
			UseCookies = true
		})
		{
			DefaultRequestHeaders =
			{
				{ "User-Agent", "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36" }
			}
		};

		public async Task<HttpResponseMessage> CfCookiePostRequest(string url, string cfClearance, HttpContent? postData = null)
		{
			using HttpRequestMessage request = new(HttpMethod.Post, new Uri("https://www.scribblehub.com/wp-admin/admin-ajax.php"));
			if (postData is not null)
				request.Content = postData;
			request.Headers.TryAddWithoutValidation("Cookie", $"cf_clearance={cfClearance}");
			return await _flareSolverClient.SendAsync(request);
		}

		public async Task InitiateSession(string sessionId)
		{
			if (string.IsNullOrEmpty(_flareSolver)) return;

			StringContent body = new(
				JsonSerializer.Serialize(new { cmd = "sessions.create", session = sessionId }),
				Encoding.UTF8,
				"application/json"
			);
			await _flareSolverClient.PostAsync(_flareSolver, body);
		}

		public async Task DestroySessionAsync(string sessionId)
		{
			if (string.IsNullOrEmpty(_flareSolver)) return;

			StringContent body = new(
				JsonSerializer.Serialize(new { cmd = "sessions.destroy", session = sessionId }),
				Encoding.UTF8,
				"application/json"
			);
			await _flareSolverClient.PostAsync(_flareSolver, body);
		}

		public async Task<(string, int, string)> PostSolver(string url, string sessionId, IEnumerable<KeyValuePair<string, string>>? postData = null)
		{
			if (string.IsNullOrEmpty(_flareSolver))
				return ("", -1, "");

			const int retryLimit = 5;
			int retries = 0;

			while (retries < retryLimit)
			{
				if (retries != 0)
				{
					Log($"Retry: {retries}/{retryLimit}");
				}

				StringContent body = new(
					JsonSerializer.Serialize(new
					{
						cmd = "request.post",
						url,
						session = sessionId,
						maxTimeout = 60000,
						postData = postData is null ? null : await new FormUrlEncodedContent(postData).ReadAsStringAsync()
					}),
					Encoding.UTF8,
					"application/json");

				using HttpResponseMessage response = await _flareSolverClient.PostAsync(_flareSolver, body);
				string json = await response.Content.ReadAsStringAsync();

				if (json.Contains("\"error\""))
				{
					using JsonDocument document = JsonDocument.Parse(json);

					Log($"FlareSolver encountered an error: {document.RootElement.GetProperty("message").GetString()}, retrying...");

					retries++;
					continue;
				}

				FlareSolver? solverData = JsonSerializer.Deserialize<FlareSolver>(json);

				if (solverData is null)
					throw new InvalidOperationException($"SolverData somehow null: {json}");

				if (solverData.Solution.Status == 429)
				{
					string? retryAfter = solverData.Solution.Headers
						.FirstOrDefault(x => x.Key == "retry-after")
						.Value;

					if (int.TryParse(retryAfter, out int retryDuration))
					{
						Log($"[POST] Rate Limited, Retry-After header found: ({retryDuration} seconds), waiting...");

						await Task.Delay(TimeSpan.FromSeconds(retryDuration));

						retries++;
						continue;
					}
				}

				Log($"[POST] ({solverData.Solution.Status}) {url}");

				return (
					solverData.Solution.Content ?? "",
					solverData.Solution.Status ?? -1,
					solverData.Solution.Cookies.FirstOrDefault(x => x.Name == "cf_clearance")?.Value ?? ""
				);
			}

			throw new InvalidOperationException($"Failed after {retryLimit} retries");
		}

		public async Task<(string, int, string)> GetSolver(string url, string sessionId)
		{
			if (string.IsNullOrEmpty(_flareSolver)) return ("", -1, "");

			const int retryLimit = 5;
			int retries = 0;

			while (retries < retryLimit)
			{
				if (retries != 0)
				{
					Log($"Retry: {retries}/{retryLimit}");
				}

				StringContent body = new(JsonSerializer.Serialize(new
				{
					cmd = "request.get",
					url,
					session = sessionId,
					maxTimeout = 60000
				}), Encoding.UTF8, "application/json");

				using HttpResponseMessage response = await _flareSolverClient.PostAsync(_flareSolver, body);
				string json = await response.Content.ReadAsStringAsync();

				if (json.Contains("\"error\""))
				{
					using JsonDocument doc = JsonDocument.Parse(json);
					Log($"FlareSolver encountered an error: {doc.RootElement.GetProperty("message").GetString()}, retrying...");
					retries++;
					continue;
				}

				FlareSolver? solverData = JsonSerializer.Deserialize<FlareSolver>(json);

				if (solverData is null)
					throw new InvalidOperationException($"SolverData somehow null: {json}");

				if (solverData.Solution.Status == 429)
				{
					string? retryAfter = solverData.Solution.Headers.FirstOrDefault(x => x.Key == "retry-after").Value;
					if (int.TryParse(retryAfter, out int retryDuration))
					{
						Log($"[GET] Rate Limited, Retry-After header found: ({retryDuration} seconds), waiting...");
						await Task.Delay(TimeSpan.FromSeconds(retryDuration));
						retries++;
						continue;
					}
				}

				Log($"[GET] ({solverData.Solution.Status}) {url}");
				return (
					solverData.Solution.Content ?? "", 
					solverData.Solution.Status ?? -1, 
					solverData.Solution.Cookies.FirstOrDefault(x => x.Name == "cf_clearance")?.Value ?? ""
				);
			}

			throw new InvalidOperationException($"Failed after {retryLimit} retries");
		}
	}
}

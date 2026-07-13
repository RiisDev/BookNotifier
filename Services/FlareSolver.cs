using BookNotifier.Integrations;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace BookNotifier.Services
{
	public class FlareSolverClient
	{
		private const int RetryLimit = 5;
		private const int ScribbleDefaultRetry = 120_000;

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
			using HttpRequestMessage request = new(HttpMethod.Post, new Uri(url));
			if (postData is not null)
				request.Content = postData;
			request.Headers.TryAddWithoutValidation("Cookie", $"cf_clearance={cfClearance}");
			return await _flareSolverClient.SendAsync(request);
		}

		private async Task SendSessionCommandAsync(string command, string sessionId)
		{
			if (string.IsNullOrEmpty(_flareSolver)) return;

			using StringContent body = new(
				JsonSerializer.Serialize(
					new
					{
						cmd = command,
						session = sessionId
					}
				),
				Encoding.UTF8,
				"application/json"
			);

			using HttpResponseMessage response = await _flareSolverClient.PostAsync(_flareSolver, body);
		}
		
		private static readonly IReadOnlyDictionary<string, (string ErrorMessage, int RetryDelay)> CustomResolvers = new Dictionary<string, (string, int)>
		{
			["hackform"] = ("[SCRIBBLE-HACKFORM] Custom captcha found, retrying", ScribbleDefaultRetry)
		};

		private async Task<(string, int, string)> SolverRequest(string requestJson, HttpMethod method, [CallerMemberName] string caller = "")
		{
			if (string.IsNullOrEmpty(_flareSolver))
				return ("", -1, "");

			string methodText = method.Method.ToUpper();

			int retries = 0;

			while (retries < RetryLimit)
			{
				if (retries != 0) Log($"[{methodText}] [{caller}] Retry: {retries}/{RetryLimit}");

				using StringContent request = new(requestJson, Encoding.UTF8, "application/json");
				using HttpResponseMessage response = await _flareSolverClient.PostAsync(_flareSolver, request);
				string json = await response.Content.ReadAsStringAsync();

				if (json.Contains("\"error\""))
				{
					try
					{
						using JsonDocument document = JsonDocument.Parse(json);
						Log($"[{methodText}] [{caller}] FlareSolver encountered an error: {document.RootElement.GetProperty("message").GetString()}, retrying...");
					}
					catch
					{
						Log($"[{methodText}] [{caller}] FlareSolver encountered an error: {json}, retrying...");
					}
					retries++;
					continue;
				}

				FlareSolver? solverData = JsonSerializer.Deserialize<FlareSolver>(json);

				if (solverData is null)
					throw new InvalidOperationException($"SolverData somehow null: {json}");

				string content = solverData.Solution.Content ?? "";
				int statusCode = solverData.Solution.Status ?? 422;

				if (statusCode != 200)
				{
					solverData.Solution.Headers.TryGetValue("retry-after", out string? retryAfter);
					if (int.TryParse(retryAfter, out int retryDuration))
					{
						Log($"[{methodText}] [{caller}] ({statusCode}) [{(HttpStatusCode)statusCode}], Retry-After header found: ({retryDuration} seconds), waiting...");
						await Task.Delay(TimeSpan.FromSeconds(retryDuration));
					}
					else switch (statusCode)
					{
						case 422:
							Log($"[{methodText}] [{caller}] Unknown flaresolver data return found: {json}");
							break;
						case 403 when content.Contains("you have been blocked"):
							Log($"[{methodText}] [{caller}] [CF-IP-BAN] Cloudflare ban detected, waiting 30 minutes before retrying");
							retries = 5;
							await Task.Delay(1_800_000);
							break;
						default:
							Log($"[{methodText}] [{caller}] ({statusCode}) [{(HttpStatusCode)statusCode}], Retry-After header not found, waiting 120 seconds...");
							await Task.Delay(ScribbleDefaultRetry);
							break;
					}

					retries++;
					continue;
				}

				if (string.IsNullOrEmpty(content))
				{
					Log($"[{methodText}] [{caller}] Solution content was empty, retrying");

					retries++;
					continue;
				}

				if (!solverData.Solution.Cookies.Any())
				{
					Log($"[{methodText}] [{caller}] Solution cookies array was empty, retrying");

					retries++;
					continue;
				}

				bool shouldRetry = false;
				foreach ((string searchKey, (string errorMessage, int retryDuration)) in CustomResolvers)
				{
					if (!content.Contains(searchKey, StringComparison.OrdinalIgnoreCase)) continue;
					Log($"[{methodText}] [{caller}] {errorMessage}");
					await Task.Delay(retryDuration);
					retries++;
					shouldRetry = true;
					break;
				}
				if (shouldRetry) continue;

				Log($"[{methodText}] [{caller}] ({statusCode}) {solverData.Solution.Url}");

				return (
					content,
					statusCode,
					solverData.Solution.Cookies.FirstOrDefault(x => x.Name == "cf_clearance")?.Value ?? ""
				);
			}

			throw new InvalidOperationException($"Failed after {RetryLimit} retries");
		}

		public Task InitiateSession(string sessionId) => SendSessionCommandAsync("sessions.create", sessionId);
		public Task DestroySessionAsync(string sessionId) => SendSessionCommandAsync("sessions.destroy", sessionId);

		public async Task<(string, int, string)> PostSolver(string url, string sessionId, IEnumerable<KeyValuePair<string, string>>? postData = null, [CallerMemberName] string caller = "")
		{
			string? encodedPostData = null;

			if (postData is not null)
			{
				using FormUrlEncodedContent formContent = new(postData);
				encodedPostData = await formContent.ReadAsStringAsync();
			}

			return await SolverRequest(
				JsonSerializer.Serialize(
					new
					{
						cmd = "request.post",
						url,
						session = sessionId,
						maxTimeout = 60000,
						postData = encodedPostData
					}
				), 
				HttpMethod.Post, 
				caller
			);
		}

		public async Task<(string, int, string)> GetSolver(string url, string sessionId, [CallerMemberName] string caller = "")
		{
			return await SolverRequest(
				JsonSerializer.Serialize
				(
					new
					{
						cmd = "request.get",
						url,
						session = sessionId,
						maxTimeout = 60000
					}
				), 
				HttpMethod.Get, 
				caller
			);
		}
	}
}

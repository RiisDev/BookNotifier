using System.Net;

namespace BookNotifier.Utilities
{
	internal static class HttpRetry
	{
		public static async Task<(string Content, HttpStatusCode StatusCode)> GetWithRetryAsync(HttpClient client, string url, int maxRetries = 5, CancellationToken cancellationToken = default)
		{
			for (int attempt = 1; ; attempt++)
			{
				try
				{
					using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);

					bool isWafChallenge = response.StatusCode == (HttpStatusCode)202 && response.Headers.Contains("x-amzn-waf-action");

					if ((response.StatusCode is HttpStatusCode.ServiceUnavailable or (HttpStatusCode)429 || isWafChallenge) && attempt < maxRetries)
					{
						TimeSpan delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
						LogError($"Retry {attempt}/{maxRetries} for {url} due to {(isWafChallenge ? "WAF challenge" : (int)response.StatusCode)}. Waiting {delay.TotalSeconds}s");
						await Task.Delay(delay, cancellationToken);
						continue;
					}

					return (await response.Content.ReadAsStringAsync(cancellationToken), response.StatusCode);
				}
				catch (HttpRequestException ex) when (attempt < maxRetries)
				{
					TimeSpan delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
					LogError($"Retry {attempt}/{maxRetries} for {url} due to {ex.Message}. Waiting {delay.TotalSeconds}s");
					await Task.Delay(delay, cancellationToken);
				}
				catch (TaskCanceledException) when (attempt < maxRetries)
				{
					TimeSpan delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
					LogError($"Retry {attempt}/{maxRetries} for {url} due to timeout. Waiting {delay.TotalSeconds}s");
					await Task.Delay(delay, cancellationToken);
				}
			}
		}

		public static bool IsSuccess(HttpStatusCode statusCode) => (int)statusCode is >= 200 and < 300;
	}
}

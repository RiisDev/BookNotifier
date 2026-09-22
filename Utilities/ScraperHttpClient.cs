using System.Net;

namespace BookNotifier.Utilities
{
	internal static class ScraperHttpClient
	{
		public const string DefaultUserAgent = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

		public static HttpClient Create(string userAgent = DefaultUserAgent, TimeSpan? timeout = null)
		{
			HttpClient client = new(new HttpClientHandler
			{
				AllowAutoRedirect = true,
				AutomaticDecompression = DecompressionMethods.All,
				UseCookies = true
			})
			{
				DefaultRequestHeaders = { { "User-Agent", userAgent } }
			};

			if (timeout is not null) client.Timeout = timeout.Value;

			return client;
		}
	}
}

using System.Text.Json.Serialization;

namespace BookNotifier.Integrations.ScribbleHub
{
	public record ScribbleChapter(string Title, string Link, string Id, DateTime? ReleasedAt = null);

	public record ScribbleReadingListStory(string Name, string Link, string Id, List<ScribbleChapter> Chapters,
		string? CoverUrl = null, string Status = "unknown");

	public record ScribbleSaveChapter(
		[property: JsonPropertyName("Title")] string Title,
		[property: JsonPropertyName("Link")] string Link,
		[property: JsonPropertyName("Id")] string Id,
		[property: JsonPropertyName("ReleasedAt")] DateTime? ReleasedAt = null
	);

	public record ScribbleSaveBookRoot(
		[property: JsonPropertyName("Name")] string Name,
		[property: JsonPropertyName("Link")] string Link,
		[property: JsonPropertyName("Id")] string Id,
		[property: JsonPropertyName("Chapters")] IReadOnlyList<ScribbleSaveChapter> Chapters,
		[property: JsonPropertyName("CoverUrl")] string? CoverUrl = null,
		[property: JsonPropertyName("Status")] string Status = "unknown"
	);

	public record FlareSolverCookie(
		[property: JsonPropertyName("name")] string Name,
		[property: JsonPropertyName("value")] string Value,
		[property: JsonPropertyName("expires")] double? Expires
	);

	public record FlareSolver(
		[property: JsonPropertyName("solution")] FlareSolverSolution Solution
	);

	public record FlareSolverSolution(
		[property: JsonPropertyName("cookies")] IReadOnlyList<FlareSolverCookie> Cookies,
		[property: JsonPropertyName("headers")] IReadOnlyDictionary<string, string> Headers,
		[property: JsonPropertyName("response")] string? Content,
		[property: JsonPropertyName("status")] int? Status,
		[property: JsonPropertyName("url")] string? Url
	);
}

using System.Text.Json.Serialization;

namespace BookNotifier.Integrations.ScribbleHub
{
	public record ScribbleChapter(string Title, string Link, string Id);

	public record ScribbleReadingListStory(string Name, string Link, string Id, List<ScribbleChapter> Chapters);

	public record ScribbleSaveChapter(
		[property: JsonPropertyName("Title")] string Title,
		[property: JsonPropertyName("Link")] string Link,
		[property: JsonPropertyName("Id")] string Id
	);

	public record ScribbleSaveBookRoot(
		[property: JsonPropertyName("Name")] string Name,
		[property: JsonPropertyName("Link")] string Link,
		[property: JsonPropertyName("Id")] string Id,
		[property: JsonPropertyName("Chapters")] IReadOnlyList<ScribbleSaveChapter> Chapters
	);
}

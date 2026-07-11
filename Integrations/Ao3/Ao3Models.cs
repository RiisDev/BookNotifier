using System.Text.Json.Serialization;

namespace BookNotifier.Integrations.Ao3;

public record Ao3ExistingChapter(
	[property: JsonPropertyName("Title")] string Title,
	[property: JsonPropertyName("Url")] string Url
);

public record Ao3ExistingWorkEntries(
	[property: JsonPropertyName("WorkId")] string WorkId,
	[property: JsonPropertyName("Title")] string Title,
	[property: JsonPropertyName("Url")] string Url,
	[property: JsonPropertyName("Author")] string Author,
	[property: JsonPropertyName("Chapters")] IReadOnlyList<Ao3ExistingChapter> Chapters
);


public record Ao3WorkEntry(string WorkId, string Title, string Url, string Author, List<Ao3Chapter> Chapters);

public  record Ao3Chapter(string Title, string Url);
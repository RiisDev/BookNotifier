namespace BookNotifier.Integrations.Literotica
{
	public record LiteroticaKnownChapter(string Title, string Url, DateTime PublishedAt);

	// A "work" is either a standalone story (one chapter) or a series (one chapter per part).
	public record LiteroticaKnownWork(string Title, string Url, string Author, IReadOnlyList<LiteroticaKnownChapter> Chapters, string Status = "unknown");

	public record LiteroticaKnownData
	{
		public required HashSet<string> Authors { get; init; }
		public required List<LiteroticaKnownWork> Works { get; init; }
	}
}

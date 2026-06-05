namespace BookNotifier.Integrations.RoyalRoad
{
	public record RoyalRoadKnownChapter
	{
		public required string Title { get; init; }
		public required string Url { get; init; }
	}

	public record RoyalRoadKnownFiction
	{
		public required string Title { get; init; }
		public required string Url { get; init; }
		public required List<RoyalRoadKnownChapter> Chapters { get; init; }
	}
}

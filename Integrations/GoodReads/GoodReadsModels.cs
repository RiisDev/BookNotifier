
namespace BookNotifier.Integrations.GoodReads
{
	public sealed record GoodReadsBook
	{
		public required Guid Id { get; init; }

		public required string Title { get; init; }

		public required Uri Url { get; init; }

		public required Guid AuthorId { get; init; }

		public Guid? SeriesId { get; init; }
	}

	public sealed record GoodReadsAuthor
	{
		public required Guid Id { get; init; }

		public required string Name { get; init; }

		public required Uri Url { get; init; }
	}

	public sealed record GoodReadsSeriesBook
	{
		public required string Title { get; init; }

		public required Uri Url { get; init; }

		public required int Position { get; init; }
	}

	public sealed record GoodReadsSeries
	{
		public required Guid Id { get; init; }

		public required string Name { get; init; }

		public required Uri Url { get; init; }

		public IReadOnlyList<GoodReadsSeriesBook> Books { get; init; } = [];
	}

	public sealed record GoodReadsBookDetails
	{
		public required GoodReadsBook Book { get; init; }

		public required GoodReadsAuthor Author { get; init; }

		public GoodReadsSeries? Series { get; init; }
	}


	public sealed record GoodReadsKnownBook
	{
		public required string Title { get; init; }

		public required string AuthorName { get; init; }

		public required string Url { get; init; }

		public string? SeriesName { get; init; }

		public int? SeriesPosition { get; init; }
	}

	public sealed record GoodReadsNotificationPayload
	{
		public required string Type { get; init; }

		public required string Title { get; init; }

		public required string Author { get; init; }

		public required string Url { get; init; }

		public string? Series { get; init; }

		public int? Position { get; init; }

		public required DateTimeOffset DetectedAtUtc { get; init; }
	}
}
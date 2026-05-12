namespace BookNotifier.SiteAPIs.Literotica
{
	public record LiteroticaKnownData
	{
		public required HashSet<string> Authors { get; init; }
		public required HashSet<string> Stories { get; init; }
	}
}
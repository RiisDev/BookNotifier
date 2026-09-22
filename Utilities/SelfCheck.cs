using BookNotifier.Integrations.GoodReads;
using BookNotifier.Integrations.ScribbleHub;

namespace BookNotifier.Utilities
{	internal static class SelfCheck
	{
		public static void Run()
		{
			DateTime now = DateTime.UtcNow;

			Assert(ScribbleClient.ParseScribbleDate("6 mins ago") is { } relMins && Math.Abs((now - relMins).TotalMinutes - 6) < 1,
				"ScribbleHub relative 'mins ago' date");

			Assert(ScribbleClient.ParseScribbleDate("2 days ago") is { } relDays && Math.Abs((now - relDays).TotalDays - 2) < 0.01,
				"ScribbleHub relative 'days ago' date");

			Assert(ScribbleClient.ParseScribbleDate("Sep 19, 2026 04:51 AM") is { Year: 2026, Month: 9, Day: 19 },
				"ScribbleHub absolute date");

			Assert(ScribbleClient.ParseScribbleDate("not a date") is null,
				"ScribbleHub unparseable date returns null");

			Assert(GoodReadsClient.ParsePublicationInfo("First published July 6, 2021") is { Year: 2021, Month: 7, Day: 6 },
				"GoodReads publication info with day");

			Assert(GoodReadsClient.ParsePublicationInfo(null) is null,
				"GoodReads null publication info returns null");

			Log("[selftest] All checks passed.");
		}

		private static void Assert(bool condition, string what)
		{
			if (!condition) throw new InvalidOperationException($"[selftest] FAILED: {what}");
		}
	}
}

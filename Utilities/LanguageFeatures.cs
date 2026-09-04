namespace BookNotifier.Utilities
{
	public static class LanguageFeatures
	{
		public static bool TryFind<T>(this IEnumerable<T> source, Func<T, bool> predicate, out T? result)
		{
			foreach (T item in source)
			{
				if (!predicate(item)) continue;
				result = item;
				return true;
			}
			result = default;
			return false;
		}
	}
}

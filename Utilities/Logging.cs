global using static BookNotifier.Utility.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace BookNotifier.Utility
{
	public static class Logging
	{
		private static readonly Lock LogLock = new();
		private static string LogDirectory => Path.Combine(AppContext.BaseDirectory, "data", "log.txt");

		public static void Log(string message, [CallerMemberName] string caller = "", [CallerFilePath] string filePath = "")
		{
			lock (LogLock)
			{
				string fileName = Path.GetFileName(filePath);
				string data = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [{fileName}.{caller}] {message}";
				Console.WriteLine(data);
				Debug.WriteLine(data);
				File.AppendAllText(LogDirectory, data + "\n");
			}
		}

		public static void LogError(string message, [CallerMemberName] string caller = "", [CallerFilePath] string filePath = "") => Log($"[ERROR] {message}", caller, filePath);
	}
}

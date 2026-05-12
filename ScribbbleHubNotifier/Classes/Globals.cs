global using static ScribbleHubNotifier.Classes.Globals;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ScribbleHubNotifier.Classes
{
	public static class Globals
	{
		public static JsonSerializerOptions JsonOptions = new()
		{
			AllowTrailingCommas = true,
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
			WriteIndented = true,
			IndentCharacter = '\t',
			IndentSize = 1
		};

		private static readonly Lock LogLock = new();
		private static string LogDirectory => Path.Combine(AppContext.BaseDirectory, "log.txt");

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

namespace LiteroticaWatcher
{
    internal class Program
    {
        static void Main()
        {
            _ = new EnvService();

            string? username = Environment.GetEnvironmentVariable("LIT_USERNAME");
            if (string.IsNullOrEmpty(username)) throw new Exception("Missing LIT_USERNAME environment variable");
            string? password  = Environment.GetEnvironmentVariable("LIT_PASSWORD");
            if (string.IsNullOrEmpty(password)) throw new Exception("Missing LIT_PASSWORD environment variable");

			string checkTime = Environment.GetEnvironmentVariable("RECHECK_MS")?.Replace("_","").Replace(" ", "") ?? "600_000".Replace("_", "").Replace(" ", "");
            if (string.IsNullOrEmpty(checkTime)) throw new Exception("Missing RECHECK_MS environment variable");
            if (!long.TryParse(checkTime, out long recheckMs)) throw new Exception("Failed to parse RECHECK_MS");

            _ = new LitParser(username, password, recheckMs);

            while (true) Console.ReadLine();
        }
    }

    internal class EnvService
    {
        public Dictionary<string, string> Variables { get; } = new();

        internal EnvService()
        {
            string envPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");
            if (!File.Exists(envPath)) return;

            string[] lines = File.ReadAllLines(envPath);
            foreach (string line in lines)
            {
                string[] parts = line.Split('=', 2);
                if (parts.Length != 2) continue;

                string key = parts[0].Trim();
                string value = parts[1].Trim();

                if (string.IsNullOrEmpty(key)) continue;

                Variables[key] = value;

                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                    Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}

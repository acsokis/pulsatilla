using System.IO;
using System.Reflection;
using System.Text;

namespace Pulsatilla.Wpf;

public static class AppLogger
{
    private static readonly object Sync = new();
    private const int RetentionDays = 14;

    public static string LogDirectory => AppStorage.FilePath("logs");

    public static void Initialize()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
            foreach (var path in Directory.EnumerateFiles(LogDirectory, "pulsatilla-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(path) < cutoff) File.Delete(path);
                }
                catch (Exception) { }
            }

            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
            Write("INFO", $"Application started. Version={version}; Runtime={Environment.Version}; OS={Environment.OSVersion}");
        }
        catch (Exception) { }
    }

    public static void WriteEvent(string level, string message)
    {
        if (level.Equals("PACKET", StringComparison.OrdinalIgnoreCase)) return;
        Write(level, message);
    }

    public static void WriteException(string level, string context, Exception exception) =>
        Write(level, context, exception);

    public static string ReadRecentLines(int maximumLines = 120)
    {
        try
        {
            if (!Directory.Exists(LogDirectory)) return "No application log has been written yet.";
            var files = Directory.EnumerateFiles(LogDirectory, "pulsatilla-*.log")
                .OrderBy(File.GetLastWriteTimeUtc)
                .ToArray();
            var recent = new Queue<string>();
            foreach (var path in files)
            {
                foreach (var line in File.ReadLines(path))
                {
                    recent.Enqueue(line);
                    while (recent.Count > maximumLines) recent.Dequeue();
                }
            }
            return recent.Count == 0 ? "No application log has been written yet." : string.Join(Environment.NewLine, recent);
        }
        catch (Exception ex) { return $"Unable to read application log: {ex.Message}"; }
    }

    private static void Write(string level, string message, Exception? exception = null)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                var path = Path.Combine(LogDirectory, $"pulsatilla-{DateTime.Now:yyyyMMdd}.log");
                var entry = new StringBuilder()
                    .Append('[').Append(DateTimeOffset.Now.ToString("O")).Append("] [")
                    .Append(level.ToUpperInvariant()).Append("] ").AppendLine(message);
                if (exception is not null) entry.AppendLine(exception.ToString());
                File.AppendAllText(path, entry.ToString(), new UTF8Encoding(false));
            }
        }
        catch (Exception) { }
    }
}

using System.Text.RegularExpressions;

namespace StreamDeck.WindowsAgent;

internal static class AgentDiagnosticLog
{
    private static readonly Regex SecretField = new("(\"(?:bearerToken|token|password)\"\\s*:\\s*\")(?:\\\\.|[^\"\\\\])*(?:\"|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static string RedactSecrets(string value) => SecretField.Replace(value, match => match.Groups[1].Value + "***REDACTED***\"");
    private static readonly object Gate = new();
    private static readonly Queue<string> RecentLines = new();
    private const int MaxRecentLines = 200;
    private const long MaxLogBytes = 1_048_576;
    private const int MaxRotatedFiles = 3;

    public static void Write(string message)
    {
        message = RedactSecrets(message);
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StreamDeckAgent");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "windows-agent.log");
            var line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}";
            lock (Gate)
            {
                RecentLines.Enqueue(line.TrimEnd());
                while (RecentLines.Count > MaxRecentLines)
                {
                    RecentLines.Dequeue();
                }

                RotateIfNeeded(path);
                File.AppendAllText(path, line);
            }
        }
        catch
        {
        }
    }

    public static IReadOnlyList<string> ReadRecentLines()
    {
        lock (Gate)
        {
            var lines = RecentLines.ToArray();
            if (lines.Length > 0)
            {
                return lines;
            }

            var path = GetLogPath();
            if (!File.Exists(path))
            {
                return [];
            }

            try
            {
                return File.ReadLines(path).TakeLast(MaxRecentLines).Select(RedactSecrets).ToArray();
            }
            catch
            {
                return [];
            }
        }
    }

    public static void ClearView()
    {
        lock (Gate)
        {
            RecentLines.Clear();
        }
    }

    public static string GetLogDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StreamDeckAgent");
    }

    public static string GetLogPath()
    {
        return Path.Combine(GetLogDirectory(), "windows-agent.log");
    }

    private static void RotateIfNeeded(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < MaxLogBytes)
        {
            return;
        }

        for (var index = MaxRotatedFiles - 1; index >= 1; index--)
        {
            var source = $"{path}.{index}";
            var target = $"{path}.{index + 1}";
            if (File.Exists(source))
            {
                File.Move(source, target, overwrite: true);
            }
        }

        File.Move(path, $"{path}.1", overwrite: true);
    }
}

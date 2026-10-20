using System.Text.Json;
using System.Text.RegularExpressions;

namespace StreamDeck.WindowsAgent.Updates;

public static class ChecksumsParser
{
    private static readonly Regex Sha256LinePattern = new(
        @"^(?<hash>[0-9a-fA-F]{64})\s+[*+]?(?<filename>[^\r\n]+)$",
        RegexOptions.Compiled);

    public static Dictionary<string, string> Parse(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(content))
        {
            return result;
        }

        var trimmed = content.Trim();
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;
                if (root.TryGetProperty("files", out var filesElement) && filesElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var file in filesElement.EnumerateArray())
                    {
                        if (file.TryGetProperty("name", out var nameProp) &&
                            file.TryGetProperty("sha256", out var hashProp))
                        {
                            var name = Path.GetFileName(nameProp.GetString() ?? string.Empty);
                            var hash = hashProp.GetString()?.Trim().ToLowerInvariant();
                            if (!string.IsNullOrEmpty(name) && hash?.Length == 64)
                            {
                                result[name] = hash;
                            }
                        }
                    }
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in root.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            var hash = prop.Value.GetString()?.Trim().ToLowerInvariant();
                            if (hash?.Length == 64)
                            {
                                result[Path.GetFileName(prop.Name)] = hash;
                            }
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Fall back to line-by-line parsing
            }
        }

        using var reader = new StringReader(content);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith('#'))
            {
                continue;
            }

            var match = Sha256LinePattern.Match(trimmedLine);
            if (match.Success)
            {
                var hash = match.Groups["hash"].Value.ToLowerInvariant();
                var rawFileName = match.Groups["filename"].Value.Trim();
                var fileName = Path.GetFileName(rawFileName);
                if (!string.IsNullOrEmpty(fileName))
                {
                    result[fileName] = hash;
                }
            }
        }

        return result;
    }
}

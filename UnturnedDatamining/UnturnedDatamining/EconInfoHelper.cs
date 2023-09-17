using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UnturnedDatamining;
internal static partial class EconInfoHelper
{
    public static async Task PrettyPrintEconAsync(string unturnedPath)
    {
        var econFile = Path.Combine(unturnedPath, "EconInfo.json");
        await using var stream = File.Open(econFile, FileMode.Open);

        string? json;
        using (var jDoc = await JsonDocument.ParseAsync(stream))
        {
            json = JsonSerializer.Serialize(jDoc, new JsonSerializerOptions { WriteIndented = true });
        }

        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        // replaces \u... (unicode escape sequence) to original value
        json = UnicodeEscapeSequence().Replace(json, match =>
        {
            return ((char)int.Parse(match.ValueSpan[2..], NumberStyles.HexNumber)).ToString();
        });

        json = json.Replace(@"\n", "");

        await using var streamWriter = new StreamWriter(stream);

        // reset original content
        stream.SetLength(0);
        streamWriter.BaseStream.Seek(0, SeekOrigin.Begin);

        await streamWriter.WriteAsync(json);
        await streamWriter.FlushAsync();
    }

    [GeneratedRegex(@"\\u([0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex UnicodeEscapeSequence();
}

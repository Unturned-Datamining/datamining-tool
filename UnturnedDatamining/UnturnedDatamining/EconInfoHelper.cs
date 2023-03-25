using System.Text.Json;

namespace UnturnedDatamining;
internal static class EconInfoHelper
{
    public static async Task PrettyPrintEconAsync(string unturnedPath)
    {
        var econFile = Path.Combine(unturnedPath, "EconInfo.json");
        string? json;
        await using var stream = File.Open(econFile, FileMode.Open);

        using (var jDoc = await JsonDocument.ParseAsync(stream))
        {
            json = JsonSerializer.Serialize(jDoc, new JsonSerializerOptions { WriteIndented = true });
        }

        if (!string.IsNullOrEmpty(json))
        {
            await using var streamWriter = new StreamWriter(stream);
            streamWriter.BaseStream.Seek(0, SeekOrigin.Begin);
            await streamWriter.WriteAsync(json);
        }
    }
}

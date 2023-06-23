using System.Text;

namespace UnturnedDatamining.Scenarios;
internal class DownloadDataFromWebsites : IScenario
{
    private static readonly HashSet<(Uri uri, string fileName)> s_DataToDownload = new()
    {
        (new("https://smartlydressedgames.com/UnturnedLiveConfig.dat"), "UnturnedLiveConfig.dat"),
        (new("https://smartlydressedgames.com/UnturnedHostBans/index.html"), "UnturnedHostBans.md"),
    };

    private string m_SummaryOfDownloadedFiles = null!;

    public async Task<bool> StartAsync(string unturnedPath, string[] args)
    {
        // todo: add
        // https://smartlydressedgames.com/UnturnedHostBans/filters.bin ( fallback http://chaotic-island-paradise.s3-website-us-west-2.amazonaws.com/UnturnedHostBans/filters.bin )

        using var client = new HttpClient();

        var tasks = s_DataToDownload
            .Select(tuple => DownloadDataAndWriteToFileAsync(client, tuple.uri, unturnedPath, tuple.fileName));

        var sb = new StringBuilder();
        foreach ((string fileName, bool isFileWritten) in await Task.WhenAll(tasks))
        {
            if (isFileWritten)
            {
                sb.Append(fileName).Append(',');
            }
        }

        sb.Remove(sb.Length - 1, 1); // remove last ','
        m_SummaryOfDownloadedFiles = sb.ToString();

        return true;
    }

    private static async Task<(string fileName, bool isFileWritten)> DownloadDataAndWriteToFileAsync(HttpClient client, Uri uri, string unturnedPath, string fileName)
    {
        var response = await client.GetStringAsync(uri);
        var pathToFile = Path.Combine(unturnedPath, fileName);

        if (!File.Exists(pathToFile))
        {
            await File.WriteAllTextAsync(pathToFile, response);
            return new(fileName, true);
        }

        if (await File.ReadAllTextAsync(pathToFile) == response)
        {
            // content is same, ignoring
            return new(fileName, false);
        }

        await File.WriteAllTextAsync(pathToFile, response);
        return new(fileName, true);
    }

    public async Task WriteCommitToFileAsync(string path, string fileName)
    {
        await File.WriteAllTextAsync(Path.Combine(path, fileName), $"{DateTime.UtcNow:dd MMMM yyyy} - Updated {m_SummaryOfDownloadedFiles}");
    }
}

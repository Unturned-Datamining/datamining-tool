namespace UnturnedDatamining.Scenarios;
internal class DownloadDataFromWebsites : IScenario
{
    public async Task<bool> StartAsync(string unturnedPath, string[] args)
    {
        // todo: add
        // https://smartlydressedgames.com/UnturnedHostBans/index.html
        // https://smartlydressedgames.com/UnturnedHostBans/filters.bin ( fallback http://chaotic-island-paradise.s3-website-us-west-2.amazonaws.com/UnturnedHostBans/filters.bin )

        using var client = new HttpClient();
        var response = await client.GetStringAsync("https://smartlydressedgames.com/UnturnedLiveConfig.dat");

        await File.WriteAllTextAsync(Path.Combine(unturnedPath, "UnturnedLiveConfig.dat"), response);
        return true;
    }

    public async Task WriteCommitToFileAsync(string path, string fileName)
    {
        await File.WriteAllTextAsync(Path.Combine(path, fileName), $"{DateTime.UtcNow:dd MMMM yyyy} - Updated `UnturnedLiveConfig.dat`");
    }
}

using SDG.HostBans;
using SDG.NetPak;
using System.Text;
using System.Text.RegularExpressions;

namespace UnturnedDatamining.Scenarios;
internal partial class DownloadDataFromWebsites : IScenario
{
    private static readonly HashSet<(Uri uri, string fileName, IDataFormatter dataFormatter)> s_DataToDownload = new()
    {
        (new("https://smartlydressedgames.com/UnturnedLiveConfig.dat"), "UnturnedLiveConfig.dat", StringDataFormatter.Instance),
        (new("https://smartlydressedgames.com/UnturnedHostBans/index.html"), "UnturnedHostBans.md", StringDataFormatter.Instance),
        (new("https://smartlydressedgames.com/UnturnedHostBans/filters.bin"), "Filters.md", MarkDownTableDataFormatter.Instance),
    };

    private string m_SummaryOfDownloadedFiles = null!;

    public async Task<bool> StartAsync(string unturnedPath, string[] args)
    {
        using var client = new HttpClient();

        var tasks = s_DataToDownload
            .Select(tuple
                => tuple.dataFormatter.DownloadAndFormatToFileAsync(client, tuple.uri, unturnedPath, tuple.fileName));

        var sb = new StringBuilder();
        foreach ((string fileName, bool isFileWritten) in await Task.WhenAll(tasks))
        {
            if (isFileWritten)
            {
                sb.Append(fileName).Append(',');
            }
        }

        // check if stringBuilder was appended atleast once
        if (sb.Length == 0)
        {
            return false;
        }

        sb.Remove(sb.Length - 1, 1); // remove last ','
        m_SummaryOfDownloadedFiles = sb.ToString();

        return true;
    }

    public async Task WriteCommitToFileAsync(string path, string fileName)
    {
        await File.WriteAllTextAsync(Path.Combine(path, fileName), $"{DateTime.UtcNow:dd MMMM yyyy} - Updated {m_SummaryOfDownloadedFiles}");
    }

    private interface IDataFormatter
    {
        Task<(string fileName, bool isFileWritten)> DownloadAndFormatToFileAsync(HttpClient client, Uri uri, string unturnedPath, string fileName);
    }

    private class StringDataFormatter : IDataFormatter
    {
        public static StringDataFormatter Instance { get; } = new();

        public async Task<(string fileName, bool isFileWritten)> DownloadAndFormatToFileAsync(HttpClient client, Uri uri, string unturnedPath, string fileName)
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
    }

    private partial class MarkDownTableDataFormatter : IDataFormatter
    {
        public static MarkDownTableDataFormatter Instance { get; } = new();

        public async Task<(string fileName, bool isFileWritten)> DownloadAndFormatToFileAsync(HttpClient client, Uri uri, string unturnedPath, string fileName)
        {
            var response = await client.GetByteArrayAsync(uri);

            var reader = new NetPakReader();
            reader.SetBuffer(response);

            var filters = new HostBanFilters();
            filters.ReadConfiguration(reader);

            var filtersEmpty = filters.addresses.Count is 0
                && filters.descriptionRegexes.Count is 0
                && filters.nameRegexes.Count is 0
                && filters.thumbnailRegexes.Count is 0
                && filters.steamIds.Count is 0;

            var pathToFile = Path.Combine(unturnedPath, fileName);
            if (filtersEmpty && File.Exists(pathToFile))
            {
                // looks like new version of filters was out that we don't understand
                return (fileName, false);
            }

            var sb = new StringBuilder();
            sb.AppendLine("# Host bans").AppendLine();

            sb.AppendLine("## IPv4 filters");
            var addresses = filters.addresses.Select(x => new HostBanAddress(x.ToString(), x.flags));
            sb.AppendLine(addresses.ToMarkdownTable()).AppendLine();

            sb.AppendLine("## Name filters");
            var names = filters.nameRegexes.Select(x => new HostBanRegex(x.regex.ToString(), x.flags));
            sb.AppendLine(names.ToMarkdownTable()).AppendLine();

            sb.AppendLine("## Description filters");
            var descriptions = filters.descriptionRegexes.Select(x => new HostBanRegex(x.regex.ToString(), x.flags));
            sb.AppendLine(descriptions.ToMarkdownTable()).AppendLine();

            sb.AppendLine("## Thumbnail filters");
            var thumbnails = filters.thumbnailRegexes.Select(x
                => new HostBanRegexPreview(x.regex.ToString(), GetIconPreview(x), x.flags));
            sb.AppendLine(thumbnails.ToMarkdownTable()).AppendLine();

            sb.AppendLine("## SteamId filters");
            var steamIds = filters.steamIds.Select(x => new HostBanSteamId(x.steamId.ToString(), x.flags));
            sb.AppendLine(steamIds.ToMarkdownTable());

            var value = sb.ToString();

            if (await File.ReadAllTextAsync(pathToFile) == value)
            {
                // content is same, ignoring
                return new(fileName, false);
            }

            await File.WriteAllTextAsync(pathToFile, value);
            return new(fileName, true);
        }

        private static string GetIconPreview(HostBanRegexFilter filter)
        {
            var match = ExtractLink().Match(filter.regex.ToString());
            if (!match.Success)
            {
                return string.Empty;
            }

            var link = match.Value;
            return $"![{link}]({link})";
        }

        private record struct HostBanAddress(string Address, EHostBanFlags BanFlags);
        private record struct HostBanSteamId(string SteamId, EHostBanFlags BanFlags);
        private record struct HostBanRegex(string Regex, EHostBanFlags BanFlags);
        private record struct HostBanRegexPreview(string Regex, string IconPreview, EHostBanFlags BanFlags);

        [GeneratedRegex(@"\b(?:https?://)\S+\b")]
        private static partial Regex ExtractLink();
    }
}

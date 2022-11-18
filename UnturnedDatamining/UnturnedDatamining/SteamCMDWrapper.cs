using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Tar;
using ICSharpCode.SharpZipLib.Zip;
using System.IO.Compression;

namespace UnturnedDatamining;
internal class SteamCMDWrapper
{
    private readonly string m_Path;

    public string SteamCMDPath { get; private set; } = null!;

    internal SteamCMDWrapper(string path)
    {
        m_Path = path;
    }

    public async Task Install()
    {
        var isWindows = OperatingSystem.IsWindows();
        var steamCmdPath = Path.Combine(m_Path, "steamcmd.") + (isWindows ? "exe" : "sh");
        if (Directory.Exists(m_Path)
            && File.Exists(steamCmdPath))
        {
            SteamCMDPath = steamCmdPath;
            Console.WriteLine("SteamCMD cached, skipping installing...");
            return;
        }

        Console.WriteLine("Installing SteamCMD...");

        Uri steamcmdUri;

        if (isWindows)
        {
            steamcmdUri = new("https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip");
        }
        else
        {
            steamcmdUri = OperatingSystem.IsLinux()
                ? (new("https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz"))
                : throw new PlatformNotSupportedException("SteamCMD cannot run on your platform");
        }

        using var httpClient = new HttpClient();
        var file = await httpClient.GetByteArrayAsync(steamcmdUri);

        await using var ms = new MemoryStream(file, false);
        if (isWindows)
        {
            await using var zip = new ZipInputStream(ms);
            while (zip.GetNextEntry() is ZipEntry entry)
            {
                var entryFileName = entry.Name;
                var buffer = new byte[4096];

                var fullZipToPath = Path.Combine(m_Path, entryFileName);
                var directoryName = Path.GetDirectoryName(fullZipToPath);
                if (directoryName?.Length > 0)
                    Directory.CreateDirectory(directoryName);

                if (Path.GetFileName(fullZipToPath).Length == 0)
                {
                    continue;
                }

                await using var streamWriter = File.Create(fullZipToPath);
                StreamUtils.Copy(zip, streamWriter, buffer);
            }
        }
        else
        {
            await using var gzip = new GZipStream(ms, CompressionMode.Decompress);
            using var tar = TarArchive.CreateInputTarArchive(gzip);

            tar.ExtractContents(m_Path);
        }

        SteamCMDPath = Path.Combine(m_Path, "steamcmd." + (isWindows ? "exe" : "sh"));
    }
}

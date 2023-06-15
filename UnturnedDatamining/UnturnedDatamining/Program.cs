using UnturnedDatamining.Scenarios;

namespace UnturnedDatamining;
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Wrong usage. Correct usage: ./UnturnedDatamining.exe <unturnedPath> <scenarioName> [args]");
            return 1;
        }

        var unturnedPath = Path.GetFullPath(args[0]);
        if (!Directory.Exists(unturnedPath))
        {
            Console.WriteLine("Unturned path is invalid");
            return 1;
        }

        // get scenario
        var scenarioName = args[1];
        IScenario scenario = scenarioName.ToLower() switch
        {
            "decompile" => new DownloadAndDecompileGame(),
            "websites" => new DownloadDataFromWebsites(),
            _ => throw new Exception("Unknown scenario to start")
        };

        var isSuccess = await scenario.StartAsync(unturnedPath, args);
        if (!isSuccess)
        {
            return 1;
        }

        await scenario.WriteCommitToFileAsync(unturnedPath, ".commit");
        return 0;
    }
}
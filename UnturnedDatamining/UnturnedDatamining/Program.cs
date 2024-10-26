using UnturnedDatamining.Scenarios;

namespace UnturnedDatamining;
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
#if DEBUG
        if (args.Length == 0)
        {
            Console.WriteLine("Select scenario to run:");
            Console.WriteLine("1 - Decompile game");
            Console.WriteLine("2 - Download data from SDG website");

            var scenarioArg = Console.ReadLine() switch
            {
                "1" => "decompile",
                "2" => "websites",
                _ => throw new Exception("Unknown scenario")
            };

            args = new[] { @"C:\Program Files (x86)\Steam\steamapps\common\Unturned", scenarioArg, "--client", "--force" };
        }
#endif

        if (args.Length < 2)
        {
            Console.WriteLine("Wrong usage. Correct usage: ./UnturnedDatamining.exe <path> <scenarioName> [args]");
            return 1;
        }

        var path = Path.GetFullPath(args[0]);
        if (!Directory.Exists(path))
        {
            Console.WriteLine("Path is invalid");
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

        var isSuccess = await scenario.StartAsync(path, args);
        if (!isSuccess)
        {
            return 1;
        }

        await scenario.WriteCommitToFileAsync(path, ".commit");
        return 0;
    }
}
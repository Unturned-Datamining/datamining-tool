using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.CSharp.ProjectDecompiler;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.Util;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Text.Json;
using System.Text.Json.Nodes;
using ValveKeyValue;

namespace UnturnedDatamining;
internal class Program
{
    private static SteamCMDWrapper? SteamCMD { get; set; }

    private static async Task<int> Main(string[] args)
    {
        var installSteamCmd = !args.Any(x => x.Equals("--nosteam", StringComparison.OrdinalIgnoreCase));
        var force = args.Any(x => x.Equals("--force", StringComparison.OrdinalIgnoreCase));

        var unturnedPath = Environment.CurrentDirectory;
        if (args.Length >= 1 && Directory.Exists(args[0]))
        {
            unturnedPath = Path.GetFullPath(args[0]);
        }

        if (!Directory.Exists(unturnedPath))
        {
            Console.WriteLine("Unturned path is invalid");
            return 1;
        }

        if (installSteamCmd)
        {
            var steamcmdPath = Path.Combine(unturnedPath, "SteamCMD");
            if (!Directory.Exists(steamcmdPath))
            {
                Directory.CreateDirectory(steamcmdPath);
            }
            SteamCMD = new(steamcmdPath);
            await SteamCMD.Install();

            Console.WriteLine("Updating the game");

            var process = Process.Start(SteamCMD.SteamCMDPath, $"+force_install_dir {unturnedPath} +login anonymous +app_update 1110390 -beta preview +quit");
            await process.WaitForExitAsync();
            Console.WriteLine("SteamCMD exited with code: " + process.ExitCode);
            if (process.ExitCode != 0
                && (!OperatingSystem.IsWindows() || process.ExitCode != 7))
            {
                // error occurred with steamCMD
                return process.ExitCode;
            }
        }

        var (isNewBuild, buildId) = await CheckIsNewBuild(unturnedPath);
        if (!isNewBuild && !force)
        {
            Console.WriteLine("New build is not detected, skipping...");
            return 0;
        }

        await WriteCommit(unturnedPath, buildId ?? "???");
        await PrettyPrintEcon(unturnedPath);

        var outputPath = Path.Combine(unturnedPath, "Assembly-CSharp");
        if (Directory.Exists(outputPath))
        {
            Directory.Delete(outputPath, true);
        }
        else
        {
            Directory.CreateDirectory(outputPath);
        }
        await DecompileDll(unturnedPath, outputPath);
        return 0;
    }

    private static async Task WriteCommit(string basePath, string buildId)
    {
        var node = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(basePath, "Status.json")))!["Game"]!;
        var version = $"3.{node["Major_Version"]}.{node["Minor_Version"]}.{node["Patch_Version"]}";

        await File.WriteAllTextAsync(Path.Combine(basePath, ".commit"), $"{DateTime.UtcNow:dd MMMM yyyy} - Version {version} ({buildId})");
    }

    private static async Task<(bool isNewBuild, string? buildId)> CheckIsNewBuild(string basePath)
    {
        var appdataPath = Path.Combine(basePath, "steamapps", "appmanifest_1110390.acf");
        if (!File.Exists(appdataPath))
        {
            throw new FileNotFoundException($"Required file is not found", "appmanifest_1110390.acf");
        }

        await using var file = File.OpenRead(appdataPath);
        var kv = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
        var obj = kv.Deserialize(file);

        var buildId = obj["buildid"].ToString();

        var currentBuildIdPath = Path.Combine(basePath, ".buildid");
        if (!File.Exists(currentBuildIdPath))
        {
            await File.WriteAllTextAsync(currentBuildIdPath, buildId);
            return (true, buildId);
        }

        if (await File.ReadAllTextAsync(currentBuildIdPath) != buildId)
        {
            await File.WriteAllTextAsync(currentBuildIdPath, buildId);
            return (true, buildId);
        }
        return (false, null);
    }

    private static async Task PrettyPrintEcon(string unturnedPath)
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

    private static async Task DecompileDll(string unturnedPath, string outputPath)
    {
        Console.WriteLine("Starting decompiling Unturned");

        // On Linux dedicated server folder "Unturned_Data" changed to "Unturned_Headless_Data"
        var dataFolderName = OperatingSystem.IsLinux() ? "Unturned_Headless_Data" : "Unturned_Data";
        var refPath = Path.Combine(unturnedPath, dataFolderName, "Managed");
        var dllPath = Path.Combine(refPath, "Assembly-CSharp.dll");

        await using var stream = File.OpenRead(dllPath);
        using var module = new PEFile("Assembly-CSharp", stream);

        var settings = GetSettings(module);
        var resolver = new UniversalAssemblyResolver(dllPath, true, module.DetectTargetFrameworkId());
        resolver.AddSearchDirectory(refPath);

        var directories = new HashSet<string>(Platform.FileNameComparer);

        var metadata = module.Metadata;
        var files = metadata.GetTopLevelTypeDefinitions()
            .Where(td => IncludeTypeWhenDecompilingProject(module, td, settings))
            .GroupBy(h =>
            {
                var type = metadata.GetTypeDefinition(h);
                var file = WholeProjectDecompiler.CleanUpFileName(metadata.GetString(type.Name)) + ".cs";
                var ns = metadata.GetString(type.Namespace);
                if (string.IsNullOrEmpty(ns))
                {
                    return file;
                }

                var dir = WholeProjectDecompiler.CleanUpDirectoryName(ns);
                if (directories.Add(dir))
                    Directory.CreateDirectory(Path.Combine(outputPath, dir));
                return Path.Combine(dir, file);
            }, StringComparer.OrdinalIgnoreCase).ToList();

        var ts = new DecompilerTypeSystem(module, resolver, settings);
        Parallel.ForEach(Partitioner.Create(files, loadBalance: true), (file, _) =>
        {
            using var writer = new StreamWriter(Path.Combine(outputPath, file.Key));
            try
            {
                var decompiler = new CSharpDecompiler(ts, settings);
                var syntaxTree = decompiler.DecompileTypes(file.ToArray());
                syntaxTree.AcceptVisitor(new CSharpOutputVisitor(writer, settings.CSharpFormattingOptions));
            }
            catch (Exception innerException) when (innerException is not (OperationCanceledException or DecompilerException))
            {
                throw new DecompilerException(module, $"Error decompiling for '{file.Key}'", innerException);
            }
        });

        Console.WriteLine("Decompiled Assembly-CSharp.dll successfully");
    }

    private static bool IncludeTypeWhenDecompilingProject(PEFile module, TypeDefinitionHandle type, DecompilerSettings settings)
    {
        var metadata = module.Metadata;
        var typeDef = metadata.GetTypeDefinition(type);
        if (metadata.GetString(typeDef.Name) == "<Module>" || CSharpDecompiler.MemberIsHidden(module, type, settings))
        {
            return false;
        }

        return metadata.GetString(typeDef.Namespace) != "XamlGeneratedNamespace" || metadata.GetString(typeDef.Name) != "GeneratedInternalTypeHelper";
    }

    private static DecompilerSettings GetSettings(PEFile module)
    {
        var settings = new DecompilerSettings(LanguageVersion.CSharp10_0)
        {
            ThrowOnAssemblyResolveErrors = true,
            RemoveDeadCode = true,
            RemoveDeadStores = true,
            UseSdkStyleProjectFormat = WholeProjectDecompiler.CanUseSdkStyleProjectFormat(module),
            UseNestedDirectoriesForNamespaces = false,
            FileScopedNamespaces = true
        };
        settings.CSharpFormattingOptions.IndentationString = new string(' ', 4);
        return settings;
    }
}
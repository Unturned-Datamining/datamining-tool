using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.CSharp.ProjectDecompiler;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.Util;
using System.Collections.Concurrent;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json.Nodes;
using ValveKeyValue;

namespace UnturnedDatamining.Scenarios;
internal class DownloadAndDecompileGame : IScenario
{
    private static readonly HashSet<string> s_DecompileDllNames = new()
    {
        "Assembly-CSharp",
        "SDG.HostBans.Runtime",
        "SDG.NetPak.Runtime",
        "SDG.NetTransport",
        "Unturned.LiveConfig.Runtime",
        "UnityEx",
        "SystemEx",
        "UnturnedDat",
    };

    private string m_BuildId = "???";
    private bool IsDedicatedServer { get; set; }

    public async Task<bool> StartAsync(string unturnedPath, string[] args)
    {
        var force = args.Any(x => x.Equals("--force", StringComparison.OrdinalIgnoreCase));
        IsDedicatedServer = !args.Any(x => x.Equals("--client", StringComparison.OrdinalIgnoreCase));

        var (isNewBuild, buildId) = await CheckIsNewBuild(unturnedPath);
        if (!isNewBuild && !force)
        {
            Console.WriteLine("New build is not detected, skipping...");
            return false;
        }

        m_BuildId = buildId ?? "???";
        await EconInfoHelper.PrettyPrintEconAsync(unturnedPath);
        await ParseAndWriteUnityVersion(unturnedPath);

        foreach (var name in s_DecompileDllNames)
        {
            var normalizedName = name.Replace('.', '-');
            var outputPath = Path.Combine(unturnedPath, normalizedName);

            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
            else
            {
                Directory.CreateDirectory(outputPath);
            }

            await DecompileDll(unturnedPath, name, outputPath);
        }

        return true;
    }

    public async Task WriteCommitToFileAsync(string path, string fileName)
    {
        if (m_BuildId is null)
            throw new Exception($"{nameof(m_BuildId)} is not initialized, maybe you forgot to call {nameof(StartAsync)}?");

        var node = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(path, "Status.json")))!["Game"]!;
        var version = $"3.{node["Major_Version"]}.{node["Minor_Version"]}.{node["Patch_Version"]}";

        await File.WriteAllTextAsync(Path.Combine(path, fileName), $"{DateTime.UtcNow:dd MMMM yyyy} - Version {version} ({m_BuildId})");
    }

    private async Task ParseAndWriteUnityVersion(string basePath)
    {
        var unturnedDataDirName = IsDedicatedServer ? "Unturned_Headless_Data" : "Unturned_Data";
        var globalGameManagersFilePath = Path.Combine(basePath, unturnedDataDirName, "globalgamemanagers");

        if (File.Exists(globalGameManagersFilePath))
        {
            await using var file = File.OpenRead(globalGameManagersFilePath);
            using var binaryReader = new BinaryReader(file);

            // skip header
            file.Seek(48, SeekOrigin.Begin);

            var unityVersion = ReadStringZeroTerm(binaryReader);

            // sanity checks
            if (string.IsNullOrEmpty(unityVersion))
            {
                Console.WriteLine("Failed to read unity version, maybe format of file is changed?");
                return;
            }

            // check if year is correct
            if (!unityVersion.StartsWith("202"))
            {
                Console.WriteLine("Unity version doesn't start with '202', maybe format of file is changed?");
                return;
            }

            var unityVersionFilePath = Path.Combine(basePath, ".unityversion");
            await File.WriteAllTextAsync(unityVersionFilePath, unityVersion);
        }

        static string? ReadStringZeroTerm(BinaryReader reader)
        {
            // read string as "C"
            Span<byte> bytes = stackalloc byte[32];
            for (var i = 0; i < bytes.Length; i++)
            {
                var bt = reader.ReadByte();
                if (bt == 0)
                {
                    return Encoding.UTF8.GetString(bytes[..i]);
                }
                bytes[i] = bt;
            }

            return null;
        }
    }

    private async Task<(bool isNewBuild, string? buildId)> CheckIsNewBuild(string basePath)
    {
        var id = IsDedicatedServer ? "1110390" : "304930";
        var appdataPath = Path.Combine(basePath, "steamapps", $"appmanifest_{id}.acf");
        if (!File.Exists(appdataPath))
        {
            throw new FileNotFoundException("Required file is not found", $"appmanifest_{id}.acf");
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

    private async Task DecompileDll(string unturnedPath, string dllName, string outputPath)
    {
        Console.WriteLine("Starting decompiling " + dllName);

        // On Linux dedicated server folder "Unturned_Data" changed to "Unturned_Headless_Data"
        var dataFolderName = IsDedicatedServer ? "Unturned_Headless_Data" : "Unturned_Data";
        var refPath = Path.Combine(unturnedPath, dataFolderName, "Managed");
        var dllPath = Path.Combine(refPath, dllName + ".dll");

        await using var stream = File.OpenRead(dllPath);
        using var module = new PEFile(dllName, stream);

        var settings = GetSettings();
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

        Console.WriteLine($"Decompiled {dllName}.dll successfully");
        ReadmeGenerator.GenerateReadmeFiles(outputPath);
    }

    private static bool IncludeTypeWhenDecompilingProject(PEFile module, TypeDefinitionHandle type, DecompilerSettings settings)
    {
        var metadata = module.Metadata;
        var typeDef = metadata.GetTypeDefinition(type);
        return metadata.GetString(typeDef.Name) != "<Module>" && !CSharpDecompiler.MemberIsHidden(module, type, settings)
            && (metadata.GetString(typeDef.Namespace) != "XamlGeneratedNamespace" || metadata.GetString(typeDef.Name) != "GeneratedInternalTypeHelper");
    }

    private static DecompilerSettings GetSettings()
    {
        var settings = new DecompilerSettings(LanguageVersion.CSharp10_0)
        {
            ThrowOnAssemblyResolveErrors = true,
            RemoveDeadCode = true,
            RemoveDeadStores = true,
            UseNestedDirectoriesForNamespaces = false,
            FileScopedNamespaces = true,
        };
        settings.CSharpFormattingOptions.IndentationString = new string(' ', 4);
        return settings;
    }
}

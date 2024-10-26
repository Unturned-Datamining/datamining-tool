using System.Text.Encodings.Web;
using System.Text.Json;
using SDG.Provider;

namespace UnturnedDatamining;
internal static partial class EconInfoHelper
{
    public static async Task PrettyPrintEconAsync(string unturnedPath)
    {
        var econFile = Path.Combine(unturnedPath, "EconInfo.bin");

        Dictionary<int, UnturnedEconInfo> econInfos = new();
        Dictionary<int, List<int>> bundleContents = new();

        await using (var inputStream = File.Open(econFile, FileMode.Open))
        {
            if (!ParseFile(inputStream, econInfos, bundleContents))
            {
                return;
            }
        }

        await using var outputStream = File.Open(Path.Combine(unturnedPath, "EconInfo.json"), FileMode.Create);
        var options = new JsonSerializerOptions()
        {
            // UnturnedEconInfo uses fields
            IncludeFields = true,
            WriteIndented = true,
            // unsafe <_<
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        await JsonSerializer.SerializeAsync(outputStream, new
        {
            econInfo = econInfos,
            bundleContent = bundleContents
        }, options);
    }

    private static bool ParseFile(Stream stream, Dictionary<int, UnturnedEconInfo> econInfos, Dictionary<int, List<int>> bundleContents)
    {
        using var reader = new BinaryReader(stream);

        int version = reader.ReadInt32();

        if (version != 1)
        {
            Console.WriteLine("Version format of EconInfo.bin was changed");
            return false;
        }

        int count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            var econInfo = new UnturnedEconInfo
            {
                name = reader.ReadString(),
                display_type = reader.ReadString(),
                description = reader.ReadString(),
                name_color = reader.ReadString(),
                itemdefid = reader.ReadInt32(),
                marketable = reader.ReadBoolean(),
                scraps = reader.ReadInt32(),
                target_game_asset_guid = new Guid(reader.ReadBytes(16)),
                item_skin = reader.ReadInt32(),
                item_effect = reader.ReadInt32(),
                quality = (UnturnedEconInfo.EQuality)reader.ReadInt32(),
                econ_type = reader.ReadInt32()
            };

            econInfos.TryAdd(econInfo.itemdefid, econInfo);
        }

        count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            int itemDefId = reader.ReadInt32();
            int itemCounts = reader.ReadInt32();

            List<int> contents = new List<int>(itemCounts);
            for (var j = 0; j < itemCounts; j++)
            {
                contents.Add(reader.ReadInt32());
            }

            bundleContents.TryAdd(itemDefId, contents);
        }

        return true;
    }
}

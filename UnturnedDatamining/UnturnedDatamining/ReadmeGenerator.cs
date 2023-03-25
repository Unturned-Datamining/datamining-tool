using System.Text;

namespace UnturnedDatamining;
internal static class ReadmeGenerator
{
    public static void GenerateReadmeFiles(string path)
    {
        var directories = Directory.GetDirectories(path);
        Parallel.ForEach(directories, path =>
        {
            var directoryName = new DirectoryInfo(path).Name;
            var files = Directory.GetFiles(path);
            Array.Sort(files);

            var sb = new StringBuilder();
            sb.Append("# ").AppendLine(directoryName);
            sb.Append("## ").AppendLine("Content");

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);

                sb.Append("- [").Append(fileName).Append("](").Append(fileName).AppendLine(")");
            }

            var readmePath = Path.Combine(path, files.Length > 1000 ? "0README.md" : "README.md");
            File.WriteAllText(readmePath, sb.ToString());
        });
    }
}

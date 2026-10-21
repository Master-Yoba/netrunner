using System.IO.Compression;
using Netrunner.Match;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// playtest-summary &lt;session-directory|session.zip|ui-diagnostics.jsonl&gt;: the U6
    /// playtest protocol's numbers for one recorded client session.
    /// </summary>
    private static int RunPlaytestSummary(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("playtest-summary requires one session directory, zip or ui-diagnostics.jsonl.");
            output.WriteLine(PlaytestSummary.Read(DiagnosticLines(args[1])).Format());
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or System.Text.Json.JsonException or InvalidDataException)
        {
            error.WriteLine($"playtest-summary failed: {exception.Message}");
            return 2;
        }
    }

    private static string[] DiagnosticLines(string path)
    {
        const string name = "ui-diagnostics.jsonl";
        if (Directory.Exists(path))
            return File.ReadAllLines(Path.Combine(path, name));
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using ZipArchive archive = ZipFile.OpenRead(path);
            ZipArchiveEntry entry = archive.Entries.FirstOrDefault(candidate => candidate.Name == name)
                ?? throw new ArgumentException($"{path} has no {name}.");
            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd().Split('\n');
        }
        return File.ReadAllLines(path);
    }
}

using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    private static int RunPolicyPerformance(string[] args, TextWriter output, TextWriter error)
    {
        bool json = args.Contains("--json", StringComparer.Ordinal);
        try
        {
            if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("ai-performance requires a frozen benchmark manifest.");
            string? destination = null;
            string? hardware = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 2; index < args.Length; index++)
            {
                string flag = args[index];
                if (!seen.Add(flag)) throw new ArgumentException($"Duplicate argument {flag}.");
                switch (flag)
                {
                    case "--output" when index + 1 < args.Length: destination = args[++index]; break;
                    case "--hardware" when index + 1 < args.Length: hardware = args[++index]; break;
                    case "--json": break;
                    default: throw new ArgumentException($"Invalid ai-performance argument {flag}.");
                }
            }
            if (string.IsNullOrWhiteSpace(destination) || string.IsNullOrWhiteSpace(hardware))
                throw new ArgumentException("--output <new directory> and --hardware <CPU/machine description> are required.");
            var file = new FileInfo(args[1]);
            if (file.Length > 32 * 1024 * 1024) throw new ArgumentException("Manifest exceeds 32 MiB.");
            AiBenchmarkManifest manifest = EngineJson.Deserialize<AiBenchmarkManifest>(File.ReadAllText(file.FullName));
            manifest.Validate();
            if (manifest.ContentMode != "official" || CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
                throw new ArgumentException("Performance evidence requires previously installed official metadata.");
            PolicyPerformanceReport report = PolicyPerformanceRunner.RunAsync(CardDataResource.LoadEmbedded(), GatewayBehaviorCatalog.Create(),
                manifest, destination, hardware, BuildMetadata.Current.ApplicationVersion).GetAwaiter().GetResult();
            output.WriteLine(json ? EngineJson.Serialize(report)
                : $"ai-performance={(report.Passed ? "pass" : "failed")} decisions={report.Samples.Length} report={Path.Combine(destination, "performance.json")}");
            return report.Passed ? 0 : 1;
        }
        catch (Exception exception) when (IsCommandFailure(exception))
        {
            if (json) output.WriteLine(EngineJson.Serialize(new { Status = "invalid", exception.Message }));
            else error.WriteLine($"ai-performance: {exception.Message}");
            return 2;
        }
    }
}

using System.Globalization;
using System.Text.Json;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    private static int RunAiBenchmark(string[] args, TextWriter output, TextWriter error)
    {
        bool json = args.Contains("--json", StringComparer.Ordinal);
        try
        {
            bool initialize = args.Length > 1 && args[1] == "init";
            int inputIndex = initialize ? 2 : 1;
            if (args.Length <= inputIndex || args[inputIndex].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("ai-benchmark requires a manifest, or init smoke|full|developer, and --output <new path>.");
            string? destination = null;
            int workers = 1;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int index = inputIndex + 1; index < args.Length; index++)
            {
                if (!seen.Add(args[index])) throw new ArgumentException($"Duplicate argument '{args[index]}'.");
                switch (args[index])
                {
                    case "--json": break;
                    case "--output" when index + 1 < args.Length:
                        destination = args[++index];
                        break;
                    case "--workers" when !initialize && index + 1 < args.Length:
                        if (!int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out workers) || workers is < 1 or > 64)
                            throw new ArgumentException("--workers requires an integer from 1 to 64.");
                        break;
                    default: throw new ArgumentException($"Unsupported benchmark argument '{args[index]}'.");
                }
            }
            if (string.IsNullOrWhiteSpace(destination)) throw new ArgumentException("--output <new path> is required.");
            AiBenchmarkManifest? manifest = null;
            string contentMode;
            if (initialize)
            {
                if (args[inputIndex] is not ("smoke" or "full" or "developer")) throw new ArgumentException("Unknown benchmark suite.");
                contentMode = args[inputIndex] == "developer" ? "developer" : "official";
            }
            else
            {
                var file = new FileInfo(args[inputIndex]);
                if (file.Length > 32 * 1024 * 1024) throw new ArgumentException("Benchmark manifest exceeds 32 MiB.");
                manifest = EngineJson.Deserialize<AiBenchmarkManifest>(File.ReadAllText(file.FullName));
                manifest.Validate();
                contentMode = manifest.ContentMode;
            }
            AiBenchmarkRunner runner;
            if (contentMode == "developer")
            {
                DeveloperMatchCards developer = DeveloperMatchCards.Create();
                runner = new(developer.Data, developer.Behavior);
            }
            else
            {
                if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
                    throw new ArgumentException("Official benchmarks need previously provisioned official content embedded at build time. Benchmark execution never downloads content.");
                runner = new(CardDataResource.LoadEmbedded(), GatewayBehaviorCatalog.Create());
            }
            if (initialize)
            {
                manifest = BenchmarkSuites.CreateContract(runner, args[inputIndex]);
                using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                JsonSerializer.Serialize(stream, manifest, EngineJson.Options);
                output.WriteLine(json ? EngineJson.Serialize(new { schemaVersion = 1, status = "created", manifest.SuiteId, games = manifest.Games.Length })
                    : $"Created {manifest.SuiteId}: {manifest.Games.Length} games.");
                return 0;
            }
            AiBenchmarkReport report = runner.RunAsync(manifest!, destination, workers, BuildMetadata.Current.ApplicationVersion).GetAwaiter().GetResult();
            if (json) output.WriteLine(EngineJson.Serialize(report));
            else output.WriteLine($"{report.SuiteId}: {report.Aggregate.Terminal}/{report.Aggregate.Games} terminal, {report.Aggregate.TurnCaps} turn caps, {report.Aggregate.Failures} failures. Report: {Path.Combine(destination, "report.json")}");
            // A turn cap is diagnosed evidence requiring review, not a green
            // result. The eventual M5 gate applies its explicit reviewed-cap allowance.
            return report.Aggregate.Terminal == report.Aggregate.Games ? 0 : 1;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            if (json) output.WriteLine(EngineJson.Serialize(new { schemaVersion = 1, kind = "m5-ai-benchmark-error", status = "invalid", message = exception.Message }));
            else error.WriteLine($"ai-benchmark: {exception.Message}");
            return 2;
        }
    }
}

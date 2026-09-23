using System.Globalization;
using System.Text.Json;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    private static int RunAiTactics(string[] args, TextWriter output, TextWriter error)
    {
        bool json = args.Contains("--json", StringComparer.Ordinal);
        try
        {
            if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("ai-tactics requires init or a manifest, and --output <new path>.");
            bool initialize = args[1] == "init";
            bool verifyFixtures = false;
            int workers = 1;
            string? destination = null;
            string? corpProfile = null;
            string? runnerProfile = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 2; index < args.Length; index++)
            {
                if (!seen.Add(args[index])) throw new ArgumentException($"Duplicate argument '{args[index]}'.");
                switch (args[index])
                {
                    case "--json": break;
                    case "--corp-profile" when initialize && index + 1 < args.Length: corpProfile = args[++index]; break;
                    case "--runner-profile" when initialize && index + 1 < args.Length: runnerProfile = args[++index]; break;
                    case "--output" when index + 1 < args.Length: destination = args[++index]; break;
                    case "--verify-fixtures" when !initialize: verifyFixtures = true; break;
                    case "--workers" when !initialize && index + 1 < args.Length:
                        if (!int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out workers) || workers is < 1 or > 64)
                            throw new ArgumentException("--workers requires 1..64.");
                        break;
                    default: throw new ArgumentException($"Unsupported tactical argument '{args[index]}'.");
                }
            }
            if (corpProfile is not null && runnerProfile is not null) throw new ArgumentException("Choose one tactical side per manifest.");
            if (string.IsNullOrWhiteSpace(destination)) throw new ArgumentException("--output <new path> is required.");
            if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
                throw new ArgumentException("Tactical Gateway benchmarks require locally provisioned official content at build time. Execution is offline.");
            var runner = new TacticalBenchmarkRunner(CardDataResource.LoadEmbedded());
            if (initialize)
            {
                TacticalBenchmarkManifest manifest = runner.CreateContractManifest();
                if (corpProfile is not null) manifest = BenchmarkSuites.WithCorpProfile(manifest, corpProfile);
                if (runnerProfile is not null) manifest = BenchmarkSuites.WithRunnerProfile(manifest, runnerProfile);
                using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                JsonSerializer.Serialize(stream, manifest, EngineJson.Options);
                output.WriteLine(json ? EngineJson.Serialize(new { schemaVersion = 1, status = "created", manifest.SuiteId, positions = manifest.Positions.Length })
                    : $"Created {manifest.SuiteId}: {manifest.Positions.Length} positions.");
                return 0;
            }
            var input = new FileInfo(args[1]);
            if (input.Length > 32 * 1024 * 1024) throw new ArgumentException("Tactical manifest exceeds 32 MiB.");
            TacticalBenchmarkManifest frozen = EngineJson.Deserialize<TacticalBenchmarkManifest>(File.ReadAllText(input.FullName));
            TacticalBenchmarkReport report = runner.RunAsync(frozen, destination, workers, verifyFixtures, BuildMetadata.Current.ApplicationVersion).GetAwaiter().GetResult();
            if (json) output.WriteLine(EngineJson.Serialize(report));
            else output.WriteLine($"{report.SuiteId} ({report.Mode}): {report.Positions.Count(position => position.Passed)}/{report.Positions.Length} correct. Report: {Path.Combine(destination, "report.json")}");
            return report.Passed ? 0 : 1;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            if (json) output.WriteLine(EngineJson.Serialize(new { schemaVersion = 1, kind = "m5-tactical-error", status = "invalid", message = exception.Message }));
            else error.WriteLine($"ai-tactics: {exception.Message}");
            return 2;
        }
    }
}

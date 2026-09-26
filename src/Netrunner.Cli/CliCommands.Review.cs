using System.Globalization;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// ai-review &lt;session-directory&gt; [--profile standard|easy] [--all] [--output file] [--json].
    /// Replays a recorded client session and compares every local-player
    /// decision with the offline bot's choice for the same position. The
    /// output is private: it names the local player's own cards.
    /// </summary>
    private static int RunAiReview(string[] args, TextWriter output, TextWriter error)
    {
        bool json = args.Contains("--json", StringComparer.Ordinal);
        try
        {
            if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("ai-review requires a session directory.");
            string profile = "standard";
            string? destination = null;
            bool all = false;
            for (int index = 2; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--json": break;
                    case "--all": all = true; break;
                    case "--profile" when index + 1 < args.Length:
                        profile = args[++index];
                        if (profile is not ("standard" or "easy")) throw new ArgumentException("--profile requires standard or easy.");
                        break;
                    case "--output" when index + 1 < args.Length:
                        destination = args[++index];
                        if (File.Exists(destination)) throw new ArgumentException("--output must name a new file.");
                        break;
                    default: throw new ArgumentException($"Unsupported ai-review argument '{args[index]}'.");
                }
            }

            string sessionDirectory = Path.GetFullPath(args[1]);
            if (!Directory.Exists(sessionDirectory))
                throw new DirectoryNotFoundException($"Session directory does not exist: {sessionDirectory}");
            SessionTraceManifest manifest = SessionTrace.ReadManifest(sessionDirectory);
            if (manifest.BehaviorRegistryVersion != GatewayBehaviorCatalog.Version)
                throw new InvalidOperationException("ai-review supports official Gateway sessions only.");
            if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
                throw new InvalidOperationException("ai-review needs previously provisioned official content embedded at build time.");
            BehaviorCatalog behavior = GatewayBehaviorCatalog.Create();
            IRuntimeCardCatalog runtime = GatewayRuntimeCardCatalog.CreateEmbedded();
            SessionRecoveryResult recovered = SessionTrace.Recover(sessionDirectory, behavior, runtime);
            IReadOnlyList<ReviewedDecision> decisions = HumanGameReview.ReviewAsync(
                recovered.Replay, manifest.HumanSide, behavior, runtime,
                new CardCatalogMetadataResolver(CardDataResource.LoadEmbedded()), profile).GetAwaiter().GetResult();

            // Single-option decisions are forced; they say nothing about play.
            ReviewedDecision[] choices = decisions.Where(decision => decision.LegalActions > 1).ToArray();
            ReviewedDecision[] disagreements = choices.Where(decision => !decision.Agrees).ToArray();
            string report = EngineJson.Serialize(new
            {
                schemaVersion = 1,
                kind = "ai-human-review",
                privacy = "private: contains the reviewed player's own hidden cards",
                session = manifest.TraceId,
                reviewedSide = manifest.HumanSide.ToString(),
                profile,
                decisions = decisions.Count,
                choices = choices.Length,
                agreements = choices.Length - disagreements.Length,
                agreementRate = choices.Length == 0 ? 1 : (choices.Length - disagreements.Length) / (double)choices.Length,
                reviewed = all ? choices : disagreements,
            });
            if (destination is not null) File.WriteAllText(destination, report);

            if (json)
            {
                output.WriteLine(report);
            }
            else
            {
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"ai-review side={manifest.HumanSide} profile={profile} choices={choices.Length} agreements={choices.Length - disagreements.Length}"));
                foreach (ReviewedDecision decision in all ? choices : disagreements)
                {
                    output.WriteLine($"#{decision.Sequence} turn {decision.Turn} {decision.DecisionKind}: "
                        + $"you {decision.HumanKind} '{decision.HumanDescription}' · bot {decision.BotKind} '{decision.BotDescription}'");
                    foreach (ReviewCandidate candidate in decision.TopCandidates)
                        output.WriteLine($"    {candidate.Priority}/{candidate.Score} {candidate.Kind} '{candidate.Description}' ({candidate.Reason})");
                }
            }

            return 0;
        }
        catch (Exception exception) when (IsCommandFailure(exception))
        {
            if (json) output.WriteLine(EngineJson.Serialize(new { schemaVersion = 1, status = "failed", message = exception.Message }));
            else error.WriteLine($"ai-review failed: {exception.Message}");
            return 2;
        }
    }
}

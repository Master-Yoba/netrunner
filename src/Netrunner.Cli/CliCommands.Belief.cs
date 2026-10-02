using System.Globalization;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// ai-belief &lt;games-directory&gt; [--games N] [--trace GAME[:TIER]] [--json]. A9a's accuracy measure: replays games saved by
    /// <c>ai-ladder --save-games</c> and scores each tier's tracker (Easy, Standard, Hard) of the Corp's hidden cards,
    /// as the Runner sees them, against the truth, next to the prior alone. Exit 0 on success, 2 for invalid arguments.
    /// </summary>
    private static int RunAiBelief(string[] args, TextWriter output, TextWriter error)
    {
        bool json = args.Contains("--json", StringComparer.Ordinal);
        string directory;
        int? games = null;
        string? trace = null;
        try
        {
            if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("ai-belief requires a directory of saved ladder games.");
            directory = Path.GetFullPath(args[1]);
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"Games directory does not exist: {directory}");
            for (int index = 2; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--json": break;
                    case "--games" when index + 1 < args.Length:
                        games = int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out int count) && count > 0
                            ? count : throw new ArgumentException("--games requires a positive integer.");
                        break;
                    case "--trace" when index + 1 < args.Length: trace = args[++index]; break;
                    default: throw new ArgumentException($"Unsupported ai-belief argument '{args[index]}'.");
                }
            }
            if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
                throw new ArgumentException("ai-belief needs previously provisioned official content embedded at build time.");
        }
        catch (Exception exception) when (IsCommandFailure(exception))
        {
            if (json) output.WriteLine(EngineJson.Serialize(new { schemaVersion = 1, status = "invalid", message = exception.Message }));
            else error.WriteLine($"ai-belief failed: {exception.Message}");
            return 2;
        }

        var saved = LadderGameArchive.Read(directory);
        if (trace is not null)
        {
            // --trace GAME[:TIER]: the tracked HQ against the truth whenever it changes, for one game.
            string[] parts = trace.Split(':');
            (LadderSavedGame game, FullMatchDefinition definition) = saved.First(entry => entry.Game.Game.Id == parts[0]);
            foreach (string line in BeliefAccuracy.Trace(game, definition, parts.Length > 1 ? parts[1] : "standard")) output.WriteLine(line);
            return 0;
        }
        IReadOnlyList<BeliefScore> scores = BeliefAccuracy.Score(games is { } limit ? saved.Take(limit) : saved);
        if (json)
        {
            output.WriteLine(EngineJson.Serialize(new { schemaVersion = 1, status = "ok", directory, scores }));
            return 0;
        }
        output.WriteLine($"ai-belief directory={directory}");
        foreach (BeliefScore score in scores)
            output.WriteLine(FormattableString.Invariant(
                $"{score.Tier,-9} slots={score.Slots,5} log-loss={score.SlotLogLoss:0.000} (prior {score.SlotPriorLogLoss:0.000}) top1={score.SlotTop1:P0} (prior {score.SlotPriorTop1:P0}) | hq-agendas error={score.HqAgendaError:0.000} (prior {score.HqPriorAgendaError:0.000}) n={score.HqSamples} | rd-top brier={score.RdTopBrier:0.000} (prior {score.RdTopPriorBrier:0.000}) | tracked-hand precision={score.TrackedHandPrecision:P0} | confident slots={score.ConfidentSlots} right={score.ConfidentPrecision:P0}"));
        return 0;
    }
}

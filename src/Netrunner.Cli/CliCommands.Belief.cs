using System.Globalization;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// ai-belief &lt;games-directory&gt; [--games N] [--trace GAME[:TIER]] [--hq-agenda-factor F] [--hq-agenda-sweep F1,F2,...]
    /// [--workers N] [--json]. A9a's accuracy measure:
    /// replays games saved by <c>ai-ladder --save-games</c> and scores each tier's tracker (Easy, Standard, Hard) of the Corp's
    /// hidden cards, as the Runner sees them, against the truth, next to the prior alone. <c>--hq-agenda-factor</c> measures
    /// with another HQ agenda factor than the pool's own (A7); <c>--hq-agenda-sweep</c> scores more factors in the same pass;
    /// <c>--workers</c> replays games in parallel (the result doesn't depend on it). Exit 0 on success, 2 for invalid arguments.
    /// </summary>
    private static int RunAiBelief(string[] args, TextWriter output, TextWriter error)
    {
        bool json = args.Contains("--json", StringComparer.Ordinal);
        string directory;
        int? games = null;
        string? trace = null;
        double? hqAgendaFactor = null;
        List<double> sweep = [];
        int workers = 1;
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
                    case "--hq-agenda-sweep" when index + 1 < args.Length:
                        foreach (string value in args[++index].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            sweep.Add(double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double swept) && swept > 0
                                ? swept : throw new ArgumentException("--hq-agenda-sweep requires positive numbers, comma-separated."));
                        break;
                    case "--workers" when index + 1 < args.Length:
                        workers = int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out int parallel) && parallel is > 0 and <= 64
                            ? parallel : throw new ArgumentException("--workers requires an integer from 1 to 64.");
                        break;
                    case "--hq-agenda-factor" when index + 1 < args.Length:
                        hqAgendaFactor = double.TryParse(args[++index], NumberStyles.Float, CultureInfo.InvariantCulture, out double factor)
                            && factor > 0 ? factor : throw new ArgumentException("--hq-agenda-factor requires a positive number.");
                        break;
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

        IEnumerable<(LadderSavedGame Game, FullMatchDefinition Definition)> saved;
        try
        {
            saved = trace is null && games is null
                ? LadderGameArchive.ReadAll(directory, workers)
                : [.. LadderGameArchive.Read(directory)];
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidDataException
            || exception is AggregateException { InnerException: System.Text.Json.JsonException or InvalidDataException })
        {
            // Games saved under another behavior catalog (cards changed since) can't be replayed.
            string message = (exception.InnerException ?? exception).Message;
            if (json) output.WriteLine(EngineJson.Serialize(new { schemaVersion = 1, status = "invalid", message }));
            else error.WriteLine($"ai-belief failed: the saved games can't be replayed with this build: {message}");
            return 2;
        }
        if (trace is not null)
        {
            // --trace GAME[:TIER]: the tracked HQ against the truth whenever it changes, for one game.
            string[] parts = trace.Split(':');
            (LadderSavedGame game, FullMatchDefinition definition) = saved.First(entry => entry.Game.Game.Id == parts[0]);
            foreach (string line in BeliefAccuracy.Trace(game, definition, parts.Length > 1 ? parts[1] : "standard")) output.WriteLine(line);
            return 0;
        }
        IReadOnlyList<BeliefScore> scores = BeliefAccuracy.Score(games is { } limit ? saved.Take(limit) : saved, hqAgendaFactor, sweep, workers);
        if (json)
        {
            output.WriteLine(EngineJson.Serialize(new { schemaVersion = 1, status = "ok", directory, scores }));
            return 0;
        }
        output.WriteLine($"ai-belief directory={directory}");
        foreach (BeliefScore score in scores)
            output.WriteLine(FormattableString.Invariant(
                $"{score.Tier,-9} slots={score.Slots,5} log-loss={score.SlotLogLoss:0.000} (prior {score.SlotPriorLogLoss:0.000}) top1={score.SlotTop1:P0} (prior {score.SlotPriorTop1:P0}) | hq-agendas error={score.HqAgendaError:0.000} (prior {score.HqPriorAgendaError:0.000}) n={score.HqSamples} | rd-top brier={score.RdTopBrier:0.000} (prior {score.RdTopPriorBrier:0.000}) | tracked-hand precision={score.TrackedHandPrecision:P0} | confident slots={score.ConfidentSlots} right={score.ConfidentPrecision:P0}"));
        foreach (BeliefScore score in scores.Where(score => score.HqAgendaErrorSweep is not null))
            output.WriteLine(FormattableString.Invariant(
                $"{score.Tier,-9} hq-agenda-sweep {string.Join(' ', sweep.Zip(score.HqAgendaErrorSweep!, (factor, error) => FormattableString.Invariant($"{factor:0.###}={error:0.000}")))}"));
        return 0;
    }
}

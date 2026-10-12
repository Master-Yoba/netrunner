using System.Globalization;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// ai-ladder-diff &lt;games-directory&gt; --baseline E --candidate E [--workers 1..64] [--top N] [--examples N] [--reasons].
    /// What a candidate changed (after chiriboga's <c>ai-batch replay --diff</c>): pairs the games a two-entrant ladder saved
    /// (<c>ai-ladder --save-games</c>) by deal and opponent, finds in each seat the first decision where the candidate's game
    /// departs from the baseline's, and groups those departures by kind, with how often the candidate's result was better,
    /// worse or the same. <c>--reasons</c> asks each entrant, at an example's decision, why it chose. Exit 0 on success, 2 for
    /// invalid arguments.
    /// </summary>
    private static int RunAiLadderDiff(string[] args, TextWriter output, TextWriter error)
    {
        string directory;
        string? baseline = null, candidate = null;
        int workers = 1, top = 20, examples = 2;
        bool reasons = false;
        try
        {
            if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("ai-ladder-diff requires a directory of saved ladder games.");
            directory = Path.GetFullPath(args[1]);
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"Games directory does not exist: {directory}");
            for (int index = 2; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--baseline" when index + 1 < args.Length: baseline = args[++index]; break;
                    case "--candidate" when index + 1 < args.Length: candidate = args[++index]; break;
                    case "--workers" when index + 1 < args.Length: workers = Bounded(args[++index], "--workers", 1, 64); break;
                    case "--top" when index + 1 < args.Length: top = Bounded(args[++index], "--top", 1, 1000); break;
                    case "--examples" when index + 1 < args.Length: examples = Bounded(args[++index], "--examples", 0, 100); break;
                    case "--reasons": reasons = true; break;
                    default: throw new ArgumentException($"Unsupported ai-ladder-diff argument '{args[index]}'.");
                }
            }
            if (baseline is null || candidate is null) throw new ArgumentException("ai-ladder-diff requires --baseline and --candidate.");
        }
        catch (Exception exception) when (IsCommandFailure(exception))
        {
            error.WriteLine($"ai-ladder-diff failed: {exception.Message}");
            return 2;
        }

        IReadOnlyList<(LadderSavedGame Game, FullMatchDefinition Definition)> games = LadderGameArchive.ReadAll(directory, workers);
        Dictionary<string, string> titles = CardTitles();
        string Title(CardDefinitionId definition) => titles.GetValueOrDefault(definition.Value, definition.Value);
        var byId = games.ToDictionary(entry => entry.Game.Game.Id, entry => entry, StringComparer.Ordinal);
        LadderDivergenceReport[] reports = [.. new[] { PlayerSide.Corp, PlayerSide.Runner }
            .Select(side => LadderDivergences.Compare(games, side, baseline, candidate, Title))];
        // The review of #145: a misspelled or absent entrant pairs nothing, which must not pass for a change that changed nothing.
        if (reports.All(report => report.Pairs == 0))
        {
            string present = string.Join(", ", games.SelectMany(entry => new[] { entry.Game.Game.Corp, entry.Game.Game.Runner })
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
            error.WriteLine($"ai-ladder-diff failed: no game of {candidate} pairs with one of {baseline} in either seat; the archive holds {present}.");
            return 2;
        }
        output.WriteLine($"ai-ladder-diff games={games.Count} directory={directory}");
        foreach (LadderDivergenceReport report in reports)
        {
            PlayerSide side = report.Side;
            if (report.Pairs == 0) continue;
            int better = report.Divergences.Count(divergence => divergence.SeatScoreChange > 0);
            int worse = report.Divergences.Count(divergence => divergence.SeatScoreChange < 0);
            output.WriteLine(FormattableString.Invariant(
                $"side={side} candidate={candidate} baseline={baseline} pairs={report.Pairs} identical={report.Identical} diverged={report.Divergences.Count} better={better} worse={worse}"));
            foreach (IGrouping<string, LadderDivergence> kind in report.Divergences.GroupBy(divergence => divergence.Kind)
                .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal).Take(top))
            {
                output.WriteLine(FormattableString.Invariant(
                    $"  {kind.Count(),5} better={kind.Count(item => item.SeatScoreChange > 0)} worse={kind.Count(item => item.SeatScoreChange < 0)} {kind.Key}"));
                foreach (LadderDivergence example in kind.OrderByDescending(item => Math.Abs(item.SeatScoreChange)).ThenBy(item => item.CandidateGameId, StringComparer.Ordinal).Take(examples))
                {
                    string why = reasons && byId.TryGetValue(example.BaselineGameId, out var source)
                        ? Reasons(source.Game, source.Definition, example)
                        : "";
                    output.WriteLine(FormattableString.Invariant(
                        $"        example {example.CandidateGameId} sequence={example.Sequence} turn={example.Turn} change={example.SeatScoreChange:+0.0;-0.0;0}{why}"));
                }
            }
        }
        return 0;

        static int Bounded(string text, string name, int minimum, int maximum) =>
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value >= minimum && value <= maximum
                ? value : throw new ArgumentException($"{name} requires an integer from {minimum} to {maximum}.");

        // Each entrant's reason and score for its own choice at the departure, asked of a diagnosing copy of its agent.
        string Reasons(LadderSavedGame game, FullMatchDefinition definition, LadderDivergence divergence)
        {
            string Ask(string entrant)
            {
                if (RatingLadder.CreateAgent(entrant, divergence.Decider, definition) is not OfflineAgent agent) return $"{entrant}: not an offline agent";
                OfflineAgent diagnosing = SavedDecision.Diagnosing(agent, definition);
                SavedChoice choice = SavedDecision.ChooseAsync(game.Replay, definition, divergence.Sequence, diagnosing).GetAwaiter().GetResult();
                AgentCandidate? chosen = diagnosing.LastEvaluation?.Candidates.FirstOrDefault(item => item.ActionId == choice.Chosen.Id);
                string recorded = entrant == divergence.Candidate ? divergence.CandidateChoice : divergence.BaselineChoice;
                string asked = LadderDivergences.Describe(choice.State, choice.State.PendingDecision, choice.Chosen.Id, Title);
                // The turn planner samples; asked again, an entrant may choose otherwise than it did in the game.
                string differs = asked == recorded ? "" : $" (asked again, chose {asked})";
                return chosen is null ? $"{entrant}: ?{differs}" : FormattableString.Invariant($"{entrant}: {chosen.Utility.Reason} {chosen.Utility.Score}{differs}");
            }
            return $" | {Ask(divergence.Baseline)} | {Ask(divergence.Candidate)}";
        }
    }

    private static Dictionary<string, string> CardTitles()
    {
        try
        {
            return CardDataResource.LoadEmbeddedPool().Cards.GroupBy(card => card.Code, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Title, StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is CardDataValidationException or InvalidOperationException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }
}

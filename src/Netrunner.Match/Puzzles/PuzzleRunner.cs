using System.Globalization;
using Netrunner.AI;
using Netrunner.Core;
using Netrunner.Match.Regret;

namespace Netrunner.Match.Puzzles;

/// <summary>A puzzle's outcome: whether the bot passed, what it did, and its reason for the deciding choice.</summary>
public sealed record PuzzleResult(Puzzle Puzzle, bool Passed, string Detail, string Reason);

/// <summary>
/// Plays a puzzle with production bots (bot plan S2.1). Both sides' bots watch the game to the root; a decision puzzle asks
/// the puzzle side's bot to choose there; a line or nested puzzle plays on to the turn's end, each side's bot making its
/// own choices, and judges the line or the nested choice.
/// </summary>
public static class PuzzleRunner
{
    /// <summary>Decisions a line plays before it is stopped short of the turn's end.</summary>
    public const int LineCap = 400;

    /// <param name="bots">Makes the production bot of a side; it is asked for both sides.</param>
    public static PuzzleResult Run(Puzzle puzzle, RegretGame game, Func<FullMatchDefinition, PlayerSide, OfflineAgent> bots)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(bots);
        FullMatchDefinition definition = RegretGames.Definition(game);
        var agents = new Dictionary<PlayerSide, OfflineAgent>
        {
            [PlayerSide.Corp] = bots(definition, PlayerSide.Corp),
            [PlayerSide.Runner] = bots(definition, PlayerSide.Runner),
        };
        GameState? root = null;
        foreach (RegretPosition position in RegretGames.Positions(game, definition))
        {
            foreach (OfflineAgent agent in agents.Values) AgentLifecycleHost.Observe(agent, position.State, definition.Behavior);
            if (position.Sequence == puzzle.Sequence) { root = position.State; break; }
        }
        if (root is null) throw new InvalidDataException($"Puzzle '{puzzle.Id}': game '{game.Id}' has no action {puzzle.Sequence}.");
        if (root.GetPlayer(root.PendingDecision!.Owner).Side != puzzle.Side)
            throw new InvalidDataException($"Puzzle '{puzzle.Id}': action {puzzle.Sequence} is not the {puzzle.Side}'s decision.");
        return puzzle.Kind == Puzzle.DecisionKind
            ? Judge(puzzle, root, Choose(agents[puzzle.Side], root, puzzle.Side, definition))
            : Play(puzzle, root, agents, definition);
    }

    private static PuzzleResult Play(Puzzle puzzle, GameState root, Dictionary<PlayerSide, OfflineAgent> agents, FullMatchDefinition definition)
    {
        GameState state = root;
        int turn = root.Turn.TurnNumber;
        PlayerSide active = root.Turn.ActiveSide;
        var line = new List<(GameState Before, LegalAction Action)>();
        string reason = "";
        for (int step = 0; step < LineCap && state.Outcome.Kind == GameOutcomeKind.Ongoing && state.PendingDecision is { } pending
            && state.Turn.TurnNumber == turn && state.Turn.ActiveSide == active; step++)
        {
            PlayerSide side = state.GetPlayer(pending.Owner).Side;
            if (puzzle.Kind == Puzzle.NestedKind && side == puzzle.Side
                && pending.LegalActions.Any(action => puzzle.Acceptance.At!.Matches(state, action)))
                return Judge(puzzle, state, Choose(agents[side], state, side, definition));
            (LegalAction chosen, string why) = Choose(agents[side], state, side, definition);
            if (side == puzzle.Side) { line.Add((state, chosen)); reason = why; }
            var submission = new ActionSubmission(pending.Owner, pending.Id, pending.Revision, chosen.Id);
            ApplyResult result = GameEngine.Apply(state, submission, definition.Behavior, definition.Runtime);
            if (!result.Accepted) throw new InvalidOperationException($"Puzzle '{puzzle.Id}': the engine rejected {chosen.Description}.");
            state = result.State;
            foreach (OfflineAgent agent in agents.Values) AgentLifecycleHost.Accepted(agent, submission, state, definition.Behavior);
        }
        if (puzzle.Kind == Puzzle.NestedKind) return new PuzzleResult(puzzle, false, "the nested decision was not reached", reason);
        string[] failed = [.. puzzle.Acceptance.Line.Where(predicate => Holds(predicate, puzzle.Side, active, turn, line, state) == predicate.Not)
            .Select(Describe)];
        string played = string.Join("; ", line.Select(entry => entry.Action.Description));
        return new PuzzleResult(puzzle, failed.Length == 0, failed.Length == 0 ? played : $"failed {string.Join(", ", failed)}: {played}", reason);
    }

    private static (LegalAction Action, string Reason) Choose(OfflineAgent agent, GameState state, PlayerSide side, FullMatchDefinition definition)
    {
        ActionId id = agent.ChooseActionAsync(ObservationBuilder.Build(state, side, definition.Behavior, definition.Runtime)).AsTask()
            .GetAwaiter().GetResult();
        string reason = agent.LastEvaluation?.Candidates.FirstOrDefault(candidate => candidate.ActionId == id)?.Utility.Reason ?? "";
        return (state.PendingDecision!.LegalActions.Single(action => action.Id == id), reason);
    }

    private static PuzzleResult Judge(Puzzle puzzle, GameState state, (LegalAction Action, string Reason) choice)
    {
        PuzzleAcceptance acceptance = puzzle.Acceptance;
        bool accepted = acceptance.Accept.IsDefaultOrEmpty || acceptance.Accept.Any(match => match.Matches(state, choice.Action));
        bool rejected = !acceptance.Reject.IsDefaultOrEmpty && acceptance.Reject.Any(match => match.Matches(state, choice.Action));
        return new PuzzleResult(puzzle, accepted && !rejected,
            $"chose {choice.Action.Kind} {choice.Action.Description} {RegretRoots.Parameters(state, choice.Action)}", choice.Reason);
    }

    private static bool Holds(LinePredicate predicate, PlayerSide side, PlayerSide active, int turn, List<(GameState Before, LegalAction Action)> line,
        GameState end) => predicate.Kind switch
        {
            "installed" => Installed(end, predicate.Card).Any(),
            "counters" => Installed(end, predicate.Card).Any(card => Counters(end, card, predicate.Counter) >= (predicate.AtLeast ?? 1)),
            "credits" => end.Players.Single(player => player.Side == side).Credits >= (predicate.AtLeast ?? 0),
            "grip" => end.Cards.Count(card => end.GetZone(card.ZoneId).Kind == ZoneKind.RunnerGrip) >= (predicate.AtLeast ?? 0),
            "successfulRun" => end.Turn.History?.ThisTurn(active) is { } facts && facts.TurnNumber == turn
                && facts.SuccessfulRunServerIds.Any(server => server.Value == predicate.Server),
            "action" => line.Count(entry => predicate.Match!.Matches(entry.Before, entry.Action)) >= (predicate.AtLeast ?? 1),
            "before" => line.FindIndex(entry => predicate.Match!.Matches(entry.Before, entry.Action)) is var first and >= 0
                && line.FindIndex(entry => predicate.Then!.Matches(entry.Before, entry.Action)) is var then && (then < 0 || first < then),
            "first" => line.Count > 0 && predicate.Match!.Matches(line[0].Before, line[0].Action),
            "precedes" => line.FindIndex(entry => predicate.Then!.Matches(entry.Before, entry.Action)) is var later
                && (later < 0 || line.Take(later).Any(entry => predicate.Match!.Matches(entry.Before, entry.Action))),
            _ => throw new InvalidDataException($"Unknown line predicate '{predicate.Kind}'."),
        };

    private static IEnumerable<CardInstanceState> Installed(GameState state, string? definition) => state.Cards.Where(card =>
        card.DefinitionId.Value == definition && state.GetZone(card.ZoneId).Kind is ZoneKind.ServerIce or ZoneKind.ServerRoot or ZoneKind.RunnerRig);

    private static int Counters(GameState state, CardInstanceState card, string? counter) => counter is null or "advancement"
        ? card.AdvancementCounters
        : (state.RulesState?.Counters ?? []).Where(entry => entry.CardId == card.Id && entry.CounterName == counter).Sum(entry => entry.Count);

    private static string Describe(LinePredicate predicate) => string.Create(CultureInfo.InvariantCulture,
        $"{(predicate.Not ? "not " : "")}{predicate.Kind}{(predicate.Card is null ? "" : " " + predicate.Card)}{(predicate.Server is null ? "" : " " + predicate.Server)}{(predicate.AtLeast is null ? "" : " >= " + predicate.AtLeast)}");
}

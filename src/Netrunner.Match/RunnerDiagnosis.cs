using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>One saved game, from the Runner's side (docs/BOT_IMPROVEMENT_PLAN.md, the diagnosis).</summary>
public sealed record RunnerGameSample(
    string Entrant,
    string RunnerDeck,
    string CorpDeck,
    bool RunnerWon,
    GameWinReason? Reason,
    bool Capped,
    bool SelfFlatline,
    string? FinalRunnerSource,
    int Turns,
    int RunnerPoints,
    int CorpPoints,
    int Runs,
    double CreditsAtTurnStart,
    string GameId = "",
    long FinalSequence = 0);

/// <summary>The Runner's results for one entrant, or one entrant and deck.</summary>
public sealed record RunnerDiagnosisRow(
    string Entrant,
    string? RunnerDeck,
    int Games,
    double WinRate,
    double LostToAgendas,
    double LostToCorpFlatline,
    double LostToSelfFlatline,
    double Capped,
    double Turns,
    double RunsPerTurn,
    double CreditsAtTurnStart,
    double RunnerPoints,
    IReadOnlyList<(string Source, int Count)> SelfFlatlineSources);

/// <summary>
/// Why the Runner loses: replays saved games and records, per game, the result and its reason, whether a flatline came
/// from the Runner's own action (and the card it used), how often it ran, and its credits at the start of its turns.
/// </summary>
public static class RunnerDiagnosis
{
    public static RunnerGameSample Measure(LadderSavedGame saved, FullMatchDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(definition);
        string[] decks = saved.Game.ContentId.Split(RulesFuzzRunner.PairSeparator);
        GameState state = saved.Replay.InitialSnapshot.ToState();
        int runs = 0, turnStarts = 0, credits = 0;
        PlayerSide? lastActive = null;
        PlayerSide? finalActor = null;
        string? finalSource = null;
        long finalSequence = 0;
        foreach (ReplayAction recorded in saved.Replay.Actions)
        {
            LegalAction? chosen = state.PendingDecision?.LegalActions.FirstOrDefault(action => action.Id == recorded.Submission.ActionId);
            PlayerSide actor = state.GetPlayer(recorded.Submission.Actor).Side;
            if (actor == PlayerSide.Runner && chosen?.Parameters is RunServerParameters) runs++;
            ApplyResult applied = GameEngine.Apply(state, recorded.Submission, definition.Behavior, definition.Runtime);
            if (!applied.Accepted) break;
            finalActor = actor;
            finalSource = chosen is null ? null : Source(state, chosen);
            finalSequence = recorded.Sequence;
            state = applied.State;
            if (state.Turn.ActiveSide == PlayerSide.Runner && lastActive != PlayerSide.Runner)
            {
                turnStarts++;
                credits += Player(state, PlayerSide.Runner).Credits;
            }
            lastActive = state.Turn.ActiveSide;
        }

        GameOutcome outcome = state.Outcome;
        bool runnerWon = outcome.Winner == PlayerSide.Runner;
        bool selfFlatline = outcome.Reason == GameWinReason.Flatline && !runnerWon && finalActor == PlayerSide.Runner;
        string entrant = saved.Game.Runner;
        return new RunnerGameSample(entrant, decks.Length == 2 ? decks[1] : saved.Game.ContentId, decks[0], runnerWon,
            outcome.Reason, saved.Capped, selfFlatline, selfFlatline ? finalSource : null, state.Turn.TurnNumber,
            Player(state, PlayerSide.Runner).AgendaPoints, Player(state, PlayerSide.Corp).AgendaPoints,
            runs, turnStarts == 0 ? 0 : (double)credits / turnStarts, saved.Game.Id, finalSequence);
    }

    /// <summary>Rows per entrant, then per entrant and Runner deck.</summary>
    public static IReadOnlyList<RunnerDiagnosisRow> Summarize(IEnumerable<RunnerGameSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        RunnerGameSample[] all = [.. samples];
        IEnumerable<RunnerDiagnosisRow> entrants = all.GroupBy(sample => sample.Entrant)
            .OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => Row(group.Key, null, [.. group]));
        IEnumerable<RunnerDiagnosisRow> decks = all.GroupBy(sample => (sample.Entrant, sample.RunnerDeck))
            .OrderBy(group => group.Key.Entrant, StringComparer.Ordinal).ThenBy(group => group.Key.RunnerDeck, StringComparer.Ordinal)
            .Select(group => Row(group.Key.Entrant, group.Key.RunnerDeck, [.. group]));
        return [.. entrants, .. decks];
    }

    private static RunnerDiagnosisRow Row(string entrant, string? deck, RunnerGameSample[] games)
    {
        double Share(Func<RunnerGameSample, bool> test) => games.Length == 0 ? 0 : (double)games.Count(test) / games.Length;
        return new RunnerDiagnosisRow(entrant, deck, games.Length,
            Share(game => game.RunnerWon),
            Share(game => !game.RunnerWon && game.Reason == GameWinReason.AgendaPoints),
            Share(game => !game.RunnerWon && game.Reason == GameWinReason.Flatline && !game.SelfFlatline),
            Share(game => game.SelfFlatline),
            Share(game => game.Capped),
            games.Average(game => (double)game.Turns),
            games.Average(game => game.Turns == 0 ? 0 : (double)game.Runs / game.Turns),
            games.Average(game => game.CreditsAtTurnStart),
            games.Average(game => (double)game.RunnerPoints),
            [.. games.Where(game => game.FinalRunnerSource is not null).GroupBy(game => game.FinalRunnerSource!)
                .Select(group => (group.Key, group.Count())).OrderByDescending(pair => pair.Item2).Take(5)]);
    }

    private static PlayerState Player(GameState state, PlayerSide side) => state.Players.Single(player => player.Side == side);

    // The action's kind and, where it has one, the card it uses or installs.
    private static string Source(GameState state, LegalAction action)
    {
        CardInstanceId? card = action.Parameters switch
        {
            AbilityActionParameters ability => ability.SourceCardId,
            InstallCardParameters install => install.CardId,
            PlayCardParameters play => play.CardId,
            // A choice inside a resolving behavior: the card whose behavior it is.
            _ => state.ResolutionStack.OfType<BehaviorResolutionFrame>().LastOrDefault()?.SourceCardId,
        };
        return card is { } id ? $"{action.Kind} {state.GetCard(id).DefinitionId.Value}" : action.Kind.ToString();
    }
}

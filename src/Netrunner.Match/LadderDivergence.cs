using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>
/// Where a candidate's game first departs from its baseline's on the same deal against the same opponent (after
/// chiriboga's <c>ai-batch replay --diff</c>): the decision, by sequence, that both games reached in the same state, and
/// what each side chose there. Later actions usually belong to two different games, so the first is the change's cause.
/// </summary>
public sealed record LadderDivergence(string CandidateGameId, string BaselineGameId, int Deal, string ContentId, PlayerSide Side,
    string Baseline, string Candidate, long Sequence, int Turn, PlayerSide Decider, DecisionKind Decision, string BaselineChoice,
    string CandidateChoice, double BaselineCorpScore, double CandidateCorpScore)
{
    /// <summary>The candidate seat's score less the baseline's in this pair: 1 when the candidate turned a loss into a win.</summary>
    public double SeatScoreChange => Side == PlayerSide.Corp ? CandidateCorpScore - BaselineCorpScore : BaselineCorpScore - CandidateCorpScore;

    /// <summary>The kind of change, for grouping: the decision and what each side chose there, without card instances.</summary>
    public string Kind => $"{Decider} {Decision}: {BaselineChoice} -> {CandidateChoice}";
}

/// <summary>A candidate's games paired with its baseline's in one seat: those that played out the same, and the first departures.</summary>
public sealed record LadderDivergenceReport(PlayerSide Side, string Baseline, string Candidate, int Pairs, int Identical,
    IReadOnlyList<LadderDivergence> Divergences);

public static partial class LadderDivergences
{
    /// <summary>
    /// Pairs the saved games of <paramref name="candidate"/> and <paramref name="baseline"/> in <paramref name="side"/> that
    /// played the same deal against the same opponent entrant, and finds each pair's first departure.
    /// </summary>
    public static LadderDivergenceReport Compare(IEnumerable<(LadderSavedGame Game, FullMatchDefinition Definition)> games, PlayerSide side,
        string baseline, string candidate, Func<CardDefinitionId, string>? title = null)
    {
        ArgumentNullException.ThrowIfNull(games);
        string Seat(LadderGame game) => side == PlayerSide.Corp ? game.Corp : game.Runner;
        string Opponent(LadderGame game) => side == PlayerSide.Corp ? game.Runner : game.Corp;
        var all = games.Where(entry => entry.Game.Game.Deal >= 0).ToArray();
        var baselines = all.Where(entry => Seat(entry.Game.Game) == baseline)
            .ToDictionary(entry => (entry.Game.Game.Deal, Opponent(entry.Game.Game)), entry => entry);
        var divergences = new List<LadderDivergence>();
        int pairs = 0;
        // Two opponent entrants of one policy play a deal's games identically; such a repeat counts once.
        var seen = new HashSet<(int, string?, string?)>();
        foreach (var mine in all.Where(entry => Seat(entry.Game.Game) == candidate).OrderBy(entry => entry.Game.Game.Deal)
            .ThenBy(entry => Opponent(entry.Game.Game), StringComparer.Ordinal))
        {
            if (!baselines.TryGetValue((mine.Game.Game.Deal, Opponent(mine.Game.Game)), out var theirs)) continue;
            if (!seen.Add((mine.Game.Game.Deal, mine.Game.Replay.ExpectedFinalStateHash, theirs.Game.Replay.ExpectedFinalStateHash))) continue;
            pairs++;
            if (First(mine.Game, theirs.Game, mine.Definition, side, baseline, candidate, title) is { } divergence) divergences.Add(divergence);
        }
        return new LadderDivergenceReport(side, baseline, candidate, pairs, pairs - divergences.Count, divergences);
    }

    /// <summary>
    /// The first decision where the two games' submissions differ, replayed from the baseline's start; null when one game's
    /// actions are a prefix of the other's or they never differ.
    /// </summary>
    public static LadderDivergence? First(LadderSavedGame candidate, LadderSavedGame baseline, FullMatchDefinition definition,
        PlayerSide side, string baselineName, string candidateName, Func<CardDefinitionId, string>? title = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(definition);
        GameState state = baseline.Replay.InitialSnapshot.ToState();
        int count = Math.Min(candidate.Replay.Actions.Count, baseline.Replay.Actions.Count);
        for (int index = 0; index < count; index++)
        {
            ReplayAction theirs = baseline.Replay.Actions[index];
            ReplayAction mine = candidate.Replay.Actions[index];
            if (theirs.Submission.ActionId != mine.Submission.ActionId || theirs.Submission.Actor != mine.Submission.Actor)
            {
                PendingDecision? pending = state.PendingDecision;
                PlayerSide decider = state.Players.FirstOrDefault(player => player.Id == pending?.Owner)?.Side ?? side;
                return new LadderDivergence(candidate.Game.Id, baseline.Game.Id, baseline.Game.Deal, baseline.Game.ContentId, side,
                    baselineName, candidateName, theirs.Sequence, state.Turn.TurnNumber, decider, pending?.Kind ?? DecisionKind.TurnAction,
                    Describe(state, pending, theirs.Submission.ActionId, title), Describe(state, pending, mine.Submission.ActionId, title),
                    baseline.CorpScore, candidate.CorpScore);
            }
            ApplyResult applied = GameEngine.Apply(state, theirs.Submission, definition.Behavior, definition.Runtime);
            if (!applied.Accepted) return null;
            state = applied.State;
        }
        return null;
    }

    /// <summary>An action as its kind and the card or server it names, without instance ids, so like choices group together.</summary>
    public static string Describe(GameState state, PendingDecision? pending, ActionId id, Func<CardDefinitionId, string>? title = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        LegalAction? action = pending?.LegalActions.FirstOrDefault(candidate => candidate.Id == id);
        if (action is null) return "?";
        string Card(CardInstanceId card)
        {
            CardDefinitionId definition = state.GetCard(card).DefinitionId;
            return title?.Invoke(definition) ?? definition.Value;
        }
        string Server(ServerId server) => state.Servers.FirstOrDefault(candidate => candidate.Id == server)?.Kind.ToString() ?? "?";
        string detail = action.Parameters switch
        {
            InstallCardParameters install => $"{Card(install.CardId)} in {(install.CreateNewRemote ? "new remote" : install.ServerId is { } server ? Server(server) : "rig")}",
            PlayCardParameters play => Card(play.CardId),
            RunServerParameters run => Server(run.ServerId),
            AdvanceCardParameters advance => Card(advance.CardId),
            ScoreAgendaParameters score => Card(score.CardId),
            RezCardParameters rez => Card(rez.CardId),
            RunIceParameters ice => Card(ice.IceId),
            AbilityActionParameters ability => ability.AbilityKey,
            _ => "",
        };
        if (detail.Length > 0) return $"{action.Kind} {detail}";
        // Otherwise the engine's own words (a mulligan's "Keep hand", a choice's option), with card instances as their cards.
        string described = CardInstance().Replace(action.Description, match =>
            state.Cards.Any(card => card.Id.Value == match.Value) ? Card(new CardInstanceId(match.Value)) : match.Value);
        return $"{action.Kind} {described}";
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"card:\d+")]
    private static partial System.Text.RegularExpressions.Regex CardInstance();
}

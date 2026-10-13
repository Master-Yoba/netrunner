using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>
/// How often one seat's deck was offered a kind of play and took it, over saved ladder games: a card's install, play, rez,
/// advance or ability, a run on a kind of server, or a basic action. Batch 11's diagnosis (Madani hosted 7.6 times a game,
/// installed from once) found its misplay this way by hand; the profile does it for every card of every deck.
/// </summary>
public sealed record OfferRow(PlayerSide Side, string Entrant, string Identity, DecisionKind Decision, string Play, int Games,
    int GamesOffered, int GamesTaken, int Offered, int Taken, int SeatActions, double? ScoreWhenTaken, double? ScoreWhenRefused)
{
    /// <summary>Of the decisions that offered the play, the share that took it.</summary>
    public double TakeRate => Offered == 0 ? 0 : (double)Taken / Offered;

    /// <summary>The play's share of the seat's choices of this decision kind (for turn actions, roughly its share of clicks).</summary>
    public double Share => SeatActions == 0 ? 0 : (double)Taken / SeatActions;
}

public static class OfferProfile
{
    private sealed class Tally
    {
        public int GamesOffered, GamesTaken, Offered, Taken;
        public double ScoreTaken, ScoreRefused;
    }

    /// <summary>
    /// Replays each saved game and, at every decision a seat makes, counts each kind of play it was offered and the one it
    /// chose. A seat's score is 1 for its win and 0 for its loss; the scores when taken and when offered but never taken
    /// describe, not cause, since a winning seat is offered more; each is null when no game counts toward it.
    /// </summary>
    public static IReadOnlyList<OfferRow> Measure(IEnumerable<(LadderSavedGame Game, FullMatchDefinition Definition)> games,
        Func<CardDefinitionId, string>? title = null, int workers = 1)
    {
        ArgumentNullException.ThrowIfNull(games);
        var tallies = new Dictionary<(PlayerSide, string, string, DecisionKind, string), Tally>();
        var seatGames = new Dictionary<(PlayerSide, string, string), int>();
        var seatActions = new Dictionary<(PlayerSide, string, string, DecisionKind), int>();
        Parallel.ForEach(games, new ParallelOptions { MaxDegreeOfParallelism = workers }, entry =>
        {
            GameProfile profile = Profile(entry.Game, entry.Definition, title);
            lock (tallies)
            {
                foreach (PlayerSide side in new[] { PlayerSide.Corp, PlayerSide.Runner })
                {
                    string entrant = side == PlayerSide.Corp ? entry.Game.Game.Corp : entry.Game.Game.Runner;
                    string identity = profile.Identities[side];
                    double score = side == PlayerSide.Corp ? entry.Game.CorpScore : 1 - entry.Game.CorpScore;
                    seatGames[(side, entrant, identity)] = seatGames.GetValueOrDefault((side, entrant, identity)) + 1;
                    foreach (var ((seat, decision), count) in profile.Actions.Where(pair => pair.Key.Side == side))
                        seatActions[(side, entrant, identity, decision)] = seatActions.GetValueOrDefault((side, entrant, identity, decision)) + count;
                    foreach (var ((seat, decision, play), (offered, taken)) in profile.Plays.Where(pair => pair.Key.Side == side))
                    {
                        var key = (side, entrant, identity, decision, play);
                        if (!tallies.TryGetValue(key, out Tally? tally)) tallies[key] = tally = new Tally();
                        tally.GamesOffered++;
                        tally.Offered += offered;
                        tally.Taken += taken;
                        if (taken > 0)
                        {
                            tally.GamesTaken++;
                            tally.ScoreTaken += score;
                        }
                        else tally.ScoreRefused += score;
                    }
                }
            }
        });
        return [.. tallies.Select(pair =>
        {
            var (side, entrant, identity, decision, play) = pair.Key;
            Tally tally = pair.Value;
            int refused = tally.GamesOffered - tally.GamesTaken;
            return new OfferRow(side, entrant, identity, decision, play, seatGames[(side, entrant, identity)], tally.GamesOffered,
                tally.GamesTaken, tally.Offered, tally.Taken, seatActions.GetValueOrDefault((side, entrant, identity, decision)),
                tally.GamesTaken == 0 ? null : tally.ScoreTaken / tally.GamesTaken, refused == 0 ? null : tally.ScoreRefused / refused);
        }).OrderBy(row => row.Side).ThenBy(row => row.Entrant, StringComparer.Ordinal).ThenBy(row => row.Identity, StringComparer.Ordinal)
            .ThenBy(row => row.Decision).ThenByDescending(row => row.Offered)];
    }

    private sealed record GameProfile(Dictionary<PlayerSide, string> Identities,
        Dictionary<(PlayerSide Side, DecisionKind Decision), int> Actions,
        Dictionary<(PlayerSide Side, DecisionKind Decision, string Play), (int Offered, int Taken)> Plays);

    private static GameProfile Profile(LadderSavedGame saved, FullMatchDefinition definition, Func<CardDefinitionId, string>? title)
    {
        GameState state = saved.Replay.InitialSnapshot.ToState();
        var identities = new Dictionary<PlayerSide, string>
        {
            [PlayerSide.Corp] = Identity(state, ZoneKind.CorpIdentity, title),
            [PlayerSide.Runner] = Identity(state, ZoneKind.RunnerIdentity, title),
        };
        var actions = new Dictionary<(PlayerSide, DecisionKind), int>();
        var plays = new Dictionary<(PlayerSide, DecisionKind, string), (int Offered, int Taken)>();
        foreach (ReplayAction recorded in saved.Replay.Actions)
        {
            PendingDecision? pending = state.PendingDecision;
            if (pending is not null && pending.LegalActions.Count > 1)
            {
                PlayerSide side = state.GetPlayer(recorded.Submission.Actor).Side;
                actions[(side, pending.Kind)] = actions.GetValueOrDefault((side, pending.Kind)) + 1;
                string? chosen = null;
                var offered = new HashSet<string>(StringComparer.Ordinal);
                foreach (LegalAction action in pending.LegalActions)
                {
                    string play = Play(state, pending, action, title);
                    offered.Add(play);
                    if (action.Id == recorded.Submission.ActionId) chosen = play;
                }
                foreach (string play in offered)
                {
                    var key = (side, pending.Kind, play);
                    (int was, int took) = plays.GetValueOrDefault(key);
                    plays[key] = (was + 1, took + (play == chosen ? 1 : 0));
                }
            }
            ApplyResult applied = GameEngine.Apply(state, recorded.Submission, definition.Behavior, definition.Runtime);
            if (!applied.Accepted) break;
            state = applied.State;
        }
        return new GameProfile(identities, actions, plays);
    }

    /// <summary>An offered action as its kind and card, without the server it targets (a run keeps its server kind).</summary>
    private static string Play(GameState state, PendingDecision pending, LegalAction action, Func<CardDefinitionId, string>? title)
    {
        string Card(CardInstanceId card) => Title(state.GetCard(card).DefinitionId, title);
        return action.Parameters switch
        {
            InstallCardParameters install => $"{action.Kind} {Card(install.CardId)}",
            AbilityActionParameters ability => $"{action.Kind} {Title(ability.DefinitionId, title)}: {ability.AbilityKey}",
            _ => LadderDivergences.Describe(state, pending, action.Id, title),
        };
    }

    private static string Identity(GameState state, ZoneKind kind, Func<CardDefinitionId, string>? title)
    {
        ZoneState? zone = state.Zones.FirstOrDefault(value => value.Kind == kind && value.ServerId is null);
        return zone is null || zone.CardIds.Count == 0 ? "?" : Title(state.GetCard(zone.CardIds[0]).DefinitionId, title);
    }

    private static string Title(CardDefinitionId definition, Func<CardDefinitionId, string>? title) => title?.Invoke(definition) ?? definition.Value;
}

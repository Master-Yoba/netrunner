using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>One side's style in one saved game (A6's style check): when it scored, what it built, how it ran.</summary>
public sealed record PlayStyleSample(
    PlayerSide Side,
    string Entrant,
    int? FirstPointTurn,
    int PointsByTurn8,
    int IceAtTurn6,
    int RemotesAtTurn6,
    int AssetsInstalled,
    int RunsByTurn6,
    int? FirstRunTurn,
    int RigAtTurn6);

/// <summary>An entrant's mean style on one side, over the games it played there.</summary>
public sealed record PlayStyleSummary(
    PlayerSide Side,
    string Entrant,
    int Games,
    double? FirstPointTurn,
    double ScoredByTurn6,
    double PointsByTurn8,
    double IceAtTurn6,
    double RemotesAtTurn6,
    double AssetsInstalled,
    double RunsByTurn6,
    double? FirstRunTurn,
    double RigAtTurn6);

/// <summary>
/// The style check of AI plan A6: replays saved ladder games and measures each side's play style, so a
/// personality can be shown to play differently from Standard (a rush Corp scores its first agenda earlier,
/// a glacier builds more ICE, an aggressive Runner runs more early). A turn number covers one Corp turn and
/// one Runner turn; "at turn 6" is the board when turn 7 starts, or at the end of a shorter game.
/// </summary>
public static class PlayStyle
{
    public const int SetupTurn = 6;
    public const int ScoreTurn = 8;

    public static IReadOnlyList<PlayStyleSample> Measure(LadderSavedGame saved, FullMatchDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(definition);
        GameState state = saved.Replay.InitialSnapshot.ToState();
        int?[] firstPoint = [null, null];
        int[] pointsBy8 = [0, 0];
        int?[] firstRun = [null, null];
        int runs = 0, assets = 0;
        GameState? atSetup = null;
        foreach (ReplayAction recorded in saved.Replay.Actions)
        {
            LegalAction? chosen = state.PendingDecision?.LegalActions.FirstOrDefault(action => action.Id == recorded.Submission.ActionId);
            ApplyResult applied = GameEngine.Apply(state, recorded.Submission, definition.Behavior, definition.Runtime);
            if (!applied.Accepted) break;
            GameState after = applied.State;
            int turn = state.Turn.TurnNumber;
            PlayerSide actor = state.GetPlayer(recorded.Submission.Actor).Side;
            if (chosen?.Parameters is RunServerParameters && actor == PlayerSide.Runner)
            {
                firstRun[1] ??= turn;
                if (turn <= SetupTurn) runs++;
            }
            if (chosen?.Parameters is InstallCardParameters { FinalizeSelection: true } install && actor == PlayerSide.Corp
                && definition.Runtime.TryGet(state.GetCard(install.CardId).DefinitionId, out CardRuntimeSpec spec) && spec.CardClass == RuntimeCardClass.Asset)
                assets++;
            foreach (PlayerSide side in new[] { PlayerSide.Corp, PlayerSide.Runner })
            {
                int index = side == PlayerSide.Corp ? 0 : 1;
                int points = Player(after, side).AgendaPoints;
                if (points > Player(state, side).AgendaPoints) firstPoint[index] ??= turn;
                if (turn <= ScoreTurn) pointsBy8[index] = points;
            }
            if (atSetup is null && after.Turn.TurnNumber > SetupTurn) atSetup = state;
            state = after;
        }
        atSetup ??= state;
        int ice = atSetup.Zones.Where(zone => zone.Kind == ZoneKind.ServerIce).Sum(zone => zone.CardIds.Count);
        int remotes = atSetup.Servers.Count(server => server.Kind == ServerKind.Remote
            && atSetup.Zones.Any(zone => zone.ServerId == server.Id && zone.Kind == ZoneKind.ServerRoot && zone.CardIds.Count > 0));
        int rig = atSetup.GetZone(ZoneKind.RunnerRig).CardIds.Count(id => definition.Runtime.TryGet(atSetup.GetCard(id).DefinitionId, out CardRuntimeSpec spec)
            && spec.CardClass is RuntimeCardClass.Program or RuntimeCardClass.Hardware);
        return
        [
            new(PlayerSide.Corp, saved.Game.Corp, firstPoint[0], pointsBy8[0], ice, remotes, assets, 0, null, 0),
            new(PlayerSide.Runner, saved.Game.Runner, firstPoint[1], pointsBy8[1], 0, 0, 0, runs, firstRun[1], rig),
        ];
    }

    public static IReadOnlyList<PlayStyleSummary> Summarize(IEnumerable<PlayStyleSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        return [.. samples.GroupBy(sample => (sample.Side, sample.Entrant))
            .OrderBy(group => group.Key.Side).ThenBy(group => group.Key.Entrant, StringComparer.Ordinal)
            .Select(group =>
            {
                PlayStyleSample[] games = [.. group];
                int[] scored = [.. games.Where(game => game.FirstPointTurn is not null).Select(game => game.FirstPointTurn!.Value)];
                int[] ran = [.. games.Where(game => game.FirstRunTurn is not null).Select(game => game.FirstRunTurn!.Value)];
                return new PlayStyleSummary(group.Key.Side, group.Key.Entrant, games.Length,
                    scored.Length > 0 ? scored.Average() : null,
                    games.Count(game => game.FirstPointTurn <= SetupTurn) / (double)games.Length,
                    games.Average(game => game.PointsByTurn8), games.Average(game => game.IceAtTurn6),
                    games.Average(game => game.RemotesAtTurn6), games.Average(game => game.AssetsInstalled),
                    games.Average(game => game.RunsByTurn6), ran.Length > 0 ? ran.Average() : null,
                    games.Average(game => game.RigAtTurn6));
            })];
    }

    private static PlayerState Player(GameState state, PlayerSide side) => state.Players.Single(player => player.Side == side);
}

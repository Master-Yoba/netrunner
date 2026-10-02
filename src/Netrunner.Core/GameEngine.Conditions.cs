namespace Netrunner.Core;

/// <summary>
/// Game-state conditions shared by abilities, behaviors and cost modifiers
/// (Elevation, EL.2): threat level, tag and zone counts, memory, turn events
/// and the active run event. Each reads only authoritative public state or the
/// source controller's own zones.
/// </summary>
public static partial class GameEngine
{
    /// <summary>CR 1.17.1a: the threat level is the greatest score of any player.</summary>
    internal static int ThreatLevel(IEnumerable<PlayerState> players) =>
        players.Select(static player => player.AgendaPoints).DefaultIfEmpty(0).Max();

    private static bool? SharedConditionMatches(
        WorkingState working,
        PlayerId controllerId,
        BehaviorCondition condition)
    {
        PlayerSide controllerSide = GetPlayer(working, controllerId).Side;
        int threshold = condition.Value.GetValueOrDefault();
        return condition.Kind switch
        {
            BehaviorConditionKind.RunEventActive => working.RunState?.ActiveRunEventId is not null,
            BehaviorConditionKind.ThreatLevelAtLeast => ThreatLevel(working.Players) >= threshold,
            BehaviorConditionKind.RunnerTagsAtLeast => RunnerTags(working) >= threshold,
            BehaviorConditionKind.ZoneCountAtMost =>
                ConditionZone(working, controllerSide, condition.Fact!) is { } zone && zone.CardIds.Count <= threshold,
            BehaviorConditionKind.ZoneCountAtLeast =>
                ConditionZone(working, controllerSide, condition.Fact!) is { } atLeastZone && atLeastZone.CardIds.Count >= threshold,
            BehaviorConditionKind.UnusedMemoryAtMost => UnusedRunnerMemory(working) <= threshold,
            BehaviorConditionKind.UnusedMemoryAtLeast => UnusedRunnerMemory(working) >= threshold,
            // "This turn" is the active turn: the controller's facts from its previous turn don't count.
            BehaviorConditionKind.PlayedThisTurn => working.Turn.ActiveSide == controllerSide
                && working.Turn.History?.ThisTurn(controllerSide)?.EventCount(
                    controllerSide == PlayerSide.Corp ? TurnEventKind.OperationPlayed : TurnEventKind.EventPlayed) > 0,
            BehaviorConditionKind.NoActionFinishedThisTurn => !HasFinishedActionThisTurn(working),
            BehaviorConditionKind.TurnEventCountIs =>
                (condition.SubjectSide is not { } turnOf || working.Turn.ActiveSide == turnOf)
                && Enum.TryParse(condition.Fact, ignoreCase: false, out TurnEventKind eventKind)
                && ActiveTurnEventCount(working, eventKind) == threshold,
            BehaviorConditionKind.ClicksRemainingAtLeast when condition.SubjectSide is { } clickSide =>
                GetPlayer(working, clickSide).Clicks >= threshold,
            _ => null,
        };
    }

    private static int RunnerTags(WorkingState working)
    {
        PlayerId runner = GetPlayer(working, PlayerSide.Runner).Id;
        return working.RulesState?.Players.SingleOrDefault(player => player.PlayerId == runner)?.Tags ?? 0;
    }

    private static int UnusedRunnerMemory(WorkingState working)
    {
        PlayerId runner = GetPlayer(working, PlayerSide.Runner).Id;
        int used = working.RulesState?.Players.SingleOrDefault(player => player.PlayerId == runner)?.UsedMemory ?? 0;
        return EffectiveMemoryLimit(working, runner) - used;
    }

    private static ZoneState? ConditionZone(WorkingState working, PlayerSide side, string zoneName)
    {
        if (!Enum.TryParse(zoneName, ignoreCase: false, out BehaviorZone zone))
        {
            return null;
        }

        ZoneKind? kind = (zone, side) switch
        {
            (BehaviorZone.Headquarters, _) => ZoneKind.CorpHand,
            (BehaviorZone.Deck, _) => ZoneKind.CorpDeck,
            (BehaviorZone.Archives, _) => ZoneKind.CorpDiscard,
            (BehaviorZone.Grip, _) => ZoneKind.RunnerGrip,
            (BehaviorZone.Stack, _) => ZoneKind.RunnerStack,
            (BehaviorZone.Heap, _) => ZoneKind.RunnerHeap,
            (BehaviorZone.ScoreArea, PlayerSide.Corp) => ZoneKind.CorpScore,
            (BehaviorZone.ScoreArea, PlayerSide.Runner) => ZoneKind.RunnerScore,
            (BehaviorZone.HostedFaceUp, PlayerSide.Corp) => ZoneKind.CorpHostedFaceUp,
            (BehaviorZone.HostedFaceUp, PlayerSide.Runner) => ZoneKind.RunnerHostedFaceUp,
            _ => null,
        };
        return kind is { } value ? working.Zones.FirstOrDefault(candidate => candidate.Kind == value) : null;
    }
}

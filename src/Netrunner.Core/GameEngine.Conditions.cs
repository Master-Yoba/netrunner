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

    /// <summary>
    /// VP-ENCOUNTER-SUBTYPE: the encountered ICE has the subtype, printed or gained (Chromatophores); with no subtype, any
    /// encounter matches.
    /// </summary>
    private static bool EncounteredIceHasSubtype(WorkingState working, string? subtype) =>
        subtype is null
        || working.RunState?.Encounter is { } encounter
            && working.Cards.FirstOrDefault(card => card.Id == encounter.IceId) is { } ice
            && (IceGainsEveryType(working.Cards, working.BehaviorCatalog, ice.Id)
                || CardSubtypes(working.RuntimeCardCatalog, ice.DefinitionId).Contains(subtype, StringComparer.Ordinal));

    private static bool? SharedConditionMatches(
        WorkingState working,
        PlayerId controllerId,
        BehaviorCondition condition,
        CardInstanceId? sourceCardId = null)
    {
        PlayerSide controllerSide = GetPlayer(working, controllerId).Side;
        int threshold = condition.Value.GetValueOrDefault();
        return condition.Kind switch
        {
            BehaviorConditionKind.RunEventActive => working.RunState?.ActiveRunEventId is not null,
            BehaviorConditionKind.RunInProgress => working.RunState is not null,
            BehaviorConditionKind.RezzedSubtypeInAttackedServer => working.RunState is { } attackedRun
                && working.Cards.Any(card => card.OwnerSide == PlayerSide.Corp
                    && card.Rezzed
                    && working.Zones.FirstOrDefault(zone => zone.Id == card.ZoneId) is { Kind: ZoneKind.ServerRoot or ZoneKind.ServerIce } zone
                    && zone.ServerId == attackedRun.AttackedServer
                    && CardSubtypes(working.RuntimeCardCatalog, card.DefinitionId).Contains(condition.Fact!, StringComparer.Ordinal)),
            BehaviorConditionKind.CreditsAtLeast when condition.SubjectSide is { } creditSide =>
                GetPlayer(working, creditSide).Credits >= threshold,
            BehaviorConditionKind.BadPublicityAtLeast => (working.RulesState?.BadPublicity ?? 0) >= threshold,
            BehaviorConditionKind.RunAgainstSourceServer => working.RunState is { } sourceRun
                && sourceCardId is { } sameSource
                && SourceServerId(working, GetCard(working, sameSource)) == sourceRun.AttackedServer,
            BehaviorConditionKind.RunAgainstAnotherServer => working.RunState is { } otherRun
                && sourceCardId is { } otherSource
                && SourceServerId(working, GetCard(working, otherSource)) is { } sourceServer
                && sourceServer != otherRun.AttackedServer,
            BehaviorConditionKind.IdentityFlipped => working.RulesState?.SecretIdentity is { Flipped: true },
            BehaviorConditionKind.IdentityNotFlipped => working.RulesState?.SecretIdentity is not { Flipped: true },
            BehaviorConditionKind.SecretIdentityMatchesAttackedServer => working.RulesState?.SecretIdentity is { } secret
                && working.RunState is { } secretRun
                && working.Servers.FirstOrDefault(server => server.Id == secretRun.AttackedServer)?.Kind == secret.Side,
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
            BehaviorConditionKind.TurnEventCountAtLeast =>
                (condition.SubjectSide is not { } atLeastTurnOf || working.Turn.ActiveSide == atLeastTurnOf)
                && Enum.TryParse(condition.Fact, ignoreCase: false, out TurnEventKind atLeastKind)
                && ActiveTurnEventCount(working, atLeastKind) >= threshold,
            BehaviorConditionKind.SuccessfulRunOnEveryCentralThisTurn =>
                working.Turn.History?.ThisTurn(PlayerSide.Runner) is { } runnerFacts
                && working.Turn.ActiveSide == PlayerSide.Runner
                && new[] { ServerKind.Headquarters, ServerKind.RAndD, ServerKind.Archives }.All(central =>
                    working.Servers.FirstOrDefault(server => server.Kind == central) is { } centralServer
                    && runnerFacts.SuccessfulRunServerIds.Contains(centralServer.Id)),
            BehaviorConditionKind.SourceScoredThisTurn => sourceCardId is { } scored
                && working.Turn.History?.ThisTurn(working.Turn.ActiveSide)?.AgendaTurnChanges.Any(change =>
                    change.CardId == scored && change.Kind == AgendaTurnChangeKind.Score) == true,
            BehaviorConditionKind.ClicksRemainingAtLeast when condition.SubjectSide is { } clickSide =>
                GetPlayer(working, clickSide).Clicks >= threshold,
            BehaviorConditionKind.ClicksRemainingAtMost when condition.SubjectSide is { } atMostSide =>
                GetPlayer(working, atMostSide).Clicks <= threshold,
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

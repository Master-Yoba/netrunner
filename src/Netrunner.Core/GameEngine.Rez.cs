namespace Netrunner.Core;

/// <summary>
/// Runtime-catalog-backed rez projection and reducer. This path is kept
/// separate from the runtime-free M2 ICE reducer so the two action contracts
/// cannot accidentally share printed metadata or event semantics.
/// </summary>
public static partial class GameEngine
{
    /// <summary>
    /// Re-materializes every legal runtime-catalog rez payload for the active
    /// turn or run timing decision without consuming allocator state.
    /// </summary>
    internal static IReadOnlyList<RezCardParameters> MaterializeRezCardParameters(
        GameState state,
        IRuntimeCardCatalog runtimeCardCatalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(runtimeCardCatalog);
        return MaterializeRezCardParameters(
            WorkingState.From(state, CardBehaviorSpecCatalog.Default, runtimeCardCatalog));
    }

    private static List<RezCardParameters> MaterializeRezCardParameters(WorkingState working)
    {
        if (working.RuntimeCardCatalog is null)
        {
            return [];
        }

        if (IsTurnRezTimingWindow(working))
        {
            return MaterializeTurnRezCardParameters(working);
        }

        if (IsRunRezTimingWindow(working))
        {
            return MaterializeRunRezCardParameters(working);
        }

        return [];
    }

    private static bool IsTurnRezTimingWindow(WorkingState working) =>
        working.RuntimeCardCatalog is not null
        && working.RunState is null
        && working.Turn.ActiveSide is PlayerSide.Corp or PlayerSide.Runner
        && working.ResolutionStack.Count == 1
        && working.ResolutionStack[^1] is TurnFrame
        {
            ActiveSide: var activeSide,
            Stage: TurnFrameStage.BeforeTurnPriority
                or TurnFrameStage.ActionPriority
                or TurnFrameStage.DiscardPriority,
            PriorityWindow: TurnPriorityWindowKind.PaidRezAndScore
                or TurnPriorityWindowKind.PaidAndRez,
            PriorityHolder: PlayerSide.Corp,
        } frame
        && frame.TurnNumber == working.Turn.TurnNumber
        && working.Turn.Phase == (activeSide == PlayerSide.Corp ? TurnPhase.Corp : TurnPhase.Runner)
        && !HasOccupiedTransientPlayArea(working);

    private static bool IsRunRezTimingWindow(WorkingState working)
    {
        if (working.RuntimeCardCatalog is null
            || working.RunState is not { } run
            || !IsPaidWindow(run.Step)
            || run.Priority is not { PriorityHolder: PlayerSide.Corp }
            || working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not RunTimingFrame frame)
        {
            return false;
        }

        return frame.RunId == run.Id && frame.Step == run.Step;
    }

    private static List<RezCardParameters> MaterializeTurnRezCardParameters(WorkingState working)
    {
        PlayerState corp = GetPlayer(working, PlayerSide.Corp);
        var materialized = new List<RezCardParameters>();

        foreach ((ServerState server, ZoneState root) in PhysicalServerRoots(working))
        {
            AddAffordableRootRezCards(working, corp.Credits, server, root, materialized);
        }

        return materialized;
    }

    private static List<RezCardParameters> MaterializeRunRezCardParameters(WorkingState working)
    {
        RunState run = working.RunState!;
        RunPriorityState priority = run.Priority!;
        PlayerState corp = GetPlayer(working, PlayerSide.Corp);
        var materialized = new List<RezCardParameters>();

        ServerState? attackedServer = working.Servers.FirstOrDefault(server => server.Id == run.AttackedServer);
        ZoneState? attackedIceZone = attackedServer is null
            ? null
            : working.Zones.FirstOrDefault(zone =>
                zone.Id == attackedServer.IceZoneId
                && zone.Kind == ZoneKind.ServerIce
                && zone.ServerId == attackedServer.Id
                && zone.OwnerSide == PlayerSide.Corp);

        foreach ((ServerState server, ZoneState root) in PhysicalServerRoots(working))
        {
            if (priority.CorpMayRezApproachedIce
                && run.Step == RunTimingStep.ApproachPaidWindow
                && attackedServer is not null
                && attackedIceZone is not null
                && server.Id == attackedServer.Id
                && run.CurrentIceIndex >= 0
                && run.CurrentIceIndex < attackedIceZone.CardIds.Count
                && run.CurrentIceId.HasValue
                && attackedIceZone.CardIds[run.CurrentIceIndex] == run.CurrentIceId.Value)
            {
                CardInstanceId currentIceId = run.CurrentIceId.Value;
                CardInstanceState? currentIce = working.Cards.FirstOrDefault(card => card.Id == currentIceId);
                if (currentIce is not null
                    && currentIce.ZoneId == attackedIceZone.Id
                    && currentIce.OwnerSide == PlayerSide.Corp
                    && !currentIce.Rezzed
                    && TryGetRezCost(working, currentIce, RuntimeCardClass.Ice, out int iceCost)
                    && corp.Credits >= iceCost)
                {
                    materialized.Add(new RezCardParameters(currentIce.Id, iceCost));
                }
            }

            if (priority.CorpMayRezNonIce)
            {
                AddAffordableRootRezCards(working, corp.Credits, server, root, materialized);
            }
        }

        return materialized;
    }

    private static IEnumerable<(ServerState Server, ZoneState Root)> PhysicalServerRoots(WorkingState working)
    {
        foreach (ServerState server in working.Servers)
        {
            ZoneState? root = working.Zones.FirstOrDefault(zone =>
                zone.Id == server.RootZoneId
                && zone.Kind == ZoneKind.ServerRoot
                && zone.ServerId == server.Id
                && zone.OwnerSide == PlayerSide.Corp);
            if (root is not null)
            {
                yield return (server, root);
            }
        }
    }

    private static void AddAffordableRootRezCards(
        WorkingState working,
        int corpCredits,
        ServerState server,
        ZoneState root,
        List<RezCardParameters> materialized)
    {
        foreach (CardInstanceId cardId in root.CardIds)
        {
            CardInstanceState? card = working.Cards.FirstOrDefault(candidate => candidate.Id == cardId);
            int assetCost = 0;
            int upgradeCost = 0;
            bool isAsset = card is not null
                && TryGetRezCost(working, card, RuntimeCardClass.Asset, out assetCost);
            bool isUpgrade = card is not null
                && TryGetRezCost(working, card, RuntimeCardClass.Upgrade, out upgradeCost);
            int cost = isAsset ? assetCost : upgradeCost;
            if (card is null
                || card.ZoneId != root.Id
                || card.OwnerSide != PlayerSide.Corp
                || card.Rezzed
                || (!isAsset && !isUpgrade)
                || (isAsset && server.Kind != ServerKind.Remote)
                || corpCredits < cost)
            {
                continue;
            }

            materialized.Add(new RezCardParameters(card.Id, cost));
        }
    }

    private static bool TryGetRezCost(
        WorkingState working,
        CardInstanceState card,
        RuntimeCardClass expectedClass,
        out int cost)
    {
        cost = 0;
        if (working.RuntimeCardCatalog is null
            || !working.RuntimeCardCatalog.TryGet(card.DefinitionId, out CardRuntimeSpec? spec)
            || spec.Side != PlayerSide.Corp
            || spec.CardClass != expectedClass
            || !spec.RezCost.HasValue
            || spec.RezCost.Value < 0)
        {
            return false;
        }

        cost = spec.RezCost.Value;
        return true;
    }

    private static void AddRezCardActions(WorkingState working, List<LegalAction> actions)
    {
        foreach (RezCardParameters parameters in MaterializeRezCardParameters(working))
        {
            actions.Add(new LegalAction(
                working.Allocator.NextActionId(),
                ActionKind.RezCard,
                $"Rez card {parameters.CardId.Value}",
                parameters));
        }
    }

    private static void ConsumeRezCardAction(WorkingState working, RezCardParameters parameters)
    {
        IReadOnlyList<RezCardParameters> materialized = MaterializeRezCardParameters(working);
        RezCardParameters? issued = materialized.FirstOrDefault(candidate =>
            candidate.CardId == parameters.CardId
            && candidate.CreditCost == parameters.CreditCost);
        if (issued is null)
        {
            throw new ResolutionFailureException(
                "The rez action does not match the complete current runtime-catalog rez set.");
        }

        CardInstanceState card = GetCard(working, parameters.CardId);
        PlayerState corp = GetPlayer(working, PlayerSide.Corp);
        if (parameters.CreditCost > 0)
        {
            ApplyCreditDelta(working, corp.Id, -parameters.CreditCost);
        }

        ReplaceCard(working, new CardInstanceState(
            card.Id,
            card.DefinitionId,
            card.ZoneId,
            card.OwnerSide,
            faceUp: true,
            rezzed: true,
            card.AdvancementCounters));
        Emit(working, EngineEventKinds.CardRezzed, new CardRezzedEventPayload(
            parameters.CardId,
            parameters.CreditCost));
        TrashCorpUniqueRezConflicts(working, card.Id);

        if (IsTurnRezTimingWindow(working))
        {
            TurnFrame frame = (TurnFrame)working.ResolutionStack[^1];
            working.ResolutionStack[^1] = new TurnFrame(
                frame.Version,
                frame.BehaviorId,
                frame.ActiveSide,
                frame.TurnNumber,
                frame.Stage,
                frame.PriorityWindow,
                PlayerSide.Corp,
                consecutivePasses: 0,
                frame.ForcedActionEnd);
            return;
        }

        RunState run = working.RunState
            ?? throw new ResolutionFailureException("A runtime rez action requires live run state.");
        RunPriorityState priority = run.Priority
            ?? throw new ResolutionFailureException("A runtime rez action requires run priority state.");
        bool consumedApproachedIce = run.Step == RunTimingStep.ApproachPaidWindow
            && priority.CorpMayRezApproachedIce
            && run.CurrentIceId == parameters.CardId;
        working.RunState = run.WithTiming(
            run.Step,
            run.CurrentIceIndex,
            run.CurrentIceId,
            run.Successful,
            run.Unsuccessful,
            run.EndReason,
            new RunPriorityState(
                priority.Version,
                priority.Window,
                PlayerSide.Corp,
                consecutivePasses: 0,
                corpMayRezApproachedIce: priority.CorpMayRezApproachedIce && !consumedApproachedIce,
                corpMayRezNonIce: priority.CorpMayRezNonIce),
            run.Encounter,
            run.AccessState);
        working.ResolutionStack[^1] = new RunTimingFrame(run.Id, run.Step);
    }

    private static void TrashCorpUniqueRezConflicts(
        WorkingState working,
        CardInstanceId incomingCardId)
    {
        if (working.RuntimeCardCatalog is not { } runtimeCardCatalog
            || !runtimeCardCatalog.TryGet(GetCard(working, incomingCardId).DefinitionId, out CardRuntimeSpec? incomingSpec)
            || incomingSpec.Side != PlayerSide.Corp
            || !incomingSpec.Unique
            || incomingSpec.UniquenessKey is not { } uniquenessKey)
        {
            return;
        }

        CardInstanceId[] conflicts = working.Cards
            .Where(card => card.Id != incomingCardId)
            .Select(card => (Card: card, IsActive: RuntimeCardActivity.IsActive(card, working.Zones)))
            .Where(value => value.IsActive)
            .Where(value => runtimeCardCatalog.TryGet(value.Card.DefinitionId, out CardRuntimeSpec? spec)
                && spec.Side == PlayerSide.Corp
                && spec.Unique
                && string.Equals(spec.UniquenessKey, uniquenessKey, StringComparison.Ordinal))
            .Select(value => value.Card.Id)
            .OrderBy(cardId => cardId.Value, StringComparer.Ordinal)
            .ToArray();

        foreach (CardInstanceId conflictId in conflicts)
        {
            CardInstanceState conflict = GetCard(working, conflictId);
            ZoneState source = ZoneForCard(working, conflict.Id);
            ZoneState archives = GetZone(working, ZoneKind.CorpDiscard);
            MoveCard(working, conflict, source, archives);
        }
    }

}

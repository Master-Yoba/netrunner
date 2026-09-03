namespace Netrunner.Core;

/// <summary>
/// Real-turn reducers for the remaining basic tag, resource, and counter
/// actions.  The projections in this file are payload-only and run against a
/// detached working copy, so asking for legal actions never consumes IDs or
/// mutates the supplied state.
/// </summary>
public static partial class GameEngine
{
    /// <summary>
    /// Re-materializes the Runner's standard tag-removal action without
    /// allocating action or decision IDs. Missing rules state means that the
    /// Runner has no tags.
    /// </summary>
    internal static IReadOnlyList<RemoveTagParameters> MaterializeRemoveTagParameters(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return MaterializeRemoveTagParameters(WorkingState.From(state));
    }

    private static List<RemoveTagParameters> MaterializeRemoveTagParameters(WorkingState working)
    {
        if (!IsBasicTurnActionWindow(working, PlayerSide.Runner))
        {
            return [];
        }

        PlayerState runner = GetPlayer(working, PlayerSide.Runner);
        if (runner.Clicks < RealGameRules.RemoveTagClickCost
            || runner.Credits < RealGameRules.RemoveTagCreditCost
            || !HasStandardTag(working, runner.Id))
        {
            return [];
        }

        return [new RemoveTagParameters(
            RealGameRules.RemoveTagClickCost,
            RealGameRules.RemoveTagCreditCost)];
    }

    /// <summary>
    /// Re-materializes tagged Runner resources in canonical physical Runner
    /// Rig order. Runtime metadata is the sole authority for the Resource
    /// class; no card text or behavior registry is inspected.
    /// </summary>
    internal static IReadOnlyList<TrashTaggedResourceParameters> MaterializeTrashTaggedResourceParameters(
        GameState state,
        IRuntimeCardCatalog runtimeCardCatalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(runtimeCardCatalog);
        return MaterializeTrashTaggedResourceParameters(
            WorkingState.From(state, CardBehaviorSpecCatalog.Default, runtimeCardCatalog));
    }

    private static List<TrashTaggedResourceParameters> MaterializeTrashTaggedResourceParameters(
        WorkingState working)
    {
        if (working.RuntimeCardCatalog is null
            || !IsBasicTurnActionWindow(working, PlayerSide.Corp))
        {
            return [];
        }

        PlayerState runner = GetPlayer(working, PlayerSide.Runner);
        PlayerState corp = GetPlayer(working, PlayerSide.Corp);
        if (corp.Clicks < RealGameRules.TrashTaggedResourceClickCost
            || corp.Credits < RealGameRules.TrashTaggedResourceCreditCost
            || !HasStandardTag(working, runner.Id)
            || !TryGetCanonicalZone(working, ZoneKind.RunnerRig, PlayerSide.Runner, out ZoneState? rig)
            || rig is null)
        {
            return [];
        }

        var materialized = new List<TrashTaggedResourceParameters>();
        // Deliberately retain the physical Runner Rig ordering. This ordering
        // is the action issuance contract and is not replaced by card-ID sort.
        foreach (CardInstanceId cardId in rig.CardIds)
        {
            CardInstanceState? card = working.Cards.FirstOrDefault(candidate => candidate.Id == cardId);
            if (card is null
                || working.Cards.Count(candidate => candidate.Id == cardId) != 1
                || working.Zones.Count(zone => zone.CardIds.Contains(cardId)) != 1
                || card.OwnerSide != PlayerSide.Runner
                || card.ZoneId != rig.Id
                || !card.FaceUp
                || card.Rezzed
                || !working.RuntimeCardCatalog.TryGet(card.DefinitionId, out CardRuntimeSpec? spec)
                || spec.DefinitionId != card.DefinitionId
                || spec.Side != PlayerSide.Runner
                || spec.CardClass != RuntimeCardClass.Resource)
            {
                continue;
            }

            materialized.Add(new TrashTaggedResourceParameters(
                card.Id,
                RealGameRules.TrashTaggedResourceClickCost,
                RealGameRules.TrashTaggedResourceCreditCost));
        }

        return materialized;
    }

    /// <summary>
    /// Re-materializes the Corp's global virus-counter purge action without
    /// allocating action or decision IDs. The action remains available when
    /// RulesState is absent because purge creates the default rules aggregate
    /// as part of its accepted reducer transaction.
    /// </summary>
    internal static IReadOnlyList<PurgeVirusCountersParameters> MaterializePurgeVirusCountersParameters(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return MaterializePurgeVirusCountersParameters(WorkingState.From(state));
    }

    private static List<PurgeVirusCountersParameters> MaterializePurgeVirusCountersParameters(
        WorkingState working)
    {
        if (!IsBasicTurnActionWindow(working, PlayerSide.Corp))
        {
            return [];
        }

        PlayerState corp = GetPlayer(working, PlayerSide.Corp);
        return corp.Clicks < RealGameRules.PurgeVirusCountersClickCost
            ? []
            : [new PurgeVirusCountersParameters(RealGameRules.PurgeVirusCountersClickCost)];
    }

    private static void AddTagResourcePurgeActions(WorkingState working, List<LegalAction> actions)
    {
        foreach (RemoveTagParameters parameters in MaterializeRemoveTagParameters(working))
        {
            actions.Add(new LegalAction(
                working.Allocator.NextActionId(),
                ActionKind.RemoveTag,
                "Remove 1 tag",
                parameters));
        }

        foreach (TrashTaggedResourceParameters parameters in MaterializeTrashTaggedResourceParameters(working))
        {
            actions.Add(new LegalAction(
                working.Allocator.NextActionId(),
                ActionKind.TrashTaggedResource,
                "Trash tagged resource",
                parameters));
        }

        foreach (PurgeVirusCountersParameters parameters in MaterializePurgeVirusCountersParameters(working))
        {
            actions.Add(new LegalAction(
                working.Allocator.NextActionId(),
                ActionKind.PurgeVirusCounters,
                "Purge virus counters",
                parameters));
        }
    }

    private static void ConsumeRemoveTagAction(
        WorkingState working,
        RemoveTagParameters parameters)
    {
        if (!IsBasicTurnActionWindow(working, PlayerSide.Runner))
        {
            throw new ResolutionFailureException(
                "Removing a tag requires the active Runner turn action frame.");
        }

        RemoveTagParameters? issued = MaterializeRemoveTagParameters(working)
            .FirstOrDefault(candidate =>
                candidate.ClickCost == parameters.ClickCost
                && candidate.CreditCost == parameters.CreditCost);
        if (issued is null
            || parameters.ClickCost != RealGameRules.RemoveTagClickCost
            || parameters.CreditCost != RealGameRules.RemoveTagCreditCost)
        {
            throw new ResolutionFailureException(
                "The remove-tag action is stale or does not carry the fixed costs.");
        }

        PlayerState runner = GetPlayer(working, PlayerSide.Runner);
        SpendClick(working, runner.Id);
        ApplyCreditDelta(working, runner.Id, -RealGameRules.RemoveTagCreditCost);
        ApplyTags(working, runner.Id, -1);
        StartNextTurnActionWindow(working, (TurnFrame)working.ResolutionStack[^1]);
    }

    private static void ConsumeTrashTaggedResourceAction(
        WorkingState working,
        TrashTaggedResourceParameters parameters)
    {
        if (!IsBasicTurnActionWindow(working, PlayerSide.Corp))
        {
            throw new ResolutionFailureException(
                "Trashing a tagged resource requires the active Corp turn action frame.");
        }

        TrashTaggedResourceParameters? issued = MaterializeTrashTaggedResourceParameters(working)
            .FirstOrDefault(candidate =>
                candidate.CardId == parameters.CardId
                && candidate.ClickCost == parameters.ClickCost
                && candidate.CreditCost == parameters.CreditCost);
        if (issued is null
            || parameters.ClickCost != RealGameRules.TrashTaggedResourceClickCost
            || parameters.CreditCost != RealGameRules.TrashTaggedResourceCreditCost)
        {
            throw new ResolutionFailureException(
                "The tagged-resource trash action is stale or does not carry the fixed costs.");
        }

        if (!TryGetCanonicalZone(working, ZoneKind.RunnerRig, PlayerSide.Runner, out ZoneState? rig)
            || !TryGetCanonicalZone(working, ZoneKind.RunnerHeap, PlayerSide.Runner, out ZoneState? heap)
            || rig is null
            || heap is null)
        {
            throw new ResolutionFailureException("The canonical Runner Rig or heap is unavailable.");
        }

        CardInstanceState card = GetCard(working, parameters.CardId);
        if (card.ZoneId != rig.Id
            || !rig.CardIds.Contains(card.Id)
            || card.OwnerSide != PlayerSide.Runner
            || !card.FaceUp
            || card.Rezzed)
        {
            throw new ResolutionFailureException("The tagged resource is no longer installed in the Runner Rig.");
        }

        PlayerState corp = GetPlayer(working, PlayerSide.Corp);
        SpendClick(working, corp.Id);
        ApplyCreditDelta(working, corp.Id, -RealGameRules.TrashTaggedResourceCreditCost);
        TrashCard(working, card, rig, heap);
        StartNextTurnActionWindow(working, (TurnFrame)working.ResolutionStack[^1]);
    }

    private static void ConsumePurgeVirusCountersAction(
        WorkingState working,
        PurgeVirusCountersParameters parameters)
    {
        if (!IsBasicTurnActionWindow(working, PlayerSide.Corp))
        {
            throw new ResolutionFailureException(
                "Purging virus counters requires the active Corp turn action frame.");
        }

        PurgeVirusCountersParameters? issued = MaterializePurgeVirusCountersParameters(working)
            .FirstOrDefault(candidate => candidate.ClickCost == parameters.ClickCost);
        if (issued is null || parameters.ClickCost != RealGameRules.PurgeVirusCountersClickCost)
        {
            throw new ResolutionFailureException(
                "The purge action is stale or does not carry the fixed click cost.");
        }

        PlayerState corp = GetPlayer(working, PlayerSide.Corp);
        for (int index = 0; index < RealGameRules.PurgeVirusCountersClickCost; index++)
        {
            SpendClick(working, corp.Id);
        }

        EnsureRulesState(working);
        PurgeVirusCounters(working);
        StartNextTurnActionWindow(working, (TurnFrame)working.ResolutionStack[^1]);
    }

    private static bool IsBasicTurnActionWindow(WorkingState working, PlayerSide side)
    {
        if (working.Turn.ActiveSide != side
            || working.Turn.Phase != (side == PlayerSide.Corp ? TurnPhase.Corp : TurnPhase.Runner)
            || working.RunState is not null
            || working.ResolutionStack.Count != 1
            || working.ResolutionStack[^1] is not TurnFrame frame
            || frame.ActiveSide != side
            || frame.Stage != TurnFrameStage.AwaitingAction
            || frame.TurnNumber != working.Turn.TurnNumber)
        {
            return false;
        }

        return !HasOccupiedTransientPlayArea(working);
    }

    private static bool HasStandardTag(WorkingState working, PlayerId runnerId)
    {
        RulesState? rules = working.RulesState;
        if (rules is null)
        {
            return false;
        }

        PlayerRulesState[] matches = rules.Players
            .Where(player => player.PlayerId == runnerId)
            .ToArray();
        return matches.Length == 1 && matches[0].Tags > 0;
    }
}

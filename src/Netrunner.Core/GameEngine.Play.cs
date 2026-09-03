namespace Netrunner.Core;

/// <summary>
/// Reducers for ordinary Corp Operation and Runner Event play.  Play is a
/// two-part transaction: the action opens a frozen parent cursor and starts a
/// generic behavior, then the parent completes only after that child returns.
/// </summary>
public static partial class GameEngine
{
    private sealed record PlayCardPlan(
        CardInstanceState Card,
        CardRuntimeSpec Spec,
        CardBehaviorDefinition Definition,
        ZoneState Source,
        ZoneState? PlayArea,
        ZoneState Discard,
        int CreditCost);

    /// <summary>
    /// Re-materializes ordinary play payloads without consuming allocator
    /// state.  The projection is intentionally payload-only so validation can
    /// compare a serialized decision without creating replacement action IDs.
    /// </summary>
    internal static IReadOnlyList<PlayCardParameters> MaterializePlayCardParameters(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog runtimeCardCatalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        ArgumentNullException.ThrowIfNull(runtimeCardCatalog);

        WorkingState working = WorkingState.From(state, behaviorCatalog, runtimeCardCatalog);
        return MaterializePlayCardParameters(working);
    }

    private static List<PlayCardParameters> MaterializePlayCardParameters(WorkingState working)
    {
        if (working.RuntimeCardCatalog is null
            || working.Turn.ActiveSide is not (PlayerSide.Corp or PlayerSide.Runner))
        {
            return [];
        }

        PlayerSide side = working.Turn.ActiveSide;
        PlayerState? player = working.Players.FirstOrDefault(candidate => candidate.Side == side);
        if (player is null || player.Clicks < 1)
        {
            return [];
        }

        ZoneKind sourceKind = side == PlayerSide.Corp ? ZoneKind.CorpHand : ZoneKind.RunnerGrip;
        ZoneKind playAreaKind = side == PlayerSide.Corp ? ZoneKind.CorpPlayArea : ZoneKind.RunnerPlayArea;
        ZoneKind discardKind = side == PlayerSide.Corp ? ZoneKind.CorpDiscard : ZoneKind.RunnerHeap;
        if (!TryGetCanonicalZone(working, sourceKind, side, out ZoneState? source)
            || !TryGetCanonicalZone(working, discardKind, side, out ZoneState? discard)
            || !TryGetCanonicalPlayArea(working, playAreaKind, side, out ZoneState? playArea))
        {
            return [];
        }

        if (source is null || discard is null)
        {
            return [];
        }

        if (playArea is not null && playArea.CardIds.Count != 0)
        {
            return [];
        }

        var materialized = new List<PlayCardParameters>(source.CardIds.Count);
        // Deliberately retain the physical source-zone ordering.  This is the
        // authoritative order used by action issuance and later revalidation.
        foreach (CardInstanceId cardId in source.CardIds)
        {
            CardInstanceState? card = working.Cards.FirstOrDefault(candidate => candidate.Id == cardId);
            if (card is null
                || source.CardIds.Count(candidate => candidate == cardId) != 1
                || card.OwnerSide != side
                || card.ZoneId != source.Id)
            {
                continue;
            }

            if (!TryBuildPlayCardPlan(working, side, card, source, discard, playArea, out PlayCardPlan? plan)
                || plan is null)
            {
                continue;
            }

            materialized.Add(new PlayCardParameters(plan.Card.Id, plan.CreditCost));
        }

        return materialized;
    }

    private static bool TryBuildPlayCardPlan(
        WorkingState working,
        PlayerSide side,
        CardInstanceState card,
        ZoneState source,
        ZoneState discard,
        ZoneState? playArea,
        out PlayCardPlan? plan,
        bool validateBehaviorTargets = true)
    {
        plan = null;
        IRuntimeCardCatalog? runtimeCardCatalog = working.RuntimeCardCatalog;
        if (runtimeCardCatalog is null
            || card.OwnerSide != side
            || card.ZoneId != source.Id
            || !source.CardIds.Contains(card.Id)
            || !runtimeCardCatalog.TryGet(card.DefinitionId, out CardRuntimeSpec? spec)
            || spec.DefinitionId != card.DefinitionId
            || spec.Side != side
            || spec.PlayCost is not int printedCost
            || printedCost < 0
            || (side == PlayerSide.Corp && spec.CardClass != RuntimeCardClass.Operation)
            || (side == PlayerSide.Runner && spec.CardClass != RuntimeCardClass.Event)
            || !working.BehaviorCatalog.TryGet(card.DefinitionId, out CardBehaviorDefinition? definition)
            || definition.DefinitionId != card.DefinitionId
            || definition.Controller != side)
        {
            return false;
        }

        int cost = 0;
        try
        {
            foreach (BehaviorCost behaviorCost in definition.Costs)
            {
                if (behaviorCost.Kind != BehaviorCostKind.Credits || behaviorCost.Amount < 0)
                {
                    return false;
                }

                cost = checked(cost + behaviorCost.Amount);
            }
        }
        catch (OverflowException)
        {
            return false;
        }

        if (cost != printedCost
            || GetPlayer(working, side).Credits < cost)
        {
            return false;
        }

        if (validateBehaviorTargets
            && !AreBehaviorPlayTargetsAvailable(working, definition))
        {
            return false;
        }

        plan = new PlayCardPlan(card, spec, definition, source, playArea, discard, cost);
        return true;
    }

    private static bool TryGetCanonicalZone(
        WorkingState working,
        ZoneKind kind,
        PlayerSide side,
        out ZoneState? zone)
    {
        ZoneState[] matches = working.Zones
            .Where(candidate => candidate.Kind == kind)
            .ToArray();
        if (matches.Length != 1)
        {
            zone = null;
            return false;
        }

        zone = matches[0];
        return zone.OwnerSide == side && zone.ServerId is null;
    }

    private static bool TryGetCanonicalPlayArea(
        WorkingState working,
        ZoneKind kind,
        PlayerSide side,
        out ZoneState? zone)
    {
        ZoneState[] matches = working.Zones
            .Where(candidate => candidate.Kind == kind)
            .ToArray();
        if (matches.Length == 0)
        {
            zone = null;
            return true;
        }

        if (matches.Length != 1)
        {
            zone = null;
            return false;
        }

        zone = matches[0];
        return zone.OwnerSide == side && zone.ServerId is null;
    }

    private static void AddPlayActions(WorkingState working, List<LegalAction> actions)
    {
        foreach (PlayCardParameters parameters in MaterializePlayCardParameters(working))
        {
            actions.Add(new LegalAction(
                working.Allocator.NextActionId(),
                ActionKind.PlayCard,
                "Play card",
                parameters));
        }
    }

    private static void ConsumeTurnPlayAction(WorkingState working, PlayCardParameters parameters)
    {
        if (working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not TurnFrame turnFrame
            || turnFrame.Stage != TurnFrameStage.AwaitingAction
            || working.Turn.ActiveSide != turnFrame.ActiveSide)
        {
            throw new ResolutionFailureException("Card play requires the active turn action frame.");
        }

        PlayerSide side = turnFrame.ActiveSide;
        IReadOnlyList<PlayCardParameters> materialized = MaterializePlayCardParameters(working);
        if (!materialized.Any(candidate => PlayParametersMatch(candidate, parameters)))
        {
            throw new ResolutionFailureException("The play action does not match the materialized play plan.");
        }

        ZoneKind sourceKind = side == PlayerSide.Corp ? ZoneKind.CorpHand : ZoneKind.RunnerGrip;
        ZoneKind playAreaKind = side == PlayerSide.Corp ? ZoneKind.CorpPlayArea : ZoneKind.RunnerPlayArea;
        ZoneKind discardKind = side == PlayerSide.Corp ? ZoneKind.CorpDiscard : ZoneKind.RunnerHeap;
        if (!TryGetCanonicalZone(working, sourceKind, side, out ZoneState? source)
            || !TryGetCanonicalZone(working, discardKind, side, out ZoneState? discard)
            || !TryGetCanonicalPlayArea(working, playAreaKind, side, out ZoneState? playArea))
        {
            throw new ResolutionFailureException("The canonical card-play zones are unavailable.");
        }

        if (source is null || discard is null)
        {
            throw new ResolutionFailureException("The canonical card-play source or discard zone is unavailable.");
        }

        CardInstanceState card = working.Cards.SingleOrDefault(candidate => candidate.Id == parameters.CardId)
            ?? throw new ResolutionFailureException($"Card '{parameters.CardId}' does not exist.");
        if (playArea is not null && playArea.CardIds.Count != 0)
        {
            throw new ResolutionFailureException("The side's play area is occupied.");
        }

        if (!TryBuildPlayCardPlan(working, side, card, source, discard, playArea, out PlayCardPlan? plan)
            || plan is null
            || plan.CreditCost != parameters.CreditCost)
        {
            throw new ResolutionFailureException("The play action is stale or unsupported.");
        }

        playArea ??= new ZoneState(working.Allocator.NextZoneId(), playAreaKind, side);
        if (playArea.CardIds.Count != 0)
        {
            throw new ResolutionFailureException("The side's play area is occupied.");
        }

        if (working.Zones.All(candidate => candidate.Id != playArea.Id))
        {
            working.Zones.Add(playArea);
        }

        var cardPlayFrame = new CardPlayFrame(
            GetPlayer(working, side).Id,
            side,
            plan.Card.Id,
            plan.Card.DefinitionId,
            plan.Spec.CardClass,
            source.Id,
            source.Kind,
            playArea.Id,
            discard.Id,
            plan.CreditCost,
            working.BehaviorCatalog.Fingerprint,
            working.BehaviorCatalog.GetDefinitionFingerprint(plan.Card.DefinitionId));
        working.ResolutionStack.Add(cardPlayFrame);

        PlayerState player = GetPlayer(working, side);
        SpendClick(working, player.Id);

        CardInstanceState sourceCard = GetCard(working, plan.Card.Id);
        ReplaceCard(working, new CardInstanceState(
            sourceCard.Id,
            sourceCard.DefinitionId,
            sourceCard.ZoneId,
            sourceCard.OwnerSide,
            faceUp: true,
            rezzed: false,
            sourceCard.AdvancementCounters));
        sourceCard = GetCard(working, sourceCard.Id);
        MoveCard(working, sourceCard, source, playArea);
        Emit(working, EngineEventKinds.CardPlayStarted, new CardPlayStartedEventPayload(
            side,
            plan.Card.Id,
            plan.Card.DefinitionId,
            source.Id,
            playArea.Id,
            plan.CreditCost));

        PushBehaviorChild(
            working,
            new BehaviorInvocationContext(plan.Card.Id, player.Id, player.Id),
            plan.Definition,
            playArea);
    }

    private static void StepCardPlay(WorkingState working, CardPlayFrame frame)
    {
        _ = ValidateCardPlayFrame(working, frame);
        CardInstanceState card = GetCard(working, frame.CardId);
        ZoneState playArea = working.Zones.Single(zone => zone.Id == frame.PlayAreaZoneId);
        ZoneState discard = working.Zones.Single(zone => zone.Id == frame.DiscardZoneId);

        TrashCard(working, card, playArea, discard);
        Emit(working, EngineEventKinds.CardPlayCompleted, new CardPlayCompletedEventPayload(
            frame.Side,
            frame.CardId,
            frame.DefinitionId,
            frame.PlayAreaZoneId,
            frame.DiscardZoneId));
        working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);

        if (working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not TurnFrame parent
            || parent.Stage != TurnFrameStage.AwaitingAction
            || parent.ActiveSide != frame.Side
            || parent.TurnNumber != working.Turn.TurnNumber)
        {
            throw new ResolutionFailureException("The completed card play has no matching turn action frame.");
        }

        working.ResolutionStack[^1] = new TurnFrame(
            parent.Version,
            parent.BehaviorId,
            parent.ActiveSide,
            parent.TurnNumber,
            TurnFrameStage.ActionPriority,
            TurnWindow(parent.ActiveSide),
            parent.ActiveSide,
            forcedActionEnd: parent.ForcedActionEnd);
    }

    private static PlayCardPlan ValidateCardPlayFrame(WorkingState working, CardPlayFrame frame)
    {
        if (working.RuntimeCardCatalog is null)
        {
            throw new ResolutionFailureException("Card play requires an injected runtime card catalog.");
        }

        if (working.ResolutionStack.Count < 2
            || working.ResolutionStack[^1] is not CardPlayFrame
            || working.ResolutionStack[^2] is not TurnFrame parent
            || parent.Stage != TurnFrameStage.AwaitingAction
            || parent.ActiveSide != frame.Side
            || working.Turn.ActiveSide != frame.Side
            || working.Turn.TurnNumber != parent.TurnNumber)
        {
            throw new ResolutionFailureException("The card-play frame has no matching active turn action parent.");
        }

        if (frame.BehaviorId != ResolutionBehaviorIds.CardPlay
            || frame.Version != 1
            || frame.Side is not (PlayerSide.Corp or PlayerSide.Runner)
            || frame.CardClass is not (RuntimeCardClass.Operation or RuntimeCardClass.Event)
            || frame.CreditCost < 0
            || frame.SourceZoneKind != (frame.Side == PlayerSide.Corp ? ZoneKind.CorpHand : ZoneKind.RunnerGrip))
        {
            throw new ResolutionFailureException("The card-play frame contains unsupported frozen facts.");
        }

        PlayerState owner = GetPlayer(working, frame.Owner);
        if (owner.Side != frame.Side)
        {
            throw new ResolutionFailureException("The card-play owner no longer matches the frozen side.");
        }

        if (!TryGetCanonicalZone(working, frame.SourceZoneKind, frame.Side, out ZoneState? source)
            || !TryGetCanonicalZone(
                working,
                frame.Side == PlayerSide.Corp ? ZoneKind.CorpDiscard : ZoneKind.RunnerHeap,
                frame.Side,
                out ZoneState? discard)
            || !TryGetCanonicalPlayArea(
                working,
                frame.Side == PlayerSide.Corp ? ZoneKind.CorpPlayArea : ZoneKind.RunnerPlayArea,
                frame.Side,
                out ZoneState? playArea)
            || source is null
            || discard is null
            || playArea is null
            || source.Id != frame.SourceZoneId
            || discard.Id != frame.DiscardZoneId
            || playArea.Id != frame.PlayAreaZoneId
            || playArea.CardIds.Count != 1
            || playArea.CardIds[0] != frame.CardId)
        {
            throw new ResolutionFailureException("The card-play frame topology is stale or forged.");
        }

        CardInstanceState card = GetCard(working, frame.CardId);
        if (card.DefinitionId != frame.DefinitionId
            || card.OwnerSide != frame.Side
            || card.ZoneId != playArea.Id
            || !card.FaceUp
            || card.Rezzed)
        {
            throw new ResolutionFailureException("The played card no longer matches the frozen play-area facts.");
        }

        if (!working.RuntimeCardCatalog.TryGet(frame.DefinitionId, out CardRuntimeSpec? spec)
            || spec.DefinitionId != frame.DefinitionId
            || spec.Side != frame.Side
            || spec.CardClass != frame.CardClass
            || spec.PlayCost is not int printedCost
            || printedCost != frame.CreditCost)
        {
            throw new ResolutionFailureException("The card-play runtime metadata no longer matches the frozen frame.");
        }

        if (!working.BehaviorCatalog.TryGet(frame.DefinitionId, out CardBehaviorDefinition? definition)
            || definition.DefinitionId != frame.DefinitionId
            || definition.Controller != frame.Side
            || !string.Equals(frame.CatalogFingerprint, working.BehaviorCatalog.Fingerprint, StringComparison.Ordinal)
            || !string.Equals(
                frame.DefinitionFingerprint,
                working.BehaviorCatalog.GetDefinitionFingerprint(frame.DefinitionId),
                StringComparison.Ordinal))
        {
            throw new ResolutionFailureException("The card-play behavior provenance no longer matches the injected catalog.");
        }

        int behaviorCost = 0;
        try
        {
            foreach (BehaviorCost cost in definition.Costs)
            {
                if (cost.Kind != BehaviorCostKind.Credits || cost.Amount < 0)
                {
                    throw new ResolutionFailureException("The card-play behavior has an unsupported cost.");
                }

                behaviorCost = checked(behaviorCost + cost.Amount);
            }
        }
        catch (OverflowException exception)
        {
            throw new ResolutionFailureException("The card-play behavior cost overflowed.", exception);
        }

        if (behaviorCost != frame.CreditCost)
        {
            throw new ResolutionFailureException("The frozen card-play cost no longer matches behavior costs.");
        }

        return new PlayCardPlan(card, spec, definition, source, playArea, discard, behaviorCost);
    }

    private static bool PlayParametersMatch(PlayCardParameters actual, PlayCardParameters expected) =>
        actual.CardId == expected.CardId
        && actual.CreditCost == expected.CreditCost;
}

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

    private sealed record RunEventPlayPlan(
        CardInstanceState Card,
        CardRuntimeSpec Spec,
        RunEventBehaviorSpec RunEvent,
        ZoneState Source,
        ZoneState? PlayArea,
        ZoneState Discard,
        ServerState TargetServer,
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
        ZoneState handSource = source;
        // Deliberately retain the physical source-zone ordering.  This is the
        // authoritative order used by action issuance and later revalidation.
        // EL-HOSTED-FACEUP: Bling's hosted cards follow, as if in the grip.
        foreach ((CardInstanceId cardId, ZoneState cardSource) in PlaySources(working, side, handSource))
        {
            source = cardSource;
            CardInstanceState? card = working.Cards.FirstOrDefault(candidate => candidate.Id == cardId);
            if (card is null
                || source.CardIds.Count(candidate => candidate == cardId) != 1
                || card.OwnerSide != side
                || card.ZoneId != source.Id)
            {
                continue;
            }

            if (side == PlayerSide.Runner
                && TryGetRunEventTargets(working, card, source, discard, playArea, out IReadOnlyList<RunEventPlayPlan>? runEventPlans))
            {
                foreach (RunEventPlayPlan runEventPlan in runEventPlans!)
                {
                    materialized.Add(new PlayCardParameters(
                        runEventPlan.Card.Id,
                        runEventPlan.CreditCost,
                        runEventPlan.TargetServer.Id));
                }

                continue;
            }

            if (!TryBuildPlayCardPlan(working, side, card, source, discard, playArea, out PlayCardPlan? plan)
                || plan is null)
            {
                continue;
            }

            if (PlayTrashCost(plan.Definition) is { } trashCost)
            {
                foreach (CardInstanceId trashed in PlayTrashCostCandidates(working, trashCost))
                {
                    materialized.Add(new PlayCardParameters(plan.Card.Id, plan.CreditCost, trashCostCardId: trashed));
                }

                continue;
            }

            materialized.Add(new PlayCardParameters(plan.Card.Id, plan.CreditCost));
        }

        return materialized;
    }

    private static IEnumerable<(CardInstanceId CardId, ZoneState Source)> PlaySources(
        WorkingState working,
        PlayerSide side,
        ZoneState hand)
    {
        IEnumerable<(CardInstanceId, ZoneState)> sources = hand.CardIds.Select(cardId => (cardId, hand));
        if (side != PlayerSide.Runner)
        {
            return sources;
        }

        ZoneState? hosted = working.Zones.FirstOrDefault(zone => zone.Kind == ZoneKind.RunnerHostedFaceUp && zone.ServerId is null);
        return hosted is null
            ? sources
            : sources.Concat(GripLikeHostedCardIds(working).Select(cardId => (cardId, hosted))).ToArray();
    }

    /// <summary>The zone a turn play takes its card from: the grip or HQ, or a Bling-like host.</summary>
    private static ZoneKind TurnPlaySourceKind(WorkingState working, PlayerSide side, CardInstanceId cardId) =>
        side == PlayerSide.Corp
            ? ZoneKind.CorpHand
            : working.Cards.FirstOrDefault(card => card.Id == cardId) is { } card && IsGripLikeHosted(working, card)
                ? ZoneKind.RunnerHostedFaceUp
                : ZoneKind.RunnerGrip;

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
        // Run events use the run-event contract and are intentionally not
        // ordinary generic card plays. This remains true even when a run
        // event also carries a generic definition for a later success effect.
        if (working.BehaviorCatalog.TryGetRunEvent(card.DefinitionId, out _))
        {
            return false;
        }

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
                // A "Double" card's additional cost spends a click on top of
                // the click of the play action itself; a trash cost is paid by the play action.
                if (behaviorCost.Kind is BehaviorCostKind.Clicks or BehaviorCostKind.Tags || IsPlayTrashCost(behaviorCost))
                {
                    continue;
                }

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
            || GetPlayer(working, side).Credits < cost
            || GetPlayer(working, side).Clicks < 1 + AdditionalPlayClicks(working, card, definition)
            || !CanPayPlayTagCost(working, definition))
        {
            return false;
        }

        if (!AbilityConditionsMatch(working, card, GetPlayer(working, side).Id, definition.Conditions))
        {
            return false;
        }

        if (validateBehaviorTargets
            && !AreBehaviorPlayTargetsAvailable(working, definition))
        {
            return false;
        }

        if (definition.Costs.Count(IsPlayTrashCost) > 1
            || (PlayTrashCost(definition) is { } trashCost && PlayTrashCostCandidates(working, trashCost).Length == 0))
        {
            return false;
        }

        plan = new PlayCardPlan(card, spec, definition, source, playArea, discard, cost);
        return true;
    }

    private static bool TryGetRunEventTargets(
        WorkingState working,
        CardInstanceState card,
        ZoneState source,
        ZoneState discard,
        ZoneState? playArea,
        out IReadOnlyList<RunEventPlayPlan>? plans)
    {
        plans = null;
        if (working.RuntimeCardCatalog is null
            || working.Turn.ActiveSide != PlayerSide.Runner
            || working.RunState is not null
            || GetPlayer(working, PlayerSide.Runner).Clicks < 1
            || working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not TurnFrame
            || !working.BehaviorCatalog.TryGetRunEvent(card.DefinitionId, out RunEventBehaviorSpec? runEvent)
            || !working.RuntimeCardCatalog.TryGet(card.DefinitionId, out CardRuntimeSpec? spec)
            || card.OwnerSide != PlayerSide.Runner
            || card.ZoneId != source.Id
            || !source.CardIds.Contains(card.Id)
            || spec.DefinitionId != card.DefinitionId
            || spec.Side != PlayerSide.Runner
            || spec.CardClass != RuntimeCardClass.Event
            || spec.PlayCost is not int printedCost
            || printedCost < 0
            || runEvent.Cost != printedCost
            || GetPlayer(working, PlayerSide.Runner).Clicks < 1 + runEvent.AdditionalClicks
            || GetPlayer(working, PlayerSide.Runner).Credits < RunEventPlayCost(working, runEvent)
            || (playArea is not null && playArea.CardIds.Count != 0))
        {
            return false;
        }

        TurnFrame turn = (TurnFrame)working.ResolutionStack[^1];
        if (turn.Stage != TurnFrameStage.AwaitingAction
            || turn.ActiveSide != PlayerSide.Runner
            || turn.TurnNumber != working.Turn.TurnNumber)
        {
            return false;
        }

        IEnumerable<ServerState> targetServers = working.Servers.Where(server => server.Exists);
        if (runEvent.AllowedTargetServers is { } allowed)
        {
            targetServers = targetServers.Where(server => allowed.Contains(server.Kind));
        }

        if (runEvent.TargetMustBeProtected)
        {
            targetServers = targetServers.Where(server =>
                working.Zones.FirstOrDefault(zone => zone.Id == server.IceZoneId)?.CardIds.Count > 0);
        }

        plans = targetServers
            .Select(server => new RunEventPlayPlan(
                card,
                spec,
                runEvent,
                source,
                playArea,
                discard,
                server,
                RunEventPlayCost(working, runEvent)))
            .ToArray();
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

        if (parameters.TargetServerId is { } targetServerId)
        {
            ConsumeTurnRunEventAction(working, turnFrame, parameters, targetServerId);
            return;
        }

        ZoneKind sourceKind = TurnPlaySourceKind(working, side, parameters.CardId);
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
        if (parameters.TrashCostCardId is { } trashCostCardId)
        {
            PayPlayTrashCost(working, trashCostCardId);
        }

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
        RecordTurnEvent(working, side, side == PlayerSide.Corp ? TurnEventKind.OperationPlayed : TurnEventKind.EventPlayed);
        if (side == PlayerSide.Corp && CardSubtypes(working.RuntimeCardCatalog, sourceCard.DefinitionId).Contains("double", StringComparer.OrdinalIgnoreCase))
        {
            RecordOptInTurnEvent(working, TurnEventKind.DoubleOperationPlayed);
        }

        if (side == PlayerSide.Corp && ActiveTurnEventCount(working, TurnEventKind.OperationPlayed) == 1)
        {
            DeferOccurrence(working, OccurrenceKind.FirstOperationPlayedThisTurn, PlayerSide.Corp, plan.Card.Id);
        }

        if (side == PlayerSide.Runner && ActiveTurnEventCount(working, TurnEventKind.EventPlayed) == 1)
        {
            DeferOccurrence(working, OccurrenceKind.FirstEventPlayedThisTurn, PlayerSide.Runner, plan.Card.Id);
        }

        PushBehaviorChild(
            working,
            new BehaviorInvocationContext(plan.Card.Id, player.Id, player.Id),
            plan.Definition,
            playArea);
    }

    private static void ConsumeTurnRunEventAction(
        WorkingState working,
        TurnFrame turnFrame,
        PlayCardParameters parameters,
        ServerId targetServerId)
    {
        if (turnFrame.ActiveSide != PlayerSide.Runner
            || working.Turn.ActiveSide != PlayerSide.Runner
            || working.RunState is not null)
        {
            throw new ResolutionFailureException("A run event requires the active Runner turn and no live run.");
        }

        ZoneKind sourceKind = TurnPlaySourceKind(working, PlayerSide.Runner, parameters.CardId);
        ZoneKind discardKind = ZoneKind.RunnerHeap;
        if (!TryGetCanonicalZone(working, sourceKind, PlayerSide.Runner, out ZoneState? source)
            || !TryGetCanonicalZone(working, discardKind, PlayerSide.Runner, out ZoneState? discard)
            || !TryGetCanonicalPlayArea(working, ZoneKind.RunnerPlayArea, PlayerSide.Runner, out ZoneState? playArea)
            || source is null
            || discard is null)
        {
            throw new ResolutionFailureException("The canonical Runner run-event zones are unavailable.");
        }

        CardInstanceState card = working.Cards.SingleOrDefault(candidate => candidate.Id == parameters.CardId)
            ?? throw new ResolutionFailureException($"Card '{parameters.CardId}' does not exist.");
        if (!TryGetRunEventTargets(working, card, source, discard, playArea, out IReadOnlyList<RunEventPlayPlan>? plans)
            || plans is null)
        {
            throw new ResolutionFailureException("The run-event action is stale or unsupported.");
        }

        RunEventPlayPlan plan = plans.SingleOrDefault(candidate => candidate.TargetServer.Id == targetServerId)
            ?? throw new ResolutionFailureException(
                $"Run event '{parameters.CardId}' cannot attack server '{targetServerId}'.");
        if (plan.CreditCost != parameters.CreditCost)
        {
            throw new ResolutionFailureException("The run-event action cost is stale.");
        }

        PlayerState runner = GetPlayer(working, PlayerSide.Runner);
        SpendClick(working, runner.Id);
        for (int extra = 0; extra < plan.RunEvent.AdditionalClicks; extra++)
        {
            // Double: the additional cost spends a click on top of the play action's.
            SpendClick(working, runner.Id);
        }

        RecordTurnEvent(working, PlayerSide.Runner, TurnEventKind.EventPlayed);
        if (ActiveTurnEventCount(working, TurnEventKind.EventPlayed) == 1)
        {
            DeferOccurrence(working, OccurrenceKind.FirstEventPlayedThisTurn, PlayerSide.Runner, parameters.CardId);
        }

        StartRunInWorkingState(
            working,
            targetServerId,
            runner.Id,
            parameters.CardId);
    }

    private static void StepCardPlay(WorkingState working, CardPlayFrame frame)
    {
        // VP-CARD-AS-AGENDA: an operation that added itself to a score area as an agenda (Myōshu) stays there.
        bool becameAgenda = ScoreAreaAgendaEntry(working, frame.CardId) is not null
            && ZoneForCard(working, frame.CardId).Kind is ZoneKind.CorpScore or ZoneKind.RunnerScore;
        if (!becameAgenda)
        {
            _ = ValidateCardPlayFrame(working, frame);
        }

        CardInstanceState card = GetCard(working, frame.CardId);
        ZoneState playArea = working.Zones.Single(zone => zone.Id == frame.PlayAreaZoneId);
        ZoneState discard = working.Zones.Single(zone => zone.Id == frame.DiscardZoneId);

        if (!becameAgenda && frame.RemoveAfterResolution)
        {
            MoveCard(working, card, playArea, GetBehaviorZone(working, BehaviorZone.RemovedFromGame, card.OwnerSide));
        }
        else if (!becameAgenda)
        {
            TrashCard(working, card, playArea, discard);
        }

        Emit(working, EngineEventKinds.CardPlayCompleted, new CardPlayCompletedEventPayload(
            frame.Side,
            frame.CardId,
            frame.DefinitionId,
            frame.PlayAreaZoneId,
            frame.DiscardZoneId));
        working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
        if (frame.IsNested)
        {
            if (working.ResolutionStack[^1] is not BehaviorResolutionFrame nestedParent
                || !string.Equals(BehaviorFrameIdentity.Compute(nestedParent), frame.ParentFrameIdentity, StringComparison.Ordinal))
            {
                throw new ResolutionFailureException("The completed nested card play has no matching behavior parent.");
            }

            if (nestedParent.SourceCardId == frame.CardId)
            {
                // The behavior played its own source (Petty Cash from Archives):
                // that play was its last act, and its source has left its zone.
                working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
                return;
            }

            AdvanceBehaviorCursor(working, nestedParent);
            return;
        }

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

        if (working.ResolutionStack.Count < 2 || working.ResolutionStack[^1] is not CardPlayFrame)
        {
            throw new ResolutionFailureException("The card-play frame has no matching active turn action parent.");
        }

        if (frame.IsNested)
        {
            // EL-NESTED-PLAY: played from inside a behavior's resolution.
            if (working.ResolutionStack[^2] is not BehaviorResolutionFrame nestedParent
                || !string.Equals(BehaviorFrameIdentity.Compute(nestedParent), frame.ParentFrameIdentity, StringComparison.Ordinal))
            {
                throw new ResolutionFailureException("A nested card play has no matching behavior parent.");
            }
        }
        else if (working.ResolutionStack[^2] is not TurnFrame parent
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
            || !(frame.SourceZoneKind == (frame.Side == PlayerSide.Corp ? ZoneKind.CorpHand : ZoneKind.RunnerGrip)
                || frame.Side == PlayerSide.Runner && frame.SourceZoneKind == ZoneKind.RunnerHostedFaceUp
                || frame.IsNested && IsNestedPlaySourceKind(frame.Side, frame.SourceZoneKind)))
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
            || source.Id != frame.SourceZoneId && !frame.IsNested
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
                if (cost.Kind is BehaviorCostKind.Clicks or BehaviorCostKind.Tags || IsPlayTrashCost(cost))
                {
                    continue;
                }

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

    /// <summary>VP-TRASH-AS-COST: an additional cost to trash one card of a kind, paid by the play action.</summary>
    /// <summary>
    /// VP-TAG-COST: a play cost removing tags (Unleash: "As an additional cost to play this operation, remove 1 tag")
    /// removes the Runner's, whoever plays the card; the Runner must have them.
    /// </summary>
    private static bool CanPayPlayTagCost(WorkingState working, CardBehaviorDefinition definition)
    {
        int tags = definition.Costs.Where(static cost => cost.Kind == BehaviorCostKind.Tags).Sum(static cost => cost.Amount);
        return tags == 0
            || (working.RulesState?.Players.SingleOrDefault(value => value.PlayerId == GetPlayer(working, PlayerSide.Runner).Id)?.Tags ?? 0) >= tags;
    }

    private static bool IsPlayTrashCost(BehaviorCost cost) =>
        cost is { Kind: BehaviorCostKind.Additional, AdditionalKind: BehaviorAdditionalCostKind.TrashCards, Amount: 1 };

    private static BehaviorCost? PlayTrashCost(CardBehaviorDefinition definition) =>
        definition.Costs.FirstOrDefault(IsPlayTrashCost);

    /// <summary>The cards that may pay a play's trash cost, in their zone's order.</summary>
    private static CardInstanceId[] PlayTrashCostCandidates(WorkingState working, BehaviorCost cost)
    {
        if (cost.AdditionalTarget is not { Kind: BehaviorTargetKind.ZoneCards, Zone: { } zoneKind, Filter: { } filter }
            || filter.OwnerSide is not { } owner
            || !TryGetExistingBehaviorZone(working, zoneKind, owner, out ZoneState? zone)
            || zone is null
            || !BehaviorZoneMatches(zoneKind, zone.Kind, owner))
        {
            return [];
        }

        return zone.CardIds
            .Where(cardId => GetCard(working, cardId).OwnerSide == owner && BehaviorCardMatches(working, cardId, filter))
            .ToArray();
    }

    private static void PayPlayTrashCost(WorkingState working, CardInstanceId cardId)
    {
        CardInstanceState card = GetCard(working, cardId);
        ZoneState source = ZoneForCard(working, cardId);
        ZoneState destination = GetBehaviorZone(
            working,
            card.OwnerSide == PlayerSide.Corp ? BehaviorZone.Archives : BehaviorZone.Heap,
            card.OwnerSide);
        TrashCard(working, card, source, destination);
        CardInstanceState trashed = GetCard(working, cardId);
        ReplaceCard(working, new CardInstanceState(trashed.Id, trashed.DefinitionId, trashed.ZoneId, trashed.OwnerSide,
            faceUp: true, rezzed: false, trashed.AdvancementCounters));
    }

    private static bool PlayParametersMatch(PlayCardParameters actual, PlayCardParameters expected) =>
        actual.CardId == expected.CardId
        && actual.CreditCost == expected.CreditCost
        && actual.TargetServerId == expected.TargetServerId
        && actual.TrashCostCardId == expected.TrashCostCardId;

    /// <summary>
    /// Clicks a "Double" card's additional cost spends beyond the play action's click, less a discount for the first
    /// Double operation of the turn (VP-CLICK-COST-MODIFIER).
    /// </summary>
    private static int AdditionalPlayClicks(WorkingState working, CardInstanceState card, CardBehaviorDefinition definition)
    {
        int clicks = definition.Costs.Where(static cost => cost.Kind == BehaviorCostKind.Clicks).Sum(static cost => cost.Amount);
        return Math.Max(0, clicks - DoubleOperationClickDiscount(working, card, doubleOperationsAlreadyPlayed: 0));
    }

    /// <summary>
    /// VP-CLICK-COST-MODIFIER: the clicks active Corp cards take off a Double operation's cost when it is the first the
    /// Corp plays this turn: before the play none was played, and while it pays its costs it is the one played.
    /// </summary>
    private static int DoubleOperationClickDiscount(WorkingState working, CardInstanceState card, int doubleOperationsAlreadyPlayed)
    {
        if (card.OwnerSide != PlayerSide.Corp
            || !CardSubtypes(working.RuntimeCardCatalog, card.DefinitionId).Contains("double", StringComparer.OrdinalIgnoreCase)
            || !working.BehaviorCatalog.RecordsTurnEvent(TurnEventKind.DoubleOperationPlayed)
            || ActiveTurnEventCount(working, TurnEventKind.DoubleOperationPlayed) != doubleOperationsAlreadyPlayed)
        {
            return 0;
        }

        return working.Cards
            .Where(source => source.OwnerSide == PlayerSide.Corp && RuntimeCardActivity.IsActive(source, working.Zones))
            .Sum(source => working.BehaviorCatalog.TryGet(source.DefinitionId, out CardBehaviorDefinition? sourceDefinition)
                ? sourceDefinition.FirstDoubleOperationClickDiscount
                : 0);
    }

    /// <summary>The zones a card played from inside a behavior may come from: HQ, Archives, the grip or the heap.</summary>
    internal static bool IsNestedPlaySourceKind(PlayerSide side, ZoneKind kind) => side switch
    {
        PlayerSide.Corp => kind is ZoneKind.CorpHand or ZoneKind.CorpDiscard,
        PlayerSide.Runner => kind is ZoneKind.RunnerGrip or ZoneKind.RunnerHeap or ZoneKind.RunnerHostedFaceUp,
        _ => false,
    };

    /// <summary>
    /// EL-NESTED-PLAY: plays the targeted operation or event from inside this
    /// behavior (Humanoid Resources, Plutus, Petty Cash): no click for the
    /// play itself, but its costs, extra clicks and play restrictions apply.
    /// The parent resumes when the played card has resolved.
    /// </summary>
    private static void ResolveBehaviorPlay(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorInstruction instruction)
    {
        IReadOnlyList<CardInstanceId> chosen = instruction.Target is { Kind: BehaviorTargetKind.ChosenCard } chosenTarget
            && IsProvenEmptyChosenCard(frame, chosenTarget)
                ? []
                : ResolveBehaviorCards(working, frame, instruction.Target);
        if (chosen.Count == 0)
        {
            AdvanceBehaviorCursor(working, frame);
            return;
        }

        if (chosen.Count != 1)
        {
            throw new ResolutionFailureException("A behavior plays exactly one card.");
        }

        PlayerState player = GetPlayer(working, frame.Controller);
        PlayerSide side = player.Side;
        CardInstanceState card = GetCard(working, chosen[0]);
        ZoneState current = ZoneForCard(working, card.Id);
        BehaviorSetAsideCard? frozen = frame.SetAside.SingleOrDefault(selected => selected.CardId == card.Id);
        ZoneKind origin = frozen is not null
            ? working.Zones.Single(zone => zone.Id == frozen.OriginalZoneId).Kind
            : current.Kind;
        if (working.RuntimeCardCatalog?.TryGet(card.DefinitionId, out CardRuntimeSpec? spec) != true
            || card.OwnerSide != side
            || spec!.CardClass != (side == PlayerSide.Corp ? RuntimeCardClass.Operation : RuntimeCardClass.Event)
            || !IsNestedPlaySourceKind(side, origin)
            || !working.BehaviorCatalog.TryGet(card.DefinitionId, out CardBehaviorDefinition? definition)
            || definition.Controller != side)
        {
            throw new ResolutionFailureException("Only one of the controller's own operations or events can be played this way.");
        }

        ZoneKind playAreaKind = side == PlayerSide.Corp ? ZoneKind.CorpPlayArea : ZoneKind.RunnerPlayArea;
        ZoneKind discardKind = side == PlayerSide.Corp ? ZoneKind.CorpDiscard : ZoneKind.RunnerHeap;
        if (!TryGetCanonicalZone(working, discardKind, side, out ZoneState? discard)
            || discard is null
            || !TryGetCanonicalPlayArea(working, playAreaKind, side, out ZoneState? playArea))
        {
            throw new ResolutionFailureException("The canonical card-play zones are unavailable.");
        }

        int creditCost = definition.Costs.Where(static cost => cost.Kind == BehaviorCostKind.Credits).Sum(static cost => cost.Amount);
        int clickCost = AdditionalPlayClicks(working, card, definition);
        bool playable = (playArea is null || playArea.CardIds.Count == 0)
            && player.Credits >= creditCost
            && player.Clicks >= clickCost
            && AreBehaviorPlayTargetsAvailable(working, definition)
            && AbilityConditionsMatch(working, card, player.Id, definition.Conditions)
            // A play by an effect has no action to name the card a trash cost trashes (an engine limit, VP.2).
            && PlayTrashCost(definition) is null;
        if (!playable)
        {
            // A card that can't be played now (a restriction, its costs) stays where it is.
            AdvanceBehaviorCursor(working, frame);
            return;
        }

        playArea ??= new ZoneState(working.Allocator.NextZoneId(), playAreaKind, side);
        if (working.Zones.All(candidate => candidate.Id != playArea.Id))
        {
            working.Zones.Add(playArea);
        }

        // The card leaves the parent's selection: it is not reinserted when the parent completes.
        BehaviorSetAsideCard[] remaining = frame.SetAside.Where(selected => selected.CardId != card.Id).ToArray();
        frame = frame.With(setAside: remaining, clearSetAsideZone: remaining.Length == 0);
        ReplaceTopFrame(working, frame);
        working.ResolutionStack.Add(new CardPlayFrame(
            1,
            ResolutionBehaviorIds.CardPlay,
            player.Id,
            side,
            card.Id,
            card.DefinitionId,
            spec.CardClass,
            current.Id,
            origin,
            playArea.Id,
            discard.Id,
            creditCost,
            working.BehaviorCatalog.Fingerprint,
            working.BehaviorCatalog.GetDefinitionFingerprint(card.DefinitionId),
            BehaviorFrameIdentity.Compute(frame),
            removeAfterResolution: instruction.DestinationZone == BehaviorZone.RemovedFromGame));
        ReplaceCard(working, new CardInstanceState(card.Id, card.DefinitionId, card.ZoneId, card.OwnerSide,
            faceUp: true, rezzed: false, card.AdvancementCounters));
        MoveCard(working, GetCard(working, card.Id), current, playArea);
        if (current.Kind == ZoneKind.BehaviorSetAside
            && working.Zones.SingleOrDefault(zone => zone.Id == current.Id) is { CardIds.Count: 0 } emptied)
        {
            working.Zones.Remove(emptied);
        }

        Emit(working, EngineEventKinds.CardPlayStarted, new CardPlayStartedEventPayload(
            side, card.Id, card.DefinitionId, current.Id, playArea.Id, creditCost));
        RecordTurnEvent(working, side, side == PlayerSide.Corp ? TurnEventKind.OperationPlayed : TurnEventKind.EventPlayed);
        if (side == PlayerSide.Corp && CardSubtypes(working.RuntimeCardCatalog, card.DefinitionId).Contains("double", StringComparer.OrdinalIgnoreCase))
        {
            RecordOptInTurnEvent(working, TurnEventKind.DoubleOperationPlayed);
        }

        if (side == PlayerSide.Corp && ActiveTurnEventCount(working, TurnEventKind.OperationPlayed) == 1)
        {
            DeferOccurrence(working, OccurrenceKind.FirstOperationPlayedThisTurn, PlayerSide.Corp, card.Id);
        }

        if (side == PlayerSide.Runner && ActiveTurnEventCount(working, TurnEventKind.EventPlayed) == 1)
        {
            DeferOccurrence(working, OccurrenceKind.FirstEventPlayedThisTurn, PlayerSide.Runner, card.Id);
        }

        PushBehaviorChild(working, new BehaviorInvocationContext(card.Id, player.Id, player.Id), definition, playArea);
    }
}

using System.Collections.ObjectModel;

namespace Netrunner.Core;

/// <summary>
/// Materialization-only portion of the Slice 4.3 ability reducer.  The
/// action projection intentionally has no allocator side effects: reducers
/// and invariant validation can ask for the same candidate set and compare
/// the complete immutable payload.
/// </summary>
public static partial class GameEngine
{
    /// <summary>
    /// Re-materializes every legal behavior-owned ability payload for the
    /// current formal turn/run window. The returned order is the physical
    /// zone order, card order within that zone, and declaration order within
    /// the card's behavior definition.
    /// </summary>
    internal static IReadOnlyList<AbilityActionParameters> MaterializeAbilityActionParameters(
        GameState state,
        IBehaviorCatalog behaviorCatalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        return new ReadOnlyCollection<AbilityActionParameters>(
            MaterializeAbilityActionParameters(WorkingState.From(state, behaviorCatalog)).ToArray());
    }

    private static List<AbilityActionParameters> MaterializeAbilityActionParameters(WorkingState working)
    {
        if (!TryGetAbilityWindow(working, out AbilityWindow window))
        {
            return [];
        }

        var materialized = new List<AbilityActionParameters>();
        foreach ((CardInstanceState source, ZoneState sourceZone) in EnumerateAbilitySources(working))
        {
            if (source.OwnerSide != window.ControllerSide
                || sourceZone.OwnerSide != source.OwnerSide)
            {
                continue;
            }

            if (!working.BehaviorCatalog.TryGet(source.DefinitionId, out CardBehaviorDefinition? definition)
                || definition.Controller != source.OwnerSide)
            {
                continue;
            }

            string definitionFingerprint;
            try
            {
                definitionFingerprint = working.BehaviorCatalog.GetDefinitionFingerprint(definition.DefinitionId);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                // A catalog that cannot prove the definition fingerprint
                // cannot issue a replayable action.
                continue;
            }

            foreach (BehaviorPaidAbility ability in definition.PaidAbilities)
            {
                if (!AbilityWindowMatches(window, ability)
                    || ability.Targets.Count != 0
                    || ability.Costs.Any(static cost => cost.Kind == BehaviorCostKind.Additional)
                    || !AbilityConditionsMatch(working, source, window.ControllerId, ability.Conditions)
                    || !CanAffordAbility(working, source, window.ControllerId, ability))
                {
                    continue;
                }

                if (BehaviorRunRules.TryGetFinalStartRun(ability, out BehaviorRunOperand startRun)
                    && !CanStartRunAbility(working, source, definition, startRun, out _))
                {
                    continue;
                }

                TurnFactsState? facts = CurrentAbilityFacts(working, window.ControllerId);
                int uses = ability.UsageLimit == BehaviorAbilityUsageLimit.OncePerTurn
                    && facts?.HasUsedAbility(source.Id, ability.Key) == true
                    ? 1
                    : 0;
                if (ability.UsageLimit == BehaviorAbilityUsageLimit.OncePerTurn && uses != 0)
                {
                    continue;
                }

                materialized.Add(new AbilityActionParameters(
                    source.Id,
                    source.DefinitionId,
                    ability.Key,
                    ability.Timing,
                    ability.Activation,
                    ability.Costs,
                    [],
                    new AbilityUsageSnapshot(ability.UsageLimit, uses),
                    working.BehaviorCatalog.Fingerprint,
                    definitionFingerprint,
                    sourceZone.Id,
                    sourceZone.Kind));
            }
        }

        return materialized;
    }

    private static bool CanStartRunAbility(
        WorkingState working,
        CardInstanceState source,
        CardBehaviorDefinition definition,
        BehaviorRunOperand operand,
        out ServerState? server)
    {
        server = null;
        if (definition.Controller != PlayerSide.Runner
            || source.OwnerSide != PlayerSide.Runner
            || !source.FaceUp
            || working.RunState is not null
            || !EnumerateAbilitySources(working).Any(candidate =>
                candidate.Card.Id == source.Id
                && candidate.Zone.Kind == ZoneKind.RunnerRig))
        {
            return false;
        }

        ServerState[] matchingServers = working.Servers
            .Where(candidate => candidate.Kind == operand.ServerKind)
            .ToArray();
        if (matchingServers.Length != 1)
        {
            return false;
        }

        ServerState selectedServer = matchingServers[0];
        server = selectedServer;
        if (!working.Zones.Any(zone => zone.Id == selectedServer.IceZoneId))
        {
            server = null;
            return false;
        }

        if (operand.RequireNotRunThisTurn)
        {
            TurnFactsState? facts = CurrentAbilityFacts(working, GetPlayer(working, PlayerSide.Runner).Id);
            if (facts is null || facts.AttemptedRunServerIds.Contains(selectedServer.Id))
            {
                server = null;
                return false;
            }
        }

        return true;
    }

    private readonly record struct AbilityWindow(
        PlayerId ControllerId,
        PlayerSide ControllerSide,
        BehaviorAbilityActivation Activation,
        BehaviorAbilityTiming? RunTiming,
        bool IsTurnWindow);

    private static bool TryGetAbilityWindow(WorkingState working, out AbilityWindow window)
    {
        window = default;
        if (working.ResolutionStack.Count == 0)
        {
            return false;
        }

        ResolutionFrame top = working.ResolutionStack[^1];
        if (top is TurnFrame turn
            && working.RunState is null
            && working.Turn.ActiveSide == turn.ActiveSide
            && working.Turn.TurnNumber == turn.TurnNumber
            && working.Turn.Phase == (turn.ActiveSide == PlayerSide.Corp ? TurnPhase.Corp : TurnPhase.Runner))
        {
            if (turn.Stage == TurnFrameStage.AwaitingAction)
            {
                if (working.ResolutionStack.Count != 1
                    || GetPlayer(working, turn.ActiveSide).Clicks <= 0
                    || HasOccupiedTransientPlayArea(working))
                {
                    return false;
                }

                window = new AbilityWindow(
                    GetPlayer(working, turn.ActiveSide).Id,
                    turn.ActiveSide,
                    BehaviorAbilityActivation.ClickAction,
                    null,
                    IsTurnWindow: true);
                return PendingOwnerMatches(working, window.ControllerId);
            }

            if (turn.Stage is (TurnFrameStage.BeforeTurnPriority
                or TurnFrameStage.ActionPriority
                or TurnFrameStage.DiscardPriority)
                && turn.AllowsPaidAbilities
                && turn.PriorityHolder.HasValue
                && working.ResolutionStack.Count == 1
                && !HasOccupiedTransientPlayArea(working))
            {
                PlayerState holder = GetPlayer(working, turn.PriorityHolder.Value);
                window = new AbilityWindow(
                    holder.Id,
                    holder.Side,
                    BehaviorAbilityActivation.PaidAbility,
                    null,
                    IsTurnWindow: true);
                return PendingOwnerMatches(working, window.ControllerId);
            }

            return false;
        }

        if (working.RunState is { } run
            && run.Priority is { } priority
            && top is RunTimingFrame runFrame
            && runFrame.RunId == run.Id
            && runFrame.Step == run.Step
            && IsPaidWindow(run.Step))
        {
            PlayerState holder = GetPlayer(working, priority.PriorityHolder);
            window = new AbilityWindow(
                holder.Id,
                holder.Side,
                BehaviorAbilityActivation.PaidAbility,
                RunTimingFor(run.Step),
                IsTurnWindow: false);
            return PendingOwnerMatches(working, window.ControllerId);
        }

        return false;
    }

    private static bool PendingOwnerMatches(WorkingState working, PlayerId controllerId) =>
        working.PendingDecision is null || working.PendingDecision.Owner == controllerId;

    private static BehaviorAbilityTiming? RunTimingFor(RunTimingStep step) => step switch
    {
        RunTimingStep.InitiationPaidWindow => BehaviorAbilityTiming.InitiationPaidWindow,
        RunTimingStep.ApproachPaidWindow => BehaviorAbilityTiming.ApproachPaidWindow,
        RunTimingStep.EncounterPaidWindow => BehaviorAbilityTiming.EncounterPaidWindow,
        RunTimingStep.MovementPaidWindow => BehaviorAbilityTiming.MovementPaidWindow,
        RunTimingStep.AfterMovePaidWindow => BehaviorAbilityTiming.AfterMovePaidWindow,
        _ => null,
    };

    private static bool AbilityWindowMatches(AbilityWindow window, BehaviorPaidAbility ability)
    {
        if (ability.Activation != window.Activation)
        {
            return false;
        }

        if (window.Activation == BehaviorAbilityActivation.ClickAction)
        {
            return ability.Timing == BehaviorAbilityTiming.ActionPhase;
        }

        if (window.IsTurnWindow)
        {
            return ability.Timing == BehaviorAbilityTiming.AnyPaidWindow;
        }

        return ability.Timing == BehaviorAbilityTiming.AnyPaidWindow
            || ability.Timing == window.RunTiming;
    }

    private static IEnumerable<(CardInstanceState Card, ZoneState Zone)> EnumerateAbilitySources(WorkingState working)
    {
        foreach (ZoneState zone in working.Zones)
        {
            bool runnerRig = zone.Kind == ZoneKind.RunnerRig
                && zone.OwnerSide == PlayerSide.Runner
                && zone.ServerId is null
                && working.Zones.Count(candidate => candidate.Kind == ZoneKind.RunnerRig
                    && candidate.OwnerSide == PlayerSide.Runner
                    && candidate.ServerId is null) == 1;

            ServerState? server = zone.Kind switch
            {
                ZoneKind.ServerRoot or ZoneKind.ServerIce => working.Servers.SingleOrDefault(candidate =>
                    candidate.Id == zone.ServerId
                    && zone.OwnerSide == PlayerSide.Corp
                    && ((zone.Kind == ZoneKind.ServerRoot && candidate.RootZoneId == zone.Id)
                        || (zone.Kind == ZoneKind.ServerIce && candidate.IceZoneId == zone.Id))),
                _ => null,
            };

            bool corpServer = server is not null;
            if (!runnerRig && !corpServer)
            {
                continue;
            }

            foreach (CardInstanceId cardId in zone.CardIds)
            {
                CardInstanceState? card = working.Cards.FirstOrDefault(candidate => candidate.Id == cardId);
                if (card is null || card.ZoneId != zone.Id)
                {
                    continue;
                }

                if (runnerRig
                    && card.OwnerSide == PlayerSide.Runner
                    && card.FaceUp)
                {
                    yield return (card, zone);
                }
                else if (corpServer
                    && card.OwnerSide == PlayerSide.Corp
                    && card.FaceUp
                    && card.Rezzed)
                {
                    yield return (card, zone);
                }
            }
        }
    }

    private static TurnFactsState? CurrentAbilityFacts(WorkingState working, PlayerId controllerId)
    {
        PlayerSide side = GetPlayer(working, controllerId).Side;
        return working.Turn.History?.ThisTurn(side);
    }

    private static bool AbilityConditionsMatch(
        WorkingState working,
        CardInstanceState source,
        PlayerId controllerId,
        IReadOnlyList<BehaviorCondition> conditions)
    {
        foreach (BehaviorCondition condition in conditions)
        {
            if (!AbilityConditionMatches(working, source, controllerId, condition))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AbilityConditionMatches(
        WorkingState working,
        CardInstanceState source,
        PlayerId controllerId,
        BehaviorCondition condition)
    {
        // The legacy condition contract permits an omitted numeric value for
        // compatibility. It is not executable for an ability materializer.
        if (condition.Kind is (BehaviorConditionKind.CounterAtLeast or BehaviorConditionKind.FactEquals)
            && (!condition.Value.HasValue || condition.Value.Value < 0))
        {
            return false;
        }

        PlayerState controller = GetPlayer(working, controllerId);
        return condition.Kind switch
        {
            BehaviorConditionKind.Tagged => AbilityTagsForCondition(working, controllerId, condition) > 0,
            BehaviorConditionKind.NotTagged => AbilityTagsForCondition(working, controllerId, condition) <= 0,
            BehaviorConditionKind.ClicksRemainingAtLeast => controller.Clicks >= condition.Value.GetValueOrDefault(),
            BehaviorConditionKind.Installed => IsInstalledCard(working, source.Id),
            BehaviorConditionKind.InstalledThisTurn => CurrentAbilityFacts(working, controllerId)?.InstalledCardIds.Contains(source.Id) == true,
            BehaviorConditionKind.SuccessfulRun => working.RunState?.Successful == true,
            BehaviorConditionKind.SuccessfulRunThisTurn => CurrentAbilityFacts(working, controllerId)?.SuccessfulRun == true,
            BehaviorConditionKind.SourceController => source.OwnerSide == controller.Side,
            BehaviorConditionKind.Encountering => working.RunState is
            {
                Step: RunTimingStep.Encounter or RunTimingStep.EncounterPaidWindow,
                Encounter: { Status: RunEncounterStatus.Initialized },
            },
            BehaviorConditionKind.CounterAtLeast => CurrentCounter(working, source.Id, condition.Fact!) >= condition.Value.GetValueOrDefault(),
            BehaviorConditionKind.FactEquals => AbilityFactEquals(working, source, controllerId, condition),
            // A paid ability has no search continuation; a forged declaration
            // depending on one therefore fails closed.
            BehaviorConditionKind.FoundCard => false,
            _ => false,
        };
    }

    private static int AbilityTagsForCondition(
        WorkingState working,
        PlayerId controllerId,
        BehaviorCondition condition)
    {
        PlayerId subject = condition.SubjectSide is { } side
            ? GetPlayer(working, side).Id
            : controllerId;
        return working.RulesState?.Players
            .SingleOrDefault(value => value.PlayerId == subject)?.Tags ?? 0;
    }

    private static bool AbilityFactEquals(
        WorkingState working,
        CardInstanceState source,
        PlayerId controllerId,
        BehaviorCondition condition)
    {
        int? actual = condition.Fact switch
        {
            "credits" => GetPlayer(working, controllerId).Credits,
            "clicks" => GetPlayer(working, controllerId).Clicks,
            "tags" => AbilityTagsForCondition(working, controllerId, condition),
            "advancement" => source.AdvancementCounters,
            _ => null,
        };
        return actual.HasValue && actual.Value == condition.Value;
    }

    private static bool CanAffordAbility(
        WorkingState working,
        CardInstanceState source,
        PlayerId controllerId,
        BehaviorPaidAbility ability)
    {
        try
        {
            long credits = 0;
            long clicks = 0;
            long tags = 0;
            long hostedCredits = 0;
            var counters = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (BehaviorCost cost in ability.Costs)
            {
                switch (cost.Kind)
                {
                    case BehaviorCostKind.Credits:
                        credits = checked(credits + cost.Amount);
                        break;
                    case BehaviorCostKind.Clicks:
                    case BehaviorCostKind.LoseClick:
                        clicks = checked(clicks + cost.Amount);
                        break;
                    case BehaviorCostKind.Tags:
                        tags = checked(tags + cost.Amount);
                        break;
                    case BehaviorCostKind.HostedCredits:
                        hostedCredits = checked(hostedCredits + cost.Amount);
                        break;
                    case BehaviorCostKind.Counter:
                        string counterName = cost.CounterName!;
                        counters.TryGetValue(counterName, out long counterCost);
                        counters[counterName] = checked(counterCost + cost.Amount);
                        break;
                    case BehaviorCostKind.TrashSelf:
                        break;
                    case BehaviorCostKind.RemoveSelfFromGame:
                        break;
                    case BehaviorCostKind.Additional:
                        return false;
                    default:
                        return false;
                }
            }

            PlayerState controller = GetPlayer(working, controllerId);
            int availableTags = working.RulesState?.Players
                .SingleOrDefault(value => value.PlayerId == controllerId)?.Tags ?? 0;
            if (hostedCredits > 0)
            {
                CardCounterState? hostedPool = working.RulesState?.Counters.SingleOrDefault(counter =>
                    counter.CardId == source.Id
                    && counter.Purpose == CardCounterPurpose.HostedCredit);
                if (hostedPool is null
                    || !CardCounterRules.CanSpend(hostedPool, HostedCreditSpendRestriction.AbilityCost)
                    || hostedCredits > hostedPool.Count)
                {
                    return false;
                }
            }

            return credits <= controller.Credits
                && clicks <= controller.Clicks
                && tags <= availableTags
                && counters.All(pair => CurrentAbilityCounter(working, source.Id, pair.Key) >= pair.Value);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// Consumes an engine-issued UseAbility action. The payload is compared
    /// against the same pure materializer used for the pending decision, then
    /// converted into a serialized lifecycle cursor. No cost is paid here;
    /// payment is an explicit reducer step so a checkpoint can be persisted
    /// between authorization and execution.
    /// </summary>
    private static void ConsumeAbilityAction(
        WorkingState working,
        AbilityActionParameters submitted,
        PlayerId? decisionOwner)
    {
        if (decisionOwner is null)
        {
            throw new ResolutionFailureException("An ability action requires a decision owner.");
        }

        IReadOnlyList<AbilityActionParameters> materialized =
            MaterializeAbilityActionParameters(working);
        AbilityActionParameters? expected = materialized.FirstOrDefault(candidate =>
            AbilityParametersMatch(candidate, submitted));
        if (expected is null)
        {
            throw new ResolutionFailureException(
                "The submitted ability action is not an exact current materialization.");
        }

        CardInstanceState source = GetCard(working, submitted.SourceCardId);
        PlayerState controller = GetPlayer(working, decisionOwner.Value);
        if (controller.Side != source.OwnerSide)
        {
            throw new ResolutionFailureException(
                "An ability controller must be the pending decision owner and source-card owner.");
        }

        if (working.ResolutionStack.Count == 0)
        {
            throw new ResolutionFailureException("An ability action requires an active turn or run parent frame.");
        }

        ResolutionFrame parent = working.ResolutionStack[^1];
        ValidateAbilityParent(working, parent, submitted, controller.Id);
        string parentIdentity = BehaviorFrameIdentity.Compute(parent);

        working.ResolutionStack.Add(new AbilityFrame(
            1,
            ResolutionBehaviorIds.Ability,
            expected.DefinitionId,
            expected.SourceCardId,
            expected.AbilityKey,
            controller.Id,
            expected.Timing,
            expected.Activation,
            expected.Costs,
            expected.TargetCardIds,
            expected.Usage,
            expected.CatalogFingerprint,
            expected.DefinitionFingerprint,
            expected.SourceZoneId,
            expected.SourceZoneKind,
            AbilityLifecycleStage.PayCosts,
            parentIdentity));
    }

    /// <summary>Advances the serialized ability lifecycle one boundary.</summary>
    private static void StepAbility(WorkingState working, AbilityFrame frame)
    {
        ValidateAbilityFrameRuntime(working, frame);
        switch (frame.Stage)
        {
            case AbilityLifecycleStage.PayCosts:
                CardBehaviorDefinition definition = GetAbilityDefinition(working, frame);
                BehaviorPaidAbility declaration = GetAbilityDeclaration(definition, frame.AbilityKey);
                CardInstanceState source = GetCard(working, frame.SourceCardId);
                PreflightAbilityPayment(working, frame, declaration, source);
                PayAbilityCosts(working, frame);
                MarkAbilityUsage(working, frame);
                ReplaceTopFrame(working, frame.With(AbilityLifecycleStage.CostCheckpoint));
                return;
            case AbilityLifecycleStage.CostCheckpoint:
                ReplaceTopFrame(working, frame.With(AbilityLifecycleStage.Execute));
                return;
            case AbilityLifecycleStage.Execute:
                PushAbilityBehaviorChild(working, frame);
                return;
            case AbilityLifecycleStage.AwaitingChild:
                ReplaceTopFrame(working, frame.With(AbilityLifecycleStage.Complete));
                return;
            case AbilityLifecycleStage.Complete:
                CompleteAbility(working, frame);
                return;
            default:
                throw new ResolutionFailureException($"Ability stage '{frame.Stage}' is not executable.");
        }
    }

    private static CardBehaviorDefinition GetAbilityDefinition(WorkingState working, AbilityFrame frame)
    {
        if (!working.BehaviorCatalog.TryGet(frame.DefinitionId, out CardBehaviorDefinition? definition))
        {
            throw new ResolutionFailureException(
                $"Ability definition '{frame.DefinitionId}' is unavailable.");
        }

        return definition;
    }

    private static BehaviorPaidAbility GetAbilityDeclaration(
        CardBehaviorDefinition definition,
        string abilityKey) => definition.PaidAbilities
            .SingleOrDefault(candidate => string.Equals(candidate.Key, abilityKey, StringComparison.Ordinal))
            ?? throw new ResolutionFailureException(
                $"Ability key '{abilityKey}' is not declared by definition '{definition.DefinitionId}'.");

    private static void ValidateAbilityParent(
        WorkingState working,
        ResolutionFrame parent,
        AbilityActionParameters parameters,
        PlayerId controllerId)
    {
        PlayerState controller = GetPlayer(working, controllerId);
        switch (parent)
        {
            case TurnFrame turn:
                if (working.RunState is not null
                    || turn.ActiveSide != controller.Side
                    || working.Turn.ActiveSide != turn.ActiveSide
                    || working.Turn.TurnNumber != turn.TurnNumber)
                {
                    throw new ResolutionFailureException("Ability parent turn does not match the active turn.");
                }

                if (parameters.Activation == BehaviorAbilityActivation.ClickAction)
                {
                    if (turn.Stage != TurnFrameStage.AwaitingAction
                        || turn.PriorityHolder.HasValue
                        || turn.PriorityWindow.HasValue)
                    {
                        throw new ResolutionFailureException(
                            "A click ability requires the active turn's AwaitingAction frame.");
                    }

                    return;
                }

                if (turn.Stage is not (TurnFrameStage.BeforeTurnPriority
                    or TurnFrameStage.ActionPriority
                    or TurnFrameStage.DiscardPriority)
                    || !turn.AllowsPaidAbilities
                    || turn.PriorityHolder != controller.Side
                    || !turn.PriorityWindow.HasValue)
                {
                    throw new ResolutionFailureException(
                        "A paid ability requires the controller's active turn priority window.");
                }

                return;
            case RunTimingFrame runFrame:
                RunState run = working.RunState
                    ?? throw new ResolutionFailureException("A run ability requires live run state.");
                BehaviorAbilityTiming? runTiming = RunTimingFor(run.Step);
                if (parameters.Activation != BehaviorAbilityActivation.PaidAbility
                    || runFrame.RunId != run.Id
                    || runFrame.Step != run.Step
                    || !IsPaidWindow(run.Step)
                    || run.Priority is null
                    || GetPlayer(working, run.Priority.PriorityHolder).Id != controllerId
                    || runTiming is null
                    || (parameters.Timing != BehaviorAbilityTiming.AnyPaidWindow
                        && parameters.Timing != runTiming.Value))
                {
                    throw new ResolutionFailureException(
                        "A run ability parent does not match the active paid window.");
                }

                return;
            default:
                throw new ResolutionFailureException(
                    "Ability parent must be the exact top TurnFrame or RunTimingFrame.");
        }
    }

    private static void ValidateAbilityFrameRuntime(WorkingState working, AbilityFrame frame)
    {
        if (working.ResolutionStack.Count < 2
            || !ReferenceEquals(working.ResolutionStack[^1], frame))
        {
            throw new ResolutionFailureException("An ability frame must be the active top resolution frame.");
        }

        CardBehaviorDefinition definition = GetAbilityDefinition(working, frame);
        BehaviorPaidAbility declaration = GetAbilityDeclaration(definition, frame.AbilityKey);
        CardInstanceState source = GetCard(working, frame.SourceCardId);

        if (BehaviorRunRules.TryGetFinalStartRun(declaration, out BehaviorRunOperand startRun)
            && !CanStartRunAbility(working, source, definition, startRun, out _))
        {
            throw new ResolutionFailureException(
                "The StartRun ability is no longer legal for its source, server topology, or turn facts.");
        }

        ZoneState sourceZone = working.Zones.SingleOrDefault(zone => zone.Id == frame.SourceZoneId)
            ?? throw new ResolutionFailureException("Ability source zone no longer exists.");
        if (sourceZone.Kind != frame.SourceZoneKind)
        {
            throw new ResolutionFailureException("Ability source zone kind no longer matches the captured snapshot.");
        }

        if (source.DefinitionId != frame.DefinitionId
            || source.OwnerSide != GetPlayer(working, frame.Controller).Side)
        {
            throw new ResolutionFailureException("Ability source card no longer matches its captured definition or owner.");
        }

        if (!string.Equals(frame.CatalogFingerprint, working.BehaviorCatalog.Fingerprint, StringComparison.Ordinal)
            || !string.Equals(
                frame.DefinitionFingerprint,
                working.BehaviorCatalog.GetDefinitionFingerprint(frame.DefinitionId),
                StringComparison.Ordinal))
        {
            throw new ResolutionFailureException("Ability frame catalog provenance does not match the injected catalog.");
        }

        if (frame.Timing != declaration.Timing
            || frame.Activation != declaration.Activation
            || frame.Usage.Limit != declaration.UsageLimit
            || !frame.Costs.SequenceEqual(declaration.Costs)
            || frame.TargetCardIds.Count != 0
            || declaration.Targets.Count != 0
            || declaration.Costs.Any(static cost => cost.Kind == BehaviorCostKind.Additional))
        {
            throw new ResolutionFailureException("Ability frame no longer matches the declared executable shape.");
        }

        ResolutionFrame parent = working.ResolutionStack[^2];
        if (frame.ParentFrameIdentity is null
            || !string.Equals(
                frame.ParentFrameIdentity,
                BehaviorFrameIdentity.Compute(parent),
                StringComparison.Ordinal))
        {
            throw new ResolutionFailureException("Ability frame parent identity no longer matches the exact parent.");
        }

        var parentParameters = new AbilityActionParameters(
            frame.SourceCardId,
            frame.DefinitionId,
            frame.AbilityKey,
            frame.Timing,
            frame.Activation,
            frame.Costs,
            frame.TargetCardIds,
            frame.Usage,
            frame.CatalogFingerprint,
            frame.DefinitionFingerprint,
            frame.SourceZoneId,
            frame.SourceZoneKind);
        ValidateAbilityParent(working, parent, parentParameters, frame.Controller);

        bool paid = frame.Stage != AbilityLifecycleStage.PayCosts;
        if (!paid && source.ZoneId != frame.SourceZoneId)
        {
            throw new ResolutionFailureException("Ability source card moved before its costs were paid.");
        }

        bool selfTrash = declaration.Costs.Any(static cost => cost.Kind == BehaviorCostKind.TrashSelf);
        bool selfRemove = declaration.Costs.Any(static cost => cost.Kind == BehaviorCostKind.RemoveSelfFromGame);
        if (source.ZoneId != frame.SourceZoneId)
        {
            ZoneState currentSourceZone = working.Zones.SingleOrDefault(zone => zone.Id == source.ZoneId)
                ?? throw new ResolutionFailureException("Ability source card current zone no longer exists.");
            bool validLifecycleZone = (selfTrash && IsAbilityTrashZone(currentSourceZone, source))
                || (selfRemove && IsAbilityRemoveZone(currentSourceZone, source));
            if (!paid || !validLifecycleZone || (selfTrash && selfRemove))
            {
                throw new ResolutionFailureException("Ability source card moved outside its permitted lifecycle.");
            }
        }

        TurnFactsState? facts = CurrentAbilityFacts(working, frame.Controller);
        if (facts is null)
        {
            throw new ResolutionFailureException("Ability lifecycle requires current turn facts.");
        }

        bool used = facts.HasUsedAbility(frame.SourceCardId, frame.AbilityKey);
        if (frame.Usage.Limit == BehaviorAbilityUsageLimit.OncePerTurn)
        {
            // UsesThisTurn is the exact action-issued snapshot.  It remains
            // zero for the whole lifecycle; the turn-facts ledger records the
            // successful payment separately and is what changes after the
            // bundle is accepted.
            if ((!paid && used) || (paid && !used) || frame.Usage.UsesThisTurn != 0)
            {
                throw new ResolutionFailureException("Ability usage snapshot does not match the current turn facts.");
            }
        }
        else if (frame.Usage.UsesThisTurn != 0)
        {
            throw new ResolutionFailureException("Unlimited abilities must retain a zero usage count.");
        }
    }

    private static bool IsAbilityTrashZone(ZoneState zone, CardInstanceState source) =>
        source.OwnerSide == PlayerSide.Corp
            ? zone.Kind == ZoneKind.CorpDiscard
            : zone.Kind == ZoneKind.RunnerHeap;

    private static bool IsAbilityRemoveZone(ZoneState zone, CardInstanceState source) =>
        source.OwnerSide == PlayerSide.Corp
            ? zone.Kind == ZoneKind.CorpRemovedFromGame
            : zone.Kind == ZoneKind.RunnerRemovedFromGame;

    private static void PreflightAbilityPayment(
        WorkingState working,
        AbilityFrame frame,
        BehaviorPaidAbility declaration,
        CardInstanceState source)
    {
        if (frame.Stage != AbilityLifecycleStage.PayCosts
            || source.ZoneId != frame.SourceZoneId
            || !IsCurrentlyActiveAbilitySource(working, source, frame.SourceZoneId)
            || !CanAffordAbility(working, source, frame.Controller, declaration))
        {
            throw new ResolutionFailureException("Ability costs or source state are no longer affordable.");
        }

        if (declaration.Costs.Any(static cost => cost.Kind == BehaviorCostKind.Additional)
            || declaration.Targets.Count != 0
            || declaration.Costs.Count(static cost => cost.Kind is BehaviorCostKind.TrashSelf or BehaviorCostKind.RemoveSelfFromGame) > 1)
        {
            throw new ResolutionFailureException("This ability contains an unsupported cost or target.");
        }
    }

    private static bool IsCurrentlyActiveAbilitySource(
        WorkingState working,
        CardInstanceState source,
        ZoneId sourceZoneId) => EnumerateAbilitySources(working)
            .Any(candidate => candidate.Card.Id == source.Id && candidate.Zone.Id == sourceZoneId);

    private static void PayAbilityCosts(WorkingState working, AbilityFrame frame)
    {
        CardInstanceState source = GetCard(working, frame.SourceCardId);
        bool deferredCounterCleanup = false;
        foreach (BehaviorCost cost in frame.Costs)
        {
            switch (cost.Kind)
            {
                case BehaviorCostKind.Credits:
                    ApplyCreditDeltaIfNonZero(working, frame.Controller, checked(-cost.Amount));
                    break;
                case BehaviorCostKind.Clicks:
                    SpendClick(working, frame.Controller);
                    break;
                case BehaviorCostKind.LoseClick:
                    ApplyClickDelta(working, frame.Controller, checked(-cost.Amount));
                    break;
                case BehaviorCostKind.Tags:
                    ApplyTags(working, frame.Controller, checked(-cost.Amount));
                    break;
                case BehaviorCostKind.HostedCredits:
                    ChangeHostedCreditPool(
                        working,
                        frame.SourceCardId,
                        HostedCreditPoolOperation.Spend,
                        cost.Amount,
                        HostedCreditSpendRestriction.AbilityCost,
                        recipient: null);
                    break;
                case BehaviorCostKind.Counter:
                    if (!StableIdentifier.TryValidate(cost.CounterName))
                    {
                        throw new ResolutionFailureException("Ability counter cost has no stable counter name.");
                    }

                    AdjustCounter(
                        working,
                        frame.SourceCardId,
                        cost.CounterName!,
                        checked(-cost.Amount));
                    break;
                case BehaviorCostKind.TrashSelf:
                    MoveBehaviorCardToTrash(working, source);
                    source = GetCard(working, frame.SourceCardId);
                    deferredCounterCleanup = true;
                    break;
                case BehaviorCostKind.RemoveSelfFromGame:
                    MoveBehaviorCardToRemovedFromGame(working, source);
                    source = GetCard(working, frame.SourceCardId);
                    deferredCounterCleanup = true;
                    break;
                default:
                    throw new ResolutionFailureException($"Ability cost '{cost.Kind}' is not executable.");
            }
        }

        if (deferredCounterCleanup)
        {
            ClearCardCounters(working, frame.SourceCardId);
        }
    }

    private static void MarkAbilityUsage(WorkingState working, AbilityFrame frame)
    {
        if (frame.Usage.Limit != BehaviorAbilityUsageLimit.OncePerTurn)
        {
            return;
        }

        PlayerSide side = GetPlayer(working, frame.Controller).Side;
        TurnHistoryState history = working.Turn.History
            ?? throw new ResolutionFailureException("Ability usage requires current turn facts.");
        working.Turn = new TurnState(
            working.Turn.ActiveSide,
            working.Turn.Phase,
            working.Turn.TurnNumber,
            working.Turn.Step,
            history.MarkAbilityUsed(side, frame.SourceCardId, frame.AbilityKey));
    }

    private static void PushAbilityBehaviorChild(WorkingState working, AbilityFrame frame)
    {
        CardBehaviorDefinition definition = GetAbilityDefinition(working, frame);
        BehaviorPaidAbility declaration = GetAbilityDeclaration(definition, frame.AbilityKey);
        AbilityFrame awaitingChild = frame.With(AbilityLifecycleStage.AwaitingChild);
        string parentIdentity = BehaviorFrameIdentity.Compute(awaitingChild);
        BehaviorResolutionFrame child = new BehaviorResolutionFrame(
            definition,
            new BehaviorInvocationContext(frame.SourceCardId, frame.Controller, frame.Controller),
            working.BehaviorCatalog.Fingerprint,
            working.BehaviorCatalog.GetDefinitionFingerprint(frame.DefinitionId),
            working.Allocator.NextBehaviorFrameId(),
            frame.SourceZoneId,
            frame.SourceZoneKind,
            frame.BehaviorId,
            parentIdentity,
            abilityKey: frame.AbilityKey,
            rootInstructions: declaration.Instructions,
            instructions: declaration.Instructions);
        child = child.With(stage: BehaviorResolutionStage.Execute);

        ReplaceTopFrame(working, awaitingChild);
        working.ResolutionStack.Add(child);
    }

    private static void CompleteAbility(WorkingState working, AbilityFrame frame)
    {
        if (working.ResolutionStack.Count < 2)
        {
            throw new ResolutionFailureException("An ability completion requires its parent frame.");
        }

        ResolutionFrame parent = working.ResolutionStack[^2];
        working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
        switch (parent)
        {
            case TurnFrame turn when frame.Activation == BehaviorAbilityActivation.ClickAction:
                if (turn.Stage != TurnFrameStage.AwaitingAction)
                {
                    throw new ResolutionFailureException("A click ability parent changed before completion.");
                }

                working.ResolutionStack[^1] = new TurnFrame(
                    turn.Version,
                    turn.BehaviorId,
                    turn.ActiveSide,
                    turn.TurnNumber,
                    TurnFrameStage.ActionPriority,
                    TurnWindow(turn.ActiveSide),
                    turn.ActiveSide,
                    0,
                    turn.ForcedActionEnd);
                return;
            case TurnFrame turn when frame.Activation == BehaviorAbilityActivation.PaidAbility:
                if (turn.PriorityHolder is null || !turn.PriorityWindow.HasValue)
                {
                    throw new ResolutionFailureException("A paid turn ability parent lost its priority context.");
                }

                working.ResolutionStack[^1] = new TurnFrame(
                    turn.Version,
                    turn.BehaviorId,
                    turn.ActiveSide,
                    turn.TurnNumber,
                    turn.Stage,
                    turn.PriorityWindow,
                    turn.PriorityHolder,
                    0,
                    turn.ForcedActionEnd);
                return;
            case RunTimingFrame runFrame when frame.Activation == BehaviorAbilityActivation.PaidAbility:
                RunState run = working.RunState
                    ?? throw new ResolutionFailureException("A run ability parent lost its live run state.");
                if (run.Id != runFrame.RunId || run.Step != runFrame.Step || run.Priority is null)
                {
                    throw new ResolutionFailureException("A run ability parent changed before completion.");
                }

                RunPriorityState priority = run.Priority;
                working.RunState = run.WithPriority(new RunPriorityState(
                    priority.Version,
                    priority.Window,
                    priority.PriorityHolder,
                    0,
                    priority.CorpMayRezApproachedIce,
                    priority.CorpMayRezNonIce));
                working.ResolutionStack[^1] = new RunTimingFrame(
                    runFrame.Version,
                    runFrame.BehaviorId,
                    runFrame.RunId,
                    runFrame.Step);
                return;
            default:
                throw new ResolutionFailureException("An ability parent frame is not a matching paid window.");
        }
    }

    private static bool AbilityParametersMatch(
        AbilityActionParameters actual,
        AbilityActionParameters expected) =>
        actual.SourceCardId == expected.SourceCardId
        && actual.DefinitionId == expected.DefinitionId
        && string.Equals(actual.AbilityKey, expected.AbilityKey, StringComparison.Ordinal)
        && actual.Timing == expected.Timing
        && actual.Activation == expected.Activation
        && actual.Costs.SequenceEqual(expected.Costs)
        && actual.TargetCardIds.SequenceEqual(expected.TargetCardIds)
        && actual.Usage.Equals(expected.Usage)
        && string.Equals(actual.CatalogFingerprint, expected.CatalogFingerprint, StringComparison.Ordinal)
        && string.Equals(actual.DefinitionFingerprint, expected.DefinitionFingerprint, StringComparison.Ordinal)
        && actual.SourceZoneId == expected.SourceZoneId
        && actual.SourceZoneKind == expected.SourceZoneKind;

    private static bool AbilityFramesMatch(
        AbilityFrame actual,
        AbilityFrame expected) =>
        actual.Version == expected.Version
        && string.Equals(actual.BehaviorId, expected.BehaviorId, StringComparison.Ordinal)
        && actual.DefinitionId == expected.DefinitionId
        && actual.SourceCardId == expected.SourceCardId
        && string.Equals(actual.AbilityKey, expected.AbilityKey, StringComparison.Ordinal)
        && actual.Controller == expected.Controller
        && actual.Timing == expected.Timing
        && actual.Activation == expected.Activation
        && actual.Costs.SequenceEqual(expected.Costs)
        && actual.TargetCardIds.SequenceEqual(expected.TargetCardIds)
        && actual.Usage.Equals(expected.Usage)
        && string.Equals(actual.CatalogFingerprint, expected.CatalogFingerprint, StringComparison.Ordinal)
        && string.Equals(actual.DefinitionFingerprint, expected.DefinitionFingerprint, StringComparison.Ordinal)
        && actual.SourceZoneId == expected.SourceZoneId
        && actual.SourceZoneKind == expected.SourceZoneKind
        && actual.Stage == expected.Stage
        && string.Equals(actual.ParentFrameIdentity, expected.ParentFrameIdentity, StringComparison.Ordinal);

    private static void AddAbilityActions(WorkingState working, List<LegalAction> actions)
    {
        foreach (AbilityActionParameters parameters in MaterializeAbilityActionParameters(working))
        {
            actions.Add(new LegalAction(
                working.Allocator.NextActionId(),
                ActionKind.UseAbility,
                $"Use ability {parameters.SourceCardId.Value}/{parameters.AbilityKey}",
                parameters));
        }
    }
}

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
                    case BehaviorCostKind.Counter:
                        string counterName = cost.CounterName!;
                        counters.TryGetValue(counterName, out long counterCost);
                        counters[counterName] = checked(counterCost + cost.Amount);
                        break;
                    case BehaviorCostKind.TrashSelf:
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
            return credits <= controller.Credits
                && clicks <= controller.Clicks
                && tags <= availableTags
                && counters.All(pair => CurrentCounter(working, source.Id, pair.Key) >= pair.Value);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

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

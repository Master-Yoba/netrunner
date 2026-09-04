using System.Collections.ObjectModel;
using System.Globalization;
using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>
/// Stable version for the bounded full-game contract-agent policy. The policy
/// consumes only the typed legal-action surface; it never receives a
/// <see cref="GameState"/>, card catalog, RNG, or hidden-zone data.
/// </summary>
public static class FullGameContractAgentVersion
{
    public const int SchemaVersion = 2;

    public const string AgentVersion = "full-game-contract-agent-v2";

    public const string PolicyVersion = "full-game-contract-policy-v2";
}

/// <summary>
/// Action-class policy for <see cref="FullGameContractAgent"/> v2.
///
/// The action lists are typed <see cref="ActionKind"/> values, not prompt or
/// description text. The dispatcher is deliberately organized by
/// <see cref="DecisionKind"/> so future setup/install/run action classes can
/// be added as typed cases without teaching the agent to parse strings.
/// </summary>
public sealed class FullGameContractPolicy
{
    public const int CurrentSchemaVersion = FullGameContractAgentVersion.SchemaVersion;

    public const string Version = FullGameContractAgentVersion.PolicyVersion;

    private static readonly ActionKind[] DefaultTurnActionOrder =
    [
        ActionKind.UseAbility,
        ActionKind.GainCredit,
        ActionKind.DrawCard,
    ];

    private static readonly ActionKind[] DefaultRunActionOrder =
    [
        ActionKind.UseAbility,
        ActionKind.RezIce,
        ActionKind.BreakSubroutines,
        ActionKind.PumpBreaker,
        ActionKind.ChooseBreachCandidate,
        ActionKind.TrashAccessedCard,
        ActionKind.NoAccessAction,
        ActionKind.ContinueRun,
        ActionKind.JackOut,
        ActionKind.PassPriority,
    ];

    public FullGameContractPolicy(
        PlayerSide controlledSide,
        int schemaVersion = CurrentSchemaVersion,
        IEnumerable<ActionKind>? turnActionOrder = null,
        IEnumerable<ActionKind>? runActionOrder = null)
    {
        if (!Enum.IsDefined(controlledSide))
        {
            throw new ArgumentOutOfRangeException(nameof(controlledSide));
        }

        if (schemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion),
                schemaVersion,
                $"The full-game contract policy schema must be {CurrentSchemaVersion}.");
        }

        SchemaVersion = schemaVersion;
        ControlledSide = controlledSide;
        PolicyVersion = Version;
        TurnActionOrder = FreezeAndValidate(
            turnActionOrder ?? DefaultTurnActionOrder,
            AllowedTurnActions,
            nameof(turnActionOrder));
        RunActionOrder = FreezeAndValidate(
            runActionOrder ?? DefaultRunActionOrder,
            AllowedRunActions,
            nameof(runActionOrder));
    }

    public int SchemaVersion { get; }

    public string PolicyVersion { get; }

    public PlayerSide ControlledSide { get; }

    /// <summary>Typed priority for the currently materialized turn actions.</summary>
    public IReadOnlyList<ActionKind> TurnActionOrder { get; }

    /// <summary>Typed priority for the currently materialized run-window actions.</summary>
    public IReadOnlyList<ActionKind> RunActionOrder { get; }

    private static IReadOnlySet<ActionKind> AllowedTurnActions { get; } =
        new HashSet<ActionKind>
        {
            ActionKind.UseAbility,
            ActionKind.GainCredit,
            ActionKind.DrawCard,
        };

    private static IReadOnlySet<ActionKind> AllowedRunActions { get; } =
        new HashSet<ActionKind>
        {
            ActionKind.UseAbility,
            ActionKind.PassPriority,
            ActionKind.RezIce,
            ActionKind.BreakSubroutines,
            ActionKind.PumpBreaker,
            ActionKind.JackOut,
            ActionKind.ContinueRun,
            ActionKind.ChooseBreachCandidate,
            ActionKind.TrashAccessedCard,
            ActionKind.NoAccessAction,
        };

    public static FullGameContractPolicy Default(PlayerSide controlledSide) =>
        new(controlledSide);

    private static ReadOnlyCollection<ActionKind> FreezeAndValidate(
        IEnumerable<ActionKind> actions,
        IReadOnlySet<ActionKind> allowed,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ActionKind[] copy = actions.ToArray();
        if (copy.Length == 0)
        {
            throw new ArgumentException("An action priority list must not be empty.", parameterName);
        }

        var seen = new HashSet<ActionKind>();
        foreach (ActionKind action in copy)
        {
            if (!allowed.Contains(action))
            {
                throw new ArgumentException(
                    $"Action kind '{action}' is not supported by this v2 policy.",
                    parameterName);
            }

            if (!seen.Add(action))
            {
                throw new ArgumentException(
                    $"Action kind '{action}' appears more than once in the priority list.",
                    parameterName);
            }
        }

        return new ReadOnlyCollection<ActionKind>(copy);
    }
}

/// <summary>
/// Deterministic observation-only v2 agent for the Slice 4.2 shell.
///
/// It currently dispatches typed decision classes for turn timing, turn
/// actions (bounded abilities/gain/draw), forced discard, generic behavior
/// choices, and M2 run windows. It returns an existing engine-issued <see
/// cref="ActionId"/> only;
/// it never synthesizes a submission and never chooses <see
/// cref="ActionKind.EndTurn"/>.
/// </summary>
public sealed class FullGameContractAgent : IGameAgent
{
    public const int CurrentSchemaVersion = FullGameContractAgentVersion.SchemaVersion;

    public const string Version = FullGameContractAgentVersion.AgentVersion;

    private readonly FullGameContractPolicy _policy;

    public FullGameContractAgent(PlayerSide controlledSide)
        : this(FullGameContractPolicy.Default(controlledSide))
    {
    }

    public FullGameContractAgent(FullGameContractPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.SchemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentException(
                $"The full-game contract agent requires policy schema {CurrentSchemaVersion}.",
                nameof(policy));
        }

        _policy = policy;
        AgentVersion = Version;
    }

    public string AgentVersion { get; }

    public FullGameContractPolicy Policy => _policy;

    public ValueTask<ActionId> ChooseActionAsync(
        Observation observation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateObservationBoundary(observation);

        if (observation.LegalActions.Count == 0)
        {
            throw new InvalidOperationException("The full-game agent received a decision with no legal actions.");
        }

        DecisionKind decisionKind = observation.Decision!.Kind;
        LegalAction selected = decisionKind switch
        {
            DecisionKind.TurnTiming => SelectTurnTiming(observation.LegalActions),
            DecisionKind.TurnAction => SelectByPriority(
                observation.LegalActions,
                _policy.TurnActionOrder,
                FullGameActionClass.TurnAction),
            DecisionKind.TurnDiscard => SelectTurnDiscard(observation.LegalActions),
            DecisionKind.BehaviorChoice => SelectBehaviorChoice(observation.LegalActions),
            DecisionKind.RunTiming => SelectByPriority(
                observation.LegalActions,
                _policy.RunActionOrder,
                FullGameActionClass.RunWindow),
            _ => throw UnsupportedDecision(decisionKind),
        };

        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(selected.Id);
    }

    private static LegalAction SelectTurnTiming(IReadOnlyList<LegalAction> actions) =>
        SelectByPriority(
            actions,
            [ActionKind.UseAbility, ActionKind.PassPriority],
            FullGameActionClass.TurnTiming);

    private static LegalAction SelectTurnDiscard(IReadOnlyList<LegalAction> actions)
    {
        EnsureActionShapes(actions, FullGameActionClass.TurnDiscard);
        return SelectSemantic(
            actions.Where(static action =>
                action.Kind == ActionKind.ChooseCard
                && action.Parameters is CardChoiceParameters),
            FullGameActionClass.TurnDiscard);
    }

    private static LegalAction SelectBehaviorChoice(IReadOnlyList<LegalAction> actions)
    {
        EnsureActionShapes(actions, FullGameActionClass.BehaviorChoice);
        LegalAction[] choices = actions
            .Where(static action =>
                action.Kind == ActionKind.ChooseBehavior
                && action.Parameters is BehaviorChoiceParameters)
            .ToArray();

        // The v2 teaching policy prefers an economy mode when one is offered
        // (for example Predictive Planogram's typed `gain` option), then
        // falls back to the complete typed semantic key. This is deliberately
        // keyed by the option payload, never by the action description.
        return SelectSemantic(
            choices,
            FullGameActionClass.BehaviorChoice,
            BehaviorOrderKey);
    }

    private static int BehaviorPreference(LegalAction action) =>
        action.Parameters is BehaviorChoiceParameters behavior
            && behavior.OptionKeys.Any(static option =>
                string.Equals(option, "gain", StringComparison.Ordinal))
            ? 0
            : 1;

    private static string BehaviorOrderKey(LegalAction action) =>
        $"{BehaviorPreference(action)}:{SemanticKey(action)}";

    private static LegalAction SelectByPriority(
        IReadOnlyList<LegalAction> actions,
        IReadOnlyList<ActionKind> priorities,
        FullGameActionClass actionClass)
    {
        EnsureActionShapes(actions, actionClass);
        foreach (ActionKind priority in priorities)
        {
            LegalAction[] candidates = actions
                .Where(action =>
                    action.Kind == priority
                    && HasExpectedParameters(action, actionClass)
                    && IsProactivelySelectable(action))
                .ToArray();
            if (candidates.Length > 0)
            {
                return SelectSemantic(candidates, actionClass);
            }
        }

        throw new InvalidOperationException(
            $"The full-game agent found no supported {actionClass} action in the issued decision.");
    }

    private static void EnsureActionShapes(
        IReadOnlyList<LegalAction> actions,
        FullGameActionClass actionClass)
    {
        var actionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (LegalAction? action in actions)
        {
            if (action is null)
            {
                throw new InvalidOperationException(
                    $"The full-game agent received a null legal action in the {actionClass} decision.");
            }

            if (!HasStableId(action.Id.Value)
                || !actionIds.Add(action.Id.Value))
            {
                throw new InvalidOperationException(
                    "The full-game agent received a missing or duplicate engine-issued ActionId.");
            }

            // EndTurn is intentionally ignored rather than selected. The
            // Slice 4.2 turn reducer ends an action phase at zero clicks, and
            // a legacy/forged EndTurn offer must never become an agent move.
            if (action.Kind == ActionKind.EndTurn)
            {
                continue;
            }

            // Real engine decisions may contain action classes that this
            // teaching policy does not yet select (install, play, score,
            // and so on). They remain opaque observations and are ignored;
            // policy-supported candidates still receive strict validation.
            if (IsActionAllowed(action, actionClass)
                && !HasExpectedParameters(action, actionClass))
            {
                throw new InvalidOperationException(
                    $"The full-game agent received malformed parameters for '{action.Kind}' in the {actionClass} decision.");
            }
        }
    }

    private static bool IsActionAllowed(LegalAction action, FullGameActionClass actionClass) =>
        actionClass switch
        {
            FullGameActionClass.TurnTiming => action.Kind is ActionKind.UseAbility or ActionKind.PassPriority,
            FullGameActionClass.TurnAction => action.Kind is
                ActionKind.UseAbility
                or ActionKind.GainCredit
                or ActionKind.DrawCard,
            FullGameActionClass.TurnDiscard => action.Kind == ActionKind.ChooseCard,
            FullGameActionClass.BehaviorChoice => action.Kind == ActionKind.ChooseBehavior,
            FullGameActionClass.RunWindow => action.Kind is
                ActionKind.PassPriority
                or ActionKind.RezIce
                or ActionKind.BreakSubroutines
                or ActionKind.PumpBreaker
                or ActionKind.JackOut
                or ActionKind.ContinueRun
                or ActionKind.ChooseBreachCandidate
                or ActionKind.TrashAccessedCard
                or ActionKind.NoAccessAction
                or ActionKind.UseAbility,
            _ => false,
        };

    private static bool HasExpectedParameters(
        LegalAction action,
        FullGameActionClass actionClass)
    {
        return (action.Kind, action.Parameters) switch
        {
            (ActionKind.PassPriority, NoActionParameters)
                or (ActionKind.GainCredit, NoActionParameters)
                or (ActionKind.DrawCard, NoActionParameters)
                or (ActionKind.JackOut, NoActionParameters)
                or (ActionKind.ContinueRun, NoActionParameters)
                or (ActionKind.NoAccessAction, NoActionParameters) => true,
            (ActionKind.ChooseCard, CardChoiceParameters card) => HasStableId(card.CardId.Value),
            (ActionKind.ChooseBehavior, BehaviorChoiceParameters behavior) =>
                HasStableId(behavior.ChoiceKey)
                && behavior.OptionKeys is not null
                && behavior.CardIds is not null
                && behavior.OptionKeys.All(HasStableId)
                && behavior.CardIds.All(card => HasStableId(card.Value)),
            (ActionKind.RezIce, RunIceParameters ice) => HasStableId(ice.IceId.Value),
            (ActionKind.BreakSubroutines, RunBreakParameters runBreak) =>
                HasStableId(runBreak.BreakerId.Value)
                && runBreak.Quantity > 0
                && runBreak.HostedCredits >= 0,
            (ActionKind.PumpBreaker, RunBreakerParameters breaker) =>
                HasStableId(breaker.BreakerId.Value)
                && breaker.HostedCredits >= 0,
            (ActionKind.ChooseBreachCandidate, BreachCandidateParameters candidate) =>
                (candidate.CardId is { } card && HasStableId(card.Value) && candidate.Source is null)
                || (candidate.CardId is null
                    && candidate.Source is { } source
                    && Enum.IsDefined(source)),
            (ActionKind.TrashAccessedCard, RunCardPaymentParameters payment) =>
                HasStableId(payment.CardId.Value)
                && payment.HostedCredits >= 0,
            (ActionKind.UseAbility, AbilityActionParameters ability) =>
                IsValidAbilityAction(ability, actionClass),
            _ => false,
        };
    }

    private static bool IsProactivelySelectable(LegalAction action) =>
        action.Kind != ActionKind.UseAbility
        || action.Parameters is AbilityActionParameters ability
            && IsBoundedAbility(ability);

    private static bool IsBoundedAbility(AbilityActionParameters ability) =>
        ability.Costs.Any(static cost =>
            cost is not null
            && cost.Amount > 0
            && cost.Kind is BehaviorCostKind.Clicks or BehaviorCostKind.LoseClick)
        || ability.Usage is
        {
            Limit: BehaviorAbilityUsageLimit.OncePerTurn,
            UsesThisTurn: 0,
        };

    private static bool HasStableId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            return false;
        }

        return value.All(static character =>
            !char.IsControl(character) && !char.IsWhiteSpace(character));
    }

    private static bool IsValidAbilityAction(
        AbilityActionParameters? ability,
        FullGameActionClass actionClass)
    {
        if (ability is null
            || !HasStableId(ability.SourceCardId.Value)
            || !HasStableId(ability.DefinitionId.Value)
            || !HasStableId(ability.SourceZoneId.Value)
            || !HasStableId(ability.AbilityKey)
            || !Enum.IsDefined(ability.SourceZoneKind)
            || !Enum.IsDefined(ability.Timing)
            || !Enum.IsDefined(ability.Activation)
            || !DeterministicHash.IsSha256Hex(ability.CatalogFingerprint)
            || !DeterministicHash.IsSha256Hex(ability.DefinitionFingerprint)
            || !IsAbilityTimingCompatible(ability, actionClass)
            || ability.Costs is null
            || ability.TargetCardIds is null
            || ability.Usage is null)
        {
            return false;
        }

        if (!HasValidAbilityCosts(ability.Costs, ability.Activation))
        {
            return false;
        }

        var targetIds = new HashSet<CardInstanceId>();
        foreach (CardInstanceId targetId in ability.TargetCardIds)
        {
            if (!HasStableId(targetId.Value) || !targetIds.Add(targetId))
            {
                return false;
            }
        }

        if (!Enum.IsDefined(ability.Usage.Limit)
            || ability.Usage.UsesThisTurn < 0
            || (ability.Usage.Limit == BehaviorAbilityUsageLimit.OncePerTurn
                && ability.Usage.UsesThisTurn > 1))
        {
            return false;
        }

        return HasValidAdditionalCostSelections(ability);
    }

    private static bool IsAbilityTimingCompatible(
        AbilityActionParameters ability,
        FullGameActionClass actionClass) =>
        actionClass switch
        {
            // The engine materializes ClickAction abilities only in the
            // active turn's AwaitingAction window.
            FullGameActionClass.TurnAction =>
                ability.Activation == BehaviorAbilityActivation.ClickAction
                && ability.Timing == BehaviorAbilityTiming.ActionPhase,

            // Turn priority exposes the engine's generic paid window; no
            // run-specific timing can be issued from this parent frame.
            FullGameActionClass.TurnTiming =>
                ability.Activation == BehaviorAbilityActivation.PaidAbility
                && ability.Timing == BehaviorAbilityTiming.AnyPaidWindow,

            // RunTiming does not expose the concrete run step in the typed
            // observation. Accept exactly the paid timings currently emitted
            // by Core, including the priority-free breach-access window.
            FullGameActionClass.RunWindow =>
                ability.Activation == BehaviorAbilityActivation.PaidAbility
                && ability.Timing is
                    BehaviorAbilityTiming.AnyPaidWindow
                    or BehaviorAbilityTiming.InitiationPaidWindow
                    or BehaviorAbilityTiming.ApproachPaidWindow
                    or BehaviorAbilityTiming.EncounterPaidWindow
                    or BehaviorAbilityTiming.MovementPaidWindow
                    or BehaviorAbilityTiming.AfterMovePaidWindow
                    or BehaviorAbilityTiming.BreachAccessPaidWindow,
            _ => false,
        };

    private static bool HasValidAbilityCosts(
        IReadOnlyList<BehaviorCost> costs,
        BehaviorAbilityActivation activation)
    {
        int selfMovementCostCount = 0;
        foreach (BehaviorCost? cost in costs)
        {
            if (!IsValidAbilityCost(cost))
            {
                return false;
            }

            if (cost!.Kind is BehaviorCostKind.TrashSelf or BehaviorCostKind.RemoveSelfFromGame
                && ++selfMovementCostCount > 1)
            {
                return false;
            }
        }

        int clickCostCount = costs.Count(static cost =>
            cost is not null && cost.Kind == BehaviorCostKind.Clicks);
        bool hasLoseClickCost = costs.Any(static cost =>
            cost is not null && cost.Kind == BehaviorCostKind.LoseClick);

        return activation switch
        {
            BehaviorAbilityActivation.ClickAction =>
                clickCostCount == 1
                && costs.Single(static cost => cost.Kind == BehaviorCostKind.Clicks).Amount == 1
                && !hasLoseClickCost,
            BehaviorAbilityActivation.PaidAbility => clickCostCount == 0,
            _ => false,
        };
    }

    private static bool IsValidAbilityCost(BehaviorCost? cost)
    {
        if (cost is null || !Enum.IsDefined(cost.Kind))
        {
            return false;
        }

        bool validAmount = cost.Kind switch
        {
            BehaviorCostKind.Credits => cost.Amount >= 0,
            BehaviorCostKind.Clicks
                or BehaviorCostKind.LoseClick
                or BehaviorCostKind.Counter
                or BehaviorCostKind.Tags
                or BehaviorCostKind.HostedCredits
                or BehaviorCostKind.Additional => cost.Amount > 0,
            BehaviorCostKind.TrashSelf
                or BehaviorCostKind.RemoveSelfFromGame => cost.Amount == 1,
            _ => false,
        };
        if (!validAmount)
        {
            return false;
        }

        if (cost.Kind == BehaviorCostKind.Counter)
        {
            if (!HasStableId(cost.CounterName))
            {
                return false;
            }
        }
        else if (cost.CounterName is not null)
        {
            return false;
        }

        if (cost.Kind != BehaviorCostKind.Additional)
        {
            return cost.AdditionalKey is null
                && cost.AdditionalTarget is null
                && cost.AdditionalKind is null;
        }

        if (!HasStableId(cost.AdditionalKey))
        {
            return false;
        }

        return cost.AdditionalKind is { } additionalKind
            && Enum.IsDefined(additionalKind)
            && additionalKind == BehaviorAdditionalCostKind.TrashCards
            && IsValidStructuredAdditionalTarget(cost.AdditionalTarget);
    }

    private static bool IsValidStructuredAdditionalTarget(BehaviorTarget? target) =>
        target is not null
        && target.Kind == BehaviorTargetKind.ZoneCards
        && target.Selector is null
        && target.Zone is { } zone
        && Enum.IsDefined(zone)
        && !target.Reveal
        && IsValidCardFilter(target.Filter)
        && target.Filter!.OwnerSide is { } ownerSide
        && Enum.IsDefined(ownerSide);

    private static bool IsValidCardFilter(BehaviorCardFilter? filter) =>
        filter is not null
        && Enum.IsDefined(filter.CardType)
        && (filter.DefinitionId is null || HasStableId(filter.DefinitionId.Value.Value))
        && (filter.OwnerSide is null || Enum.IsDefined(filter.OwnerSide.Value))
        && (filter.Subtype is null || HasStableId(filter.Subtype));

    private static bool HasValidAdditionalCostSelections(AbilityActionParameters ability)
    {
        int additionalCostCount = ability.Costs.Count(static cost =>
            cost.Kind == BehaviorCostKind.Additional);
        IReadOnlyList<AbilityAdditionalCostSelection>? selections =
            ability.AdditionalCostSelections;

        // Core's constructor normalizes an empty optional list to null. A
        // null list therefore represents the valid no-additional-cost shape;
        // structured additional costs must carry one selection per cost.
        if (selections is null)
        {
            return additionalCostCount == 0;
        }

        if (selections.Count == 0 || selections.Count != additionalCostCount)
        {
            return false;
        }

        var seenIndexes = new HashSet<int>();
        var allSelectedCards = new HashSet<CardInstanceId>();
        foreach (AbilityAdditionalCostSelection? selection in selections)
        {
            if (selection is null
                || selection.CostIndex < 0
                || selection.CostIndex >= ability.Costs.Count
                || !seenIndexes.Add(selection.CostIndex)
                || ability.Costs[selection.CostIndex].Kind != BehaviorCostKind.Additional
                || !string.Equals(
                    selection.CostKey,
                    ability.Costs[selection.CostIndex].AdditionalKey,
                    StringComparison.Ordinal)
                || !HasStableId(selection.CostKey)
                || selection.CardIds is null
                || selection.CardIds.Count == 0)
            {
                return false;
            }

            var selectedCards = new HashSet<CardInstanceId>();
            if (selection.CardIds.Count != ability.Costs[selection.CostIndex].Amount)
            {
                return false;
            }

            foreach (CardInstanceId cardId in selection.CardIds)
            {
                if (!HasStableId(cardId.Value)
                    || !selectedCards.Add(cardId)
                    || !allSelectedCards.Add(cardId))
                {
                    return false;
                }
            }
        }

        return seenIndexes.Count == additionalCostCount;
    }

    private static LegalAction SelectSemantic(
        IEnumerable<LegalAction> source,
        FullGameActionClass actionClass,
        Func<LegalAction, string>? orderKey = null)
    {
        LegalAction[] candidates = source.ToArray();
        if (candidates.Length == 0)
        {
            throw new InvalidOperationException(
                $"The full-game agent found no supported action in the {actionClass} decision.");
        }

        // Equal semantic payloads are still deterministic: the engine-issued
        // stable ActionId is the final tie-breaker, never list position.
        Func<LegalAction, string> stableOrderKey = orderKey ?? SemanticKey;
        return candidates
            .OrderBy(stableOrderKey, StringComparer.Ordinal)
            .ThenBy(SemanticKey, StringComparer.Ordinal)
            .ThenBy(static action => action.Id.Value, StringComparer.Ordinal)
            .First();
    }

    private static string SemanticKey(LegalAction action) => action.Parameters switch
    {
        NoActionParameters => string.Empty,
        CardChoiceParameters card => $"card:{card.CardId.Value}",
        NumberChoiceParameters number =>
            $"number:{number.Value.ToString(CultureInfo.InvariantCulture)}",
        RunIceParameters ice => $"ice:{ice.IceId.Value}",
        RunBreakerParameters breaker =>
            $"breaker:{breaker.BreakerId.Value}:hosted:{breaker.HostedCredits.ToString(CultureInfo.InvariantCulture)}",
        RunBreakParameters runBreak =>
            $"break:{runBreak.BreakerId.Value}:quantity:{runBreak.Quantity.ToString(CultureInfo.InvariantCulture)}"
            + $":hosted:{runBreak.HostedCredits.ToString(CultureInfo.InvariantCulture)}",
        RunCardPaymentParameters payment =>
            $"payment:{payment.CardId.Value}:hosted:{payment.HostedCredits.ToString(CultureInfo.InvariantCulture)}",
        BreachCandidateParameters candidate when candidate.CardId is { } card => $"breach-card:{card.Value}",
        BreachCandidateParameters candidate when candidate.Source is { } source => $"breach-source:{source}",
        BehaviorChoiceParameters behavior => BehaviorKey(behavior),
        AbilityActionParameters ability => AbilityKey(ability),
        _ => throw new InvalidOperationException(
            $"The full-game agent cannot derive a semantic key for '{action.Kind}'."),
    };

    private static string AbilityKey(AbilityActionParameters ability)
    {
        // Costs, effect target IDs, and card IDs within a selection retain
        // their declaration/physical-zone order. Selections themselves are
        // keyed by CostIndex, so their transport order is not semantic and
        // is normalized here. CanonicalJson then freezes every immutable
        // field (including nested additional-target/filter data) without
        // consulting descriptions or mutable state.
        var selections = ability.AdditionalCostSelections?
            .OrderBy(static selection => selection.CostIndex)
            .Select(static selection => new
            {
                selection.CostIndex,
                selection.CostKey,
                CardIds = selection.CardIds,
            })
            .ToArray();

        return CanonicalJson.Serialize(new
        {
            Kind = ActionKind.UseAbility,
            ability.SourceCardId,
            ability.DefinitionId,
            ability.SourceZoneId,
            ability.SourceZoneKind,
            ability.AbilityKey,
            ability.Timing,
            ability.Activation,
            ability.Costs,
            ability.TargetCardIds,
            AdditionalCostSelections = selections,
            ability.Usage,
            ability.CatalogFingerprint,
            ability.DefinitionFingerprint,
        });
    }

    private static string BehaviorKey(BehaviorChoiceParameters behavior)
    {
        string options = string.Join(",", behavior.OptionKeys);
        string cards = string.Join(
            ",",
            behavior.CardIds
                .Select(static card => card.Value)
                .OrderBy(static value => value, StringComparer.Ordinal));
        return $"behavior:{behavior.ChoiceKey}:options:{options}:cards:{cards}:final:{behavior.FinalizeSelection}";
    }

    private void ValidateObservationBoundary(Observation observation)
    {
        if (observation.LegalActions is null)
        {
            throw new InvalidOperationException("The observation did not contain a legal-action list.");
        }

        if (!Enum.IsDefined(observation.Viewer))
        {
            throw new InvalidOperationException($"The observation has an unsupported viewer '{observation.Viewer}'.");
        }

        if (observation.Viewer != _policy.ControlledSide)
        {
            throw new InvalidOperationException(
                $"The observation viewer '{observation.Viewer}' does not match the agent side '{_policy.ControlledSide}'.");
        }

        ObservationDecision? decision = observation.Decision;
        if (decision is null)
        {
            throw new InvalidOperationException(
                "The full-game agent requires viewer-owned typed decision metadata.");
        }

        if (!HasStableId(decision.Id.Value)
            || !HasStableId(observation.DecisionId)
            || !HasStableId(decision.Owner.Value)
            || decision.Revision <= 0
            || !Enum.IsDefined(decision.OwnerSide))
        {
            throw new InvalidOperationException("The observation contains malformed decision metadata.");
        }

        if (decision.OwnerSide != observation.Viewer)
        {
            throw new InvalidOperationException(
                $"The decision owner side '{decision.OwnerSide}' does not match observation viewer '{observation.Viewer}'.");
        }

        if (!string.Equals(decision.Id.Value, observation.DecisionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The typed decision ID does not match the observation decision ID.");
        }

        if (!Enum.IsDefined(decision.Kind))
        {
            throw new InvalidOperationException($"The observation has an unsupported decision kind '{decision.Kind}'.");
        }
    }

    private static InvalidOperationException UnsupportedDecision(DecisionKind kind) =>
        new($"The full-game agent does not support decision kind '{kind}' in v2.");

    private enum FullGameActionClass
    {
        TurnTiming,
        TurnAction,
        TurnDiscard,
        BehaviorChoice,
        RunWindow,
    }
}

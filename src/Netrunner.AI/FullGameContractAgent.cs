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
    public const int SchemaVersion = 4;

    public const string AgentVersion = "full-game-contract-agent-v4";

    public const string PolicyVersion = "full-game-contract-policy-v4";
}

/// <summary>
/// Action-class policy for <see cref="FullGameContractAgent"/> v4.
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
        ActionKind.InstallCard,
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
            ActionKind.InstallCard,
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
                    $"Action kind '{action}' is not supported by this v4 policy.",
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
/// Deterministic observation-only v4 agent for the Slice 4.5 shell.
///
/// It currently dispatches typed decision classes for turn timing, turn
/// actions (bounded abilities/install/gain/draw), forced discard, generic
/// behavior choices, engine-owned trigger choices, and M2 run windows. It
/// returns an existing engine-issued
/// <see cref="ActionId"/> only;
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
            DecisionKind.TurnAction => SelectTurnAction(observation.LegalActions),
            DecisionKind.TurnDiscard => SelectTurnDiscard(observation.LegalActions),
            DecisionKind.BehaviorChoice => SelectBehaviorChoice(observation.LegalActions),
            DecisionKind.InstallChoice => SelectInstallChoice(observation.LegalActions),
            DecisionKind.InterruptChoice => SelectInterruptChoice(observation.LegalActions),
            DecisionKind.TriggerChoice => SelectTriggerChoice(observation.LegalActions),
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

        // The v4 teaching policy prefers an economy mode when one is offered
        // (for example Predictive Planogram's typed `gain` option). Structured
        // option/card payloads then outrank an optional skip, and the issued
        // action ID preserves Core's declaration/materialization order for
        // alternatives such as Manegarm and Funhouse. Target selections use
        // the complete typed semantic key. None of these choices inspect an
        // action description or observation state.
        return SelectSemantic(
            choices,
            FullGameActionClass.BehaviorChoice,
            BehaviorOrderKey);
    }

    private static LegalAction SelectInstallChoice(IReadOnlyList<LegalAction> actions)
    {
        EnsureActionShapes(actions, FullGameActionClass.InstallChoice);
        LegalAction[] choices = actions
            .Where(static action =>
                action.Kind == ActionKind.InstallCard
                && action.Parameters is InstallCardParameters)
            .ToArray();

        LegalAction[] finalChoices = choices
            .Where(static action => ((InstallCardParameters)action.Parameters).FinalizeSelection)
            .ToArray();
        if (finalChoices.Length > 0)
        {
            // Once Core issues a valid final action, completing the frozen
            // selection is always preferred to taking another continuation.
            return SelectSemantic(finalChoices, FullGameActionClass.InstallChoice);
        }

        return SelectSemantic(
            choices.Where(static action => !((InstallCardParameters)action.Parameters).FinalizeSelection),
            FullGameActionClass.InstallChoice);
    }

    private static LegalAction SelectInterruptChoice(IReadOnlyList<LegalAction> actions)
    {
        EnsureActionShapes(actions, FullGameActionClass.InterruptChoice);
        LegalAction[] replacements = actions
            .Where(static action =>
                action.Kind is ActionKind.UseReplacement or ActionKind.DeclineReplacement
                && action.Parameters is ReplacementChoiceParameters)
            .ToArray();

        LegalAction[] use = replacements
            .Where(static action => action.Kind == ActionKind.UseReplacement)
            .ToArray();
        if (use.Length > 0)
        {
            // Replacement use is the deterministic v4 preference. The typed
            // interrupt ID and then the issued ActionId break any tie.
            return SelectSemantic(use, FullGameActionClass.InterruptChoice);
        }

        return SelectSemantic(replacements, FullGameActionClass.InterruptChoice);
    }

    private static LegalAction SelectTriggerChoice(IReadOnlyList<LegalAction> actions)
    {
        EnsureActionShapes(actions, FullGameActionClass.TriggerChoice);
        LegalAction[] triggers = actions
            .Where(static action =>
                action.Kind is ActionKind.UseTrigger or ActionKind.DeclineTrigger
                && action.Parameters is TriggerChoiceParameters)
            .ToArray();

        // Trigger decisions are emitted by Core's checkpoint machinery. Use
        // is the progress-making choice whenever it is legal; decline is a
        // fail-closed fallback only when no use action was materialized. The
        // trigger ID and issued ActionId make ties independent of list order.
        LegalAction[] use = triggers
            .Where(static action => action.Kind == ActionKind.UseTrigger)
            .ToArray();
        if (use.Length > 0)
        {
            return SelectSemantic(use, FullGameActionClass.TriggerChoice, TriggerKey);
        }

        return SelectSemantic(
            triggers.Where(static action => action.Kind == ActionKind.DeclineTrigger),
            FullGameActionClass.TriggerChoice,
            TriggerKey);
    }

    private static int BehaviorPreference(LegalAction action) =>
        action.Parameters is BehaviorChoiceParameters behavior
            && behavior.OptionKeys.Any(static option =>
                string.Equals(option, "gain", StringComparison.Ordinal))
            ? 0
            : 1;

    private static string BehaviorOrderKey(LegalAction action) =>
        action.Parameters is BehaviorChoiceParameters behavior
            ? $"{BehaviorChoiceProgressRank(behavior)}:{BehaviorPreference(action)}:"
                + (behavior.OptionKeys.Count > 0
                    // Core materializes option alternatives in declaration
                    // order. The issued ID is the only observation-safe
                    // representation of that order; labels and descriptions
                    // are intentionally not consulted.
                    ? $"action:{action.Id.Value}"
                    : $"semantic:{SemanticKey(action)}")
            : throw new InvalidOperationException(
                "The full-game agent cannot order a malformed behavior choice.");

    private static int BehaviorChoiceProgressRank(BehaviorChoiceParameters behavior) =>
        behavior.OptionKeys.Count > 0 && behavior.CardIds.Count > 0
            ? 0 // Structured option plus its frozen private card-cost payload.
            : behavior.OptionKeys.Count > 0
                ? 1 // A materialized option, including a normal/fallback mode.
                : behavior.CardIds.Count > 0
                    ? 2 // A target/search selection.
                    : 3; // Explicit optional skip.

    private static string TriggerKey(LegalAction action) =>
        action.Parameters is TriggerChoiceParameters trigger
            ? $"trigger:{trigger.TriggerId.Value}"
            : throw new InvalidOperationException(
                "The full-game agent cannot order a malformed trigger choice.");

    private LegalAction SelectTurnAction(IReadOnlyList<LegalAction> actions)
    {
        EnsureActionShapes(actions, FullGameActionClass.TurnAction);

        LegalAction[] candidates = actions
            .Where(action =>
                _policy.TurnActionOrder.Contains(action.Kind)
                && IsActionAllowed(action, FullGameActionClass.TurnAction)
                && HasExpectedParameters(action, FullGameActionClass.TurnAction)
                && IsProactivelySelectable(action))
            .ToArray();
        if (candidates.Length == 0)
        {
            throw new InvalidOperationException(
                $"The full-game agent found no supported {FullGameActionClass.TurnAction} action in the issued decision.");
        }

        // The category is derived from the immutable typed policy order. The
        // install category then receives a small detached utility adjustment:
        // a lower current effective cost is better, and a larger current
        // savings term is better. This consumes only the quote on this fresh
        // observation; there is deliberately no cross-observation cache from
        // which a first-X bonus could persist after Core stops issuing it.
        return candidates
            .OrderByDescending(action => TurnActionUtilityScore(action, _policy.TurnActionOrder))
            .ThenBy(SemanticKey, StringComparer.Ordinal)
            .ThenBy(static action => action.Id.Value, StringComparer.Ordinal)
            .First();
    }

    private static int TurnActionUtilityScore(
        LegalAction action,
        IReadOnlyList<ActionKind> actionOrder)
    {
        int priorityIndex = -1;
        for (int index = 0; index < actionOrder.Count; index++)
        {
            if (actionOrder[index] == action.Kind)
            {
                priorityIndex = index;
                break;
            }
        }

        if (priorityIndex < 0)
        {
            return int.MinValue;
        }

        // Keep category ordering explicit and bounded. The maximum configured
        // list contains only the four supported turn action kinds, so this
        // leaves ample room for a detached install-cost adjustment without
        // making normal category ordering depend on card text or state.
        long score = (actionOrder.Count - priorityIndex) * 100_000L;
        if (action.Parameters is InstallCardParameters install)
        {
            CostProjection projection = InstallCostProjection(install);
            score -= projection.EffectiveCost;
            score += projection.Savings;
        }

        return score switch
        {
            > int.MaxValue => int.MaxValue,
            < int.MinValue => int.MinValue,
            _ => (int)score,
        };
    }

    private static CostProjection InstallCostProjection(InstallCardParameters install)
    {
        CostQuote? quote = install.CostQuote;
        return quote is null
            ? new CostProjection(install.CreditCost, 0)
            : new CostProjection(quote.EffectiveCost, quote.BaseCost - quote.EffectiveCost);
    }

    private readonly record struct CostProjection(int EffectiveCost, int Savings);

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
            // Slice 4.5 turn reducer ends an action phase at zero clicks, and
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
                or ActionKind.InstallCard
                or ActionKind.GainCredit
                or ActionKind.DrawCard,
            FullGameActionClass.TurnDiscard => action.Kind == ActionKind.ChooseCard,
            FullGameActionClass.BehaviorChoice => action.Kind == ActionKind.ChooseBehavior,
            FullGameActionClass.InstallChoice => action.Kind == ActionKind.InstallCard,
            FullGameActionClass.InterruptChoice => action.Kind is
                ActionKind.UseReplacement
                or ActionKind.DeclineReplacement,
            FullGameActionClass.TriggerChoice => action.Kind is
                ActionKind.UseTrigger
                or ActionKind.DeclineTrigger,
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
                HasValidBehaviorChoiceParameters(behavior),
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
            (ActionKind.InstallCard, InstallCardParameters install) =>
                IsValidInstallAction(install, actionClass),
            (ActionKind.UseReplacement or ActionKind.DeclineReplacement, ReplacementChoiceParameters replacement) =>
                HasStableId(replacement.InterruptId.Value),
            (ActionKind.UseTrigger or ActionKind.DeclineTrigger, TriggerChoiceParameters trigger) =>
                HasStableId(trigger.TriggerId.Value),
            _ => false,
        };
    }

    private static bool HasValidBehaviorChoiceParameters(BehaviorChoiceParameters? behavior)
    {
        if (behavior is null
            || !HasStableId(behavior.ChoiceKey)
            || behavior.OptionKeys is null
            || behavior.CardIds is null
            || behavior.OptionKeys.Count > 1
            || behavior.OptionKeys.Any(option => !HasStableId(option))
            || behavior.CardIds.Any(card => !HasStableId(card.Value))
            || behavior.OptionKeys.Distinct(StringComparer.Ordinal).Count() != behavior.OptionKeys.Count
            || behavior.CardIds.Distinct().Count() != behavior.CardIds.Count)
        {
            return false;
        }

        // The Core choice reducer accepts one of three complete typed
        // shapes: a materialized option, a card selection, or an explicit
        // optional skip. An option plus card IDs is the structured private
        // card-cost form used by Anoetic Void and remains a single issued
        // action; it is never synthesized by the agent.
        if (!behavior.FinalizeSelection)
        {
            return behavior.OptionKeys.Count == 0
                && behavior.CardIds.Count == 1;
        }

        if (behavior.OptionKeys.Count > 0 && behavior.CardIds.Count > 0)
        {
            return behavior.OptionKeys.Count == 1;
        }

        return true;
    }

    private static bool IsValidInstallAction(
        InstallCardParameters? install,
        FullGameActionClass actionClass)
    {
        if (install is null
            || actionClass is not (FullGameActionClass.TurnAction or FullGameActionClass.InstallChoice)
            || !HasStableId(install.CardId.Value)
            || !Enum.IsDefined(install.DestinationKind)
            || install.CreditCost < 0
            || !HasValidInstallDestination(install)
            || !HasStableCardIds(install.CandidateReplacementCardIds)
            || !HasStableCardIds(install.RequiredReplacementCardIds)
            || !HasStableCardIds(install.SelectedReplacementCardIds)
            || !IsSubset(
                install.RequiredReplacementCardIds,
                install.CandidateReplacementCardIds)
            || !IsSubset(
                install.SelectedReplacementCardIds,
                install.CandidateReplacementCardIds)
            || !HasValidInstallSelectionShape(install, actionClass)
            || !IsValidInstallCostQuote(install.CostQuote, install.CreditCost))
        {
            return false;
        }

        return true;
    }

    private static bool HasValidInstallDestination(InstallCardParameters install)
    {
        if (install.ServerId is { } serverId && !HasStableId(serverId.Value))
        {
            return false;
        }

        return install.DestinationKind switch
        {
            InstallDestinationKind.RunnerRig =>
                install.ServerId is null && !install.CreateNewRemote,
            InstallDestinationKind.ServerRoot
                or InstallDestinationKind.ServerIce =>
                install.ServerId.HasValue != install.CreateNewRemote,
            _ => false,
        };
    }

    private static bool HasStableCardIds(IReadOnlyList<CardInstanceId>? cardIds)
    {
        if (cardIds is null)
        {
            return false;
        }

        var seen = new HashSet<CardInstanceId>();
        foreach (CardInstanceId cardId in cardIds)
        {
            if (!HasStableId(cardId.Value) || !seen.Add(cardId))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSubset(
        IReadOnlyList<CardInstanceId> subset,
        IReadOnlyList<CardInstanceId> superset) =>
        subset.All(superset.Contains);

    private static bool HasValidInstallSelectionShape(
        InstallCardParameters install,
        FullGameActionClass actionClass)
    {
        if (actionClass == FullGameActionClass.TurnAction)
        {
            return install.CandidateReplacementCardIds.Count == 0
                ? install.FinalizeSelection
                    && install.SelectedReplacementCardIds.Count == 0
                : !install.FinalizeSelection
                    && install.SelectedReplacementCardIds.Count == 0;
        }

        // InstallChoice is a continuation over the frozen candidate stream.
        // A non-final action must append at least one remaining candidate. It
        // may append the last candidate too: Core issues that action before a
        // fresh observation can expose the corresponding final action. A final action
        // may select zero cards when no replacement is required, but it must
        // include every required replacement when one is issued.
        if (install.CandidateReplacementCardIds.Count == 0)
        {
            return false;
        }

        if (install.FinalizeSelection)
        {
            return install.RequiredReplacementCardIds.All(install.SelectedReplacementCardIds.Contains);
        }

        return install.SelectedReplacementCardIds.Count > 0
            && install.SelectedReplacementCardIds.Count <= install.CandidateReplacementCardIds.Count;
    }

    private static bool IsValidInstallCostQuote(CostQuote? quote, int creditCost)
    {
        if (quote is null)
        {
            return true;
        }

        if (quote.BaseCost < 0
            || quote.EffectiveCost < 0
            || quote.EffectiveCost != creditCost
            || quote.Adjustments is null
            || quote.Adjustments.Count == 0)
        {
            return false;
        }

        long expected = quote.BaseCost;
        foreach (CostAdjustment? adjustment in quote.Adjustments)
        {
            if (adjustment is null
                || adjustment.Kind != BehaviorCostModifierKind.RunnerInstallCreditCost
                || adjustment.Delta == 0
                || !HasStableId(adjustment.ReasonKey)
                || (adjustment.SourceDefinitionId is { } definitionId
                    && !HasStableId(definitionId.Value))
                || (adjustment.SourceCardId is { } sourceCardId
                    && !HasStableId(sourceCardId.Value)))
            {
                return false;
            }

            try
            {
                expected = checked(expected + adjustment.Delta);
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        expected = Math.Max(0, expected);
        return expected == quote.EffectiveCost;
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
        InstallCardParameters install => InstallKey(install),
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
        ReplacementChoiceParameters replacement =>
            $"replacement:{replacement.InterruptId.Value}",
        TriggerChoiceParameters trigger =>
            $"trigger:{trigger.TriggerId.Value}",
        _ => throw new InvalidOperationException(
            $"The full-game agent cannot derive a semantic key for '{action.Kind}'."),
    };

    private static string InstallKey(InstallCardParameters install) =>
        CanonicalJson.Serialize(new
        {
            Kind = ActionKind.InstallCard,
            install.CardId,
            install.DestinationKind,
            install.ServerId,
            install.CreateNewRemote,
            install.CreditCost,
            install.CandidateReplacementCardIds,
            install.RequiredReplacementCardIds,
            install.SelectedReplacementCardIds,
            install.FinalizeSelection,
            install.CostQuote,
        });

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
        new($"The full-game agent does not support decision kind '{kind}' in v4.");

    private enum FullGameActionClass
    {
        TurnTiming,
        TurnAction,
        TurnDiscard,
        BehaviorChoice,
        InstallChoice,
        InterruptChoice,
        TriggerChoice,
        RunWindow,
    }
}

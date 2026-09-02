using System.Collections.ObjectModel;
using System.Globalization;
using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>
/// Stable version for the first full-game contract-agent policy. The policy
/// consumes only the typed legal-action surface; it never receives a
/// <see cref="GameState"/>, card catalog, RNG, or hidden-zone data.
/// </summary>
public static class FullGameContractAgentVersion
{
    public const int SchemaVersion = 1;

    public const string AgentVersion = "full-game-contract-agent-v1";

    public const string PolicyVersion = "full-game-contract-policy-v1";
}

/// <summary>
/// Action-class policy for <see cref="FullGameContractAgent"/> v1.
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
        ActionKind.GainCredit,
        ActionKind.DrawCard,
    ];

    private static readonly ActionKind[] DefaultRunActionOrder =
    [
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
            ActionKind.GainCredit,
            ActionKind.DrawCard,
        };

    private static IReadOnlySet<ActionKind> AllowedRunActions { get; } =
        new HashSet<ActionKind>
        {
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
                    $"Action kind '{action}' is not supported by this v1 policy.",
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
/// Deterministic observation-only v1 agent for the Slice 4.2 shell.
///
/// It currently dispatches typed decision classes for turn timing, turn
/// actions (gain/draw), forced discard, generic behavior choices, and M2 run
/// windows. It returns an existing engine-issued <see cref="ActionId"/> only;
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
        SelectByPriority(actions, [ActionKind.PassPriority], FullGameActionClass.TurnTiming);

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

        // The v1 teaching policy prefers an economy mode when one is offered
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
                .Where(action => action.Kind == priority && HasExpectedParameters(action))
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

            if (string.IsNullOrWhiteSpace(action.Id.Value)
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

            if (!IsActionAllowed(action, actionClass))
            {
                throw new InvalidOperationException(
                    $"The full-game agent received unsupported action '{action.Kind}' in the {actionClass} decision.");
            }

            if (!HasExpectedParameters(action))
            {
                throw new InvalidOperationException(
                    $"The full-game agent received malformed parameters for '{action.Kind}' in the {actionClass} decision.");
            }
        }
    }

    private static bool IsActionAllowed(LegalAction action, FullGameActionClass actionClass) =>
        actionClass switch
        {
            FullGameActionClass.TurnTiming => action.Kind == ActionKind.PassPriority,
            FullGameActionClass.TurnAction => action.Kind is ActionKind.GainCredit or ActionKind.DrawCard,
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
                or ActionKind.NoAccessAction,
            _ => false,
        };

    private static bool HasExpectedParameters(LegalAction action)
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
            _ => false,
        };
    }

    private static bool HasStableId(string? value) => !string.IsNullOrWhiteSpace(value);

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
        _ => throw new InvalidOperationException(
            $"The full-game agent cannot derive a semantic key for '{action.Kind}'."),
    };

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
        new($"The full-game agent does not support decision kind '{kind}' in v1.");

    private enum FullGameActionClass
    {
        TurnTiming,
        TurnAction,
        TurnDiscard,
        BehaviorChoice,
        RunWindow,
    }
}

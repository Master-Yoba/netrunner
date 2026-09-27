using System.Collections.Immutable;
using Netrunner.Core;

namespace Netrunner.AI;

public sealed partial class RunnerHeuristicPolicy : IOfflinePolicy
{
    /// <summary>The M5 policy, kept exactly for frozen benchmarks and older saves.</summary>
    public const string V1 = "runner-heuristic-v1";
    /// <summary>Adds advancement inference, agenda density and score-race terms (A2).</summary>
    public const string V2 = "runner-heuristic-v2";
    /// <summary>V2 choices, with turn actions planned by rollouts over sampled states (A3).</summary>
    public const string V3 = "runner-heuristic-v3";
    /// <summary>
    /// V2 choices with playtest repairs: credits placed on a card that already
    /// holds a stockpile it pays out slowly (Smartware Distributor) are valued
    /// at what they return, not face value.
    /// </summary>
    public const string V4 = "runner-heuristic-v4";
    /// <summary>The version new games and new manifests use.</summary>
    /// <summary>V3 plans but has not shown a ladder gain yet (Wave 3), so new games use V4, which doesn't plan.</summary>
    public const string PolicyVersion = V4;
    /// <summary>The latest version that never plans: the planner's model of the Runner.</summary>
    public const string LatestHeuristic = V4;
    public static IReadOnlyList<string> Versions { get; } = [V1, V2, V3, V4];

    public RunnerHeuristicPolicy() : this(PolicyVersion) { }

    public RunnerHeuristicPolicy(string version)
    {
        if (!Versions.Contains(version)) throw new ArgumentException($"Unknown Runner policy version '{version}'.", nameof(version));
        Version = version;
        Generation = version switch { V1 => 1, V4 => 3, _ => 2 };
        Plans = version == V3;
        Repairs = version == V4;
    }

    /// <summary>Whether this version has the playtest repairs (V4).</summary>
    private bool Repairs { get; }

    private bool Plans { get; }

    public ImmutableArray<AgentCandidate> EvaluateDecision(AgentFeatures features, AgentKnowledge knowledge, AgentObjective? objective,
        OfflineAgentConfiguration configuration, ICardEvaluationCatalog catalog, AgentWorkBudget budget, PlanningContext? planning)
    {
        ImmutableArray<AgentCandidate> heuristic = EvaluateDecision(features, knowledge, objective, configuration, catalog, budget);
        if (!Plans || planning is null) return heuristic;
        var planner = new TurnPlanner(this, configuration, catalog);
        return planner.Applies(features) ? planner.Plan(features, heuristic, knowledge, planning) : heuristic;
    }

    public string Version { get; }
    private int Generation { get; }

    public void ValidateAction(AgentFeatures features, AgentAction action)
    {
        if (features.Side != PlayerSide.Runner) throw new ArgumentException("Runner policy requires the Runner observation.");
        bool valid = action.Kind switch
        {
            ActionKind.GainCredit or ActionKind.DrawCard or ActionKind.PassPriority or ActionKind.EndTurn
                or ActionKind.JackOut or ActionKind.ContinueRun or ActionKind.NoAccessAction => action.Parameters is NoActionParameters,
            ActionKind.PlayCard => action.Parameters is PlayCardParameters,
            ActionKind.InstallCard => action.Parameters is InstallCardParameters,
            ActionKind.StartRun => action.Parameters is RunServerParameters,
            ActionKind.RemoveTag => action.Parameters is RemoveTagParameters,
            ActionKind.PumpBreaker => action.Parameters is RunBreakerParameters,
            ActionKind.BreakSubroutines => action.Parameters is RunBreakParameters,
            ActionKind.UseBioroidAbility => action.Parameters is RunBioroidAbilityParameters,
            ActionKind.TrashAccessedCard => action.Parameters is RunCardPaymentParameters,
            ActionKind.ChooseBreachCandidate => action.Parameters is BreachCandidateParameters,
            ActionKind.UseAbility => action.Parameters is AbilityActionParameters,
            ActionKind.ChooseBehavior => action.Parameters is BehaviorChoiceParameters,
            ActionKind.ChooseCard or ActionKind.TrashProgram or ActionKind.StealAgenda => action.Parameters is CardChoiceParameters,
            ActionKind.ChooseNumber => action.Parameters is NumberChoiceParameters,
            ActionKind.UseTrigger or ActionKind.DeclineTrigger => action.Parameters is TriggerChoiceParameters,
            ActionKind.OrderTriggers => action.Parameters is TriggerOrderParameters,
            ActionKind.ResolveIceSubroutine => action.Parameters is IceSubroutineChoiceParameters,
            ActionKind.DeclinePrevention or ActionKind.UsePrevention => action.Parameters is PreventionChoiceParameters,
            ActionKind.DeclineReplacement or ActionKind.UseReplacement => action.Parameters is ReplacementChoiceParameters,
            _ => false,
        };
        if (!valid) throw new ArgumentException($"Unsupported Runner action contract {action.Kind}/{action.Parameters.GetType().Name}.");
    }

    public ImmutableArray<AgentCandidate> EvaluateDecision(AgentFeatures features, AgentKnowledge knowledge, AgentObjective? objective,
        OfflineAgentConfiguration configuration, ICardEvaluationCatalog catalog, AgentWorkBudget budget)
    {
        AgentAction[] preferred = IssuedRunPaymentPreference.Select(features.Actions, action => (action.Id, action.Kind, action.Parameters)).ToArray();
        var preferredIds = preferred.Select(action => action.Id).ToHashSet();
        var context = new HeuristicContext(features with { Actions = preferred.ToImmutableArray() }, catalog, configuration, Generation);
        IReadOnlyDictionary<ActionId, AgentUtility> encounter = RunnerEncounterPlanner.Evaluate(context, budget);
        return features.Actions.Select(action => new AgentCandidate(action.Id,
            !preferredIds.Contains(action.Id) ? new(int.MinValue, 0, "payment/prefer-expiring-run-credits")
            : encounter.TryGetValue(action.Id, out AgentUtility? plan) ? plan
            : budget.TrySpend(8 + features.VisibleCards().Count() * 2) ? Evaluate(context, action)
            : Fallback(features, action))).ToImmutableArray();
    }

    public AgentUtility Evaluate(AgentFeatures features, AgentAction action, AgentKnowledge knowledge, AgentObjective? objective,
        OfflineAgentConfiguration configuration, ICardEvaluationCatalog catalog, AgentWorkBudget budget)
    {
        ValidateAction(features, action);
        return EvaluateDecision(features, knowledge, objective, configuration, catalog, budget).Single(candidate => candidate.ActionId == action.Id).Utility;
    }

    private static AgentUtility Evaluate(HeuristicContext context, AgentAction action) => action.Kind switch
    {
        ActionKind.GainCredit => Value(Credit(context), "economy/credit-and-install-reserve"),
        ActionKind.DrawCard => Value(context.Features.Board.HiddenZoneCounts.RunnerStack > 0 ? context.DrawValue
            + (context.Features.Hand.Length < 2 && context.Features.Board.Servers.SelectMany(server => server.Ice).Any(card => context.Definition(card)?.Ice?.Subroutines.Any(subroutine => subroutine is DamageSubroutineSpec) == true) ? 80 : 0) : -1000, "economy/draw-and-survival"),
        ActionKind.PlayCard => Play(context, (PlayCardParameters)action.Parameters),
        ActionKind.InstallCard => Install(context, (InstallCardParameters)action.Parameters),
        ActionKind.StartRun => Run(context, context.Server(((RunServerParameters)action.Parameters).ServerId)),
        ActionKind.RemoveTag => new(10, RunnerValues.TagRisk(context) - ((RemoveTagParameters)action.Parameters).CreditCost * context.CreditValue, "defense/remove-tag"),
        ActionKind.JackOut => Value(0, "run/exit"),
        ActionKind.ContinueRun => Continue(context),
        ActionKind.TrashAccessedCard => Trash(context, (RunCardPaymentParameters)action.Parameters),
        ActionKind.NoAccessAction => Value(0, "access/continue-to-steal-or-finish"),
        ActionKind.StealAgenda => new(100, 1000, "access/steal-issued-agenda"),
        ActionKind.ChooseBreachCandidate => Access(context, (BreachCandidateParameters)action.Parameters),
        ActionKind.ChooseCard or ActionKind.TrashProgram => Value(-RunnerValues.Card(context, context.Card(((CardChoiceParameters)action.Parameters).CardId)), "forced-loss/preserve-coverage-and-investment"),
        ActionKind.UseAbility => Ability(context, (AbilityActionParameters)action.Parameters),
        ActionKind.UseTrigger or ActionKind.DeclineTrigger => Trigger(context, ((TriggerChoiceParameters)action.Parameters).TriggerId, action.Kind == ActionKind.UseTrigger),
        ActionKind.OrderTriggers => Trigger(context, ((TriggerOrderParameters)action.Parameters).TriggerId, true),
        ActionKind.ChooseBehavior => ChooseBehavior(context, (BehaviorChoiceParameters)action.Parameters),
        ActionKind.ResolveIceSubroutine => Subroutine(context, (IceSubroutineChoiceParameters)action.Parameters),
        ActionKind.ChooseNumber => Mulligan(context, (NumberChoiceParameters)action.Parameters),
        ActionKind.PassPriority or ActionKind.EndTurn or ActionKind.DeclinePrevention or ActionKind.DeclineReplacement => Value(0, "timing/continue"),
        ActionKind.UsePrevention => new(20, ((PreventionChoiceParameters)action.Parameters).Amount * 200, "defense/issued-prevention", true),
        ActionKind.UseReplacement => Value(50, "defense/issued-replacement", true),
        ActionKind.PumpBreaker or ActionKind.BreakSubroutines or ActionKind.UseBioroidAbility => new(-200, 0, "encounter/unsupported-estimate", true, UsedFallback: true),
        _ => throw new ArgumentException($"Unsupported Runner action {action.Kind}."),
    };

    public AgentUtility Fallback(AgentFeatures features, AgentAction action) => new(0, action.Kind switch
    {
        ActionKind.NoAccessAction => 500,
        ActionKind.JackOut => 400,
        ActionKind.PassPriority or ActionKind.DeclineTrigger => 100,
        ActionKind.GainCredit => 60,
        ActionKind.EndTurn => 10,
        _ => 0,
    }, "fallback/conservative-issued-choice", true, UsedFallback: true);

    private static AgentUtility Value(int score, string reason, bool approximate = false, bool fallback = false) => new(0, score, reason, approximate, UsedFallback: fallback);
    private static int Credit(HeuristicContext context)
    {
        bool saving = context.Features.Hand.Any(card => context.Definition(card)?.Icebreaker is { } breaker
            && !context.Features.Board.RunnerRig.Any(other => context.Definition(other)?.Icebreaker?.InterfaceType == breaker.InterfaceType)
            && (context.Definition(card)?.Printed.InstallCost ?? 0) > context.Self.Credits);
        return context.CreditValue + (saving ? 180 : 0);
    }

    private static AgentUtility Mulligan(HeuristicContext context, NumberChoiceParameters choice)
    {
        if (context.Features.Decision?.Kind != DecisionKind.Setup) return Value(-choice.Value, "choice/number", true);
        bool economy = context.Features.Hand.Any(card => context.Definition(card)?.Behavior is { } behavior
            && HeuristicContext.Flatten(behavior.Instructions).Any(instruction => instruction.Kind is BehaviorInstructionKind.GainCredits or BehaviorInstructionKind.LoadHostedCredits));
        bool coverage = context.Features.Hand.Any(card => context.Definition(card)?.Icebreaker is not null);
        return Value((choice.Value == 0) == (economy && coverage) ? 100 : 0, "opening/economy-and-breaker-foundation");
    }
}

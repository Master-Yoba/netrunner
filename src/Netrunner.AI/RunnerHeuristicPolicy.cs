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
    /// <summary>
    /// V4 with a playtest repair: a rezzed upgrade's toll on approaching the
    /// server (Manegarm Skunkworks: 2 clicks or 5 credits, else the run ends)
    /// counts when judging whether a run can get in.
    /// </summary>
    public const string V5 = "runner-heuristic-v5";
    /// <summary>
    /// V5 with repairs from the first Gateway-deck audit (Wave 4 C1): a run event
    /// that raises ICE rez costs is worth the unrezzed ICE it neutralizes, a card
    /// that improves the basic draw is worth its future draws, a run into an
    /// advanced unknown card weighs its possible trap damage against the grip, and
    /// trashing an advanced trap is worth the damage it would deal.
    /// </summary>
    public const string V6 = "runner-heuristic-v6";
    /// <summary>
    /// V6 with the whole-run model (A4a): a run is costed as one path through every
    /// piece of ICE, choosing per piece what to break and what to let fire.
    /// </summary>
    public const string V7 = "runner-heuristic-v7";
    /// <summary>V7 choices with turn actions planned by rollouts (A3), scored by the fitted evaluation (A5).</summary>
    public const string V8 = "runner-heuristic-v8";
    /// <summary>
    /// V7 choices with run search (A4): inside its own run, continuing or jacking
    /// out and trashing an accessed card or not are chosen by rollouts to the
    /// run's end, scored by the fitted evaluation. Turn actions aren't planned.
    /// </summary>
    public const string V9 = "runner-heuristic-v9";
    /// <summary>
    /// V7 with central memory (A4a): R&amp;D is worth less while its top card is one
    /// the Runner has already seen, and HQ in proportion to how much of it the
    /// Corp has drawn since the last access.
    /// </summary>
    public const string V10 = "runner-heuristic-v10";
    /// <summary>
    /// V10 playing to its deck's archetype (A8): big rig, aggro, central
    /// pressure, econ denial or virus set how eagerly it runs, how much it
    /// values setting up, which centrals it presses, and what trashing and
    /// viruses are worth.
    /// </summary>
    public const string V11 = "runner-heuristic-v11";
    /// <summary>
    /// V10 with the self-play review fixes that came with V11 (28 September), and
    /// no archetype profile: trap risk weighed by how likely the Corp plays traps,
    /// with a hard stop only after one was seen; the fear of unrezzed ICE scaled by
    /// what the Corp can rez and the ICE types covered. V11's archetype knobs were
    /// weaker on the ladder (-32 on two seeds), these fixes alone are not (+29).
    /// </summary>
    public const string V12 = "runner-heuristic-v12";
    /// <summary>
    /// V12 with the 30 September playtest fixes: the Runner remembers the face-down root cards it
    /// accessed, so a known trap's damage is certain and a known asset is worth only an affordable
    /// trash; an economy asset is worth the money it will still make, a trap little, and a trash
    /// that rewards the Runner (René "Loup" Arcemont) that reward.
    /// </summary>
    public const string V13 = "runner-heuristic-v13";
    /// <summary>
    /// V13 with the 1 October Codex review fixes: Zahya's income from a run's accesses is taken, not
    /// declined; Diviner ends the run for certain when every card in the grip has an odd cost; and a run
    /// whose ICE would forbid stealing and trashing (Ansel 1.0) gains nothing past it.
    /// </summary>
    public const string V14 = "runner-heuristic-v14";
    /// <summary>
    /// V14 with the fixes from the reviews of its losses to Codex (1 October): an advanced card in a remote
    /// is contested when the Corp is one agenda from winning, the trap's risk notwithstanding unless lethal.
    /// </summary>
    public const string V15 = "runner-heuristic-v15";
    /// <summary>
    /// V15 with the fixes from the reviews of its losses to Codex (1 October, batch 3): a unique card isn't installed
    /// over its own copy (Verbal Plasticity), an event that loses a click left over is played with the last click
    /// (Creative Commission), and an extra HQ access is worth installing (Docklands Pass).
    /// </summary>
    public const string V16 = "runner-heuristic-v16";
    /// <summary>
    /// V16 without its click-loss rule, the suspect in its losses to Codex, and with a console counted as unique: no
    /// Pantograph over its own copy (batch 4's reviews, 1 October).
    /// </summary>
    public const string V17 = "runner-heuristic-v17";
    /// <summary>
    /// V15 with only the reinstall fix: no install over its own copy or a second console. V16 and V17 both lost to Codex on
    /// deals V15 won (1 of 23 against 11 of 35); what they share beyond that is Docklands Pass's install bonus, left out.
    /// </summary>
    public const string V18 = "runner-heuristic-v18";
    /// <summary>The version new games and new manifests use.</summary>
    /// <remarks>
    /// V15 again: V16 was level or better on the Gateway ladder (+22 and +45), but against Codex on the same 12 deals it
    /// won none of the games where V15 had won 7 of 23 (1 October), so V15 stays the default until a version matches it there.
    /// </remarks>
    public const string PolicyVersion = V15;
    /// <summary>
    /// The planner's model of the Runner. It stays V7, which Corp v9 was measured
    /// with, so the shipped Corp planner plays as it did.
    /// </summary>
    public const string LatestHeuristic = V7;
    public static IReadOnlyList<string> Versions { get; } = [V1, V2, V3, V4, V5, V6, V7, V8, V9, V10, V11, V12, V13, V14, V15, V16, V17, V18];

    public RunnerHeuristicPolicy() : this(PolicyVersion) { }

    public RunnerHeuristicPolicy(string version)
    {
        if (!Versions.Contains(version)) throw new ArgumentException($"Unknown Runner policy version '{version}'.", nameof(version));
        Version = version;
        Generation = version switch { V1 => 1, V4 => 3, V5 => 4, V6 => 5, V7 or V8 or V9 => 6, V10 => 7, V11 or V12 => 8, V13 => 9, V14 => 10, V15 => 11, V16 => 12, V17 => 13, V18 => 14, _ => 2 };
        Plans = version is V3 or V8 or V9;
        Repairs = version is V4 or V5 or V6 or V7 or V8 or V9 or V10 or V11 or V12 or V13 or V14 or V15 or V16 or V17 or V18;
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
        var context = new HeuristicContext(features with { Actions = preferred.ToImmutableArray() }, catalog, configuration, Generation)
        {
            Knowledge = Generation >= 7 ? knowledge : null,
        };
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

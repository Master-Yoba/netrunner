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
    /// <summary>
    /// V18 with the economy fixes from the Codex reviews (1 October): an ability that puts credits in the wallet now is worth
    /// what as many basic credits are (Pennyshaver's cash-out lost to a basic credit), and a tutor counts as breaker coverage
    /// when keeping an opening hand (it threw back Sure Gamble, Jailbreak and Mutual Favor).
    /// </summary>
    public const string V19 = "runner-heuristic-v19";
    /// <summary>
    /// V18 with the fixes from the Wave 5 playtest (2 October), and V19's tutor-in-the-opening-hand fix but not its wallet rule:
    /// a tutor's breaker is valued by what it breaks on the board, as installing it would be (Mutual Favor fetched Carmen with
    /// two barriers out and the R&amp;D barrier never broken); a tag is cleared first only while its risk is worth more than the
    /// clearing costs (broke, with two Sure Gambles in the grip, it paid 2 credits a tag seven times against a Corp that
    /// never punished one); and a remembered root card stays known when the Corp installs beside it (against Claude Sonnet it
    /// accessed an Urtica Cipher, forgot it when Manegarm Skunkworks went into the same root, and ran there again to flatline).
    /// </summary>
    public const string V20 = "runner-heuristic-v20";
    /// <summary>
    /// V20 with A9b, inference from what it has seen (the A9a tracker): HQ and R&amp;D are valued by the agendas they likely
    /// hold; the Corp's tag punishers it knows of raise a tag's risk when the Corp can play them; and what a face-down card
    /// probably is (a trap seen in HQ, an upgrade left there, ICE of a seen type) prices runs.
    /// </summary>
    public const string V21 = "runner-heuristic-v21";
    /// <summary>V21 with A9c, copy counting: a Hard Runner, which knows the Corp's list, values HQ and R&amp;D by the agendas left in it.</summary>
    public const string V22 = "runner-heuristic-v22";
    /// <summary>
    /// V22 with bot loop 1's fixes (docs/history/BOT_IMPROVEMENT_PLAN.md): damage to the Runner costs a card a point, and damage
    /// the grip can't absorb (after the cards the same effect installs from it) is a flatline, never chosen. 41% of
    /// Standard's Core Battle Box games were lost to the Runner's own choice: Topan installing its last card, and taking
    /// Semak-samun's 3 net damage or Measured Response's 4 meat damage with too few cards in the grip.
    /// </summary>
    public const string V23 = "runner-heuristic-v23";
    /// <summary>
    /// V23 with bot loop 1's deferred fixes (docs/history/BOT_LOOP_1.md): Elevation subroutines written as behaviors are read for what
    /// they do (N-Pot's end the run at threat 2 and 4, Lamplighter's on a tagged Runner), and N-Pot's break costs credits,
    /// not clicks, so a run isn't left half-broken; an unadvanced root card may be an ambush that flatlines a small grip
    /// (Byte!), and a known operation that would is trashed (Measured Response); Clean Getaway is played on a run whose
    /// success it can see; a second copy of an installed breaker isn't installed; and a tutor isn't played when the rig and
    /// grip already break every ICE type (Mutual Favor).
    /// </summary>
    public const string V24 = "runner-heuristic-v24";
    /// <summary>
    /// V24 with bot loop 2's fixes (docs/history/BOT_LOOP_2.md): Scatter Field's +4 as the only ICE is seen (the Runner ran it with
    /// credits for strength 0); an unrezzed ICE already passed isn't unknown ICE ahead at the jack-out decision; Topan isn't
    /// used without a card it can pay for and would install; a card MuslihaT adds to the grip is a free draw; BANGUN's damage
    /// on accessing a faceup agenda is priced; and with one card in the grip the Runner draws before running unknown ICE.
    /// </summary>
    public const string V25 = "runner-heuristic-v25";
    /// <summary>
    /// V25 against its self-flatlines (docs/history/BOT_STRATEGY_1.md): after a successful run, a grip too small for an operation that
    /// punishes one (Measured Response's 4 meat damage at threat 4) is drawn up, as likely as the Corp holding one; a run
    /// event's trap risk counts the grip without the event; at match point an empty grip draws before running a possible
    /// trap; and a breaker trashed at the run's end (Mayfly) is priced as spent by the run.
    /// </summary>
    public const string V26 = "runner-heuristic-v26";
    /// <summary>
    /// V26's behaviour as the one live policy (bot plan S3): the version checks resolved to V26's and the dead branches gone.
    /// Every earlier version is <see cref="LegacyRunnerPolicy"/>, frozen.
    /// </summary>
    public const string V27 = "runner-heuristic-v27";
    /// <summary>
    /// V27 with a face-down card in a remote valued by what it likely is (the S1.5 follow-up, 6 October): an agenda at the
    /// chance its unseen root cards hold one, raised when the remote is protected by ICE, as the Corp protects agendas.
    /// </summary>
    public const string V28 = "runner-heuristic-v28";
    /// <summary>
    /// V28 with a grip cushion against the damage a run may meet (bot plan H3, the strategy guide's R18): an ambush the Corp
    /// can pay for among HQ's and R&amp;D's cards, unknown ICE that deals damage, and an operation that punishes the run's
    /// success when too few cards and clicks would be left to survive it. Against PT Untaian Shell Game the Runner won 8% of
    /// its box games, 172 of 200 lost to flatlines.
    /// </summary>
    public const string V29 = "runner-heuristic-v29";
    /// <summary>
    /// V29 with the PT Untaian matchup note's rules (bot plan H3 batch 2, docs/MATCHUP_PT_UNTAIAN.md): an engine whose own
    /// triggers damage the Runner (Phật Gioan Baotixita) is worth trashing before agendas are farmed (PT08), a non-winning
    /// steal's damage is part of the run's cushion (PT13, PT15), and the damage a run is expected to deal beyond the clicks
    /// left to draw it back costs a card each, so dangerous runs go early in the turn (PT03, PT04).
    /// </summary>
    public const string V30 = "runner-heuristic-v30";
    /// <summary>The version new games and new manifests use.</summary>
    /// <remarks>
    /// V18 is V15 with only the reinstall fix (no install over its own copy or a second console), a fix that can only remove
    /// wasted installs. V16 and V17, with more changes, lost to Codex on deals V15 won (1 October), so they aren't the default,
    /// nor is V19: level on the bot ladder, it won 1 of 12 on batch 3's deals where V18 won 6 of 24. V20 (V18 with the Wave 5
    /// playtest's fixes, 2 October) is: -17 and +49 on the bot ladder, and its fixes only remove plays the playtest showed were
    /// wrong. Its Codex check was cut short when the quota ran out (1 of 6). V21 (A9b, inference from the A9a tracker, 2 October)
    /// is: +75, -31, +16 and +30 against V20 on the bot ladder, and against Claude Sonnet as Corp on 24 fixed deals it won 8
    /// where V20 won 6. V22 (A9c, 2 October) is: at Standard and Easy it plays as V21 (its counting needs the Corp's list),
    /// and at Hard it was level with V21 on the bot ladder. V23 (bot loop 1, 3 October) is the default while loop 1 plays
    /// against it: its fixes remove plays that flatline the Runner itself. V24 (loop 1's deferred fixes, 4 October) is the
    /// default loop 2 played against. V25 (bot loop 2, 4 October) was the default until V26. V27 (bot plan S3) is V26 as the
    /// live policy, identical game by game. V28 values a protected face-down remote card as the agenda it likely is. V29 (the
    /// grip cushion) was level and is not the default. V30 (bot plan H3 batch 2, the PT Untaian note) is: +30 Elo [9, 55] over
    /// V28 on 800 Core Battle Box deals, 11% to 30% against PT Untaian, level on Gateway.
    /// </remarks>
    public const string PolicyVersion = V30;
    /// <summary>
    /// The planner's model of the Runner. It stays V7, which Corp v9 was measured
    /// with, so the shipped Corp planner plays as it did.
    /// </summary>
    public const string LatestHeuristic = V7;
    public static IReadOnlyList<string> Versions { get; } = [V1, V2, V3, V4, V5, V6, V7, V8, V9, V10, V11, V12, V13, V14, V15, V16, V17, V18, V19, V20, V21, V22, V23, V24, V25, V26, V27, V28, V29, V30];

    /// <summary>Whether the version's memory of a face-down root card survives installs in that root (v20).</summary>
    public static bool KeepsRootMemoryThroughInstalls(string version) => version is V20 or V21 or V22 or V23 or V24 or V25 or V26 or V27 or V28 or V29 or V30;

    /// <summary>Whether the version keeps the A9a tracker of the Corp's hidden cards (v21).</summary>
    public static bool UsesTracker(string version) => version is V21 or V22 or V23 or V24 or V25 or V26 or V27 or V28 or V29 or V30;

    public RunnerHeuristicPolicy() : this(PolicyVersion) { }

    public RunnerHeuristicPolicy(string version)
    {
        if (version is not (V27 or V28 or V29 or V30))
            throw new ArgumentException($"The live Runner policy is {V27} to {V30}; '{version}' is {nameof(LegacyRunnerPolicy)}.", nameof(version));
        Version = version;
        _contextGeneration = version switch { V30 => 25, V29 => 24, V28 => 23, _ => 22 };
    }

    /// <summary>
    /// The generation the shared helpers (<see cref="HeuristicContext"/>, RunnerValues, RunModel) switch on: V26's (22) for
    /// V27, 23 for V28's remote valuation, 24 for V29's grip cushion, and 25 for V30's matchup rules. They keep their switches for <see cref="LegacyRunnerPolicy"/>, and V27 stays
    /// constructible so the regret suite can compare the two (<c>ai-regret score --discordant</c>).
    /// </summary>
    private readonly int _contextGeneration;

    public ImmutableArray<AgentCandidate> EvaluateDecision(AgentFeatures features, AgentKnowledge knowledge, AgentObjective? objective,
        OfflineAgentConfiguration configuration, ICardEvaluationCatalog catalog, AgentWorkBudget budget, PlanningContext? planning)
    {
        ImmutableArray<AgentCandidate> heuristic = EvaluateDecision(features, knowledge, objective, configuration, catalog, budget);
        if (planning is null) return heuristic;
        var planner = new TurnPlanner(this, configuration, catalog);
        return planner.Applies(features) ? planner.Plan(features, heuristic, knowledge, planning) : heuristic;
    }

    public string Version { get; }

    public void ValidateAction(AgentFeatures features, AgentAction action)
    {
        if (features.Side != PlayerSide.Runner) throw new ArgumentException("Runner policy requires the Runner observation.");
        bool valid = action.Kind switch
        {
            ActionKind.GainCredit or ActionKind.DrawCard or ActionKind.PassPriority or ActionKind.EndTurn
                or ActionKind.JackOut or ActionKind.ContinueRun or ActionKind.NoAccessAction => action.Parameters is NoActionParameters,
            ActionKind.PlayCard => action.Parameters is PlayCardParameters,
            ActionKind.InstallCard => action.Parameters is InstallCardParameters or BehaviorInstallChoiceParameters,
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
        var context = new HeuristicContext(features with { Actions = preferred.ToImmutableArray() }, catalog, configuration, _contextGeneration)
        {
            Knowledge = knowledge,
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
        ActionKind.DrawCard => Value(context.Features.Board.HiddenZoneCounts.RunnerStack > 0 ? context.DrawValue + PunisherDraw(context)
            + (context.Features.Hand.Length < 2 && context.Features.Board.Servers.SelectMany(server => server.Ice).Any(card => context.Definition(card)?.Ice?.Subroutines.Any(subroutine => subroutine is DamageSubroutineSpec) == true) ? 80 : 0) : -1000, "economy/draw-and-survival"),
        ActionKind.PlayCard => Play(context, (PlayCardParameters)action.Parameters),
        ActionKind.InstallCard when action.Parameters is BehaviorInstallChoiceParameters behaviorInstall =>
            behaviorInstall.Choice is { } chosen
                ? Install(context, HeuristicContext.AsInstall(chosen))
                : new AgentUtility(0, 0, "install/decline-card-effect"),
        ActionKind.InstallCard => Install(context, (InstallCardParameters)action.Parameters),
        ActionKind.StartRun => Run(context, context.Server(((RunServerParameters)action.Parameters).ServerId)),
        ActionKind.RemoveTag => RemoveTag(context, (RemoveTagParameters)action.Parameters),
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

    /// <summary>v26: after a successful run, drawing up to the grip that survives the Corp's punishing operations comes first.</summary>
    private static int PunisherDraw(HeuristicContext context)
    {
        if (context.Features.Board.TurnFacts?.Runner.ThisTurn?.SuccessfulRun != true) return 0;
        (int grip, double chance) = RunnerInference.Punishers(context);
        return grip > context.Features.Hand.Length ? (int)Math.Min(700, chance * PunishedGame) : 0;
    }

    /// <summary>v26: what a flatline to a punishing operation costs, before the chance the Corp holds one.</summary>
    private const int PunishedGame = 4000;

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
        // v19 (Codex review, 1 October): a tutor (Mutual Favor) finds the breaker the hand lacks.
        bool coverage = context.Features.Hand.Any(card => context.Definition(card)?.Icebreaker is not null
            || context.Definition(card)?.Behavior is { } behavior
                && HeuristicContext.Flatten(behavior.Instructions).Any(instruction => instruction.Kind == BehaviorInstructionKind.Search));
        return Value((choice.Value == 0) == (economy && coverage) ? 100 : 0, "opening/economy-and-breaker-foundation");
    }

    /// <summary>
    /// Clearing a tag comes first (priority 10). v20 (Wave 5 playtest, 2 October): only while the tag's risk is worth more
    /// than the clearing costs; otherwise it competes with every other action on its score.
    /// </summary>
    private static AgentUtility RemoveTag(HeuristicContext context, RemoveTagParameters remove)
    {
        int score = RunnerValues.TagRisk(context) - remove.CreditCost * context.CreditValue;
        return new(score <= 0 ? 0 : 10, score, "defense/remove-tag");
    }
}

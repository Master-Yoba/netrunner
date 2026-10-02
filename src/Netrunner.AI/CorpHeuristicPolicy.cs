using System.Collections.Immutable;
using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>One-ply Corp utility evaluation over filtered, engine-issued choices.</summary>
public sealed partial class CorpHeuristicPolicy : IOfflinePolicy
{
    /// <summary>The M5 policy, kept exactly for frozen benchmarks and older saves.</summary>
    public const string V2 = "corp-heuristic-v2";
    /// <summary>Adds breaker-coverage protection and score-race terms (A2).</summary>
    public const string V3 = "corp-heuristic-v3";
    /// <summary>
    /// Values advancement effects by their best legal target, pre-advances
    /// agendas it can finish next turn, and keeps a working hand (playtest fixes).
    /// </summary>
    public const string V4 = "corp-heuristic-v4";
    /// <summary>V4 choices, with turn actions planned by rollouts over sampled states (A3).</summary>
    public const string V5 = "corp-heuristic-v5";
    /// <summary>
    /// V4 choices with playtest repairs: an install that must replace a remote's
    /// agenda or asset pays for what it trashes, an upgrade in the attacked
    /// server is rezzed once the Runner is past its ICE, advancing keeps enough
    /// credits to rez the ICE protecting that agenda, and an agenda that needs
    /// more than a turn is advanced toward finishing next turn.
    /// </summary>
    public const string V6 = "corp-heuristic-v6";
    /// <summary>
    /// V6 with repairs from the first Gateway-deck audit (Wave 4 C1): a choice the
    /// Runner makes is valued as the Runner's best option, a tag is worth what it
    /// costs the Runner to clear plus the trash threat it opens, a trash effect is
    /// worth its best target, and an advanceable trap is advanced and rezzed.
    /// </summary>
    public const string V7 = "corp-heuristic-v7";
    /// <summary>
    /// V7 judging where an agenda is safe with the whole-run model (A4a), run from
    /// the Runner's side on public information with the Corp's own ICE known,
    /// instead of counting ICE and breaker types.
    /// </summary>
    public const string V8 = "corp-heuristic-v8";
    /// <summary>V8 choices with turn actions planned by rollouts (A3), scored by the fitted evaluation (A5).</summary>
    public const string V9 = "corp-heuristic-v9";
    /// <summary>
    /// V8 choices with run search (A4): whether to rez a card during a run (the approached
    /// ICE, an upgrade or an asset) is chosen by rollouts to the run's end, scored by the fitted evaluation.
    /// Turn actions aren't planned.
    /// </summary>
    public const string V10 = "corp-heuristic-v10";
    /// <summary>
    /// Playtest repairs on V9 (28 September): ICE goes where it protects
    /// something (no empty remotes, HQ and R&amp;D first), upgrades go where they
    /// work, approached ICE is rezzed for the runs it denies, advanceable ICE is
    /// advanced, and the planner scores with the refitted evaluation (feature set 2).
    /// </summary>
    public const string V11 = "corp-heuristic-v11";
    /// <summary>
    /// V11 playing to its deck's archetype (A8): glacier, rush, fast advance,
    /// shell game, kill, tag punishment or horizontal set how many remotes it
    /// builds and how tall, when agendas go in, and how much tags, traps and
    /// damage are worth.
    /// </summary>
    public const string V12 = "corp-heuristic-v12";
    /// <summary>
    /// Playtest repairs on V12 (29 September): scoring upgrades aren't rezzed during runs, ICE rezzes
    /// keep money for the agenda's ICE, Archives is walled against a money farm, tagged Runner resources
    /// are trashed for what they're worth to the Runner, a tag is played only when it can be punished
    /// this turn, and agendas wait in HQ until their server and money are ready.
    /// </summary>
    public const string V13 = "corp-heuristic-v13";
    /// <summary>
    /// V13 with the rez timing of economy assets from the 29 September playtest: an asset whose
    /// money comes when the Corp's turn begins (Nico Campaign) isn't rezzed in the Corp's own turn,
    /// where it sat rezzed through the Runner's whole turn; it waits for the last windows before the
    /// Corp's turn, as V12 already did for the Runner's turn.
    /// </summary>
    public const string V14 = "corp-heuristic-v14";
    /// <summary>
    /// V14 with the 30 September playtest's fix: an ability that removes or trashes its own card
    /// counts the card as a cost, and shuffling Archives into R&amp;D is worth only the cards
    /// there to shuffle (Spin Doctor was spent with Archives empty).
    /// </summary>
    public const string V15 = "corp-heuristic-v15";
    /// <summary>
    /// V15 with the other half of the playtester's Spin Doctor trick: an agenda the Runner can
    /// reach before it can be scored is trashed by installing over it when an installed card can
    /// shuffle it back from Archives into R&amp;D.
    /// </summary>
    public const string V16 = "corp-heuristic-v16";
    /// <summary>
    /// V16 with the 30 September playtest's subroutine fix: installing a card through an ICE
    /// subroutine (Ansel 1.0) into a remote counts the root card it trashes, so the Corp no longer
    /// trashes its own agenda there by accident; it does so only to deny a steal the Runner can't make
    /// from Archives this turn (no click left, or not the credits to get through its ICE).
    /// </summary>
    public const string V17 = "corp-heuristic-v17";
    /// <summary>
    /// V17 with the 30 September playtest's fixes: ICE goes on an open HQ first when agendas wait
    /// there (the planner's fitted evaluation can't see the Corp's hand and prefers R&amp;D); and
    /// counters from an effect (Seamless Launch) go where they count, never on ICE that ignores them.
    /// </summary>
    public const string V18 = "corp-heuristic-v18";
    /// <summary>
    /// V18 planning with the corrected evaluation (feature set 3): board advancement counts only
    /// counters on agendas and advanceable cards. The oddity lints (Wave 4.5, P1) found V18 playing
    /// Seamless Launch onto Nico Campaign or ICE when no agenda was installed: the planner's
    /// evaluation counted any counter in a remote as progress.
    /// </summary>
    public const string V19 = "corp-heuristic-v19";
    /// <summary>
    /// V19 with the 1 October Codex review fixes: a trap whose damage already exceeds the Runner's grip
    /// isn't advanced further (Urtica Cipher took every spare click and credit); Sprint keeps the most
    /// valuable cards in HQ and never shuffles back the agenda that would win; and ICE protecting R&D, or
    /// HQ holding an agenda, is agenda ICE, not ICE to hold credits back from.
    /// </summary>
    public const string V20 = "corp-heuristic-v20";
    /// <summary>
    /// V20 with the fixes from the reviews of its losses to Codex (1 October): an agenda that can't be scored this
    /// turn waits in HQ while the Runner's rig would get into its server, and Archives gets ICE only when it holds
    /// agendas or pays the Runner for running it.
    /// </summary>
    public const string V21 = "corp-heuristic-v21";
    /// <summary>
    /// V21 with the fixes from the reviews of its losses to Codex (1 October, batch 3): an agenda waits in HQ for
    /// protection however full HQ is, unless it holds two agendas; an asset that pays back at once (Regolith Mining
    /// License) is rezzed though it dips into the ICE budget; and a purge is worth less while the Corp is broke.
    /// </summary>
    public const string V22 = "corp-heuristic-v22";
    /// <summary>
    /// V22 with the fixes from the reviews of batch 4's losses to Codex (1 October): a trap the Runner has already
    /// accessed isn't advanced (it won't be run into again), and an asset that pays back at once is rezzed only with a
    /// click left to start taking its credits.
    /// </summary>
    public const string V23 = "corp-heuristic-v23";
    /// <summary>
    /// V23 with the removal fixes from the Codex reviews (1 October): a Runner card to trash is the one that threatens most,
    /// a breaker by the installed Corp ICE it gets through (Retribution trashed Botulus over the Carmen breaking every
    /// sentry, and its target score had the wrong sign), and Ballista's end-the-run is worth the server it protects.
    /// </summary>
    public const string V24 = "corp-heuristic-v24";
    /// <summary>
    /// V23 with the fixes from the Wave 5 playtest (2 October), without V24's: a trap already past lethal is never advanced,
    /// even when the planner's rollouts like it (two Urtica Ciphers went to +6 and +5 on remotes the Runner never ran, while
    /// HQ stayed empty and R&amp;D thin); and the planner scores rollouts with evaluation feature set 4, where counters on the Corp's
    /// own non-agenda cards aren't scoring progress.
    /// </summary>
    public const string V25 = "corp-heuristic-v25";
    /// <summary>
    /// V25 with A9c, copy counting, and a tracker of the Runner's cards: a Hard Corp, which knows the Runner's list, values
    /// ICE of a type no breaker left in the Runner's deck can break, and gives the Runner a burst only while its burst
    /// economy (Sure Gamble) isn't spent.
    /// </summary>
    public const string V26 = "corp-heuristic-v26";
    /// <summary>The version new games and new manifests use.</summary>
    /// <remarks>
    /// V23 fixes V22's faults the reviews of batch 4's losses to Codex found (1 October: known traps advanced, two agendas
    /// pushed out of a safer HQ, Regolith rezzed with no click to use it). On batch 3's deals against Codex it won 6 of 12,
    /// where V21 and V22 won 8 of 36. V24, level on the bot ladder, won 3 of 12 on the same deals where V23 won 10 of 24, so it
    /// isn't the default. V25 (V23 with the Wave 5 playtest's fixes, 2 October) is: +6 and +70 on the bot ladder, and its
    /// fixes only remove plays the playtest showed were wrong. Its Codex check was cut short when the quota ran out (1 of 3).
    /// </remarks>
    public const string PolicyVersion = V25;
    /// <summary>The latest version that never plans: the planner's model of the Corp.</summary>
    public const string LatestHeuristic = V8;
    public static IReadOnlyList<string> Versions { get; } = [V2, V3, V4, V5, V6, V7, V8, V9, V10, V11, V12, V13, V14, V15, V16, V17, V18, V19, V20, V21, V22, V23, V24, V25, V26];

    /// <summary>Whether the version keeps the A9 tracker of the Runner's hidden cards (v26).</summary>
    public static bool UsesTracker(string version) => version == V26;

    public CorpHeuristicPolicy() : this(PolicyVersion) { }

    public CorpHeuristicPolicy(string version)
    {
        if (!Versions.Contains(version)) throw new ArgumentException($"Unknown Corp policy version '{version}'.", nameof(version));
        Version = version;
        Generation = version switch { V2 => 1, V3 => 2, V7 => 4, V8 or V9 or V10 => 5, V11 => 6, V12 => 7, V13 => 8, V14 => 9, V15 => 10, V16 => 11, V17 => 12, V18 => 13, V19 => 14, V20 => 15, V21 => 16, V22 => 17, V23 => 18, V24 => 19, V25 => 20, V26 => 21, _ => 3 };
        Plans = version is V5 or V9 or V10 or V11 or V12 or V13 or V14 or V15 or V16 or V17 or V18 or V19 or V20 or V21 or V22 or V23 or V24 or V25 or V26;
        Repairs = version is V6 or V7 or V8 or V9 or V10 or V11 or V12 or V13 or V14 or V15 or V16 or V17 or V18 or V19 or V20 or V21 or V22 or V23 or V24 or V25 or V26;
    }

    /// <summary>Whether this version has the playtest repairs (V6).</summary>
    private bool Repairs { get; }

    private bool Plans { get; }

    public ImmutableArray<AgentCandidate> EvaluateDecision(AgentFeatures features, AgentKnowledge knowledge, AgentObjective? objective,
        OfflineAgentConfiguration configuration, ICardEvaluationCatalog catalog, AgentWorkBudget budget, PlanningContext? planning)
    {
        ImmutableArray<AgentCandidate> heuristic = ((IOfflinePolicy)this).EvaluateDecision(features, knowledge, objective,
            configuration, catalog, budget);
        if (!Plans || planning is null) return heuristic;
        var planner = new TurnPlanner(this, configuration, catalog);
        return planner.Applies(features) ? planner.Plan(features, heuristic, knowledge, planning) : heuristic;
    }

    public string Version { get; }
    private int Generation { get; }

    public void ValidateAction(AgentFeatures features, AgentAction action)
    {
        if (features.Side != PlayerSide.Corp) throw new ArgumentException("Corp policy requires the Corp observation.");
        bool valid = action.Kind switch
        {
            ActionKind.GainCredit or ActionKind.DrawCard or ActionKind.PassPriority or ActionKind.EndTurn => action.Parameters is NoActionParameters,
            ActionKind.PlayCard => action.Parameters is PlayCardParameters,
            ActionKind.InstallCard => action.Parameters is InstallCardParameters,
            ActionKind.RezCard => action.Parameters is RezCardParameters,
            ActionKind.RezIce => action.Parameters is RunIceParameters,
            ActionKind.AdvanceAgenda or ActionKind.AdvanceCard => action.Parameters is AdvanceCardParameters,
            ActionKind.ScoreAgenda => action.Parameters is ScoreAgendaParameters,
            ActionKind.TrashTaggedResource => action.Parameters is TrashTaggedResourceParameters,
            ActionKind.PurgeVirusCounters => action.Parameters is PurgeVirusCountersParameters,
            ActionKind.UseAbility => action.Parameters is AbilityActionParameters,
            ActionKind.ChooseBehavior => action.Parameters is BehaviorChoiceParameters,
            ActionKind.ChooseCard => action.Parameters is CardChoiceParameters,
            ActionKind.ChooseNumber => action.Parameters is NumberChoiceParameters,
            ActionKind.UseTrigger or ActionKind.DeclineTrigger => action.Parameters is TriggerChoiceParameters,
            ActionKind.OrderTriggers => action.Parameters is TriggerOrderParameters,
            ActionKind.ResolveIceSubroutine => action.Parameters is IceSubroutineChoiceParameters,
            ActionKind.DeclinePrevention or ActionKind.UsePrevention => action.Parameters is PreventionChoiceParameters,
            ActionKind.DeclineReplacement or ActionKind.UseReplacement => action.Parameters is ReplacementChoiceParameters,
            _ => false,
        };
        if (!valid) throw new ArgumentException($"Unsupported Corp action contract {action.Kind}/{action.Parameters.GetType().Name}.");
    }

    public AgentUtility Evaluate(AgentFeatures features, AgentAction action, AgentKnowledge knowledge, AgentObjective? objective,
        OfflineAgentConfiguration configuration, ICardEvaluationCatalog catalog, AgentWorkBudget budget)
    {
        ValidateAction(features, action);
        if (!budget.TrySpend(8 + features.VisibleCards().Count() * 2)) return Fallback(features, action) with { UsedFallback = true };
        var context = new HeuristicContext(features, catalog, configuration, Generation)
        {
            Knowledge = Generation >= 6 ? knowledge : null,
        };
        return action.Kind switch
        {
            ActionKind.GainCredit => Value(context.CreditValue + (context.Self.Credits < (Repairs ? ProtectionReserve(context) : context.RezReserve) ? 150 : 0),
                "economy/basic-credit-and-rez-reserve"),
            ActionKind.DrawCard => Value(features.Board.HiddenZoneCounts.CorpDeck > 1 ? context.DrawValue : -10000, "economy/draw-reserve"),
            ActionKind.PlayCard => Play(context, Require<PlayCardParameters>(action)),
            ActionKind.InstallCard => Install(context, Require<InstallCardParameters>(action), Repairs),
            ActionKind.RezCard => Rez(context, Require<RezCardParameters>(action).CardId, Require<RezCardParameters>(action).CreditCost, Repairs),
            ActionKind.RezIce => Rez(context, Require<RunIceParameters>(action).IceId, Require<RunIceParameters>(action).CostQuote?.EffectiveCost
                ?? context.Definition(Require<RunIceParameters>(action).IceId)?.Ice?.RezCost ?? 0, Repairs),
            ActionKind.AdvanceAgenda or ActionKind.AdvanceCard => Advance(context, Require<AdvanceCardParameters>(action), Repairs),
            ActionKind.ScoreAgenda => Score(context, Require<ScoreAgendaParameters>(action)),
            ActionKind.TrashTaggedResource => Generation >= 8
                ? TrashTagged(context, Require<TrashTaggedResourceParameters>(action))
                : Value(context.CardValue(context.Card(Require<TrashTaggedResourceParameters>(action).CardId), installed: true)
                    - Require<TrashTaggedResourceParameters>(action).CreditCost * context.CreditValue, "pressure/trash-tagged-resource"),
            ActionKind.PurgeVirusCounters => Purge(context, Require<PurgeVirusCountersParameters>(action)),
            ActionKind.UseAbility => Ability(context, Require<AbilityActionParameters>(action)),
            ActionKind.ChooseBehavior => ChooseBehavior(context, Require<BehaviorChoiceParameters>(action)),
            ActionKind.ChooseCard => Value(-context.CardValue(context.Card(Require<CardChoiceParameters>(action).CardId)), "forced-loss/retain-useful-cards"),
            ActionKind.ChooseNumber => Mulligan(context, Require<NumberChoiceParameters>(action)),
            ActionKind.UseTrigger or ActionKind.DeclineTrigger => Trigger(context, Require<TriggerChoiceParameters>(action).TriggerId, action.Kind == ActionKind.UseTrigger),
            ActionKind.OrderTriggers => Trigger(context, Require<TriggerOrderParameters>(action).TriggerId, true),
            ActionKind.ResolveIceSubroutine => Subroutine(context, Require<IceSubroutineChoiceParameters>(action)),
            ActionKind.PassPriority or ActionKind.EndTurn => RequireNone(action, 0, "timing/continue"),
            ActionKind.DeclinePrevention or ActionKind.DeclineReplacement => Value(0, "interrupt/decline"),
            ActionKind.UsePrevention => Value(100, "interrupt/prevent", approximate: true),
            ActionKind.UseReplacement => Value(50, "interrupt/replace", approximate: true),
            _ => throw new ArgumentException($"Unsupported Corp action contract {action.Kind}/{action.Parameters.GetType().Name}."),
        };
    }

    public AgentUtility Fallback(AgentFeatures features, AgentAction action) => action.Kind switch
    {
        ActionKind.ScoreAgenda when action.Parameters is ScoreAgendaParameters score => new(100, score.AgendaPoints, "fallback/score", true),
        ActionKind.PassPriority or ActionKind.DeclineTrigger or ActionKind.DeclinePrevention or ActionKind.DeclineReplacement => new(0, 100, "fallback/conservative-pass", true),
        ActionKind.GainCredit => new(0, 50, "fallback/basic-economy", true),
        ActionKind.EndTurn => new(0, 10, "fallback/finish-turn", true),
        _ => new(0, 0, "fallback/issued-choice", true),
    };

    private static AgentUtility Value(int score, string reason, bool approximate = false, AgentObjective? objective = null, bool fallback = false) => new(0, score, reason, approximate, objective, fallback);
    private static T Require<T>(AgentAction action) where T : ActionParameters => action.Parameters as T
        ?? throw new ArgumentException($"Malformed {action.Kind} action: expected {typeof(T).Name}.");
    private static AgentUtility RequireNone(AgentAction action, int score, string reason)
    {
        _ = Require<NoActionParameters>(action);
        return Value(score, reason);
    }

    private static AgentUtility Mulligan(HeuristicContext context, NumberChoiceParameters choice)
    {
        if (context.Features.Decision?.Kind != DecisionKind.Setup) return Value(-choice.Value, "choice/number", approximate: true);
        bool economy = context.Features.Hand.Any(card => context.Definition(card)?.Behavior is { } behavior
            && HeuristicContext.Flatten(behavior.Instructions).Any(instruction => instruction.Kind == BehaviorInstructionKind.GainCredits)
            && (context.Definition(card)!.Printed.PlayCost ?? 0) <= 5);
        bool protection = context.Features.Hand.Any(card => context.Definition(card)?.Ice is { RezCost: <= 5 });
        bool keep = economy && protection;
        return Value((choice.Value == 0) == keep ? 100 : 0, keep ? "opening/keep-economy-and-protection" : "opening/mulligan-missing-foundation");
    }
}

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
    /// <summary>The version new games and new manifests use.</summary>
    public const string PolicyVersion = V4;
    public static IReadOnlyList<string> Versions { get; } = [V2, V3, V4];

    public CorpHeuristicPolicy() : this(PolicyVersion) { }

    public CorpHeuristicPolicy(string version)
    {
        if (!Versions.Contains(version)) throw new ArgumentException($"Unknown Corp policy version '{version}'.", nameof(version));
        Version = version;
        Generation = version switch { V2 => 1, V3 => 2, _ => 3 };
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
        var context = new HeuristicContext(features, catalog, configuration, Generation);
        return action.Kind switch
        {
            ActionKind.GainCredit => Value(context.CreditValue + (context.Self.Credits < context.RezReserve ? 150 : 0), "economy/basic-credit-and-rez-reserve"),
            ActionKind.DrawCard => Value(features.Board.HiddenZoneCounts.CorpDeck > 1 ? context.DrawValue : -10000, "economy/draw-reserve"),
            ActionKind.PlayCard => Play(context, Require<PlayCardParameters>(action)),
            ActionKind.InstallCard => Install(context, Require<InstallCardParameters>(action)),
            ActionKind.RezCard => Rez(context, Require<RezCardParameters>(action).CardId, Require<RezCardParameters>(action).CreditCost),
            ActionKind.RezIce => Rez(context, Require<RunIceParameters>(action).IceId, Require<RunIceParameters>(action).CostQuote?.EffectiveCost
                ?? context.Definition(Require<RunIceParameters>(action).IceId)?.Ice?.RezCost ?? 0),
            ActionKind.AdvanceAgenda or ActionKind.AdvanceCard => Advance(context, Require<AdvanceCardParameters>(action)),
            ActionKind.ScoreAgenda => Score(context, Require<ScoreAgendaParameters>(action)),
            ActionKind.TrashTaggedResource => Value(context.CardValue(context.Card(Require<TrashTaggedResourceParameters>(action).CardId), installed: true)
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

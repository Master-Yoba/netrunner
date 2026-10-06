using Netrunner.Core;

namespace Netrunner.AI;

public sealed partial class RunnerHeuristicPolicy
{
    private static AgentUtility Trigger(HeuristicContext context, TriggerId id, bool use)
    {
        if (!use) return Value(0, "trigger/decline");
        AgentTrigger? offered = context.Features.PrivateCards.Triggers.SingleOrDefault(trigger => trigger.Id == id.Value);
        BehaviorTrigger? declaration = offered is null ? null : context.Definition(offered.Source)?.Behavior?.Triggers.SingleOrDefault(trigger => trigger.Key == offered.Key);
        if (declaration is null) return Value(-10, "trigger/unknown-context", true, true);
        int value = Effects(context, declaration.Instructions, offered!.Source, PlayerSide.Runner);
        if (HeuristicContext.Flatten(declaration.Instructions).Any(instruction => instruction.Kind == BehaviorInstructionKind.SwapInstalledIce))
            value = BestSwap(context, []) - 10;
        return Value(value, "trigger/visible-benefit", true);
    }

    private static AgentUtility ChooseBehavior(HeuristicContext context, BehaviorChoiceParameters choice)
    {
        BehaviorChoiceLookup.ChoicePlan? plan = BehaviorChoiceLookup.Find(context.Catalog, choice.ChoiceKey);
        if (plan is null) return Value(choice.FinalizeSelection ? 1 : 0, "choice/unmodelled-finalize", true, true);
        if (choice.OptionKeys.Count > 0)
            return Value(choice.OptionKeys.Sum(key =>
            {
                BehaviorChoiceOption option = plan.Choice!.Options.Single(option => option.Key == key);
                return Effects(context, option.Instructions, null, plan.Controller) - (option.Costs ?? []).Sum(cost => Cost(context, cost));
            }) - choice.CardIds.Sum(id => RunnerValues.Card(context, context.Card(id))), "choice/mode-and-cost", true);
        if (plan.Consumers.Any(instruction => instruction.Kind == BehaviorInstructionKind.SwapInstalledIce))
            return Value(BestSwap(context, choice.CardIds) + choice.CardIds.Count * 10 + (choice.FinalizeSelection ? 10 : 0), "choice/improve-server-protection-matchup", true);
        bool trash = plan.Consumers.Any(instruction => instruction.Kind == BehaviorInstructionKind.TrashCard);
        int value = choice.CardIds.Sum(id => (trash ? -1 : 1) * RunnerValues.Card(context, context.Card(id)));
        // v17 (batch 4's reviews, 1 October): an effect that installs a card (Pantograph, Mutual Favor) doesn't pick one that
        // would replace its own installed copy or a second console.
        if (plan.Consumers.Any(instruction => instruction.Kind == BehaviorInstructionKind.InstallCard))
            value -= choice.CardIds.Count(id => ReplacesItsOwn(context, context.Card(id))) * 500;
        // v20 (Wave 5 playtest, 2 October): a breaker a tutor finds is worth what it would break, as installing it is; Mutual
        // Favor took Carmen over Marjanah with two barriers on the board and no sentry, and R&D's barrier was never broken.
        // Measured against an ordinary new breaker, so a breaker keeps its rank against other kinds of cards.
        if (!trash)
            value += choice.CardIds.Sum(id => context.Definition(context.Card(id))?.Icebreaker is { } breaker
                ? BreakerCoverage(context, breaker) - 730 : 0);
        // v25 (bot loop 2): a card that only goes to the grip is a free draw at least, whatever installing it would add; MuslihaT's
        // free Marjanah was declined as a second copy, and the Runner spent a click drawing it.
        BehaviorInstruction[] uses = [.. plan.Consumers.Where(instruction => instruction.Target?.Selector == choice.ChoiceKey)];
        if (!trash && choice.CardIds.Count > 0 && uses.Length > 0
            && uses.All(instruction => instruction.Kind is BehaviorInstructionKind.AddToHand or BehaviorInstructionKind.Reveal))
            value = Math.Max(value, choice.CardIds.Count * context.DrawValue);
        return Value(value + (choice.FinalizeSelection ? 1 : 0), "choice/target-value-and-confirmation", true);
    }

    /// <summary>v17: whether installing this card would replace an installed copy of it (unique) or a second console.</summary>
    private static bool ReplacesItsOwn(HeuristicContext context, AgentCard? card)
    {
        CardEvaluationDescriptor? definition = context.Definition(card);
        if (card is null || definition is null) return false;
        return definition.Printed.Unique && context.Features.Board.RunnerRig.Any(installed => installed.DefinitionId == card.DefinitionId)
            || definition.Printed.Console && context.Features.Board.RunnerRig.Any(installed => context.Definition(installed)?.Printed.Console == true);
    }

    private static AgentUtility Subroutine(HeuristicContext context, IceSubroutineChoiceParameters choice) => choice.Choice switch
    {
        IceSubroutineChoiceKind.PayCredits => Value(-Math.Max(0, choice.CreditCost - choice.HostedCredits) * context.CreditValue - choice.HostedCredits * 15, "subroutine/issued-credit-payment"),
        IceSubroutineChoiceKind.TakeTag => Value(-RunnerValues.TagRisk(context), "subroutine/tag-exposure", true),
        IceSubroutineChoiceKind.JackOut => Value(0, "subroutine/exit"),
        IceSubroutineChoiceKind.ContinueRun => RemainingDamage(context) > context.Features.Hand.Length
            ? new(-100, 0, "subroutine/avoid-known-flatline") : Value(250, "subroutine/survive-and-continue", true),
        IceSubroutineChoiceKind.EndRun => Value(-Math.Max(300, RunnerValues.Server(context, RunnerValues.Attacked(context))), "subroutine/end-run", true),
        IceSubroutineChoiceKind.TrashInstalledRunnerCard => Value(-RunnerValues.Card(context, choice.CardId is { } id ? context.Card(id) : null), "subroutine/minimize-forced-loss", true),
        IceSubroutineChoiceKind.Decline => Value(0, "subroutine/decline"),
        _ => Value(-50, "subroutine/unmodelled", true, true),
    };

    private static int RemainingDamage(HeuristicContext context)
    {
        AgentEncounter? encounter = context.Features.Board.Run?.Encounter;
        IceBehaviorSpec? ice = context.Definition(encounter?.Ice)?.Ice;
        return encounter is null || ice is null ? 0 : encounter.Subroutines.Where(subroutine => !subroutine.Broken && !subroutine.Resolved && subroutine.Ordinal < ice.Subroutines.Count)
            .Sum(subroutine => ice.Subroutines[subroutine.Ordinal] is DamageSubroutineSpec damage ? damage.Amount : 0);
    }

    private static int BestSwap(HeuristicContext context, IReadOnlyList<CardInstanceId> selected)
    {
        AgentCard[] ice = context.Features.Board.Servers.SelectMany(server => server.Ice).OfType<AgentCard>().Where(card => context.Definition(card)?.Ice is not null).ToArray();
        int best = int.MinValue;
        for (int first = 0; first < ice.Length; first++)
            for (int second = first + 1; second < ice.Length; second++)
            {
                AgentCard a = ice[first], b = ice[second];
                if (selected.Any(id => id.Value != a.Id && id.Value != b.Id)) continue;
                AgentServer? from = context.ServerOf(a.Id), to = context.ServerOf(b.Id);
                int resistanceA = context.Definition(a)!.Ice!.Strength + context.Definition(a)!.Ice!.Subroutines.Count * 2;
                int resistanceB = context.Definition(b)!.Ice!.Strength + context.Definition(b)!.Ice!.Subroutines.Count * 2;
                int gain = (resistanceA - resistanceB) * (RunnerValues.Server(context, from) - RunnerValues.Server(context, to)) / 10;
                best = Math.Max(best, gain);
            }
        return best == int.MinValue ? -100 : best;
    }
}

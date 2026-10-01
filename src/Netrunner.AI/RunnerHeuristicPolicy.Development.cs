using Netrunner.Core;

namespace Netrunner.AI;

public sealed partial class RunnerHeuristicPolicy
{
    /// <summary>
    /// The credits a counter-growing card pays when cashed out after two of the Runner's turns: its counters from the
    /// install and two turn begins, times what its paid ability gains per counter.
    /// </summary>
    private static int GrowingIncome(CardEvaluationDescriptor definition)
    {
        if (definition.Behavior is not { } behavior) return 0;
        foreach (BehaviorPaidAbility ability in behavior.PaidAbilities)
        {
            foreach (BehaviorInstruction gain in ability.Instructions.Where(instruction => instruction.Kind == BehaviorInstructionKind.GainCredits
                && instruction.AmountOperand is not null))
            {
                string counter = gain.AmountOperand!.SourceCounterName;
                int counters = behavior.Triggers.Sum(trigger => HeuristicContext.Flatten(trigger.Instructions)
                    .Where(instruction => instruction.Kind == BehaviorInstructionKind.AddCounter && instruction.Argument == counter)
                    .Sum(instruction => instruction.Amount * (trigger.Occurrence == OccurrenceKind.TurnBegin ? 2 : 1)));
                if (counters > 0) return counters * gain.AmountOperand.Multiplier;
            }
        }
        return 0;
    }

    private static AgentUtility Install(HeuristicContext context, InstallCardParameters install)
    {
        AgentCard? card = context.Card(install.CardId);
        CardEvaluationDescriptor? definition = context.Definition(card);
        if (context.Features.Decision?.Kind == DecisionKind.InstallChoice)
        {
            int required = install.SelectedReplacementCardIds.Count(install.RequiredReplacementCardIds.Contains);
            int optional = install.SelectedReplacementCardIds.Where(id => !install.RequiredReplacementCardIds.Contains(id)).Sum(id => RunnerValues.Card(context, context.Card(id)));
            return new(20, (install.FinalizeSelection ? 1000 : required * 100) - optional, "install/complete-required-selection");
        }
        if (definition is null) return Value(-100, "install/unknown-metadata", true, true);
        int value;
        if (definition.Icebreaker is { } breaker)
        {
            bool duplicate = context.Features.Board.RunnerRig.Any(known => context.Definition(known)?.Icebreaker?.InterfaceType == breaker.InterfaceType);
            bool visibleThreat = context.Features.Board.Servers.SelectMany(server => server.Ice).Any(known => context.Definition(known)?.Ice is { } ice
                && (breaker.InterfaceType == IceType.Any || ice.IceType == breaker.InterfaceType));
            value = duplicate ? 150 : breaker.InterfaceType == IceType.Any ? 550 : 730;
            if (visibleThreat && !duplicate) value += 180;
        }
        else if (definition.Behavior?.Hosting is not null)
        {
            value = 250 + (install.HostCardId is { } host ? (context.Definition(host)?.Ice?.Subroutines.Count ?? 0) * 100 : 0);
        }
        else
        {
            int income = definition.Behavior?.Triggers.SelectMany(trigger => HeuristicContext.Flatten(trigger.Instructions))
                .Where(instruction => instruction.Kind == BehaviorInstructionKind.LoadHostedCredits).Sum(instruction => instruction.Amount) ?? 0;
            // v15 (reviews of the losses to Codex, 1 October): a card whose counters grow each turn and cash out for credits
            // (Fermenter) is income too, about two turns' worth; it was worth less than a click for a credit and never installed.
            if (context.Generation >= 11) income += GrowingIncome(definition);
            value = 180 + income * 45;
            // v6: a better basic draw pays off over the coming turns (Verbal Plasticity).
            if (context.Generation >= 5)
                value += (definition.Behavior?.FirstBasicDrawReplacements ?? []).Sum(replacement => (replacement.DrawCount - 1) * context.DrawValue * 3);
            if (definition.Printed.Console) value += 180;
            if (context.Self.UsedMemory >= context.Self.MemoryLimit && definition.Printed.CardClass == RuntimeCardClass.Hardware) value += 170;
        }
        if (context.Generation >= 8)
        {
            // v11: a setup deck builds its rig first; a virus deck values every virus.
            RunnerArchetypeProfile profile = context.RunnerProfile;
            bool rigPiece = definition.Icebreaker is not null || definition.Printed.CardClass is RuntimeCardClass.Hardware or RuntimeCardClass.Resource;
            if (rigPiece && !context.RigComplete) value += (int)(profile.SetupWeight * 150);
            if (definition.Printed.Subtypes.Any(subtype => string.Equals(subtype, "virus", StringComparison.OrdinalIgnoreCase)))
                value += (int)(profile.VirusWeight * 180);
        }
        int replacement = install.RequiredReplacementCardIds.Sum(id => RunnerValues.Card(context, context.Card(id)));
        return new(0, value - install.CreditCost * context.CreditValue - replacement, "rig/coverage-minus-issued-cost-and-replacement", true,
            new("install", null, null, install.CardId.Value));
    }

    private static AgentUtility Trash(HeuristicContext context, RunCardPaymentParameters payment)
    {
        AgentCard? card = context.Card(payment.CardId);
        int cost = context.Definition(card)?.Printed.TrashCost ?? context.Self.Credits;
        int personal = Math.Max(0, cost - payment.RunCredits);
        int impact = RunnerValues.Trash(context, card);
        if (context.Generation >= 8) impact = (int)(impact * context.RunnerProfile.TrashWeight);
        int value = impact - personal * context.CreditValue - payment.RunCredits * 15;
        return Value(value, "access/trash-value-and-reserve", true);
    }

    private static AgentUtility Access(HeuristicContext context, BreachCandidateParameters candidate)
    {
        if (candidate.CardId is not { } id) return Value(120, "access/unknown-card-without-identity-belief", true);
        AgentCard? card = context.Card(id);
        if (context.Definition(card)?.Agenda is { } agenda)
            return new(context.Self.AgendaPoints + agenda.AgendaPoints >= 7 ? 100 : 10, agenda.AgendaPoints * 100, "access/known-agenda");
        return Value(RunnerValues.Trash(context, card), "access/known-impact-first", true);
    }
}

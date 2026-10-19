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
                && instruction.AmountOperand is { SourceCounterName: not null }))
            {
                string counter = gain.AmountOperand!.SourceCounterName!;
                int counters = behavior.Triggers.Sum(trigger => HeuristicContext.Flatten(trigger.Instructions)
                    .Where(instruction => instruction.Kind == BehaviorInstructionKind.AddCounter && instruction.Argument == counter)
                    .Sum(instruction => instruction.Amount * (trigger.Occurrence == OccurrenceKind.TurnBegin ? 2 : 1)));
                if (counters > 0) return counters * gain.AmountOperand.Multiplier;
            }
        }
        return 0;
    }

    /// <summary>What a breaker adds to the rig: little for a second of a type, more against ICE of its type on the board.</summary>
    internal static int BreakerCoverage(HeuristicContext context, IcebreakerBehaviorSpec breaker)
    {
        bool duplicate = context.Features.Board.RunnerRig.Any(known => context.Definition(known)?.Icebreaker?.InterfaceType == breaker.InterfaceType);
        bool visibleThreat = context.Features.Board.Servers.SelectMany(server => server.Ice).Any(known => context.Definition(known)?.Ice is { } ice
            && (breaker.InterfaceType == IceType.Any || ice.IceType == breaker.InterfaceType));
        int value = duplicate ? 150 : breaker.InterfaceType == IceType.Any ? 550 : 730;
        if (visibleThreat && !duplicate) value += 180;
        return value;
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
        // v16 (reviews of the losses to Codex, 1 October): a unique card over its own copy pays for the same ability again.
        if (definition.Printed.Unique
            && context.Features.Board.RunnerRig.Any(installed => installed.DefinitionId == card!.DefinitionId))
            return Value(-500, "rig/unique-already-installed", true);
        // v17 (batch 4's reviews, 1 October): an install that replaces an installed copy of the same card pays for what the
        // rig already has (Verbal Plasticity, Pantograph, a unique program); one that replaces another card is judged below by
        // what it gains over what it trashes.
        if (install.RequiredReplacementCardIds.Concat(install.SelectedReplacementCardIds)
                .Any(id => context.Card(id)?.DefinitionId == card!.DefinitionId))
            return Value(-500, "rig/replaces-its-own-copy", true);
        // v24 (bot loop 1): a second copy of an installed breaker breaks nothing the first doesn't, for its memory and credits
        // (a second Marjanah, installed with the last click and credit).
        if (definition.Icebreaker is not null
            && context.Features.Board.RunnerRig.Any(installed => installed.DefinitionId == card!.DefinitionId))
            return Value(-500, "rig/breaker-already-installed", true);
        if (definition.Printed.Console
            && context.Features.Board.RunnerRig.Any(installed => context.Definition(installed)?.Printed.Console == true))
            return Value(-500, "rig/console-already-installed", true);
        int value;
        if (definition.Icebreaker is { } breaker)
        {
            value = BreakerCoverage(context, breaker);
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
            income += GrowingIncome(definition);
            value = 180 + income * 45;
            // v6: a better basic draw pays off over the coming turns (Verbal Plasticity).
            value += (definition.Behavior?.FirstBasicDrawReplacements ?? []).Sum(replacement => (replacement.DrawCount - 1) * context.DrawValue * 3);
            if (definition.Printed.Console) value += 180;
            // VP.5 (misplay sweep): a program whose click-run may become an HQ or R&D run (Baker) is worth a run a turn; it was
            // never installed.
            if (definition.Behavior?.Triggers.Any(trigger => trigger.Occurrence == OccurrenceKind.ServerWouldBeApproached) == true) value += 250;
            if (context.Self.UsedMemory >= context.Self.MemoryLimit && definition.Printed.CardClass == RuntimeCardClass.Hardware) value += 170;
        }
        // v11: a setup deck builds its rig first; a virus deck values every virus.
        RunnerArchetypeProfile profile = context.RunnerProfile;
        bool rigPiece = definition.Icebreaker is not null || definition.Printed.CardClass is RuntimeCardClass.Hardware or RuntimeCardClass.Resource;
        if (rigPiece && !context.RigComplete) value += (int)(profile.SetupWeight * 150);
        if (definition.Printed.Subtypes.Any(subtype => string.Equals(subtype, "virus", StringComparison.OrdinalIgnoreCase)))
            value += (int)(profile.VirusWeight * 180);
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
        impact = (int)(impact * context.RunnerProfile.TrashWeight);
        // v24 (bot loop 1): a known operation the Corp could play next turn to flatline the Runner is worth its trash cost
        // (Measured Response's 4 meat damage, left in HQ while the Runner had 2 cards and 4 credits).
        if (KillCard(context, card)) impact = Math.Max(impact, 1500);
        int value = impact - personal * context.CreditValue - payment.RunCredits * 15;
        return Value(value, "access/trash-value-and-reserve", true);
    }

    /// <summary>
    /// v24: whether the Corp can play this operation next turn for more damage than the Runner's grip can take, after the
    /// clicks it has left to draw, when the Runner can't pay its way out with its credits and clicks.
    /// </summary>
    private static bool KillCard(HeuristicContext context, AgentCard? card)
    {
        CardEvaluationDescriptor? definition = context.Definition(card);
        if (definition?.Printed.CardClass != RuntimeCardClass.Operation || definition.Behavior is not { } behavior) return false;
        if ((definition.Printed.PlayCost ?? 0) > context.Opponent.Credits) return false;
        int threat = Math.Max(context.Self.AgendaPoints, context.Opponent.AgendaPoints);
        if (behavior.Conditions.Any(condition => condition.Kind == BehaviorConditionKind.ThreatLevelAtLeast && threat < (condition.Value ?? 0)))
            return false;
        int payable = context.Self.Credits + context.Self.Clicks;
        return Damage(behavior.Instructions) > context.Features.Hand.Length + context.Self.Clicks;

        int Damage(IEnumerable<BehaviorInstruction> instructions) => instructions.Sum(instruction => instruction switch
        {
            { Kind: BehaviorInstructionKind.DealDamage } => instruction.Amount,
            { Kind: BehaviorInstructionKind.Choose, Choice: { Options.Count: > 0 } choice } when choice.Chooser == BehaviorChooser.Runner =>
                choice.Options.Where(option => (option.Costs ?? []).Where(cost => cost.Kind == BehaviorCostKind.Credits).Sum(cost => cost.Amount) <= payable)
                    .Select(option => Damage(option.Instructions)).DefaultIfEmpty(0).Min(),
            { Kind: BehaviorInstructionKind.Choose, Choice: { Options.Count: > 0 } choice } => choice.Options.Max(option => Damage(option.Instructions)),
            { Kind: BehaviorInstructionKind.Branch, Branch: { } branch } => Math.Max(Damage(branch.WhenTrue), Damage(branch.WhenFalse)),
            _ => 0,
        });
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

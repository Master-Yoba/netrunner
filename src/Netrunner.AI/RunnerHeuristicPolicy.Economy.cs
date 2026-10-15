using Netrunner.Core;

namespace Netrunner.AI;

public sealed partial class RunnerHeuristicPolicy
{
    private static AgentUtility Play(HeuristicContext context, PlayCardParameters play)
    {
        AgentCard? card = context.Card(play.CardId);
        CardEvaluationDescriptor? definition = context.Definition(card);
        if (definition?.RunEvent is { } runEvent)
        {
            // v6: raising ICE rez costs is worth the unrezzed ICE it keeps down (Tread Lightly).
            int rezDelta = (runEvent.RezCostModifiers ?? []).Sum(modifier => modifier.Delta);
            int kept = rezDelta <= 0 ? 0 : context.Server(play.TargetServerId)?.Ice
                .Count(ice => ice is { Rezzed: not true }) is int unrezzed and > 0
                    ? unrezzed * (context.Opponent.Credits < rezDelta + 4 ? 450 : context.Opponent.Credits < rezDelta + 7 ? 250 : 120) : 0;
            // v24 (bot loop 1): what the event's own successful-run trigger pays (Clean Getaway's 6 credits), counted when the
            // Runner can see the run succeed; a basic run was always preferred to it.
            int success = definition.Behavior?.Triggers
                .Where(trigger => trigger.Occurrence == OccurrenceKind.RunSuccessful && trigger.Scope == BehaviorTriggerScope.Self)
                .Sum(trigger => Effects(context, trigger.Instructions, card, PlayerSide.Runner)) ?? 0;
            AgentUtility utility = Run(context, context.Server(play.TargetServerId), runEvent.HostedCredits,
                (runEvent.BreachAccessModifier?.Delta ?? 0) * 140 + (runEvent.SuccessDraw?.DrawCount ?? 0) * context.DrawValue + kept,
                upfrontCost: play.CreditCost, knownSuccessValue: success, gripSpent: 1);
            return utility with { Reason = "run/event-benefit-and-issued-cost" };
        }
        if (definition?.Behavior is not { } behavior) return Value(-50, "play/unmodelled", true, true);
        // v24 (bot loop 1): a tutor for a breaker finds nothing the Runner needs once its rig and grip break every ICE type
        // (Mutual Favor, played twice to find nothing).
        if (TutorsOnlyBreakers(behavior) && BreakersCovered(context))
            return Value(-100, "play/tutor-finds-nothing-needed", true);
        return Value(Effects(context, behavior.Instructions, card, behavior.Controller) - play.CreditCost * context.CreditValue, "economy/event-net-payoff", true);
    }

    private static AgentUtility Ability(HeuristicContext context, AbilityActionParameters ability)
    {
        AgentCard? card = context.Card(ability.SourceCardId);
        BehaviorPaidAbility? declaration = context.Definition(card)?.Behavior?.PaidAbilities.SingleOrDefault(value => value.Key == ability.AbilityKey);
        if (declaration is null) return Value(-50, "ability/unmodelled", true, true);
        if (declaration.Instructions.FirstOrDefault(instruction => instruction.Kind == BehaviorInstructionKind.StartRun)?.RunOperand is { } run)
        {
            AgentServer? server = context.Features.Board.Servers.FirstOrDefault(server => server.Kind == run.ServerKind);
            int access = (run.BreachAccessDeltaPerCounter ?? 0) * (card?.Counters.Where(counter => counter.CounterName == run.SourceCounterName).Sum(counter => counter.Count) ?? 0);
            int payout = context.Definition(card)?.Behavior?.Triggers.Where(trigger => trigger.Occurrence == OccurrenceKind.RunEnded)
                .SelectMany(trigger => trigger.Instructions).Where(instruction => instruction.Kind == BehaviorInstructionKind.TakeHostedCredits)
                .Sum(instruction => Math.Min(instruction.Amount, HeuristicContext.Hosted(card))) ?? 0;
            int consumables = ability.Costs.Where(cost => cost.Kind == BehaviorCostKind.Counter).Sum(cost => cost.Amount * 50)
                + ability.AdditionalCostSelectionsOrEmpty.SelectMany(selection => selection.CardIds)
                    .Sum(id => RunnerValues.Card(context, context.Card(id)))
                + ability.Costs.Where(cost => cost.Kind is BehaviorCostKind.TrashSelf or BehaviorCostKind.RemoveSelfFromGame)
                    .Sum(_ => RunnerValues.Card(context, card));
            return Run(context, server, extraValue: access * 140 + payout * context.CreditValue
                - (access == 0 && payout == 0 ? 30 : 0) - consumables,
                upfrontCost: ability.Costs.Where(cost => cost.Kind == BehaviorCostKind.Credits).Sum(cost => cost.Amount));
        }
        int benefit = Effects(context, declaration.Instructions, card, PlayerSide.Runner);
        // v35 (bot plan H3 batch 11): hosting on a console whose cards don't play from the grip (Madani) only saves the
        // clicks of the programs past the first, which its once-a-turn free install brings out later.
        if (context.Generation >= HeuristicContext.ConsoleGeneration && context.Definition(card)?.Behavior is { HostedCardsPlayAsGrip: false }
            && HeuristicContext.Flatten(declaration.Instructions).Any(instruction => instruction.Kind == BehaviorInstructionKind.HostFaceUp))
            benefit = 100 * (context.Features.Hand.Count(held => context.Definition(held)?.Printed.CardClass == RuntimeCardClass.Program) - 1);
        // v19 (Codex review, 1 October): credits the ability puts in the wallet now are worth what as many basic credits are,
        // saving for a breaker included (Pennyshaver's 2 lost to a basic credit's 1). Only v19: it lost to Codex where v18
        // won, and v20 leaves it out.
        int cost = ability.Costs.Where(cost => cost.Kind == BehaviorCostKind.Credits).Sum(cost => cost.Amount) * context.CreditValue;
        cost += ability.AdditionalCostSelectionsOrEmpty.SelectMany(selection => selection.CardIds).Sum(id => RunnerValues.Card(context, context.Card(id)));
        cost += ability.Costs.Where(cost => cost.Kind is BehaviorCostKind.TrashSelf or BehaviorCostKind.RemoveSelfFromGame)
            .Sum(_ => RunnerValues.Card(context, card));
        return Value(benefit - cost, "ability/benefit-minus-issued-additional-costs", true);
    }

    // v23: past any other cost, so a choice that flatlines the Runner is never taken while another isn't.
    private const int Flatline = 100_000;

    /// <param name="gripSpent">Cards an enclosing effect has already installed from the grip (v23's damage check).</param>
    private static int Effects(HeuristicContext context, IEnumerable<BehaviorInstruction> instructions, AgentCard? source, PlayerSide controller,
        int gripSpent = 0)
    {
        int total = 0;
        // v23: what each choice from the grip may take, so the install or hosting it feeds is worth nothing when nothing matches.
        var gripChoices = new Dictionary<string, BehaviorCardFilter?>(StringComparer.Ordinal);
        foreach (BehaviorInstruction instruction in instructions)
        {
            if (instruction.Choice is { Target: { Zone: BehaviorZone.Grip } choiceTarget } gripChoice)
                gripChoices[gripChoice.Key] = choiceTarget.Filter;
            bool self = controller == PlayerSide.Runner;
            if (instruction.Recipient is BehaviorPlayerSelector.Corp or BehaviorPlayerSelector.Runner)
                self = instruction.Recipient == BehaviorPlayerSelector.Runner;
            int sign = self ? 1 : -1;
            int amount = HeuristicContext.EffectAmount(context, instruction, source);
            switch (instruction.Kind)
            {
                // Elevation (EL.3): counters, faces, tags, sabotage and additional access.
                case BehaviorInstructionKind.RemoveCounter: total -= amount * 50; break;
                case BehaviorInstructionKind.RemoveTags: total += amount * 200; break;
                // Vantage Point (VP.3): the Corp's bad publicity is a credit on every later run.
                case BehaviorInstructionKind.TakeBadPublicity: total += amount * 200; break;
                case BehaviorInstructionKind.RemoveBadPublicity:
                    total -= Math.Min(amount, (context.Features.Board.Players.Corp.BadPublicity ?? 0)) * 200; break;
                case BehaviorInstructionKind.FlipSelf: total += 50; break;
                case BehaviorInstructionKind.GainAllottedClicksNextTurn: total += sign * amount * 90; break;
                case BehaviorInstructionKind.Sabotage: total += amount * 80; break;
                case BehaviorInstructionKind.AddBreachAccess: total += amount * 150; break;
                case BehaviorInstructionKind.RefillRecurringCredits: total += 2 * context.CreditValue; break;
                case BehaviorInstructionKind.ForfeitAgenda: total -= 600; break;
                // Elevation (EL.5): a card hosted faceup on a console is one more card to play or install;
                // a nested play is the played card's own value, roughly a good event.
                // v23 (bot loop 1): Madani hosted nothing 65 times in a game with no program in the grip.
                case BehaviorInstructionKind.HostFaceUp when instruction.Target?.Selector is { } hosted
                    && gripChoices.TryGetValue(hosted, out BehaviorCardFilter? hostFilter) && !GripHolds(context, hostFilter, install: false):
                    break;
                case BehaviorInstructionKind.HostFaceUp: total += 100; break;
                case BehaviorInstructionKind.PlayCard: total += 250; break;
                // Elevation (EL.7): runs and ICE. A bypass passes the ICE untouched; a derez costs the
                // Corp its rez; a steal outside a run is worth the agenda; a mod is lasting strength.
                case BehaviorInstructionKind.BypassEncounteredIce: total += 300; break;
                case BehaviorInstructionKind.Derez: total += 200; break;
                case BehaviorInstructionKind.StealAgenda: total += 400; break;
                case BehaviorInstructionKind.HostSourceOnTarget: total += 80; break;
                case BehaviorInstructionKind.GainCredits: total += sign * amount * context.CreditValue; break;
                // v14 (Codex review, 1 October): Zahya pays a credit a card the ended run accessed; it scored 0 and was declined.
                case BehaviorInstructionKind.GainCreditsPerAccess:
                    total += sign * (context.Features.Board.RunEnd?.AccessOccurrenceCount ?? 0) * context.CreditValue;
                    break;
                case BehaviorInstructionKind.LoseCredits: total -= sign * amount * context.CreditValue; break;
                case BehaviorInstructionKind.GainClicks: total += sign * amount * 100; break;
                case BehaviorInstructionKind.LoseClicks: total -= sign * amount * 100; break;
                case BehaviorInstructionKind.Draw: total += sign * amount * context.DrawValue; break;
                case BehaviorInstructionKind.LoadHostedCredits:
                case BehaviorInstructionKind.PlaceHostedCredits:
                    // v4: a card that already holds a stockpile pays it out slowly (Smartware
                    // Distributor gives 1 a turn), so topping it up is worth far less than face value.
                    total += sign * amount * (HeuristicContext.Hosted(source) >= amount ? 12 : 50);
                    break;
                case BehaviorInstructionKind.TakeHostedCredits:
                case BehaviorInstructionKind.TakeUpToHostedCredits: total += sign * Math.Min(amount, HeuristicContext.Hosted(source)) * context.CreditValue; break;
                case BehaviorInstructionKind.TakeAllHostedCredits: total += sign * HeuristicContext.Hosted(source) * context.CreditValue; break;
                case BehaviorInstructionKind.AddCounter: total += sign * amount * 70; break;
                case BehaviorInstructionKind.Search: total += 220; break;
                case BehaviorInstructionKind.AddToHand: total += 180; break;
                // v23 (bot loop 1): Topan spent a click installing from a grip of events.
                case BehaviorInstructionKind.InstallCard when instruction.SourceZone == BehaviorZone.Grip
                    && !GripHolds(context, instruction.Target?.Filter, install: true, discount: instruction.Amount):
                    break;
                case BehaviorInstructionKind.InstallCard:
                    total += 350;
                    if (instruction.SourceZone == BehaviorZone.Grip) gripSpent++;
                    break;
                // v23 (bot loop 1, 3 October): damage is always the Runner's. It costs a card a point; more than the grip
                // holds once this effect's installs from it are made is a flatline (Topan installing its last card,
                // Semak-samun's 3 net or Measured Response's 4 meat with too few cards).
                case BehaviorInstructionKind.DealDamage:
                    total -= amount > context.Features.Hand.Length - gripSpent ? Flatline : amount * context.DrawValue;
                    break;
                case BehaviorInstructionKind.TrashSelf: total -= RunnerValues.Card(context, source); break;
                case BehaviorInstructionKind.TrashCard:
                    total += context.Features.Board.Run?.Breach.Chosen.Where(card => card is not null).Select(card => RunnerValues.Trash(context, card)).DefaultIfEmpty(0).Max() ?? 0;
                    break;
                case BehaviorInstructionKind.SwapInstalledIce: total += BestSwap(context, []); break;
                case BehaviorInstructionKind.EndRun or BehaviorInstructionKind.JackOut: total -= Math.Max(300, RunnerValues.Server(context, RunnerValues.Attacked(context))); break;
                case BehaviorInstructionKind.AddTags: total -= RunnerValues.TagRisk(context, amount); break;
                // v23: a card choice from a grip that holds nothing it takes chooses nothing (Madani's empty hosting).
                case BehaviorInstructionKind.Choose when instruction.Choice is { Options.Count: 0, Target: { Zone: BehaviorZone.Grip } emptyTarget }
                    && !GripHolds(context, emptyTarget.Filter, install: false):
                    break;
                case BehaviorInstructionKind.Choose:
                    total += instruction.Choice is { Options.Count: > 0 } choice
                        ? choice.Options.Max(option => Effects(context, option.Instructions, source, controller, gripSpent) - (option.Costs ?? []).Sum(cost => Cost(context, cost)))
                        : 180;
                    break;
                case BehaviorInstructionKind.Branch:
                    if (instruction.Branch is { } branch)
                    {
                        bool condition = branch.Condition.Kind switch
                        {
                            BehaviorConditionKind.SuccessfulRunThisTurn => context.Features.Board.TurnFacts?.Runner.ThisTurn?.SuccessfulRun == true,
                            BehaviorConditionKind.Tagged => context.Self.Tags > 0,
                            BehaviorConditionKind.NotTagged => context.Self.Tags == 0,
                            _ => true,
                        };
                        total += Effects(context, condition ? branch.WhenTrue : branch.WhenFalse, source, controller, gripSpent);
                    }
                    break;
            }
        }
        return total;
    }

    /// <summary>v24: whether every search the effect makes looks for an icebreaker.</summary>
    private static bool TutorsOnlyBreakers(CardBehaviorDefinition behavior)
    {
        BehaviorInstruction[] searches = [.. HeuristicContext.Flatten(behavior.Instructions).Where(instruction => instruction.Kind == BehaviorInstructionKind.Search)];
        return searches.Length > 0 && searches.All(search => search.Target?.Filter?.CardType == BehaviorCardType.Icebreaker);
    }

    /// <summary>v24: whether the installed breakers and those in the grip break barriers, code gates and sentries.</summary>
    private static bool BreakersCovered(HeuristicContext context)
    {
        IceType[] types = [.. context.Features.Board.RunnerRig.Concat(context.Features.Hand)
            .Select(card => context.Definition(card)?.Icebreaker?.InterfaceType).OfType<IceType>().Distinct()];
        return types.Contains(IceType.Any) || new[] { IceType.Barrier, IceType.CodeGate, IceType.Sentry }.All(types.Contains);
    }

    /// <summary>
    /// v23: whether the grip holds a card a filter takes; an install never takes an event. v25 (bot loop 2): nor a card the
    /// Runner can't pay for after the discount, or one it wouldn't install (Topan spent clicks installing nothing with 1
    /// credit, and replaced its own Verbal Plasticity).
    /// </summary>
    private static bool GripHolds(HeuristicContext context, BehaviorCardFilter? filter, bool install, int discount = 0) =>
        context.Features.Hand.Any(card => context.Definition(card) is { } definition
            && !(install && definition.Printed.CardClass == RuntimeCardClass.Event)
            && !(install && (Math.Max(0, (definition.Printed.InstallCost ?? 0) - discount) > context.Self.Credits
                || ReplacesItsOwn(context, card) || definition.Icebreaker is not null
                    && context.Features.Board.RunnerRig.Any(installed => installed.DefinitionId == card.DefinitionId)))
            && (filter?.CardType ?? BehaviorCardType.Any) switch
            {
                BehaviorCardType.Any => true,
                BehaviorCardType.Event => definition.Printed.CardClass == RuntimeCardClass.Event,
                BehaviorCardType.Hardware => definition.Printed.CardClass == RuntimeCardClass.Hardware,
                BehaviorCardType.Resource => definition.Printed.CardClass == RuntimeCardClass.Resource,
                BehaviorCardType.Program or BehaviorCardType.Icebreaker => definition.Printed.CardClass == RuntimeCardClass.Program,
                _ => true,
            });

    private static int Cost(HeuristicContext context, BehaviorCost cost) => cost.Kind switch
    {
        BehaviorCostKind.Credits => cost.Amount * context.CreditValue,
        BehaviorCostKind.Clicks or BehaviorCostKind.LoseClick => cost.Amount * 100,
        BehaviorCostKind.Tags => RunnerValues.TagRisk(context, cost.Amount),
        BehaviorCostKind.Counter => cost.Amount * 50,
        _ => 0,
    };
}

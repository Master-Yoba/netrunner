using Netrunner.Core;

namespace Netrunner.AI;

public sealed partial class CorpHeuristicPolicy
{
    private static AgentUtility Trigger(HeuristicContext context, TriggerId id, bool use)
    {
        if (!use) return Value(0, "trigger/decline");
        AgentTrigger? offered = context.Features.PrivateCards.Triggers.SingleOrDefault(trigger => trigger.Id == id.Value);
        BehaviorTrigger? declaration = offered is null ? null : context.Definition(offered.Source)?.Behavior?.Triggers.SingleOrDefault(trigger => trigger.Key == offered.Key);
        if (declaration is null) return Value(-10, "trigger/unknown-context", approximate: true, fallback: true);
        return Value(10 + Effects(context, declaration.Instructions, offered!.Source, context.Features.Side), "trigger/typed-benefit", approximate: true);
    }

    private static AgentUtility Subroutine(HeuristicContext context, IceSubroutineChoiceParameters choice)
    {
        if (choice.Choice == IceSubroutineChoiceKind.Decline) return Value(0, "subroutine/decline");
        if (choice.Choice == IceSubroutineChoiceKind.TrashInstalledRunnerCard && choice.CardId is { } target)
            return Value(context.CardValue(context.Card(target), installed: true), "subroutine/remove-coverage", approximate: true);
        if (choice.Choice == IceSubroutineChoiceKind.InstallCard && choice.CardId is { } incoming)
        {
            CardEvaluationDescriptor? definition = context.Definition(incoming);
            AgentServer? server = context.Server(choice.ServerId);
            bool exposingWinningHq = context.Opponent.AgendaPoints >= 5 && context.AgendaCount > 0
                && context.Features.Board.Run?.AttackedServer == context.Features.Board.Servers.Single(value => value.Kind == ServerKind.Headquarters).Id
                && definition?.Ice is { } ice && ice.RezCost > context.Self.Credits;
            if (context.Generation >= 12 && definition is { Ice: null })
                return Value(RootInstallBySubroutine(context, server, definition) - choice.CreditCost * context.CreditValue,
                    "subroutine/root-install-minus-displaced", approximate: true);
            int value = exposingWinningHq ? -1000 : context.ServerValue(server) / ((server?.Ice.Length ?? 0) + 1)
                - choice.CreditCost * context.CreditValue - Math.Max(0, (definition?.Printed.RezCost ?? 0) - context.Self.Credits) * 100;
            return Value(value, "subroutine/install-protection-and-reserve", approximate: true);
        }
        return Value(0, "subroutine/issued-continuation", approximate: true);
    }

    /// <summary>
    /// v17 (playtest, 30 September): a card installed by an ICE subroutine into a server's root
    /// (Ansel 1.0) trashes the agenda or asset already there, and earlier versions valued it like ICE
    /// protecting that server, so the Corp trashed its own agenda in a remote the Runner was running.
    /// That trash is worth it only to deny a steal the Runner can't make from Archives this turn (no
    /// click left, or Archives' ICE beyond their credits), or to hide the agenda for a recovery card (v16).
    /// </summary>
    private static int RootInstallBySubroutine(HeuristicContext context, AgentServer? server, CardEvaluationDescriptor definition)
    {
        // An agenda goes in only behind ICE; in a new or open remote the Runner simply takes it.
        int gain = definition.Agenda is not null ? ((server?.Ice.Length ?? 0) > 0 ? 200 : -400)
            : definition.Printed.CardClass == RuntimeCardClass.Asset ? 160 : 100;
        if (server is not { Kind: ServerKind.Remote }) return gain;
        int displaced = DisplacedRootValue(context, server, definition);
        bool stealDenied = context.Features.Board.Run?.AttackedServer == server.Id && !RunnerCanReachArchivesThisTurn(context)
            && server.Root.Any(card => context.Definition(card)?.Agenda is not null);
        if (stealDenied)
            displaced = server.Root.Sum(card => context.Definition(card)?.Agenda is { } agenda ? 100 - agenda.AgendaPoints * 250 : 0);
        return gain - displaced;
    }

    /// <summary>
    /// Whether the Runner could still take a trashed agenda from Archives this turn: a click left for
    /// the run, and the credits to get through Archives' ICE as the run model sees it, with the Corp
    /// rezzing what it can pay for (the playtester: trashing the agenda also pays when Archives holds
    /// and the Runner is short of money, even with a click left).
    /// </summary>
    private static bool RunnerCanReachArchivesThisTurn(HeuristicContext context)
    {
        if (context.Opponent.Clicks < 1) return false;
        AgentServer? archives = context.Features.Board.Servers.FirstOrDefault(server => server.Kind == ServerKind.Archives);
        if (archives is null) return true;
        RunForecast forecast = RunModel.Plan(context, archives, archives.Ice.Length - 1, context.Opponent.Credits, context.Opponent.Clicks - 1,
            grip: context.Features.Board.HiddenZoneCounts.RunnerGrip, corpRezBudget: context.Self.Credits);
        return forecast.CanGetIn;
    }

    private static AgentUtility ChooseBehavior(HeuristicContext context, BehaviorChoiceParameters choice)
    {
        BehaviorChoiceLookup.ChoicePlan? plan = BehaviorChoiceLookup.Find(context.Catalog, choice.ChoiceKey);
        if (plan is null) return Value(choice.FinalizeSelection ? 1 : 0, "choice/unmodelled-finalize", approximate: true, fallback: true);
        if (choice.OptionKeys.Count > 0)
        {
            int value = choice.OptionKeys.Sum(key =>
            {
                BehaviorChoiceOption option = plan.Choice!.Options.Single(candidate => candidate.Key == key);
                return Effects(context, option.Instructions, null, plan.Controller)
                    - (option.Costs ?? []).Where(cost => cost.Kind == BehaviorCostKind.Credits).Sum(cost => cost.Amount) * context.CreditValue;
            });
            value -= choice.CardIds.Sum(id => context.CardValue(context.Card(id)));
            return Value(value, "choice/mode-and-additional-cost", approximate: true);
        }
        BehaviorInstructionKind intent = plan.Intent ?? plan.Consumers.FirstOrDefault(instruction => instruction.Kind is BehaviorInstructionKind.TrashCard
            or BehaviorInstructionKind.AdvanceCard or BehaviorInstructionKind.RezIgnoringCosts or BehaviorInstructionKind.AddToHand
            or BehaviorInstructionKind.ShuffleIntoDeck)?.Kind ?? BehaviorInstructionKind.AddToHand;
        int score = choice.CardIds.Sum(id => TargetValue(context, id, intent));
        return Value(score + (choice.FinalizeSelection ? 1 : 0), "choice/target-value-and-confirmation", approximate: true);
    }

    private static int TargetValue(HeuristicContext context, CardInstanceId id, BehaviorInstructionKind intent)
    {
        AgentCard? card = context.Card(id);
        CardEvaluationDescriptor? definition = context.Definition(card);
        return intent switch
        {
            BehaviorInstructionKind.TrashCard => -context.CardValue(card),
            BehaviorInstructionKind.AdvanceCard => definition?.Agenda is not null ? 300 + Math.Min(2, Math.Max(0,
                definition.Printed.AdvancementRequirement - (card?.AdvancementCounters ?? 0))) * 100 : 10,
            BehaviorInstructionKind.RezIgnoringCosts => context.ServerValue(context.ServerOf(id.Value)) + (definition?.Printed.RezCost ?? 0) * 80,
            BehaviorInstructionKind.ShuffleIntoDeck => context.CardValue(card) + (definition?.Agenda is not null ? 200 : 0),
            _ => context.CardValue(card),
        };
    }

}

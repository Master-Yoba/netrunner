using Netrunner.AI;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>
/// Which of the player's trigger decisions the client resolves for them
/// (playtest, 29 September): an optional trigger whose effect only gains
/// credits, counters, clicks or cards, with nothing to pay, target or choose
/// (Pennyshaver, Conduit, Zahya), is used; and when every trigger waiting to
/// be ordered is such a trigger, their order can't matter and the first goes.
/// A trigger with a cost, a choice or a target, or one that harms, stays the
/// player's decision.
/// </summary>
public static class BeneficialTriggers
{
    private static readonly HashSet<BehaviorInstructionKind> Benefits =
    [
        BehaviorInstructionKind.GainCredits, BehaviorInstructionKind.GainClicks, BehaviorInstructionKind.AddCounter,
        BehaviorInstructionKind.PlaceHostedCredits, BehaviorInstructionKind.LoadHostedCredits, BehaviorInstructionKind.Draw,
        // Credits taken from the player's own card (Red Team) are a pure gain.
        BehaviorInstructionKind.TakeHostedCredits,
    ];

    /// <summary>The action to take for the player, or null when the decision is theirs to make.</summary>
    public static ActionId? AutoAction(PendingDecision pending, AgentFeatures features, ICardEvaluationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(catalog);
        switch (pending.Kind)
        {
            case DecisionKind.TriggerChoice:
                LegalAction? use = pending.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.UseTrigger
                    && action.Parameters is TriggerChoiceParameters parameters && IsBeneficial(parameters.TriggerId.Value, features, catalog));
                return use?.Id;
            case DecisionKind.TriggerOrdering:
                LegalAction[] orders = [.. pending.LegalActions.Where(action => action.Kind == ActionKind.OrderTriggers)];
                if (orders.Length == 0 || orders.Length != pending.LegalActions.Count) return null;
                return orders.All(action => action.Parameters is TriggerOrderParameters parameters
                    && IsBeneficial(parameters.TriggerId.Value, features, catalog)) ? orders[0].Id : null;
            default:
                return null;
        }
    }

    public static bool IsBeneficial(string triggerId, AgentFeatures features, ICardEvaluationCatalog catalog)
    {
        AgentTrigger? offered = features.PrivateCards.Triggers.FirstOrDefault(trigger => trigger.Id == triggerId);
        if (offered is null || !catalog.TryGet(new CardDefinitionId(offered.Source.DefinitionId), out CardEvaluationDescriptor card))
            return false;
        BehaviorTrigger? declaration = card.Behavior?.Triggers.FirstOrDefault(trigger => trigger.Key == offered.Key);
        if (declaration is null || declaration.Instructions.Count == 0) return false;
        // Every step gains something for the player or its own card; nothing goes to the opponent.
        // A per-access gain (Zahya) counts only after a run that accessed something: at zero it
        // would spend a once-per-turn use for nothing (playtest, 29 September).
        return declaration.Instructions.All(instruction => (Benefits.Contains(instruction.Kind)
                || instruction.Kind == BehaviorInstructionKind.GainCreditsPerAccess && features.Board.RunEnd is { AccessOccurrenceCount: > 0 })
            && instruction.Choice is null && instruction.Branch is null
            && (instruction.Target is null || instruction.Target.Kind is BehaviorTargetKind.Self or BehaviorTargetKind.Controller));
    }
}

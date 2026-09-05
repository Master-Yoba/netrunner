namespace Netrunner.Core;

/// <summary>Pure provenance for a hosted-credit ability whose empty-pool reaction trashes its source.</summary>
internal static class AbilityLifecycleRules
{
    internal static bool IsEmptyPoolSelfTrash(
        CardBehaviorDefinition? definition, BehaviorPaidAbility? ability, AbilityFrame frame,
        CardInstanceState source, ZoneState? currentZone, RulesState? rules, TurnState turn) =>
        frame.Stage is AbilityLifecycleStage.AwaitingChild or AbilityLifecycleStage.Complete
        && source.Id == frame.SourceCardId && source.DefinitionId == frame.DefinitionId
        && definition?.DefinitionId == frame.DefinitionId && ability?.Key == frame.AbilityKey
        && currentZone?.Kind == (source.OwnerSide == PlayerSide.Runner ? ZoneKind.RunnerHeap : ZoneKind.CorpDiscard)
        && rules is not null && !rules.Counters.Any(counter => counter.CardId == source.Id && counter.CounterName == "credit")
        && turn.History?.ThisTurn(turn.ActiveSide)?.TrashedCardOccurrenceIds.Contains(source.Id) == true
        && ability.Instructions.Count == 1 && ability.Instructions[0].Kind == BehaviorInstructionKind.TakeHostedCredits
        && definition.Triggers.Any(trigger => trigger.Occurrence == OccurrenceKind.HostedCreditPoolEmptied
            && trigger.Scope == BehaviorTriggerScope.Self && trigger.Mode == TriggerMode.Mandatory
            && trigger.Instructions.Count == 1 && trigger.Instructions[0].Kind == BehaviorInstructionKind.TrashSelf);
}

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

    /// <summary>
    /// EL-NESTED-PLAY: an ability that plays its own source (Petty Cash from
    /// Archives) lets the source move to its play area, discard or
    /// removed-from-game zone once its costs are paid.
    /// </summary>
    /// <summary>
    /// VP-OPPONENT-ABILITY: an ability whose effect trashes its own source (Rotary: "Trash this hardware") finishes with
    /// the source in its owner's trash.
    /// </summary>
    internal static bool IsSelfTrashEffect(BehaviorPaidAbility? ability, AbilityFrame frame, CardInstanceState source, ZoneState? currentZone) =>
        frame.Stage is AbilityLifecycleStage.AwaitingChild or AbilityLifecycleStage.Complete
        && source.Id == frame.SourceCardId
        && ability?.Key == frame.AbilityKey
        && ability.Instructions.Any(static instruction => instruction.Kind == BehaviorInstructionKind.TrashSelf)
        && currentZone?.Kind == (source.OwnerSide == PlayerSide.Corp ? ZoneKind.CorpDiscard : ZoneKind.RunnerHeap);

    internal static bool IsSelfPlay(BehaviorPaidAbility? ability, AbilityFrame frame, CardInstanceState source, ZoneState? currentZone) =>
        frame.Stage is AbilityLifecycleStage.AwaitingChild or AbilityLifecycleStage.Complete
        && source.Id == frame.SourceCardId
        && ability?.Key == frame.AbilityKey
        && ability.Instructions.Any(static instruction => instruction.Kind == BehaviorInstructionKind.PlayCard
            && instruction.Target is { Kind: BehaviorTargetKind.Self })
        && currentZone?.Kind is ZoneKind.CorpPlayArea or ZoneKind.RunnerPlayArea or ZoneKind.CorpDiscard or ZoneKind.RunnerHeap
            or ZoneKind.CorpRemovedFromGame or ZoneKind.RunnerRemovedFromGame;
}

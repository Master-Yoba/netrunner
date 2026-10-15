namespace Netrunner.Core;

public static partial class StateInvariantValidator
{
    private static void ValidatePersistentRuns(GameState state, IBehaviorCatalog? catalog, List<StateInvariantViolation> violations)
    {
        if (state.RunState is { AccessedCards.Count: > 0, PersistentContext: null })
            Add(violations, "run.missing-access-context", "runState.persistentContext", "Accessed runs must retain their frozen access count.");
        if (state.RunState?.PersistentContext is { } live)
        {
            ValidatePersistentContext(state, live, catalog, violations);
            // AccessedCards appends each occurrence and preserves repeats; it
            // is an ordered access ledger, not a distinct-card projection.
            if (live.AccessOccurrenceCount != state.RunState.AccessedCards.Count
                || live.RunId != state.RunState.Id || live.ServerId != state.RunState.AttackedServer
                || live.StolenAgendas.Any(id => !state.RunState.AccessedCards.Contains(id))
                || live.Abilities.Any(value => !state.RunState.AccessedCards.Contains(value.SourceCardId)))
                Add(violations, "run.persistent-context", "runState.persistentContext", "Persistent facts must belong to this run.");
        }
        foreach (ResolutionFrame frame in state.ResolutionStack)
        {
            IEnumerable<GameplayOccurrenceEffect> occurrences = frame switch
            {
                EffectResolutionFrame effects => effects.Effects.Instructions.OfType<GameplayOccurrenceEffect>(),
                EffectCheckpointFrame checkpoint => new[] { checkpoint.Occurrence.Effect }.OfType<GameplayOccurrenceEffect>(),
                _ => [],
            };
            foreach (GameplayOccurrenceEffect occurrence in occurrences)
            {
                if (occurrence.RunContext is not { } context) continue;
                ValidatePersistentContext(state, context, catalog, violations);
                if (occurrence.Kind != OccurrenceKind.RunEnded || occurrence.ServerId != context.ServerId)
                    Add(violations, "resolution.persistent-context", "resolutionStack", "Run-end facts must match the occurrence server.");
            }
        }
    }

    private static void ValidatePersistentContext(GameState state, RunPersistentContext context,
        IBehaviorCatalog? catalog, List<StateInvariantViolation> violations)
    {
        foreach (CardInstanceId agenda in context.StolenAgendas)
        {
            CardInstanceState? card = state.Cards.FirstOrDefault(value => value.Id == agenda);
            if (card is null || card.OwnerSide != PlayerSide.Corp
                || catalog is not null && !catalog.TryGetAgenda(card.DefinitionId, out _))
                Add(violations, "run.stolen-agenda", "runPersistentContext.stolenAgendas", "Stolen facts require registered Corp agendas.");
        }
        foreach (PersistentRunAbility retained in context.Abilities)
        {
            CardInstanceState? source = state.Cards.FirstOrDefault(value => value.Id == retained.SourceCardId);
            ZoneState? zone = state.Zones.FirstOrDefault(value => value.Id == retained.SourceZoneId);
            if (source is null || source.DefinitionId != retained.DefinitionId || source.OwnerSide != PlayerSide.Corp
                || zone?.Kind != ZoneKind.ServerRoot || zone.ServerId != context.ServerId
                || catalog is not null && (!catalog.TryGet(retained.DefinitionId, out CardBehaviorDefinition? definition)
                    || !(definition.Triggers.Any(trigger => trigger.Key == retained.TriggerKey && trigger.Persistent
                            && trigger.Occurrence == OccurrenceKind.RunEnded)
                        || string.Equals(retained.TriggerKey, GameEngine.RootAssetTrashCostKey, StringComparison.Ordinal)
                            && definition.RootAssetTrashCostIncrease > 0)))
                Add(violations, "run.persistent-source", "runPersistentContext.abilities", "Persistent source provenance must match its installed object and catalog declaration.");
        }
        // VP-RETURN-AT-RUN-END: a remembered card and the source that remembered it are cards of the game.
        foreach (RunRememberedCard remembered in context.RememberedCards ?? [])
        {
            if (!state.Cards.Any(value => value.Id == remembered.SourceCardId) || !state.Cards.Any(value => value.Id == remembered.CardId))
                Add(violations, "run.remembered-card", "runPersistentContext.rememberedCards", "A remembered card and its source must exist.");
        }
    }
}

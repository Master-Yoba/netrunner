namespace Netrunner.Core;

public static partial class GameEngine
{
    private static RunState RetainAccessTrashedAbilities(WorkingState working, RunState run, CardInstanceState card)
    {
        // CR 9.12.5: only rezzed cards trashed while being accessed persist.
        // Uninstalling, derezzing, or trashing an unrelated object does not arm this path.
        ZoneState zone = ZoneForCard(working, card.Id);
        if (!card.Rezzed || zone.ServerId != run.AttackedServer
            || !working.BehaviorCatalog.TryGet(card.DefinitionId, out CardBehaviorDefinition? definition))
            return run;
        BehaviorTrigger[] triggers = definition.Triggers.Where(trigger => trigger.Persistent).ToArray();
        string[] keys = triggers.Select(static trigger => trigger.Key)
            .Concat(definition.RootAssetTrashCostIncrease > 0 ? [RootAssetTrashCostKey] : [])
            .ToArray();
        if (keys.Length == 0) return run;
        RunPersistentContext context = run.PersistentContext
            ?? new RunPersistentContext(run.Id, run.AttackedServer, [], []);
        var retained = context.Abilities.ToList();
        foreach (string key in keys)
        {
            if (!retained.Any(value => value.SourceCardId == card.Id && value.TriggerKey == key))
                retained.Add(new PersistentRunAbility(card.Id, card.DefinitionId, zone.Id, key));
        }
        return run.WithPersistentContext(new RunPersistentContext(run.Id, run.AttackedServer, context.StolenAgendas, retained, context.AccessOccurrenceCount,
            context.RememberedCards));
    }

    private static bool CatalogRunAgendaConditionMatches(BehaviorTrigger trigger, OccurrenceSnapshot occurrence) =>
        !trigger.RequireAgendaStolenDuringRun
        || occurrence.Effect is GameplayOccurrenceEffect { RunContext.StolenAgendas.Count: > 0 };

    private static IEnumerable<TriggerInvocation> MaterializePersistentRunTriggers(
        WorkingState working, OccurrenceSnapshot occurrence, EffectCheckpointFrame frame)
    {
        if (occurrence.Effect is not GameplayOccurrenceEffect
            { Kind: OccurrenceKind.RunEnded, RunContext: { } context }) yield break;
        foreach (PersistentRunAbility retained in context.Abilities)
        {
            if (string.Equals(retained.TriggerKey, RootAssetTrashCostKey, StringComparison.Ordinal))
            {
                continue;
            }

            CardInstanceState source = GetCard(working, retained.SourceCardId);
            if (!working.BehaviorCatalog.TryGet(retained.DefinitionId, out CardBehaviorDefinition? definition))
                throw new ResolutionFailureException("Persistent source is absent from its authenticated catalog.");
            BehaviorTrigger trigger = definition.Triggers.Single(value => value.Key == retained.TriggerKey && value.Persistent);
            TriggerId id = CatalogTriggerId(occurrence, source, trigger.Key);
            if (frame.AppliedTriggers.Contains(id) || !CatalogRunAgendaConditionMatches(trigger, occurrence)) continue;
            ZoneState zone = working.Zones.Single(value => value.Id == retained.SourceZoneId);
            yield return new TriggerInvocation(id, GetPlayer(working, PlayerSide.Corp).Id, trigger,
                source.Id, retained.DefinitionId, retained.SourceZoneId, PlayerSide.Corp, zone.Kind,
                occurrence, working.BehaviorCatalog.Fingerprint,
                working.BehaviorCatalog.GetDefinitionFingerprint(retained.DefinitionId));
        }
    }
}

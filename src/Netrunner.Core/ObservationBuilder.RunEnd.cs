namespace Netrunner.Core;

public static partial class ObservationBuilder
{
    private static void AddRunEndAccessPresentation(GameState state, Dictionary<string, object?> publicState)
    {
        if (state.Cards.Any(card => card.HostCardId is not null)
            || state.PendingDecision?.LegalActions.Any(action => action.Parameters
                is BehaviorChoiceParameters { CardIds.Count: 2 }) == true)
        {
            publicState["icePositions"] = state.Zones.Where(zone => zone.Kind == ZoneKind.ServerIce)
                .SelectMany(zone => zone.CardIds.Select((cardId, index) => new Dictionary<string, object?>
                {
                    ["cardId"] = cardId.Value,
                    ["serverId"] = zone.ServerId!.Value.Value,
                    ["index"] = index,
                })).ToArray();
        }
        GameplayOccurrenceEffect? occurrence = state.ResolutionStack
            .OfType<EffectCheckpointFrame>()
            .Select(frame => frame.Occurrence.Effect)
            .OfType<GameplayOccurrenceEffect>()
            .LastOrDefault(effect => effect.Kind == OccurrenceKind.RunEnded && effect.RunContext is not null);
        if (occurrence?.RunContext is not { } context) return;
        publicState["runEnd"] = new Dictionary<string, object?>
        {
            ["runId"] = context.RunId.Value,
            ["serverId"] = context.ServerId.Value,
            ["accessOccurrenceCount"] = context.AccessOccurrenceCount,
        };
    }
}

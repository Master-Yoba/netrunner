namespace Netrunner.Core;

public static partial class ObservationBuilder
{
    private static void AddRunEndAccessPresentation(GameState state, Dictionary<string, object?> publicState)
    {
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

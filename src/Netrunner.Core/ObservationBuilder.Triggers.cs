namespace Netrunner.Core;

public static partial class ObservationBuilder
{
    private static void AddOwnedTriggerContext(GameState state, PlayerSide viewer, Dictionary<string, object?> privateState)
    {
        if (state.PendingDecision is not { Kind: DecisionKind.TriggerChoice or DecisionKind.TriggerOrdering } pending
            || state.GetPlayer(pending.Owner).Side != viewer
            || (state.ResolutionStack.Count > 0 ? state.ResolutionStack[^1] : null) is not EffectCheckpointFrame checkpoint) return;

        HashSet<TriggerId> offered = pending.LegalActions.Select(action => action.Parameters switch
        {
            TriggerChoiceParameters choice => (TriggerId?)choice.TriggerId,
            TriggerOrderParameters order => order.TriggerId,
            _ => null,
        }).OfType<TriggerId>().ToHashSet();
        var contexts = new List<object>();
        foreach (TriggerInvocation trigger in checkpoint.TriggerQueue.Where(trigger => offered.Contains(trigger.Id))
            .OrderBy(trigger => trigger.Id.Value, StringComparer.Ordinal))
        {
            // Only the owner of an issued choice receives its public catalog
            // key. Resolve physical sources through the existing visibility
            // boundary; never disclose other queued or concealed triggers.
            if (trigger.Controller != pending.Owner || trigger.TriggerKey is null || trigger.SourceCardId is not { } source
                || ObserveCard(state, viewer, source) is not { } card) continue;
            contexts.Add(new Dictionary<string, object?>
            {
                ["id"] = trigger.Id.Value,
                ["key"] = trigger.TriggerKey,
                ["mode"] = trigger.Mode,
                ["source"] = card,
            });
        }
        if (contexts.Count > 0) privateState["triggers"] = contexts.ToArray();
    }
}

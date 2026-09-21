using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>
/// Builds client controls on the match worker. Stable position handles never
/// enter Observation or IGameAgent. Core remains the card-visibility authority.
/// </summary>
public static class MatchPresentationProjector
{
    public static MatchPresentationContext Build(GameState state, PlayerSide viewer)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!Enum.IsDefined(viewer)) throw new ArgumentOutOfRangeException(nameof(viewer));
        if (state.PendingDecision is not { } pending || state.GetPlayer(pending.Owner).Side != viewer)
            return MatchPresentationContext.Empty;
        IReadOnlyList<LegalAction> actions = pending.LegalActions;
        if (actions.Count == 0) return MatchPresentationContext.Empty;
        CardPositionViewModel[] positions = state.Zones
            .Where(zone => zone.Kind is ZoneKind.ServerIce or ZoneKind.ServerRoot)
            .SelectMany(zone => zone.CardIds.Select((id, index) =>
                new CardPositionViewModel(id.Value, zone.ServerId!.Value.Value, zone.Kind, index))).ToArray();
        CardSelectionViewModel? selection = null;
        var cards = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.Ordinal);
        if ((state.ResolutionStack.Count > 0 ? state.ResolutionStack[^1] : null) is BehaviorResolutionFrame { Choice: { } choice } frame
            && state.GetPlayer(choice.Owner).Side == viewer
            && actions.Any(action => action.Parameters is BehaviorChoiceParameters parameters && parameters.ChoiceKey == choice.Key))
        {
            string continuation = $"{frame.FrameIdentity}:{frame.SourceCardId}:{frame.InstructionCursor}:"
                + string.Join("/", frame.Continuation.Select(step => $"{step.Kind}:{step.InstructionCursor}:{step.Key}:{step.OptionKey}"));
            selection = new(continuation, choice.Key, choice.Mode, choice.CardinalityKind, choice.CardinalityCount,
                Array.AsReadOnly(choice.CandidateCardIds.Select(id => id.Value).Order(StringComparer.Ordinal).ToArray()),
                Array.AsReadOnly(choice.SelectedCardIds.Select(id => id.Value).ToArray()));
            foreach (CardInstanceId id in choice.CandidateCardIds) AddCard(id);
        }
        foreach (CardInstanceId id in actions.Select(action => action.Parameters).OfType<AbilityActionParameters>()
            .SelectMany(ability => ability.TargetCardIds.Append(ability.SourceCardId)
                .Concat(ability.AdditionalCostSelectionsOrEmpty.SelectMany(cost => cost.CardIds))).Distinct()) AddCard(id);

        var sources = new List<ActionSourceContext>();
        EffectCheckpointFrame[] checkpoints = state.ResolutionStack.OfType<EffectCheckpointFrame>().ToArray();
        foreach (LegalAction action in actions)
        {
            TriggerId? triggerId = action.Parameters switch
            {
                TriggerChoiceParameters trigger => trigger.TriggerId,
                TriggerOrderParameters order => order.TriggerId,
                _ => null,
            };
            InterruptId? interruptId = action.Parameters switch
            {
                PreventionChoiceParameters prevention => prevention.InterruptId,
                ReplacementChoiceParameters replacement => replacement.InterruptId,
                _ => null,
            };
            TriggerInvocation? triggerSource = triggerId is null ? null
                : checkpoints.SelectMany(checkpoint => checkpoint.TriggerQueue).FirstOrDefault(trigger => trigger.Id == triggerId);
            InterruptInvocation? interruptSource = interruptId is null ? null
                : checkpoints.SelectMany(checkpoint => checkpoint.InterruptQueue).FirstOrDefault(interrupt => interrupt.Id == interruptId);
            if ((triggerSource?.SourceCardId ?? interruptSource?.SourceCardId) is { } id && AddCard(id))
                sources.Add(new(action.Id, id, state.GetCard(id).DefinitionId,
                    triggerSource?.TriggerKey ?? interruptSource?.InterruptKey, interruptSource?.BasicDrawCount));
        }
        return new(Array.AsReadOnly(positions), Array.AsReadOnly(cards.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value).ToArray()),
            selection, sources.AsReadOnly());

        bool AddCard(CardInstanceId id)
        {
            if (cards.ContainsKey(id.Value)) return true;
            if (ObservationBuilder.ObserveCard(state, viewer, id) is not { } card) return false;
            cards.Add(id.Value, card);
            return true;
        }
    }
}

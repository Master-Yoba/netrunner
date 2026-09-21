using System.Collections.ObjectModel;

namespace Netrunner.Core;

public static partial class ObservationBuilder
{
    private static ObservationBoardContext BuildBoardContext(
        GameState state, PlayerSide viewer, IReadOnlySet<CardInstanceId> accessVisible,
        IReadOnlyList<LegalAction> actions)
    {
        if (actions.Count == 0) return new([], [], null);
        // Installed locations are public. A positional handle reveals neither
        // the definition nor any hidden hand/deck membership or ordering.
        ObservationCardPosition[] positions = state.Zones
            .Where(zone => zone.Kind is ZoneKind.ServerIce or ZoneKind.ServerRoot)
            .SelectMany(zone => zone.CardIds.Select((id, index) =>
                new ObservationCardPosition(id.Value, zone.ServerId!.Value.Value, zone.Kind, index))).ToArray();
        ObservationCardSelection? selection = null;
        var cards = new List<IReadOnlyDictionary<string, object?>>();
        if ((state.ResolutionStack.Count > 0 ? state.ResolutionStack[^1] : null) is BehaviorResolutionFrame { Choice: { } choice } frame
            && state.GetPlayer(choice.Owner).Side == viewer
            && actions.Any(action => action.Parameters is BehaviorChoiceParameters p && p.ChoiceKey == choice.Key))
        {
            string continuation = $"{frame.FrameIdentity}:{frame.SourceCardId}:{frame.InstructionCursor}:"
                + string.Join("/", frame.Continuation.Select(step => $"{step.Kind}:{step.InstructionCursor}:{step.Key}:{step.OptionKey}"));
            selection = new(continuation, choice.Key, choice.Mode, choice.CardinalityKind, choice.CardinalityCount,
                Array.AsReadOnly(choice.CandidateCardIds.Select(id => id.Value).Order(StringComparer.Ordinal).ToArray()),
                Array.AsReadOnly(choice.SelectedCardIds.Select(id => id.Value).ToArray()));
            // The engine materialized these specific candidates for the chooser.
            // Own search candidates may be inspected, but deck order is not exposed.
            foreach (CardInstanceId id in choice.CandidateCardIds.OrderBy(id => id.Value, StringComparer.Ordinal))
            {
                CardInstanceState card = state.GetCard(id);
                if (card.OwnerSide == viewer || IsCardVisible(state, id, viewer, accessVisible))
                    cards.Add(new ReadOnlyDictionary<string, object?>(CardView(state, card)));
            }
        }
        // Public or otherwise visible ability sources/targets may be in a
        // temporary or removed-from-game zone that has no permanent board row.
        IEnumerable<CardInstanceId> abilityCards = actions.Select(action => action.Parameters).OfType<AbilityActionParameters>()
            .SelectMany(ability => ability.TargetCardIds.Append(ability.SourceCardId)
                .Concat(ability.AdditionalCostSelectionsOrEmpty.SelectMany(cost => cost.CardIds)));
        foreach (CardInstanceId id in abilityCards.Distinct().OrderBy(id => id.Value, StringComparer.Ordinal))
            if (IsCardVisible(state, id, viewer, accessVisible) && cards.All(card => !Equals(card["id"], id.Value)))
                cards.Add(new ReadOnlyDictionary<string, object?>(CardView(state, state.GetCard(id))));
        return new(Array.AsReadOnly(positions), cards.AsReadOnly(), selection);
    }
}

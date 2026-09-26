using System.Collections.ObjectModel;

namespace Netrunner.Core;

public static partial class ObservationBuilder
{
    private static void AddPrivateChoiceCards(GameState state, PlayerSide viewer, Dictionary<string, object?> privateState)
    {
        if (state.PendingDecision is not { } pending || state.GetPlayer(pending.Owner).Side != viewer
            || (state.ResolutionStack.Count > 0 ? state.ResolutionStack[^1] : null) is not BehaviorResolutionFrame { Choice: { } choice }
            || state.GetPlayer(choice.Owner).Side != viewer) return;

        // Search permissions reveal eligible definitions, never the order of
        // the deck, ineligible cards, or another player's private search.
        var cards = choice.CandidateCardIds.Distinct().OrderBy(id => id.Value, StringComparer.Ordinal)
            .Where(id => state.GetCard(id).OwnerSide == viewer
                && state.GetZone(state.GetCard(id).ZoneId).Kind is ZoneKind.CorpDeck or ZoneKind.RunnerStack)
            .Select(id => ObserveCard(state, viewer, id)).Where(card => card is not null).ToArray();
        if (cards.Length > 0) privateState["choiceCards"] = cards;
    }

    /// <summary>
    /// Observes a specific card only when the viewer can inspect it now. An
    /// owned card in a draw deck requires an active, viewer-owned choice that
    /// explicitly includes it. This query exposes no zone membership or order.
    /// </summary>
    public static IReadOnlyDictionary<string, object?>? ObserveCard(GameState state, PlayerSide viewer, CardInstanceId cardId)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!Enum.IsDefined(viewer)) throw new ArgumentOutOfRangeException(nameof(viewer));
        CardInstanceState? card = state.Cards.FirstOrDefault(candidate => candidate.Id == cardId);
        if (card is null) return null;
        bool choicePermission = card.OwnerSide == viewer
            && state.PendingDecision is { } pending && state.GetPlayer(pending.Owner).Side == viewer
            && (state.ResolutionStack.Count > 0 ? state.ResolutionStack[^1] : null) is BehaviorResolutionFrame { Choice: { } choice }
            && state.GetPlayer(choice.Owner).Side == viewer && choice.CandidateCardIds.Contains(cardId)
            && pending.LegalActions.Any(action => action.Parameters is BehaviorChoiceParameters parameters && parameters.ChoiceKey == choice.Key);
        return choicePermission || IsVisibleTo(card, state.GetZone(card.ZoneId), viewer, BuildAccessVisibleSet(state))
            ? new ReadOnlyDictionary<string, object?>(CardView(state, card)) : null;
    }
}

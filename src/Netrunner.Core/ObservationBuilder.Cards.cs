using System.Collections.ObjectModel;

namespace Netrunner.Core;

public static partial class ObservationBuilder
{
    private static void AddPrivateChoiceCards(GameState state, PlayerSide viewer, Dictionary<string, object?> privateState)
    {
        // Bot loop 1: a card an effect offers to install from a hidden zone (Poétrï installing from R&D) is the chooser's
        // own card, shown to them; the Corp bot valued those installs blind and declined them.
        CardInstanceId[] installable = [.. InstallChoiceCandidates(state, viewer)];
        if (installable.Length > 0)
        {
            object?[] offered = [.. installable.OrderBy(id => id.Value, StringComparer.Ordinal).Select(id => ObserveCard(state, viewer, id))
                .Where(card => card is not null)];
            if (offered.Length > 0) privateState["choiceCards"] = offered;
            return;
        }

        if (state.PendingDecision is not { } pending || state.GetPlayer(pending.Owner).Side != viewer
            || (state.ResolutionStack.Count > 0 ? state.ResolutionStack[^1] : null) is not BehaviorResolutionFrame { Choice: { } choice }
            || state.GetPlayer(choice.Owner).Side != viewer) return;

        // Search permissions reveal eligible definitions, never the order of
        // the deck, ineligible cards, or another player's private search.
        // A choice among the opponent's hand cards follows their reveal (Touch-ups' grip): the chooser sees them.
        var cards = choice.CandidateCardIds.Distinct().OrderBy(id => id.Value, StringComparer.Ordinal)
            .Where(id => state.GetCard(id).OwnerSide == viewer
                    && state.GetZone(state.GetCard(id).ZoneId).Kind is ZoneKind.CorpDeck or ZoneKind.RunnerStack
                || state.GetCard(id).OwnerSide != viewer && IsHandZone(state.GetZone(state.GetCard(id).ZoneId).Kind))
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
        bool choicePermission = (card.OwnerSide == viewer || IsHandZone(state.GetZone(card.ZoneId).Kind))
            && state.PendingDecision is { } pending && state.GetPlayer(pending.Owner).Side == viewer
            && (state.ResolutionStack.Count > 0 ? state.ResolutionStack[^1] : null) is BehaviorResolutionFrame { Choice: { } choice }
            && state.GetPlayer(choice.Owner).Side == viewer && choice.CandidateCardIds.Contains(cardId)
            && pending.LegalActions.Any(action => action.Parameters is BehaviorChoiceParameters parameters && parameters.ChoiceKey == choice.Key);
        choicePermission |= InstallChoiceCandidates(state, viewer).Contains(cardId);
        return choicePermission || IsVisibleTo(card, state.GetZone(card.ZoneId), viewer, BuildAccessVisibleSet(state))
            ? new ReadOnlyDictionary<string, object?>(CardView(state, card)) : null;
    }

    private static bool IsHandZone(ZoneKind kind) => kind is ZoneKind.CorpHand or ZoneKind.RunnerGrip;

    /// <summary>The viewer's own cards a pending behavior install choice of theirs offers, wherever they are.</summary>
    private static IEnumerable<CardInstanceId> InstallChoiceCandidates(GameState state, PlayerSide viewer) =>
        state.PendingDecision is { } pending && state.GetPlayer(pending.Owner).Side == viewer
        && (state.ResolutionStack.Count > 0 ? state.ResolutionStack[^1] : null) is BehaviorInstallChoiceFrame frame
        && frame.Owner == pending.Owner
            ? frame.Choices.Select(choice => choice.CardId).Distinct().Where(id => state.GetCard(id).OwnerSide == viewer)
            : [];
}

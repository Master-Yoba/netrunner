using System.Reflection;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>
/// Applies the Runner-side visibility boundary to engine event batches.
/// Engine events remain authoritative inside Core; Match owns the immutable
/// projection that is published to clients. A payload whose required card ID
/// is not visible is omitted rather than replaced with an invented ID.
/// </summary>
internal static class RunnerEventVisibility
{
    public static IReadOnlyList<EngineEvent> Filter(
        IReadOnlyList<EngineEvent> events,
        GameState state)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(state);

        if (events.Count == 0)
        {
            return Array.Empty<EngineEvent>();
        }

        var visible = new List<EngineEvent>(events.Count);
        var accessedThisBatch = new HashSet<CardInstanceId>();
        foreach (EngineEvent @event in events)
        {
            if (@event.Payload is DamageDealtEventPayload damage)
            {
                // Damage and its result are public even when flatline clears
                // the run's temporary access visibility before publication.
                // Preserve only a source known now or revealed earlier in this
                // exact batch; never turn optional provenance into suppression
                // of the public damage event.
                CardInstanceId? source = damage.SourceCardId;
                if (source is { } sourceId
                    && !IsCardVisible(state, sourceId)
                    && !accessedThisBatch.Contains(sourceId))
                {
                    source = null;
                }
                visible.Add(source == damage.SourceCardId ? @event : new EngineEvent(
                    @event.Version, @event.Sequence, @event.Kind,
                    new DamageDealtEventPayload(damage.Version, damage.Target, damage.Kind,
                        damage.Amount, damage.CardsTrashed, damage.CoreDamageAfter, damage.Flatlined,
                        source, damage.FlatlineRisk, damage.TrashedCardPrintedCost, damage.ConditionalEndRun)));
                continue;
            }
            if (IsVisible(@event.Payload, state))
            {
                visible.Add(@event);
                if (@event.Payload is CardAccessedEventPayload accessed && HasCard(state, accessed.CardId))
                {
                    accessedThisBatch.Add(accessed.CardId);
                }
            }
        }
        return visible.ToArray();
    }

    private static bool IsVisible(EngineEventPayload payload, GameState state) =>
        payload switch
        {
            // These payloads are themselves the public reveal boundary. The
            // physical card can still be face-down after a candidate choice
            // or generic reveal, so state-only visibility is insufficient.
            // A generic Corp search/reveal is often private to the Corp even
            // though Core emits the same diagnostic payload for both sides.
            // It is safe for the Runner only when the card is already known
            // through the ordinary observation boundary.
            BehaviorCardRevealedEventPayload value => IsCardVisible(state, value.CardId),
            CardAccessedEventPayload value => HasCard(state, value.CardId),
            BreachCandidateChosenEventPayload value => HasCard(state, value.CardId),
            CardRezzedEventPayload value => HasCard(state, value.CardId),
            IceRezzedEventPayload value => HasCard(state, value.IceId),
            AgendaScoredEventPayload value => HasCard(state, value.AgendaId),
            AgendaStolenEventPayload value => HasCard(state, value.AgendaId),

            ProgramTrashedForMemoryEventPayload value => IsCardVisible(state, value.CardId),
            CounterAdjustedEventPayload value => IsCardVisible(state, value.CardId),
            HostedCreditPoolChangedEventPayload value => IsCardVisible(state, value.CardId),
            TurnCardDiscardedEventPayload value => IsCardVisible(state, value.CardId),
            CardMovedEventPayload value => IsCardVisible(state, value.CardId),
            CardDrawnEventPayload value => IsCardVisible(state, value.CardId),
            CardInstalledEventPayload value => IsCardVisible(state, value.CardId),
            CardPlayStartedEventPayload value => IsCardVisible(state, value.CardId),
            CardPlayCompletedEventPayload value => IsCardVisible(state, value.CardId),
            BehaviorCardAdvancedEventPayload value => IsCardVisible(state, value.CardId),
            AgendaAdvancedEventPayload value => IsCardVisible(state, value.AgendaId),
            RunEventPlayedEventPayload value => IsCardVisible(state, value.EventCardId),
            IceApproachedEventPayload value => IsCardVisible(state, value.IceId),
            SubroutinesBrokenEventPayload value => IsCardVisible(state, value.BreakerId),
            BioroidAbilityUsedEventPayload value => IsCardVisible(state, value.IceId),
            RunMovedEventPayload value => value.NextIceId is null
                || IsCardVisible(state, value.NextIceId.Value),
            AccessedCardTrashedEventPayload value => IsCardVisible(state, value.CardId),
            _ => AreCardPropertiesVisible(payload, state),
        };

    private static bool IsCardVisible(GameState state, CardInstanceId cardId)
    {
        CardInstanceState? card = state.Cards.FirstOrDefault(value => value.Id == cardId);
        if (card is null)
        {
            // Unknown IDs are never allowed through the client boundary.
            return false;
        }

        ZoneState? zone = state.Zones.FirstOrDefault(value => value.Id == card.ZoneId);
        if (zone is null)
        {
            return false;
        }

        if (zone.Kind is ZoneKind.RunnerStack or ZoneKind.CorpDeck)
        {
            return state.RunState?.ChosenAccessCards.Contains(cardId) == true;
        }

        return card.OwnerSide == PlayerSide.Runner
            || card.FaceUp
            || card.Rezzed
            || state.RunState?.ChosenAccessCards.Contains(cardId) == true;
    }

    private static bool HasCard(GameState state, CardInstanceId cardId) =>
        state.Cards.Any(value => value.Id == cardId);

    /// <summary>
    /// Keep this boundary fail-closed when Core adds a typed event carrying a
    /// card reference but the Match projection has not yet added a dedicated
    /// case. Typed payloads use scalar <see cref="CardInstanceId"/> properties;
    /// nullable values are absent references and therefore safe. Payloads
    /// without card references remain public diagnostics.
    /// </summary>
    private static bool AreCardPropertiesVisible(EngineEventPayload payload, GameState state)
    {
        foreach (PropertyInfo property in payload.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length != 0
                || (Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType)
                    != typeof(CardInstanceId))
            {
                continue;
            }

            object? value;
            try
            {
                value = property.GetValue(payload);
            }
            catch (Exception exception) when (exception is ArgumentException or TargetInvocationException or InvalidOperationException)
            {
                // A malformed/future payload must not make a private card ID
                // observable merely because its accessor failed.
                return false;
            }

            if (value is CardInstanceId cardId && !IsCardVisible(state, cardId))
            {
                return false;
            }
        }

        return true;
    }
}

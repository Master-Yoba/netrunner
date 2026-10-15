using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>How the presentation layer treats a Core event payload type.</summary>
public enum PresentationEventRule
{
    /// <summary>Becomes a <see cref="PresentationEvent"/>.</summary>
    Projected,
    /// <summary>Shown through another projected event or the settled view, not on its own.</summary>
    ViewOnly,
    /// <summary>Timing bookkeeping with nothing to show.</summary>
    Bookkeeping,
}

/// <summary>
/// Trusted adapter from one accepted action's raw engine events to viewer-safe
/// presentation events (engine plan E4b). It runs on the match worker with the
/// authoritative states before and after the action, and publishes only
/// presentation keys: a hidden card is either an opaque tracked handle or
/// omitted. Core is unchanged, so saves and replay hashes are unaffected.
/// </summary>
public static class PresentationEventProjector
{
    /// <summary>Every Core payload type and how presentation treats it; a test requires each type to be listed.</summary>
    public static IReadOnlyDictionary<Type, PresentationEventRule> Rules { get; } = new Dictionary<Type, PresentationEventRule>
    {
        [typeof(CreditsAdjustedEventPayload)] = PresentationEventRule.Projected,
        [typeof(ClicksAdjustedEventPayload)] = PresentationEventRule.Projected,
        [typeof(DamageDealtEventPayload)] = PresentationEventRule.Projected,
        [typeof(TagsAdjustedEventPayload)] = PresentationEventRule.Projected,
        // VP-BAD-PUBLICITY: shown through the settled view's Corp count until the client presents it (Vantage Point plan,
        // section 8).
        [typeof(BadPublicityAdjustedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(AdvancementCountersRemovedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(MemoryAdjustedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(ProgramTrashedForMemoryEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(CounterAdjustedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(HostedCreditPoolChangedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(VirusPurgedEventPayload)] = PresentationEventRule.Projected,
        [typeof(LingeringEffectAddedEventPayload)] = PresentationEventRule.Bookkeeping,
        [typeof(LingeringEffectRemovedEventPayload)] = PresentationEventRule.Bookkeeping,
        [typeof(LingeringEffectExpiredEventPayload)] = PresentationEventRule.Bookkeeping,
        [typeof(TurnStartedEventPayload)] = PresentationEventRule.Projected,
        [typeof(TurnEndedEventPayload)] = PresentationEventRule.Bookkeeping,
        [typeof(TurnClicksAllottedEventPayload)] = PresentationEventRule.Projected,
        [typeof(TurnTimingWindowOpenedEventPayload)] = PresentationEventRule.Bookkeeping,
        [typeof(TurnPriorityPassedEventPayload)] = PresentationEventRule.Bookkeeping,
        [typeof(TurnPhaseStartedEventPayload)] = PresentationEventRule.Bookkeeping,
        [typeof(TurnPhaseEndedEventPayload)] = PresentationEventRule.Bookkeeping,
        [typeof(TurnClicksLostEventPayload)] = PresentationEventRule.Projected,
        [typeof(TurnCardDiscardedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(ClickSpentEventPayload)] = PresentationEventRule.Projected,
        [typeof(IceSwappedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(CardDerezzedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(CardMovedEventPayload)] = PresentationEventRule.Projected,
        [typeof(CardDrawnEventPayload)] = PresentationEventRule.Projected,
        [typeof(CardInstalledEventPayload)] = PresentationEventRule.Projected,
        [typeof(CardRezzedEventPayload)] = PresentationEventRule.Projected,
        [typeof(CardPlayStartedEventPayload)] = PresentationEventRule.Projected,
        [typeof(CardPlayCompletedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(BehaviorCardRevealedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(BehaviorCardsShuffledEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(BehaviorCardAdvancedEventPayload)] = PresentationEventRule.Projected,
        [typeof(AgendaAdvancedEventPayload)] = PresentationEventRule.Projected,
        [typeof(AgendaScoredEventPayload)] = PresentationEventRule.Projected,
        [typeof(AgendaStolenEventPayload)] = PresentationEventRule.Projected,
        [typeof(GameWonEventPayload)] = PresentationEventRule.Projected,
        [typeof(RunInitiatedEventPayload)] = PresentationEventRule.Projected,
        [typeof(RunEventPlayedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(RunCreditsAdjustedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(RunPriorityPassedEventPayload)] = PresentationEventRule.Bookkeeping,
        [typeof(RunTimingWindowOpenedEventPayload)] = PresentationEventRule.Bookkeeping,
        [typeof(IceApproachedEventPayload)] = PresentationEventRule.Projected,
        [typeof(ServerApproachedEventPayload)] = PresentationEventRule.Projected,
        [typeof(IceRezzedEventPayload)] = PresentationEventRule.Projected,
        [typeof(SubroutinesBrokenEventPayload)] = PresentationEventRule.Projected,
        [typeof(IceSubroutineJackOutEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(BioroidAbilityUsedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(BreakerPumpedEventPayload)] = PresentationEventRule.Projected,
        [typeof(EncounterInitializedEventPayload)] = PresentationEventRule.Projected,
        [typeof(EncounterCompletedEventPayload)] = PresentationEventRule.Projected,
        [typeof(RunMovedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(RunSuccessfulEventPayload)] = PresentationEventRule.Projected,
        [typeof(BreachStartedEventPayload)] = PresentationEventRule.Bookkeeping,
        [typeof(BreachCandidateChosenEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(CardAccessedEventPayload)] = PresentationEventRule.Projected,
        [typeof(AccessedCardTrashedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(RunEndedEventPayload)] = PresentationEventRule.Projected,
    };

    public static IReadOnlyList<PresentationEvent> Project(GameState before, GameState after,
        IReadOnlyList<EngineEvent> events, PlayerSide viewer,
        IReadOnlyDictionary<CardInstanceId, string> keysBefore, IReadOnlyDictionary<CardInstanceId, string> keysAfter,
        IBehaviorCatalog behavior)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(behavior);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(events);
        var projected = new List<PresentationEvent>();
        // Subroutines fired by this action precede their effects and the end of the encounter.
        projected.AddRange(FiredSubroutines(before, after, events, keysBefore, keysAfter, behavior));
        // Core moves a card silently in a few places (a search sets cards aside);
        // follow each card's zone so every leg of its journey is reported.
        Dictionary<CardInstanceId, ZoneId> zones = before.Cards.ToDictionary(card => card.Id, card => card.ZoneId);
        foreach (EngineEvent entry in events)
        {
            if (entry.Payload is CardMovedEventPayload journey)
            {
                if (zones.TryGetValue(journey.CardId, out ZoneId current) && current != journey.FromZoneId)
                    projected.Add(Moved(journey.CardId, current, journey.FromZoneId));
                zones[journey.CardId] = journey.ToZoneId;
            }
            PresentationEvent? value = entry.Payload switch
            {
                TurnStartedEventPayload turn => new(PresentationEventKind.TurnStarted, turn.ActiveSide)
                { Amount = turn.TurnNumber, Total = turn.Clicks },
                ClickSpentEventPayload click => new(PresentationEventKind.ClickSpent, click.Side) { Total = click.ClicksRemaining },
                CreditsAdjustedEventPayload credits => new(PresentationEventKind.CreditsChanged, after.GetPlayer(credits.Target).Side)
                { Amount = credits.Delta, Total = credits.CreditsAfter },
                CardDrawnEventPayload drawn => new(PresentationEventKind.CardDrawn, drawn.Side) { CardKey = Key(keysAfter, drawn.CardId) },
                CardInstalledEventPayload installed => new(PresentationEventKind.CardInstalled, installed.Side)
                {
                    CardKey = Key(keysAfter, installed.CardId),
                    ServerId = ServerOf(after, installed.ToZoneId),
                    Amount = installed.Cost,
                },
                CardRezzedEventPayload rez => Revealed(PresentationEventKind.CardRezzed, rez.CardId) with { Amount = rez.CreditCost },
                IceRezzedEventPayload rez => Revealed(PresentationEventKind.CardRezzed, rez.IceId),
                // Advancement counters are public even on a face-down card; the payload type is not.
                AgendaAdvancedEventPayload advanced => Advanced(advanced.AgendaId, 1, advanced.AdvancementCounters),
                BehaviorCardAdvancedEventPayload advanced => Advanced(advanced.CardId, advanced.Amount, advanced.AdvancementCounters),
                AgendaScoredEventPayload scored => Revealed(PresentationEventKind.AgendaScored, scored.AgendaId)
                    with
                { Side = PlayerSide.Corp, Amount = scored.AgendaPoints },
                AgendaStolenEventPayload stolen => Revealed(PresentationEventKind.AgendaStolen, stolen.AgendaId)
                    with
                { Side = PlayerSide.Runner, Amount = stolen.AgendaPoints },
                RunInitiatedEventPayload run => new(PresentationEventKind.RunStarted, run.InitiatingSide) { ServerId = run.ServerId.Value },
                IceApproachedEventPayload approached => new(PresentationEventKind.IceApproached, PlayerSide.Runner)
                { CardKey = Key(keysAfter, approached.IceId) ?? Key(keysBefore, approached.IceId) },
                EncounterInitializedEventPayload encounter => new(PresentationEventKind.EncounterStarted, PlayerSide.Runner)
                { CardKey = Key(keysAfter, encounter.IceId) ?? Key(keysBefore, encounter.IceId), Total = encounter.SubroutineCount },
                SubroutinesBrokenEventPayload broken => new(PresentationEventKind.SubroutinesBroken, PlayerSide.Runner)
                {
                    CardKey = before.RunState?.Encounter?.IceId is { } ice ? Key(keysAfter, ice) ?? Key(keysBefore, ice) : null,
                    Amount = broken.BrokenCount,
                    SourceCardKey = Key(keysAfter, broken.BreakerId) ?? Key(keysBefore, broken.BreakerId),
                },
                EncounterCompletedEventPayload => new(PresentationEventKind.EncounterEnded, PlayerSide.Runner),
                ServerApproachedEventPayload approached => new(PresentationEventKind.ServerApproached, PlayerSide.Runner)
                { ServerId = approached.ServerId.Value },
                RunSuccessfulEventPayload successful => new(PresentationEventKind.RunSuccessful, PlayerSide.Runner)
                { ServerId = successful.ServerId.Value },
                CardAccessedEventPayload accessed => Revealed(PresentationEventKind.CardAccessed, accessed.CardId)
                    with
                { Side = PlayerSide.Runner, ServerId = accessed.ServerId.Value },
                RunEndedEventPayload ended => new(PresentationEventKind.RunEnded, ended.InitiatingSide)
                { RunEndReason = ended.Reason, Successful = ended.Successful },
                CardMovedEventPayload moved => Moved(moved.CardId, moved.FromZoneId, moved.ToZoneId),
                CardPlayStartedEventPayload played => new(PresentationEventKind.CardPlayed, played.Side)
                { CardKey = Key(keysAfter, played.CardId) ?? Key(keysBefore, played.CardId), Amount = played.Cost },
                ClicksAdjustedEventPayload clicks => new(PresentationEventKind.ClicksChanged, after.GetPlayer(clicks.Target).Side)
                { Amount = clicks.Delta, Total = clicks.ClicksAfter },
                // A turn ends by losing every click left, and starts with its allotment.
                TurnClicksLostEventPayload lost => new(PresentationEventKind.ClicksChanged, lost.Side)
                { Amount = -lost.ClicksLost, Total = 0 },
                TurnClicksAllottedEventPayload allotted => new(PresentationEventKind.ClicksChanged, allotted.Side)
                { Total = allotted.Clicks },
                DamageDealtEventPayload damage => new(PresentationEventKind.DamageDealt, after.GetPlayer(damage.Target).Side)
                {
                    Damage = damage.Kind,
                    Amount = damage.Amount,
                    Total = damage.CardsTrashed,
                    SourceCardKey = damage.SourceCardId is { } source ? Key(keysAfter, source) ?? Key(keysBefore, source) : null,
                },
                TagsAdjustedEventPayload tags => new(PresentationEventKind.TagsChanged, after.GetPlayer(tags.Target).Side)
                { Amount = tags.Delta, Total = tags.TagsAfter },
                BreakerPumpedEventPayload pumped => new(PresentationEventKind.BreakerPumped, PlayerSide.Runner)
                { CardKey = Key(keysAfter, pumped.BreakerId) ?? Key(keysBefore, pumped.BreakerId) },
                VirusPurgedEventPayload purged => new(PresentationEventKind.VirusPurged, PlayerSide.Corp) { Amount = purged.CountersRemoved },
                GameWonEventPayload won => new(PresentationEventKind.GameEnded, won.Winner) { WinReason = won.Reason },
                _ => null,
            };
            // The run already showed as over when its end reason appeared; don't end it twice.
            if (value is { Kind: PresentationEventKind.RunEnded } && before.RunState?.EndReason is not null) continue;
            if (value is not null) projected.Add(value);
        }
        foreach (CardInstanceState card in after.Cards)
        {
            if (zones.TryGetValue(card.Id, out ZoneId last) && last != card.ZoneId) projected.Add(Moved(card.Id, last, card.ZoneId));
        }
        // A breach can complete (for example on a steal) before Core emits RunEnded
        // in a later action; the run is over on the board as soon as it has an end reason.
        // It is also over when the run disappears without RunEnded, as when the game ends during access.
        if (before.RunState is { EndReason: null } live && !events.Any(entry => entry.Payload is RunEndedEventPayload))
        {
            if (after.RunState is { EndReason: { } reason } over && over.Id == live.Id)
                projected.Add(new(PresentationEventKind.RunEnded, over.InitiatingSide) { RunEndReason = reason, Successful = over.Successful });
            else if (after.RunState is null || after.RunState.Id != live.Id)
                projected.Add(new(PresentationEventKind.RunEnded, live.InitiatingSide) { Successful = false });
        }
        // Any other change of an installed card's key (revealed or concealed by
        // some effect) still has to move what the board shows for that card.
        HashSet<string> reported = projected.Where(value => value.PreviousCardKey is not null)
            .Select(value => value.PreviousCardKey!).ToHashSet(StringComparer.Ordinal);
        foreach ((CardInstanceId id, string now) in keysAfter)
        {
            if (keysBefore.TryGetValue(id, out string? previous) && previous != now && !reported.Contains(previous)
                && after.Cards.FirstOrDefault(card => card.Id == id) is { } card && Installed(after, card.ZoneId))
                projected.Add(new(PresentationEventKind.CardKeyChanged, card.OwnerSide) { CardKey = now, PreviousCardKey = previous });
        }
        return AttributeCredits(projected);

        PresentationEvent Moved(CardInstanceId id, ZoneId fromZoneId, ZoneId toZoneId)
        {
            ZoneState? from = ZoneOf(before, fromZoneId) ?? ZoneOf(after, fromZoneId);
            ZoneState? to = ZoneOf(after, toZoneId) ?? ZoneOf(before, toZoneId);
            string? now = Key(keysAfter, id);
            string? previous = Key(keysBefore, id);
            CardInstanceState? card = after.Cards.FirstOrDefault(value => value.Id == id)
                ?? before.Cards.FirstOrDefault(value => value.Id == id);
            return new(PresentationEventKind.CardMoved, card?.OwnerSide)
            {
                CardKey = now ?? previous,
                PreviousCardKey = now is not null && previous is not null && previous != now ? previous : null,
                FromZone = from?.Kind,
                ToZone = to?.Kind,
                ServerId = from?.ServerId?.Value ?? to?.ServerId?.Value,
            };
        }

        PresentationEvent Revealed(PresentationEventKind kind, CardInstanceId id)
        {
            string? now = Key(keysAfter, id);
            string? previous = Key(keysBefore, id);
            return new(kind, PlayerSide.Corp) { CardKey = now, PreviousCardKey = previous != now ? previous : null };
        }

        PresentationEvent Advanced(CardInstanceId id, int amount, int total) => new(PresentationEventKind.CardAdvanced, PlayerSide.Corp)
        {
            CardKey = Key(keysAfter, id),
            ServerId = after.Cards.FirstOrDefault(card => card.Id == id) is { } card ? ServerOf(after, card.ZoneId) : null,
            Amount = amount,
            Total = total,
        };
    }

    /// <summary>
    /// Core records each unbroken subroutine that resolves in the run's ledger.
    /// While the run continues, the new ledger entries are exactly the fired
    /// subroutines. When a subroutine ends the run in this action the ledger is
    /// gone, so the unresolved, unbroken subroutines fire in printed order up to
    /// and including the first one that ends the run.
    /// </summary>
    private static IEnumerable<PresentationEvent> FiredSubroutines(GameState before, GameState after,
        IReadOnlyList<EngineEvent> events, IReadOnlyDictionary<CardInstanceId, string> keysBefore,
        IReadOnlyDictionary<CardInstanceId, string> keysAfter, IBehaviorCatalog behavior)
    {
        if (before.RunState is not { } run || run.Encounter is not { } encounter) yield break;
        string? iceKey = Key(keysAfter, encounter.IceId) ?? Key(keysBefore, encounter.IceId);
        IReadOnlyList<RunResolvedSubroutineLedgerEntry> earlier = run.ResolvedSubroutineLedger ?? [];
        IEnumerable<int> ordinals;
        if (after.RunState is { } continued && continued.Id == run.Id)
        {
            ordinals = (continued.ResolvedSubroutineLedger ?? []).Where(entry => !earlier.Contains(entry)
                && entry.EncounterId == encounter.Id).Select(entry => entry.Ordinal);
        }
        else if (events.Any(entry => entry.Payload is RunEndedEventPayload { Reason: RunEndReason.EndedByRule }))
        {
            CardDefinitionId? definition = before.Cards.FirstOrDefault(card => card.Id == encounter.IceId)?.DefinitionId;
            IReadOnlyList<IceSubroutineSpec> printed = definition is { } id && behavior.TryGetIce(id, out IceBehaviorSpec ice)
                ? ice.Subroutines : [];
            var fired = new List<int>();
            foreach (RunSubroutineState subroutine in encounter.Subroutines.Where(value => !value.Broken && !value.Resolved)
                .OrderBy(value => value.Ordinal))
            {
                fired.Add(subroutine.Ordinal);
                if (subroutine.Ordinal < printed.Count && printed[subroutine.Ordinal] is EndRunSubroutineSpec) break;
            }
            ordinals = fired;
        }
        else yield break;
        foreach (int ordinal in ordinals)
            yield return new PresentationEvent(PresentationEventKind.SubroutineFired, PlayerSide.Corp) { CardKey = iceKey, Ordinal = ordinal };
    }

    /// <summary>
    /// Names the card that paid for or produced each credit change in the same
    /// action. A payment goes to the install, rez or play whose cost matches,
    /// else to a pumped or breaking breaker, an advanced card or a card trashed
    /// from a server or R&amp;D; a gain goes to the played card.
    /// </summary>
    private static List<PresentationEvent> AttributeCredits(List<PresentationEvent> projected)
    {
        for (int index = 0; index < projected.Count; index++)
        {
            if (projected[index] is not { Kind: PresentationEventKind.CreditsChanged, Amount: { } delta } credits) continue;
            string? source = delta < 0
                ? projected.FirstOrDefault(value => value.Kind is PresentationEventKind.CardInstalled or PresentationEventKind.CardRezzed
                    or PresentationEventKind.CardPlayed && value.Side == credits.Side && value.Amount == -delta && value.CardKey is not null)?.CardKey
                    ?? projected.Select(value => Paid(value, credits.Side)).FirstOrDefault(key => key is not null)
                : projected.LastOrDefault(value => value.Kind == PresentationEventKind.CardPlayed && value.Side == credits.Side
                    && value.CardKey is not null)?.CardKey;
            if (source is not null) projected[index] = credits with { SourceCardKey = source };
        }
        return projected;

        // The Runner pays to trash a Corp card, so a trash matches either side's payment.
        static string? Paid(PresentationEvent value, PlayerSide? payer) => value switch
        {
            { Kind: PresentationEventKind.BreakerPumped } when payer == PlayerSide.Runner => value.CardKey,
            { Kind: PresentationEventKind.SubroutinesBroken } when payer == PlayerSide.Runner => value.SourceCardKey,
            { Kind: PresentationEventKind.CardAdvanced } when payer == PlayerSide.Corp => value.CardKey,
            {
                Kind: PresentationEventKind.CardMoved, ToZone: ZoneKind.CorpDiscard, FromZone: ZoneKind.ServerRoot or ZoneKind.ServerIce
                or ZoneKind.CorpDeck or ZoneKind.CorpHand
            } when payer == PlayerSide.Runner => value.CardKey,
            _ => null,
        };
    }

    private static bool Installed(GameState state, ZoneId zoneId) =>
        state.Zones.FirstOrDefault(zone => zone.Id == zoneId)?.Kind is ZoneKind.ServerRoot or ZoneKind.ServerIce;

    private static ZoneState? ZoneOf(GameState state, ZoneId zoneId) => state.Zones.FirstOrDefault(zone => zone.Id == zoneId);

    private static string? Key(IReadOnlyDictionary<CardInstanceId, string> keys, CardInstanceId id) =>
        keys.TryGetValue(id, out string? key) ? key : null;

    private static string? ServerOf(GameState state, ZoneId zoneId) =>
        state.Zones.FirstOrDefault(zone => zone.Id == zoneId)?.ServerId?.Value;
}

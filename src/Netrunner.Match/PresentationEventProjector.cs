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
        [typeof(ClicksAdjustedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(DamageDealtEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(TagsAdjustedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(MemoryAdjustedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(ProgramTrashedForMemoryEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(CounterAdjustedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(HostedCreditPoolChangedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(VirusPurgedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(LingeringEffectAddedEventPayload)] = PresentationEventRule.Bookkeeping,
        [typeof(LingeringEffectRemovedEventPayload)] = PresentationEventRule.Bookkeeping,
        [typeof(LingeringEffectExpiredEventPayload)] = PresentationEventRule.Bookkeeping,
        [typeof(TurnStartedEventPayload)] = PresentationEventRule.Projected,
        [typeof(TurnEndedEventPayload)] = PresentationEventRule.Bookkeeping,
        [typeof(TurnClicksAllottedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(TurnTimingWindowOpenedEventPayload)] = PresentationEventRule.Bookkeeping,
        [typeof(TurnPriorityPassedEventPayload)] = PresentationEventRule.Bookkeeping,
        [typeof(TurnPhaseStartedEventPayload)] = PresentationEventRule.Bookkeeping,
        [typeof(TurnPhaseEndedEventPayload)] = PresentationEventRule.Bookkeeping,
        [typeof(TurnClicksLostEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(TurnCardDiscardedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(ClickSpentEventPayload)] = PresentationEventRule.Projected,
        [typeof(IceSwappedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(CardDerezzedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(CardMovedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(CardDrawnEventPayload)] = PresentationEventRule.Projected,
        [typeof(CardInstalledEventPayload)] = PresentationEventRule.Projected,
        [typeof(CardRezzedEventPayload)] = PresentationEventRule.Projected,
        [typeof(CardPlayStartedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(CardPlayCompletedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(BehaviorCardRevealedEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(BehaviorCardsShuffledEventPayload)] = PresentationEventRule.ViewOnly,
        [typeof(BehaviorCardAdvancedEventPayload)] = PresentationEventRule.Projected,
        [typeof(AgendaAdvancedEventPayload)] = PresentationEventRule.Projected,
        [typeof(AgendaScoredEventPayload)] = PresentationEventRule.Projected,
        [typeof(AgendaStolenEventPayload)] = PresentationEventRule.Projected,
        [typeof(GameWonEventPayload)] = PresentationEventRule.ViewOnly,
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
        [typeof(BreakerPumpedEventPayload)] = PresentationEventRule.ViewOnly,
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
        foreach (EngineEvent entry in events)
        {
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
                },
                EncounterCompletedEventPayload => new(PresentationEventKind.EncounterEnded, PlayerSide.Runner),
                ServerApproachedEventPayload approached => new(PresentationEventKind.ServerApproached, PlayerSide.Runner)
                { ServerId = approached.ServerId.Value },
                RunSuccessfulEventPayload successful => new(PresentationEventKind.RunSuccessful, PlayerSide.Runner)
                { ServerId = successful.ServerId.Value },
                CardAccessedEventPayload accessed => new(PresentationEventKind.CardAccessed, PlayerSide.Runner)
                { CardKey = Key(keysAfter, accessed.CardId), ServerId = accessed.ServerId.Value },
                RunEndedEventPayload ended => new(PresentationEventKind.RunEnded, ended.InitiatingSide)
                { RunEndReason = ended.Reason, Successful = ended.Successful },
                _ => null,
            };
            if (value is not null) projected.Add(value);
        }
        return projected;

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

    private static string? Key(IReadOnlyDictionary<CardInstanceId, string> keys, CardInstanceId id) =>
        keys.TryGetValue(id, out string? key) ? key : null;

    private static string? ServerOf(GameState state, ZoneId zoneId) =>
        state.Zones.FirstOrDefault(zone => zone.Id == zoneId)?.ServerId?.Value;
}

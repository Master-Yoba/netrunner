namespace Netrunner.Core;

/// <summary>
/// Where unseen cards come from when a state is sampled. This is an explicit
/// fairness choice for a search agent, not something the sampler infers.
/// </summary>
public abstract record OpponentCardPool
{
    private OpponentCardPool() { }

    /// <summary>The opponent's known decklist; unseen cards are its copies not visible anywhere.</summary>
    public sealed record Decklist(IReadOnlyDictionary<CardDefinitionId, int> Counts) : OpponentCardPool;

    /// <summary>Any of these cards, independently per slot (for example every card legal for the faction).</summary>
    public sealed record AnyOf(IReadOnlyList<CardDefinitionId> Candidates) : OpponentCardPool;

    /// <summary>Every unseen card becomes a stand-in: <paramref name="Ice"/> in ICE positions, <paramref name="Card"/> elsewhere.</summary>
    public sealed record Placeholders(CardDefinitionId Card, CardDefinitionId Ice) : OpponentCardPool;
}

/// <summary>A hidden card the viewer knows right now, by its observable position.</summary>
public sealed record KnownHiddenCard(ZoneId ZoneId, int Index, CardDefinitionId DefinitionId);

/// <summary>
/// What the viewer knows about cards it can't currently see. Knowing a card's
/// identity from the past is not enough: an entry is a claim about where the
/// card is now (for example the top of R&amp;D after an access that left it
/// there), and it is dropped by the caller once a shuffle or move invalidates it.
/// </summary>
public sealed record SamplerKnowledge(IReadOnlyList<KnownHiddenCard> Cards)
{
    public static SamplerKnowledge None { get; } = new([]);
}

/// <summary>
/// Samples a full game state consistent with what <c>viewer</c> can observe
/// (engine plan E5), for determinized search.
///
/// The authoritative state is the structural template: zones, instance IDs,
/// public cards, counters and the rules state are kept. Every card hidden from
/// the viewer first loses its identity, then receives one dealt from the pool
/// (opponent cards) or from the viewer's own unseen cards (the viewer's deck
/// order), using only <paramref name="random"/>. A hidden identity is never
/// read, so two states with the same observation give the same sample for the
/// same seed. The engine's random state is reseeded, so the sample can't
/// predict real shuffles or random accesses. Samples are simulation states:
/// they can't be serialized, snapshotted, hashed or applied authoritatively.
/// </summary>
public static class ObservationSampler
{
    public static GameState Sample(GameState state, PlayerSide viewer, SamplerKnowledge knowledge,
        OpponentCardPool pool, IRuntimeCardCatalog runtime, DeterministicRandom random)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(random);
        if (!Enum.IsDefined(viewer)) throw new ArgumentOutOfRangeException(nameof(viewer));

        Dictionary<ZoneId, ZoneState> zones = state.Zones.ToDictionary(zone => zone.Id);
        IReadOnlySet<CardInstanceId> accessVisible = ObservationBuilder.BuildAccessVisibleSet(state);
        Dictionary<CardInstanceId, CardInstanceState> cards = state.Cards.ToDictionary(card => card.Id);
        bool Hidden(CardInstanceState card) => !ObservationBuilder.IsVisibleTo(card, zones[card.ZoneId], viewer, accessVisible);

        // Slots in a canonical order that depends only on observable structure.
        Slot[] slots = [.. state.Zones.OrderBy(zone => zone.Id.Value, StringComparer.Ordinal)
            .SelectMany(zone => zone.CardIds.Select((id, index) => (zone, id, index)))
            .Where(entry => Hidden(cards[entry.id]))
            .Select(entry => new Slot(entry.id, entry.zone, entry.index, cards[entry.id].OwnerSide))];
        var dealt = new Dictionary<CardInstanceId, CardDefinitionId>();
        Dictionary<(ZoneId, int), CardDefinitionId> pins = knowledge.Cards
            .GroupBy(card => (card.ZoneId, card.Index)).ToDictionary(group => group.Key, group => group.Last().DefinitionId);

        // The viewer's own unseen cards (its deck order) are a known multiset:
        // everything it owns that it can't see right now.
        Slot[] own = [.. slots.Where(slot => slot.Owner == viewer)];
        // Sorted, so the deal can't depend on the true order being resampled.
        DealFromMultiset(own, [.. own.Select(slot => cards[slot.Id].DefinitionId).OrderBy(id => id.Value, StringComparer.Ordinal)],
            pins, runtime, random, dealt, fallback: null);

        Slot[] opponent = [.. slots.Where(slot => slot.Owner != viewer)];
        switch (pool)
        {
            case OpponentCardPool.Decklist decklist:
                {
                    var remaining = new List<CardDefinitionId>();
                    foreach ((CardDefinitionId definition, int count) in decklist.Counts.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal))
                    {
                        int seen = state.Cards.Count(card => card.OwnerSide != viewer && card.DefinitionId == definition && !Hidden(card));
                        remaining.AddRange(Enumerable.Repeat(definition, Math.Max(0, count - seen)));
                    }
                    DealFromMultiset(opponent, remaining, pins, runtime, random, dealt,
                        fallback: [.. decklist.Counts.Keys.OrderBy(id => id.Value, StringComparer.Ordinal)]);
                    break;
                }
            case OpponentCardPool.AnyOf any:
                if (any.Candidates.Count == 0) throw new ArgumentException("The card pool is empty.", nameof(pool));
                foreach (Slot slot in opponent)
                {
                    if (pins.TryGetValue((slot.Zone.Id, slot.Index), out CardDefinitionId pinned)) { dealt[slot.Id] = pinned; continue; }
                    CardDefinitionId[] fitting = [.. any.Candidates.Where(candidate => Fits(slot, candidate, runtime))];
                    if (fitting.Length == 0) throw new ArgumentException($"No pool card fits {slot.Zone.Kind}.", nameof(pool));
                    dealt[slot.Id] = fitting[random.NextInt(fitting.Length)];
                }
                break;
            case OpponentCardPool.Placeholders placeholders:
                foreach (Slot slot in opponent)
                {
                    dealt[slot.Id] = pins.TryGetValue((slot.Zone.Id, slot.Index), out CardDefinitionId pinned) ? pinned
                        : slot.Zone.Kind == ZoneKind.ServerIce ? placeholders.Ice : placeholders.Card;
                }
                break;
            default:
                throw new ArgumentException("Unknown card pool.", nameof(pool));
        }

        CardInstanceState[] sampled = [.. state.Cards.Select(card => dealt.TryGetValue(card.Id, out CardDefinitionId definition)
            ? new CardInstanceState(card.Id, definition, card.ZoneId, card.OwnerSide, card.FaceUp, card.Rezzed,
                card.AdvancementCounters, card.HostCardId)
            : card)];
        return new GameState(state.SchemaVersion, state.GameId, state.RulesVersion, state.CardDataVersion, state.Players,
            sampled, state.Zones, state.Servers, state.Turn, state.Outcome, random.NextUInt64(), state.Allocator,
            state.DecisionRevision, state.EventSequence, state.ResolutionStack, state.PendingDecision, state.ActionSequence,
            state.InitialSeed, state.RunState, state.RulesState, state.BehaviorRegistryVersion, state.BehaviorCatalogFingerprint,
            state.RuntimeCardCatalogVersion, state.RuntimeCardCatalogFingerprint).MarkSimulated();
    }

    private sealed record Slot(CardInstanceId Id, ZoneState Zone, int Index, PlayerSide Owner);

    /// <summary>
    /// Deals a multiset to slots: pinned slots first, then ICE and server-root
    /// slots from the cards that fit them, then everything else. A slot no
    /// remaining card fits (inconsistent knowledge) draws from <paramref name="fallback"/>.
    /// </summary>
    private static void DealFromMultiset(IReadOnlyList<Slot> slots, List<CardDefinitionId> remaining,
        Dictionary<(ZoneId, int), CardDefinitionId> pins, IRuntimeCardCatalog runtime, DeterministicRandom random,
        Dictionary<CardInstanceId, CardDefinitionId> dealt, IReadOnlyList<CardDefinitionId>? fallback)
    {
        random.Shuffle(remaining);
        var open = new List<Slot>();
        foreach (Slot slot in slots)
        {
            if (pins.TryGetValue((slot.Zone.Id, slot.Index), out CardDefinitionId pinned))
            {
                dealt[slot.Id] = pinned;
                remaining.Remove(pinned);
            }
            else open.Add(slot);
        }
        foreach (Slot slot in open.OrderBy(slot => slot.Zone.Kind switch { ZoneKind.ServerIce => 0, ZoneKind.ServerRoot => 1, _ => 2 }))
        {
            int index = remaining.FindIndex(candidate => Fits(slot, candidate, runtime));
            if (index >= 0)
            {
                dealt[slot.Id] = remaining[index];
                remaining.RemoveAt(index);
                continue;
            }
            CardDefinitionId[] fitting = [.. (fallback ?? []).Where(candidate => Fits(slot, candidate, runtime))];
            if (fitting.Length == 0)
                throw new InvalidOperationException($"No card is left to sample for a hidden {slot.Zone.Kind} slot.");
            dealt[slot.Id] = fitting[random.NextInt(fitting.Length)];
        }
    }

    /// <summary>An installed position holds only cards that can be installed there.</summary>
    private static bool Fits(Slot slot, CardDefinitionId candidate, IRuntimeCardCatalog runtime) => slot.Zone.Kind switch
    {
        ZoneKind.ServerIce => runtime.GetSpec(candidate).CardClass == RuntimeCardClass.Ice,
        ZoneKind.ServerRoot => runtime.GetSpec(candidate).CardClass is RuntimeCardClass.Agenda or RuntimeCardClass.Asset
            or RuntimeCardClass.Upgrade,
        _ => true,
    };
}

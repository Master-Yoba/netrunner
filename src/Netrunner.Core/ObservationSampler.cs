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

        RulesState? rules = WithHiddenSecretIdentity(state.RulesState, viewer, random);
        CardInstanceState[] sampled = [.. state.Cards.Select(card => dealt.TryGetValue(card.Id, out CardDefinitionId definition)
            ? new CardInstanceState(card.Id, definition, card.ZoneId, card.OwnerSide, card.FaceUp, card.Rezzed,
                card.AdvancementCounters, card.HostCardId)
            : card)];
        return new GameState(state.SchemaVersion, state.GameId, state.RulesVersion, state.CardDataVersion, state.Players,
            sampled, state.Zones, state.Servers, state.Turn, state.Outcome, random.NextUInt64(), state.Allocator,
            state.DecisionRevision, state.EventSequence, state.ResolutionStack, state.PendingDecision, state.ActionSequence,
            state.InitialSeed, state.RunState, rules, state.BehaviorRegistryVersion, state.BehaviorCatalogFingerprint,
            state.RuntimeCardCatalogVersion, state.RuntimeCardCatalogFingerprint).MarkSimulated();
    }

    /// <summary>
    /// VP-SECRET-IDENTITY: the Runner doesn't know which copy of Méliès U the Corp set until it flips, so a Runner's sample
    /// draws the side again among the three it could be. Nothing is drawn without a secret identity, so other games' samples
    /// are unchanged.
    /// </summary>
    private static RulesState? WithHiddenSecretIdentity(RulesState? rules, PlayerSide viewer, DeterministicRandom random) =>
        viewer == PlayerSide.Runner && rules?.SecretIdentity is { Flipped: false }
            ? rules.WithSecretIdentity(new SecretIdentityState(SecretIdentitySides[random.NextInt(SecretIdentitySides.Length)], Flipped: false))
            : rules;

    private static readonly ServerKind[] SecretIdentitySides = [ServerKind.Headquarters, ServerKind.RAndD, ServerKind.Archives];

    /// <summary>
    /// Bot plan S4.2c: one hidden world for several simulation states that share their card instances (the ends of lines
    /// played from one sample), so a pair of them is played out again on common random numbers, as the sample they came
    /// from paired them. A card the viewer sees in any of the states keeps that identity in all of them; the cards hidden
    /// in all of them are dealt once, as <see cref="Sample"/> deals (the viewer's own unseen cards from their multiset, the
    /// opponent's from the pool), and each instance has its one identity in every state. Every state gets the same new
    /// random state. No pins: a tracker's pins are positions in one state.
    /// </summary>
    public static IReadOnlyList<GameState> SampleJointly(IReadOnlyList<GameState> states, PlayerSide viewer, OpponentCardPool pool,
        IRuntimeCardCatalog runtime, DeterministicRandom random)
    {
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(random);
        if (!Enum.IsDefined(viewer)) throw new ArgumentOutOfRangeException(nameof(viewer));
        if (states.Count == 0) return [];
        var known = new Dictionary<CardInstanceId, CardDefinitionId>();
        var hidden = new Dictionary<CardInstanceId, (List<Slot> Slots, CardDefinitionId Identity)>();
        foreach (GameState state in states)
        {
            Dictionary<ZoneId, ZoneState> zones = state.Zones.ToDictionary(zone => zone.Id);
            IReadOnlySet<CardInstanceId> accessVisible = ObservationBuilder.BuildAccessVisibleSet(state);
            Dictionary<CardInstanceId, CardInstanceState> cards = state.Cards.ToDictionary(card => card.Id);
            foreach (ZoneState zone in state.Zones)
                for (int index = 0; index < zone.CardIds.Count; index++)
                {
                    CardInstanceState card = cards[zone.CardIds[index]];
                    if (ObservationBuilder.IsVisibleTo(card, zone, viewer, accessVisible)) known[card.Id] = card.DefinitionId;
                    else
                    {
                        if (!hidden.TryGetValue(card.Id, out var entry)) hidden[card.Id] = entry = ([], card.DefinitionId);
                        entry.Slots.Add(new Slot(card.Id, zone, index, card.OwnerSide));
                    }
                }
        }
        // An instance's most constrained position decides which cards fit it.
        Slot[] slots = [.. hidden.Where(entry => !known.ContainsKey(entry.Key)).OrderBy(entry => entry.Key.Value, StringComparer.Ordinal)
            .Select(entry => entry.Value.Slots.OrderBy(slot => slot.Zone.Kind switch { ZoneKind.ServerIce => 0, ZoneKind.ServerRoot => 1, _ => 2 }).First())];
        var dealt = new Dictionary<CardInstanceId, CardDefinitionId>();
        var noPins = new Dictionary<(ZoneId, int), CardDefinitionId>();
        Slot[] own = [.. slots.Where(slot => slot.Owner == viewer)];
        DealFromMultiset(own, [.. own.Select(slot => hidden[slot.Id].Identity).OrderBy(id => id.Value, StringComparer.Ordinal)],
            noPins, runtime, random, dealt, fallback: null);
        Slot[] opponent = [.. slots.Where(slot => slot.Owner != viewer)];
        switch (pool)
        {
            case OpponentCardPool.Decklist decklist:
                {
                    var remaining = new List<CardDefinitionId>();
                    Dictionary<CardDefinitionId, int> seen = states[0].Cards.Where(card => card.OwnerSide != viewer && known.ContainsKey(card.Id))
                        .GroupBy(card => known[card.Id]).ToDictionary(group => group.Key, group => group.Count());
                    foreach ((CardDefinitionId definition, int count) in decklist.Counts.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal))
                        remaining.AddRange(Enumerable.Repeat(definition, Math.Max(0, count - seen.GetValueOrDefault(definition))));
                    DealFromMultiset(opponent, remaining, noPins, runtime, random, dealt,
                        fallback: [.. decklist.Counts.Keys.OrderBy(id => id.Value, StringComparer.Ordinal)]);
                    break;
                }
            case OpponentCardPool.AnyOf any:
                if (any.Candidates.Count == 0) throw new ArgumentException("The card pool is empty.", nameof(pool));
                foreach (Slot slot in opponent)
                {
                    CardDefinitionId[] fitting = [.. any.Candidates.Where(candidate => Fits(slot, candidate, runtime))];
                    if (fitting.Length == 0) throw new ArgumentException($"No pool card fits {slot.Zone.Kind}.", nameof(pool));
                    dealt[slot.Id] = fitting[random.NextInt(fitting.Length)];
                }
                break;
            case OpponentCardPool.Placeholders placeholders:
                foreach (Slot slot in opponent) dealt[slot.Id] = slot.Zone.Kind == ZoneKind.ServerIce ? placeholders.Ice : placeholders.Card;
                break;
            default:
                throw new ArgumentException("Unknown card pool.", nameof(pool));
        }
        // One drawn side for every state, as each hidden card has one identity in all of them.
        ServerKind? secretSide = viewer == PlayerSide.Runner && states.Any(state => state.RulesState?.SecretIdentity is { Flipped: false })
            ? SecretIdentitySides[random.NextInt(SecretIdentitySides.Length)] : null;
        ulong randomState = random.NextUInt64();
        return [.. states.Select(state => new GameState(state.SchemaVersion, state.GameId, state.RulesVersion, state.CardDataVersion, state.Players,
            [.. state.Cards.Select(card => (dealt.TryGetValue(card.Id, out CardDefinitionId definition) ? definition
                    : known.TryGetValue(card.Id, out CardDefinitionId shown) ? shown : card.DefinitionId) is var identity && identity != card.DefinitionId
                ? new CardInstanceState(card.Id, identity, card.ZoneId, card.OwnerSide, card.FaceUp, card.Rezzed, card.AdvancementCounters, card.HostCardId)
                : card)],
            state.Zones, state.Servers, state.Turn, state.Outcome, randomState, state.Allocator,
            state.DecisionRevision, state.EventSequence, state.ResolutionStack, state.PendingDecision, state.ActionSequence,
            state.InitialSeed, state.RunState,
            secretSide is { } side && state.RulesState?.SecretIdentity is { Flipped: false }
                ? state.RulesState.WithSecretIdentity(new SecretIdentityState(side, Flipped: false)) : state.RulesState,
            state.BehaviorRegistryVersion, state.BehaviorCatalogFingerprint,
            state.RuntimeCardCatalogVersion, state.RuntimeCardCatalogFingerprint).MarkSimulated())];
    }

    /// <summary>
    /// Bot plan S4.2c: a simulation state with only its random state replaced, so a play-out from it differs only where the
    /// engine draws (shuffles, random accesses): the play-out's own noise, every card where it was.
    /// </summary>
    public static GameState Reseeded(GameState state, ulong randomState)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.IsSimulated) throw new ArgumentException("Only a simulation state is reseeded.", nameof(state));
        return new GameState(state.SchemaVersion, state.GameId, state.RulesVersion, state.CardDataVersion, state.Players,
            state.Cards, state.Zones, state.Servers, state.Turn, state.Outcome, randomState, state.Allocator,
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

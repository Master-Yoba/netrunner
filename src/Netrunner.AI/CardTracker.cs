using System.Collections.Immutable;
using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>Where a tracked opponent card is believed to be (A9a).</summary>
public enum TrackedPlace
{
    /// <summary>The opponent's hand (HQ or the grip).</summary>
    Hand,
    /// <summary>The opponent's deck (R&amp;D or the stack) at a known depth from the top.</summary>
    DeckTop,
    /// <summary>The opponent's deck at an unknown depth.</summary>
    Deck,
    /// <summary>Archives, face down.</summary>
    FaceDownDiscard,
    /// <summary>A face-down installed slot: a server's root or ICE at an index (installs append, so indices are stable while a zone grows).</summary>
    Slot,
}

/// <summary>
/// A share of belief that one card of a definition is in a place. Several entries may share a definition: two copies, or
/// one copy whose location is uncertain (the weight of an HQ card split between HQ and a slot it may have gone to).
/// Weights are probabilities; what a card's entries don't cover is unknown.
/// </summary>
public sealed record TrackedCard(string DefinitionId, TrackedPlace Place, double Weight, int SeenTurn,
    int Depth = 0, string? ServerId = null, string? Zone = null, int Index = 0)
{
    public bool At(TrackedPlace place, string? serverId, string? zone, int index) =>
        Place == place && (place != TrackedPlace.Slot || ServerId == serverId && Zone == zone && Index == index);
}

/// <summary>What the tracker last saw of the public state, to tell what changed since.</summary>
public sealed record TrackerSnapshot(int Hand, int Deck, int FaceDownDiscard, ImmutableArray<string> FaceUpDiscard,
    ImmutableArray<TrackerZoneSnapshot> Zones, ImmutableArray<string> VisibleIds, string? RunId, string? RunServer,
    ImmutableArray<TrackerAccess> Accessed);

/// <summary>A card accessed in the current run, in access order: the instance, while it stays in view, and its definition.</summary>
public sealed record TrackerAccess(string Id, string DefinitionId);

/// <summary>One server zone as last seen: its definitions by index, null where face down.</summary>
public sealed record TrackerZoneSnapshot(string ServerId, string Zone, ImmutableArray<string?> Cards);

/// <summary>How much a tracker keeps: Easy forgets on purpose (A9, decided 2 October).</summary>
public sealed record TrackerMemory(int Capacity, int ExpiryTurns)
{
    /// <summary>Standard and Hard: everything seen, forgotten when no public event has confirmed it for six turns.</summary>
    public static TrackerMemory Full { get; } = new(256, 6);
    /// <summary>Easy: a few cards, and what it knows of hidden zones lasts one opponent turn.</summary>
    public static TrackerMemory Forgetful { get; } = new(4, 1);
}

/// <summary>
/// A9a: one bot's beliefs about where the opponent's cards are, built only from its own observations. Generic over cards:
/// it follows public changes between observations (hidden-zone counts, public zones, face-down slots, accesses) and the
/// declared behaviour of cards resolving; a movement it can't follow forgets, never guesses.
/// </summary>
public sealed record CardTracker(ImmutableArray<TrackedCard> Cards, TrackerSnapshot? Last)
{
    public static CardTracker Empty { get; } = new([], null);

    private const double Epsilon = 1e-6;

    /// <summary>The expected number of cards of each definition in a place, from tracked entries only.</summary>
    public double Expected(string definitionId, TrackedPlace place) =>
        Cards.Where(card => card.DefinitionId == definitionId && card.Place == place).Sum(card => card.Weight);

    /// <summary>The tracked share of belief for each definition in a face-down slot; the rest is unknown.</summary>
    public IReadOnlyDictionary<string, double> SlotBelief(string serverId, string zone, int index) =>
        Cards.Where(card => card.At(TrackedPlace.Slot, serverId, zone, index))
            .GroupBy(card => card.DefinitionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(card => card.Weight), StringComparer.Ordinal);

    /// <summary>The known card at a depth of the opponent's deck, if any.</summary>
    public string? DeckTopAt(int depth) => Cards.Where(card => card.Place == TrackedPlace.DeckTop && card.Depth == depth)
        .OrderByDescending(card => card.Weight).FirstOrDefault()?.DefinitionId;

    /// <summary>
    /// Updates the beliefs from an observation of <paramref name="features"/>'s side. <paramref name="eligible"/> says
    /// which of the opponent's unknown cards could fill a face-down slot (roots take agendas, assets and upgrades; ICE
    /// positions take ICE), as a share of the opponent's deck from the tier's knowledge (the pool, or Hard's list).
    /// </summary>
    public CardTracker Observe(AgentFeatures features, ICardEvaluationCatalog catalog, Func<string, double> eligible,
        TrackerMemory memory)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(eligible);
        ArgumentNullException.ThrowIfNull(memory);
        var work = new Work(features, catalog, eligible, [.. Cards]);
        TrackerSnapshot now = work.Snapshot();
        if (Last is { } last) work.Apply(last, now);
        work.RecordAccesses(Last, now);
        work.Decay(memory);
        return new([.. work.Cards.Where(card => card.Weight > Epsilon)
            .OrderBy(card => card.Place).ThenBy(card => card.ServerId, StringComparer.Ordinal).ThenBy(card => card.Zone, StringComparer.Ordinal)
            .ThenBy(card => card.Index).ThenBy(card => card.Depth).ThenBy(card => card.DefinitionId, StringComparer.Ordinal)
            .Select(card => card with { Weight = Math.Round(card.Weight, 6) })], now);
    }

    private sealed class Work(AgentFeatures features, ICardEvaluationCatalog catalog, Func<string, double> eligible, List<TrackedCard> cards)
    {
        public List<TrackedCard> Cards { get; } = cards;
        private readonly bool _corp = features.Side == PlayerSide.Runner;
        private int Turn => features.Board.Turn.TurnNumber;

        public TrackerSnapshot Snapshot()
        {
            AgentBoard board = features.Board;
            int hand = _corp ? board.HiddenZoneCounts.CorpHand : board.HiddenZoneCounts.RunnerGrip;
            int deck = _corp ? board.HiddenZoneCounts.CorpDeck : board.HiddenZoneCounts.RunnerStack;
            ImmutableArray<AgentCard> faceUp = _corp ? board.CorpDiscard : board.RunnerHeap;
            int faceDown = _corp ? Math.Max(0, (board.ZoneCounts?.CorpDiscard ?? faceUp.Length) - faceUp.Length) : 0;
            ImmutableArray<TrackerZoneSnapshot> zones = _corp
                ? [.. board.Servers.SelectMany(server => new[]
                {
                    new TrackerZoneSnapshot(server.Id, "ice", [.. server.Ice.Select(card => card?.DefinitionId)]),
                    new TrackerZoneSnapshot(server.Id, "root", [.. server.Root.Select(card => card?.DefinitionId)]),
                })]
                : [];
            ImmutableArray<string> visible = [.. features.VisibleCards().Where(pair => Owner(pair.Card) == (_corp ? PlayerSide.Corp : PlayerSide.Runner))
                .Select(pair => pair.Card.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
            AgentRun? run = board.Run;
            return new(hand, deck, faceDown, [.. faceUp.Select(card => card.DefinitionId)], zones, visible, run?.Id, run?.AttackedServer,
                [.. run?.Breach.Accessed.OfType<AgentCard>().Select(card => new TrackerAccess(card.Id, card.DefinitionId)) ?? []]);
        }

        private PlayerSide? Owner(AgentCard card) => catalog.TryGet(new CardDefinitionId(card.DefinitionId), out CardEvaluationDescriptor definition)
            ? definition.Printed.Side : null;

        public void Apply(TrackerSnapshot last, TrackerSnapshot now)
        {
            // The hand's flows since the last observation: what left it (face-down installs, face-up plays and installs,
            // face-down discards, cards put into the deck) and what came in (draws). Only as many cards can have left it
            // as it lost beyond what the deck could have given it.
            int deckChange = now.Deck - last.Deck;
            int maxDraws = Math.Max(0, -deckChange);
            int outflow = 0;
            int Available() => last.Hand + maxDraws - now.Hand - outflow;

            // Face-down slots: a removal forgets its zone (it moves the later cards); a new slot takes a card from the hand.
            foreach (TrackerZoneSnapshot zone in now.Zones)
            {
                ImmutableArray<string?> old = last.Zones.FirstOrDefault(value => value.ServerId == zone.ServerId && value.Zone == zone.Zone)?.Cards ?? [];
                if (zone.Cards.Length < old.Length)
                {
                    Cards.RemoveAll(card => card.Place == TrackedPlace.Slot && card.ServerId == zone.ServerId && card.Zone == zone.Zone);
                    continue;
                }
                for (int index = 0; index < zone.Cards.Length; index++)
                {
                    string? seen = zone.Cards[index];
                    bool added = index >= old.Length;
                    if (seen is not null)
                    {
                        // A face-down slot turned face up (rezzed, accessed or revealed): the card is known.
                        if (!added && old[index] is null) Revealed(zone.ServerId, zone.Zone, index, seen);
                        continue;
                    }
                    if (added && Available() > 0)
                    {
                        InstallFromHand(zone.ServerId, zone.Zone, index, last.Hand - outflow);
                        outflow++;
                    }
                }
            }

            // Cards played or installed face up: newly visible cards of this side in a play area, the discard, the rig, or
            // appended to a server zone. Each that the hand can account for came from it.
            ImmutableArray<string> fresh = [.. now.VisibleIds.Except(last.VisibleIds, StringComparer.Ordinal)];
            foreach (string definition in features.VisibleCards().Where(pair => fresh.Contains(pair.Card.Id) && FromHand(last, pair))
                .GroupBy(pair => pair.Card.Id, StringComparer.Ordinal).Select(group => group.First().Card.DefinitionId).ToArray())
            {
                if (Available() <= 0) break;
                TakeShare(definition, TrackedPlace.Hand);
                outflow++;
            }

            int faceDownIn = now.FaceDownDiscard - last.FaceDownDiscard;
            if (faceDownIn > 0 && Available() > 0)
            {
                int discarded = Math.Min(faceDownIn, Available());
                MoveShare(TrackedPlace.Hand, TrackedPlace.FaceDownDiscard, discarded, Math.Max(1, last.Hand - outflow));
                outflow += discarded;
            }

            // Cards coming into the deck, or a resolving search or shuffle, lose its order. They come from face-up Archives
            // (Spin Doctor), the hand, or face-down Archives.
            if (deckChange > 0 || ShuffleResolving())
            {
                LoseDeckOrder();
                int rest = Math.Max(0, deckChange);
                List<string> leftFaceUp = Multiset(last.FaceUpDiscard).Subtract(Multiset(now.FaceUpDiscard));
                foreach (string definition in leftFaceUp.Take(rest)) Cards.Add(new(definition, TrackedPlace.Deck, 1, Turn));
                rest -= Math.Min(rest, leftFaceUp.Count);
                int handIntoDeck = Math.Min(rest, Math.Max(0, last.Hand - outflow - now.Hand));
                if (handIntoDeck > 0)
                {
                    MoveShare(TrackedPlace.Hand, TrackedPlace.Deck, handIntoDeck, Math.Max(1, last.Hand - outflow));
                    outflow += handIntoDeck;
                    rest -= handIntoDeck;
                }
                int faceDownOut = last.FaceDownDiscard - now.FaceDownDiscard;
                if (rest > 0 && faceDownOut > 0)
                    MoveShare(TrackedPlace.FaceDownDiscard, TrackedPlace.Deck, Math.Min(rest, faceDownOut), Math.Max(1, last.FaceDownDiscard));
            }

            // Draws: what the hand gained beyond its outflows came from the deck's top. Anything else the deck lost went
            // elsewhere (milled or trashed).
            if (deckChange < 0)
            {
                int drawn = Math.Clamp(now.Hand - (last.Hand - outflow), 0, -deckChange);
                Draw(drawn, last.Deck);
                Draw(-deckChange - drawn, last.Deck - drawn, toHand: false);
            }

            // A card that left face-down Archives face up was one of them.
            if (last.FaceDownDiscard > now.FaceDownDiscard)
                foreach (string definition in Multiset(now.FaceUpDiscard).Subtract(Multiset(last.FaceUpDiscard)))
                    TakeShare(definition, TrackedPlace.FaceDownDiscard);
        }

        /// <summary>Whether a newly visible card can have come from the hand: played, discarded or installed face up.</summary>
        private bool FromHand(TrackerSnapshot last, (AgentCard Card, string Location) pair) => pair.Location switch
        {
            "corp-play" or "runner-play" or "corp-discard" or "runner-heap" or "runner-rig" => true,
            _ when pair.Location.StartsWith("server/", StringComparison.Ordinal) => IsNewFaceUpInstall(last, pair),
            _ => false,
        };

        private bool IsNewFaceUpInstall(TrackerSnapshot last, (AgentCard Card, string Location) pair)
        {
            // A face-up install appends a visible card to a zone that was shorter before.
            if (!pair.Location.StartsWith("server/", StringComparison.Ordinal)) return false;
            string[] parts = pair.Location.Split('/');
            TrackerZoneSnapshot? before = last.Zones.FirstOrDefault(zone => zone.ServerId == parts[1] && zone.Zone == parts[2]);
            AgentServer? server = features.Board.Servers.FirstOrDefault(value => value.Id == parts[1]);
            ImmutableArray<AgentCard?> cards = parts[2] == "ice" ? server?.Ice ?? [] : server?.Root ?? [];
            int index = cards.IndexOf(pair.Card);
            return index >= (before?.Cards.Length ?? 0);
        }

        /// <summary>Whether a card resolving now declares a shuffle or a search of the opponent's deck.</summary>
        private bool ShuffleResolving()
        {
            IEnumerable<AgentCard> resolving = _corp ? features.Board.CorpPlayArea : features.Board.RunnerPlayArea;
            return resolving.Any(card => catalog.TryGet(new CardDefinitionId(card.DefinitionId), out CardEvaluationDescriptor definition)
                && definition.Behavior is { } behavior
                && HeuristicContext.Flatten(behavior.Instructions).Any(instruction => instruction.Kind is BehaviorInstructionKind.ShuffleIntoDeck
                    or BehaviorInstructionKind.Search));
        }

        private void InstallFromHand(string serverId, string zone, int index, int handSize)
        {
            // Each hand card that can go here shares the install by how likely it is to be the one.
            List<int> candidates = [.. Enumerable.Range(0, Cards.Count).Where(i => Cards[i].Place == TrackedPlace.Hand && Fits(Cards[i].DefinitionId, zone))];
            double known = candidates.Sum(i => Cards[i].Weight);
            double unknownCards = Math.Max(0, handSize - Cards.Where(card => card.Place == TrackedPlace.Hand).Sum(card => card.Weight));
            double expectedFitting = known + unknownCards * eligible(zone);
            if (expectedFitting <= Epsilon) return;
            foreach (int i in candidates)
            {
                TrackedCard card = Cards[i];
                double share = card.Weight / Math.Max(1, expectedFitting);
                Cards[i] = card with { Weight = card.Weight - share };
                Cards.Add(card with { Place = TrackedPlace.Slot, ServerId = serverId, Zone = zone, Index = index, Weight = share });
            }
        }

        private void Revealed(string serverId, string zone, int index, string definition)
        {
            // The slot is this card: shares of other cards that thought they were here go back to the hand.
            for (int i = Cards.Count - 1; i >= 0; i--)
            {
                TrackedCard card = Cards[i];
                if (!card.At(TrackedPlace.Slot, serverId, zone, index)) continue;
                Cards.RemoveAt(i);
                if (card.DefinitionId != definition) AddShare(card with { Place = TrackedPlace.Hand, ServerId = null, Zone = null, Index = 0 });
                else TakeShare(definition, TrackedPlace.Hand, 1 - card.Weight);
            }
        }

        private bool Fits(string definitionId, string zone) => catalog.TryGet(new CardDefinitionId(definitionId), out CardEvaluationDescriptor definition)
            && (zone == "ice" ? definition.Printed.CardClass == RuntimeCardClass.Ice
                : definition.Printed.CardClass is RuntimeCardClass.Agenda or RuntimeCardClass.Asset or RuntimeCardClass.Upgrade);

        /// <summary>Removes up to <paramref name="amount"/> of a definition's weight in a place, largest entries first.</summary>
        private double TakeShare(string definition, TrackedPlace place, double amount = 1)
        {
            double taken = 0;
            foreach (int i in Enumerable.Range(0, Cards.Count).Where(i => Cards[i].DefinitionId == definition && Cards[i].Place == place)
                .OrderByDescending(i => Cards[i].Weight).ToArray())
            {
                if (taken >= amount - Epsilon) break;
                double take = Math.Min(Cards[i].Weight, amount - taken);
                Cards[i] = Cards[i] with { Weight = Cards[i].Weight - take };
                taken += take;
            }
            return taken;
        }

        private void AddShare(TrackedCard card)
        {
            int existing = Cards.FindIndex(value => value.DefinitionId == card.DefinitionId && value.At(card.Place, card.ServerId, card.Zone, card.Index)
                && value.Depth == card.Depth);
            if (existing >= 0) Cards[existing] = Cards[existing] with { Weight = Math.Min(1, Cards[existing].Weight + card.Weight) };
            else Cards.Add(card);
        }

        /// <summary>Moves <paramref name="count"/> of <paramref name="size"/> unidentified cards' worth of belief between places.</summary>
        private void MoveShare(TrackedPlace from, TrackedPlace to, int count, int size)
        {
            double fraction = Math.Min(1, count / (double)size);
            for (int i = 0; i < Cards.Count; i++)
            {
                if (Cards[i].Place != from) continue;
                TrackedCard card = Cards[i];
                double moved = card.Weight * fraction;
                Cards[i] = card with { Weight = card.Weight - moved };
                Cards.Add(card with { Place = to, Weight = moved, Depth = 0 });
            }
        }

        private void Draw(int count, int deckSize, bool toHand = true)
        {
            if (count <= 0) return;
            for (int i = 0; i < Cards.Count; i++)
            {
                TrackedCard card = Cards[i];
                if (card.Place == TrackedPlace.DeckTop)
                {
                    if (card.Depth < count)
                        Cards[i] = toHand ? card with { Place = TrackedPlace.Hand, Depth = 0, SeenTurn = Turn } : card with { Weight = 0 };
                    else Cards[i] = card with { Depth = card.Depth - count };
                }
                else if (card.Place == TrackedPlace.Deck && toHand)
                {
                    // A card at an unknown depth is drawn in proportion.
                    double drawn = card.Weight * Math.Min(1, count / (double)Math.Max(1, deckSize));
                    Cards[i] = card with { Weight = card.Weight - drawn };
                    Cards.Add(card with { Place = TrackedPlace.Hand, Weight = drawn });
                }
            }
        }

        private void LoseDeckOrder()
        {
            for (int i = 0; i < Cards.Count; i++)
                if (Cards[i].Place == TrackedPlace.DeckTop) Cards[i] = Cards[i] with { Place = TrackedPlace.Deck, Depth = 0 };
        }

        /// <summary>Accessed cards are known where they stay: R&amp;D's top in access order, HQ, or a root slot (by the slot snapshot).</summary>
        public void RecordAccesses(TrackerSnapshot? last, TrackerSnapshot now)
        {
            // When a run ends, the cards it accessed in R&D or HQ that aren't public now stayed there.
            if (last?.RunId is not { } runId || now.RunId == runId || last.RunServer is not { } serverId) return;
            AgentServer? server = features.Board.Servers.FirstOrDefault(value => value.Id == serverId);
            if (server?.Kind is not (ServerKind.RAndD or ServerKind.Headquarters)) return;
            // An accessed card still in view when the run ends was stolen, trashed or otherwise made public; the rest stayed
            // where they were (the same card, followed while it was in view, as a player would).
            List<string> stayed = [.. last.Accessed.Where(card => !now.VisibleIds.Contains(card.Id)).Select(card => card.DefinitionId)];
            if (server.Kind == ServerKind.RAndD)
            {
                Cards.RemoveAll(card => card.Place == TrackedPlace.DeckTop && card.Depth < stayed.Count);
                for (int depth = 0; depth < stayed.Count; depth++)
                {
                    TakeShare(stayed[depth], TrackedPlace.Deck);
                    Cards.Add(new(stayed[depth], TrackedPlace.DeckTop, 1, Turn, Depth: depth));
                }
            }
            else
                foreach (string definition in stayed)
                {
                    double known = Cards.Where(card => card.DefinitionId == definition && card.Place == TrackedPlace.Hand).Sum(card => card.Weight);
                    if (known < 1) AddShare(new(definition, TrackedPlace.Hand, 1 - known, Turn));
                    for (int i = 0; i < Cards.Count; i++)
                        if (Cards[i].DefinitionId == definition && Cards[i].Place == TrackedPlace.Hand) Cards[i] = Cards[i] with { SeenTurn = Turn };
                }
        }

        public void Decay(TrackerMemory memory)
        {
            Cards.RemoveAll(card => card.Place != TrackedPlace.Slot && Turn - card.SeenTurn > memory.ExpiryTurns);
            if (Cards.Count <= memory.Capacity) return;
            List<TrackedCard> kept = [.. Cards.OrderByDescending(card => card.SeenTurn).ThenByDescending(card => card.Weight).Take(memory.Capacity)];
            Cards.Clear();
            Cards.AddRange(kept);
        }
    }

    private static List<string> Multiset(IEnumerable<string> values) => [.. values];
}

internal static class MultisetExtensions
{
    /// <summary>The values of <paramref name="left"/> not matched one for one in <paramref name="right"/>.</summary>
    public static List<string> Subtract(this List<string> left, List<string> right)
    {
        List<string> result = [.. left];
        foreach (string value in right) result.Remove(value);
        return result;
    }

    public static List<string> Intersect(this List<string> left, List<string> right)
    {
        List<string> pool = [.. right], result = [];
        foreach (string value in left)
            if (pool.Remove(value)) result.Add(value);
        return result;
    }
}

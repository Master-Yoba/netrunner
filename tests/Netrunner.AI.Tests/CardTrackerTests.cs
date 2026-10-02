using System.Collections.Immutable;
using Netrunner.Core;
using Xunit;

namespace Netrunner.AI.Tests;

/// <summary>A9a: the tracker follows public changes, and a guess from memory scales with the unknown cards around it.</summary>
public sealed class CardTrackerTests
{
    private const string Tithe = "test-ice-tithe", Hedge = "test-operation-hedge", Offworld = "test-agenda-offworld";

    [Fact]
    public void AnIceInstallIsASeenIceInProportionToTheUnknownCardsBesideIt()
    {
        // HQ of five: Tithe seen in it, four unknown; a third of the Corp's deck is ICE.
        CardTracker tracker = Seen(Tithe, hand: 5);
        tracker = tracker.Observe(Board(hand: 4, ice: [null]), Catalog, zone => zone == "ice" ? 1 / 3.0 : 0.5, TrackerMemory.Full);

        double tithe = tracker.SlotBelief("server:hq", "ice", 0).GetValueOrDefault(Tithe);
        // One known ICE plus four unknown cards, a third of them ICE: 1 / (1 + 4/3).
        Assert.Equal(1 / (1 + 4 / 3.0), tithe, 3);
        Assert.Equal(1 - tithe, tracker.Expected(Tithe, TrackedPlace.Hand), 3);
    }

    [Fact]
    public void WithNothingElseInHqTheInstallIsTheSeenIce()
    {
        CardTracker tracker = Seen(Tithe, hand: 1).Observe(Board(hand: 0, ice: [null]), Catalog, _ => 1 / 3.0, TrackerMemory.Full);
        Assert.Equal(1, tracker.SlotBelief("server:hq", "ice", 0).GetValueOrDefault(Tithe), 3);
    }

    [Fact]
    public void AnOperationIsNeverGuessedIntoASlot()
    {
        CardTracker tracker = Seen(Hedge, hand: 2).Observe(Board(hand: 1, ice: [null]), Catalog, _ => 1 / 3.0, TrackerMemory.Full);
        Assert.Empty(tracker.SlotBelief("server:hq", "ice", 0));
        Assert.Equal(1, tracker.Expected(Hedge, TrackedPlace.Hand), 3);
    }

    [Fact]
    public void ARezzedSlotThatIsAnotherCardReturnsTheGuessToHq()
    {
        CardTracker tracker = Seen(Tithe, hand: 2).Observe(Board(hand: 1, ice: [null]), Catalog, _ => 1 / 3.0, TrackerMemory.Full);
        tracker = tracker.Observe(Board(hand: 1, ice: ["test-ice-other"]), Catalog, _ => 1 / 3.0, TrackerMemory.Full);
        Assert.Equal(1, tracker.Expected(Tithe, TrackedPlace.Hand), 3);
        Assert.Empty(tracker.SlotBelief("server:hq", "ice", 0));
    }

    [Fact]
    public void ADrawTakesTheKnownTopOfRdIntoHq()
    {
        var tracker = new CardTracker([new(Offworld, TrackedPlace.DeckTop, 1, 1)], Snapshot(hand: 4, deck: 20));
        tracker = tracker.Observe(Board(hand: 5, deck: 19), Catalog, _ => 1 / 3.0, TrackerMemory.Full);
        Assert.Equal(1, tracker.Expected(Offworld, TrackedPlace.Hand), 3);
        Assert.Null(tracker.DeckTopAt(0));
    }

    [Fact]
    public void ASeenCardIsForgottenWhenNothingConfirmsItAndEasyForgetsSooner()
    {
        CardTracker seen = Seen(Tithe, hand: 5);
        Assert.Equal(1, seen.Observe(Board(hand: 5, turn: 7), Catalog, _ => 1 / 3.0, TrackerMemory.Full).Expected(Tithe, TrackedPlace.Hand), 3);
        Assert.Equal(0, seen.Observe(Board(hand: 5, turn: 8), Catalog, _ => 1 / 3.0, TrackerMemory.Full).Expected(Tithe, TrackedPlace.Hand), 3);
        Assert.Equal(0, seen.Observe(Board(hand: 5, turn: 3), Catalog, _ => 1 / 3.0, TrackerMemory.Forgetful).Expected(Tithe, TrackedPlace.Hand), 3);
    }

    [Fact]
    public void CopiesAreCountedFromWhereTheyAreNowSoRecursionReturnsThem()
    {
        // Hard's list: three Hedge Funds. One in face-up Archives is not unseen; shuffled back into R&D (Spin Doctor), it is.
        var list = TrackerPrior.List([(Hedge, 3), (Tithe, 2)]);
        AgentFeatures inArchives = Board(hand: 5) with { Board = Board(hand: 5).Board with { CorpDiscard = [new AgentCard { Id = "card:hedge-1", DefinitionId = Hedge }] } };
        Assert.Equal(2, TrackerPrior.Remaining(list, inArchives)[Hedge]);
        Assert.Equal(3, TrackerPrior.Remaining(list, Board(hand: 5))[Hedge]);
        Assert.Equal(2, TrackerPrior.Remaining(list, inArchives)[Tithe]);
    }

    /// <summary>A tracker that saw <paramref name="definition"/> in HQ on turn 1, and last saw HQ of <paramref name="hand"/> cards and no installs.</summary>
    private static CardTracker Seen(string definition, int hand) =>
        new([new(definition, TrackedPlace.Hand, 1, 1)], Snapshot(hand, deck: 20));

    private static TrackerSnapshot Snapshot(int hand, int deck) => new(hand, deck, 0, [],
        [new("server:hq", "ice", []), new("server:hq", "root", [])], [], null, null, []);

    private static AgentFeatures Board(int hand, int deck = 20, string?[]? ice = null, int turn = 1) => new(PlayerSide.Runner, null, [],
        new AgentBoard
        {
            Turn = new(PlayerSide.Corp, TurnPhase.Corp, turn, null),
            Players = new(new("corp", 5, 3, 0, 5, null, null, null, null), new("runner", 5, 0, 0, 5, 0, 0, 0, 4)),
            Outcome = new(GameOutcomeKind.Ongoing, null, null),
            HiddenZoneCounts = new(hand, deck, 5, 30),
            Servers = [new("server:hq", ServerKind.Headquarters,
                [.. (ice ?? []).Select(id => id is null ? null : new AgentCard { Id = "card:" + id, DefinitionId = id })], [])],
        },
        new AgentPrivateCards());

    private static readonly TestCatalog Catalog = new();

    private sealed class TestCatalog : ICardEvaluationCatalog
    {
        private static readonly Dictionary<string, RuntimeCardClass> Classes = new(StringComparer.Ordinal)
        {
            [Tithe] = RuntimeCardClass.Ice,
            ["test-ice-other"] = RuntimeCardClass.Ice,
            [Hedge] = RuntimeCardClass.Operation,
            [Offworld] = RuntimeCardClass.Agenda,
        };

        public string Version => "test";
        public string Fingerprint => "test";
        public IReadOnlyCollection<CardDefinitionId> DefinitionIds => [.. Classes.Keys.Select(key => new CardDefinitionId(key))];

        public bool TryGet(CardDefinitionId definitionId, out CardEvaluationDescriptor descriptor)
        {
            descriptor = null!;
            if (!Classes.TryGetValue(definitionId.Value, out RuntimeCardClass cardClass)) return false;
            CardRuntimeSpec printed = cardClass switch
            {
                RuntimeCardClass.Ice => new(definitionId, PlayerSide.Corp, cardClass, rezCost: 1),
                RuntimeCardClass.Agenda => new(definitionId, PlayerSide.Corp, cardClass, agendaPoints: 2, advancementRequirement: 3),
                _ => new(definitionId, PlayerSide.Corp, cardClass, playCost: 0),
            };
            descriptor = new(printed, null, null, null, null, null, null, [], false);
            return true;
        }
    }
}

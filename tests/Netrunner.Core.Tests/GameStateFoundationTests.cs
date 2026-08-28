using System.Text.Json;
using Xunit;

namespace Netrunner.Core.Tests;

public sealed class StableIdTests
{
    [Fact]
    public void EveryEngineIdSerializesAsItsStableString()
    {
        Assert.Equal("\"game:42\"", EngineJson.Serialize(new GameId("game:42")));
        Assert.Equal("\"definition:30001\"", EngineJson.Serialize(new CardDefinitionId("definition:30001")));
        Assert.Equal("\"card:7\"", EngineJson.Serialize(new CardInstanceId("card:7")));
        Assert.Equal("\"player:corp\"", EngineJson.Serialize(new PlayerId("player:corp")));
        Assert.Equal("\"server:remote-1\"", EngineJson.Serialize(new ServerId("server:remote-1")));
        Assert.Equal("\"zone:hq\"", EngineJson.Serialize(new ZoneId("zone:hq")));
        Assert.Equal("\"decision:3\"", EngineJson.Serialize(new DecisionId("decision:3")));
        Assert.Equal("\"action:draw\"", EngineJson.Serialize(new ActionId("action:draw")));
    }

    [Fact]
    public void InvalidIdentifiersAreRejectedBeforeTheyEnterState()
    {
        Assert.Throws<ArgumentException>(() => new GameId(""));
        Assert.Throws<ArgumentException>(() => new ZoneId("zone with spaces"));
        Assert.Throws<ArgumentException>(() => new ActionId("action:\n"));
        Assert.False(PlayerId.TryParse(null, out _));
        Assert.Throws<JsonException>(() => EngineJson.Deserialize<GameId>("17"));
        Assert.Throws<JsonException>(() => EngineJson.Deserialize<GameId>("\"bad id\""));
    }

    [Fact]
    public void CardOwnerIsRequiredByTheJsonContract()
    {
        var card = new CardInstanceState(
            new CardInstanceId("card:1"),
            new CardDefinitionId("definition:1"),
            new ZoneId("zone:corp"),
            PlayerSide.Corp);
        string json = EngineJson.Serialize(card);
        string missingOwner = json.Replace("\"ownerSide\":\"corp\",", string.Empty, StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => EngineJson.Deserialize<CardInstanceState>(missingOwner));
    }

    [Fact]
    public void AllocatorIsMonotonicAndRestorable()
    {
        var first = new StableIdAllocator(12);
        GameId game = first.NextGameId();
        PlayerId player = first.NextPlayerId();
        IdAllocatorState saved = first.State;

        var restored = new StableIdAllocator(saved);

        Assert.Equal("game:12", game.Value);
        Assert.Equal("player:13", player.Value);
        Assert.Equal(first.NextServerId(), restored.NextServerId());
        Assert.Equal(first.State, restored.State);
    }
}

public sealed class GameStateFoundationTests
{
    [Fact]
    public void NewStateSatisfiesAllFoundationInvariants()
    {
        GameState state = GameState.CreateNew(42);

        StateInvariantValidator.Validate(state);

        Assert.Equal(2, state.Players.Count);
        Assert.Equal(3, state.Servers.Count);
        Assert.Equal(15, state.Zones.Count);
        Assert.Equal(TurnPhase.Corp, state.Turn.Phase);
        Assert.Contains(state.Zones, zone => zone.Kind == ZoneKind.RunnerRig);
    }

    [Fact]
    public void ZeroRngStateIsValidInAnAuthoritativeGameState()
    {
        GameState state = GameState.CreateNew(0);

        StateInvariantValidator.Validate(state);

        Assert.Equal(0UL, state.RandomState);
    }

    [Fact]
    public void StateCollectionsAreDetachedAndOrderingIsStable()
    {
        var players = new List<PlayerState>
        {
            new(new PlayerId("player:runner"), PlayerSide.Runner),
            new(new PlayerId("player:corp"), PlayerSide.Corp),
        };
        var cards = new List<CardInstanceState>();
        var zones = new List<ZoneState>
        {
            new(new ZoneId("zone:runner"), ZoneKind.RunnerStack, PlayerSide.Runner),
            new(new ZoneId("zone:corp"), ZoneKind.CorpDeck, PlayerSide.Corp),
        };
        var servers = new List<ServerState>();

        GameState state = new(
            new GameId("game:1"),
            "26.03",
            "fixture-1",
            players,
            cards,
            zones,
            servers,
            new TurnState(PlayerSide.Corp),
            new GameOutcome(),
            1,
            new IdAllocatorState(1));

        players.Clear();
        zones.Clear();

        Assert.Equal(2, state.Players.Count);
        Assert.Equal(["player:corp", "player:runner"], state.Players.Select(player => player.Id.Value));
        Assert.Equal(2, state.Zones.Count);
        Assert.Equal(["zone:corp", "zone:runner"], state.Zones.Select(zone => zone.Id.Value));
    }

    [Fact]
    public void SnapshotDeeplyDetachesSourceCollectionsAndRejectsMutation()
    {
        GameState state = GameState.CreateNew(99);
        GameSnapshot snapshot = state.CreateSnapshot();

        Assert.NotSame(state.Players, snapshot.Players);
        Assert.NotSame(state.Zones, snapshot.Zones);
        Assert.Equal(EngineJson.Serialize(state), EngineJson.Serialize(snapshot.ToState()));

        var snapshotZones = Assert.IsAssignableFrom<IList<ZoneState>>(snapshot.Zones);
        Assert.Throws<NotSupportedException>(() => snapshotZones.Add(
            new ZoneState(new ZoneId("zone:forbidden"), ZoneKind.CorpDeck, PlayerSide.Corp)));

        var cardIds = Assert.IsAssignableFrom<IList<CardInstanceId>>(snapshot.Zones[0].CardIds);
        Assert.Throws<NotSupportedException>(() => cardIds.Add(new CardInstanceId("card:forbidden")));
    }

    [Fact]
    public void StateJsonRoundTripsByteForByte()
    {
        GameState original = GameState.CreateNew(8675309);

        string json = EngineJson.Serialize(original);
        GameState restored = EngineJson.DeserializeState(json);

        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(original.GameId, restored.GameId);
        Assert.Equal(original.RandomState, restored.RandomState);
        Assert.Equal(original.Allocator, restored.Allocator);

        GameSnapshot snapshot = original.CreateSnapshot();
        GameSnapshot restoredSnapshot = EngineJson.Deserialize<GameSnapshot>(EngineJson.Serialize(snapshot));
        Assert.Equal(EngineJson.Serialize(snapshot), EngineJson.Serialize(restoredSnapshot));

        GameOutcome outcome = new(GameOutcomeKind.Won, PlayerSide.Runner, GameWinReason.DeckOut);
        Assert.Equal(outcome, EngineJson.Deserialize<GameOutcome>(EngineJson.Serialize(outcome)));
    }

    [Fact]
    public void UnknownStateFieldsAndMalformedSnapshotsFailClosed()
    {
        GameState state = GameState.CreateNew(4);
        string json = EngineJson.Serialize(state);
        string unknownField = json[..^1] + ",\"unexpected\":true}";

        Assert.Throws<JsonException>(() => EngineJson.DeserializeState(unknownField));
        Assert.Throws<JsonException>(() => EngineJson.DeserializeState("{}"));
        Assert.Throws<JsonException>(() => EngineJson.DeserializeSnapshot("{}"));
    }
}

public sealed class StateInvariantTests
{
    [Fact]
    public void ValidatorReportsActionableZoneAndTopologyViolations()
    {
        GameState valid = GameState.CreateNew(8);
        GameState invalid = new(
            valid.GameId,
            valid.RulesVersion,
            valid.CardDataVersion,
            [new PlayerState(valid.Players[0].Id, PlayerSide.Corp, credits: -1), valid.Players[1]],
            valid.Cards,
            valid.Zones.Where(zone => zone.Kind != ZoneKind.ServerRoot),
            valid.Servers,
            valid.Turn,
            valid.Outcome,
            0,
            valid.Allocator);

        StateInvariantException exception = Assert.Throws<StateInvariantException>(
            () => StateInvariantValidator.Validate(invalid));

        Assert.Contains(exception.Violations, violation => violation.Code == "players.negative-credits");
        Assert.Contains(exception.Violations, violation => violation.Code == "servers.unknown-zone");
        Assert.All(exception.Violations, violation =>
        {
            Assert.NotEmpty(violation.Path);
            Assert.NotEmpty(violation.Message);
        });
    }

    [Fact]
    public void EachCardMustHaveExactlyOneMatchingZoneMembership()
    {
        GameState valid = GameState.CreateNew(10);
        ZoneState deck = valid.Zones.Single(zone => zone.Kind == ZoneKind.CorpDeck);
        var card = new CardInstanceState(
            new CardInstanceId("card:1"),
            new CardDefinitionId("definition:1"),
            deck.Id,
            PlayerSide.Corp);
        ZoneState withCard = new(deck.Id, deck.Kind, deck.OwnerSide, deck.ServerId, [card.Id, card.Id]);
        var zones = valid.Zones.Select(zone => zone.Id == deck.Id ? withCard : zone).ToArray();

        GameState invalid = new(
            valid.GameId,
            valid.RulesVersion,
            valid.CardDataVersion,
            valid.Players,
            [card],
            zones,
            valid.Servers,
            valid.Turn,
            valid.Outcome,
            valid.RandomState,
            valid.Allocator);

        StateInvariantException exception = Assert.Throws<StateInvariantException>(
            () => StateInvariantValidator.Validate(invalid));

        Assert.Contains(exception.Violations, violation => violation.Code == "cards.duplicate-membership");
        Assert.Contains(exception.Violations, violation => violation.Code == "cards.membership-count");
    }

    [Fact]
    public void ServerZonesMustBeDeclaredExactlyOnceByTheirServer()
    {
        GameState valid = GameState.CreateNew(11);
        ServerState server = valid.Servers[0];
        ZoneState orphan = new(
            new ZoneId("zone:extra-ice"),
            ZoneKind.ServerIce,
            PlayerSide.Corp,
            server.Id);

        GameState invalid = Rebuild(valid, zones: valid.Zones.Append(orphan));

        StateInvariantException exception = Assert.Throws<StateInvariantException>(
            () => StateInvariantValidator.Validate(invalid));

        Assert.Contains(exception.Violations, violation => violation.Code == "zones.unreferenced-server-zone");
    }

    [Fact]
    public void ServerZonesCannotReferToAnUnknownServer()
    {
        GameState valid = GameState.CreateNew(12);
        ZoneState orphan = new(
            new ZoneId("zone:unknown-ice"),
            ZoneKind.ServerIce,
            PlayerSide.Corp,
            new ServerId("server:missing"));

        GameState invalid = Rebuild(valid, zones: valid.Zones.Append(orphan));

        StateInvariantException exception = Assert.Throws<StateInvariantException>(
            () => StateInvariantValidator.Validate(invalid));

        Assert.Contains(exception.Violations, violation => violation.Code == "zones.unknown-server");
    }

    [Fact]
    public void ExactlyOneFoundationalZoneOfEachKindIsRequired()
    {
        GameState valid = GameState.CreateNew(13);
        ZoneState runnerRig = valid.Zones.Single(zone => zone.Kind == ZoneKind.RunnerRig);

        GameState missingRig = Rebuild(valid, zones: valid.Zones.Where(zone => zone != runnerRig));
        GameState duplicateCorpDeck = Rebuild(valid, zones: valid.Zones.Append(
            new ZoneState(new ZoneId("zone:second-corp-deck"), ZoneKind.CorpDeck, PlayerSide.Corp)));

        Assert.Contains(
            StateInvariantValidator.Collect(missingRig),
            violation => violation.Code == "zones.required-kind-count" && violation.Message.Contains("RunnerRig", StringComparison.Ordinal));
        Assert.Contains(
            StateInvariantValidator.Collect(duplicateCorpDeck),
            violation => violation.Code == "zones.required-kind-count" && violation.Message.Contains("CorpDeck", StringComparison.Ordinal));
    }

    [Fact]
    public void AllocatorCannotBeRestoredBehindAnIssuedNumericId()
    {
        GameState valid = GameState.CreateNew(14);
        GameState invalid = Rebuild(valid, allocator: new IdAllocatorState(1));

        StateInvariantException exception = Assert.Throws<StateInvariantException>(
            () => StateInvariantValidator.Validate(invalid));

        Assert.Contains(exception.Violations, violation => violation.Code == "allocator.behind");
    }

    [Fact]
    public void AllocatorCannotBeRestoredAtTheMaximumIssuedSequence()
    {
        GameState valid = GameState.CreateNew(16);
        IdAllocatorState collidingAllocator = new(valid.Allocator.NextSequence - 1);
        GameState invalid = Rebuild(valid, allocator: collidingAllocator);

        StateInvariantException exception = Assert.Throws<StateInvariantException>(
            () => StateInvariantValidator.Validate(invalid));

        Assert.Contains(exception.Violations, violation => violation.Code == "allocator.behind");
    }

    [Fact]
    public void OverflowingNumericIdsDoNotThrowDuringAllocatorValidation()
    {
        GameState valid = GameState.CreateNew(15);
        GameState overflow = new(
            new GameId("game:18446744073709551616"),
            valid.RulesVersion,
            valid.CardDataVersion,
            valid.Players,
            valid.Cards,
            valid.Zones,
            valid.Servers,
            valid.Turn,
            valid.Outcome,
            valid.RandomState,
            valid.Allocator);

        StateInvariantValidator.Validate(overflow);
    }

    [Fact]
    public void InvariantExceptionFreezesItsViolationList()
    {
        var violations = new List<StateInvariantViolation>
        {
            new("test", "state", "diagnostic"),
        };
        var exception = new StateInvariantException(violations);

        violations.Clear();

        Assert.Single(exception.Violations);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<StateInvariantViolation>)exception.Violations).Clear());
    }

    [Fact]
    public void OutcomeRequiresAConsistentTypedWinnerAndReason()
    {
        GameState valid = GameState.CreateNew(17);
        GameState ongoingWithWinner = Rebuild(
            valid,
            outcome: new GameOutcome(GameOutcomeKind.Ongoing, PlayerSide.Corp, GameWinReason.AgendaPoints));
        GameState wonWithoutReason = Rebuild(
            valid,
            turn: new TurnState(PlayerSide.Corp, TurnPhase.GameOver),
            outcome: new GameOutcome(GameOutcomeKind.Won, PlayerSide.Corp));
        GameState corpDeckOut = Rebuild(
            valid,
            turn: new TurnState(PlayerSide.Corp, TurnPhase.GameOver),
            outcome: new GameOutcome(GameOutcomeKind.Won, PlayerSide.Corp, GameWinReason.DeckOut));

        Assert.Contains(
            StateInvariantValidator.Collect(ongoingWithWinner),
            violation => violation.Code == "outcome.ongoing-winner");
        Assert.Contains(
            StateInvariantValidator.Collect(ongoingWithWinner),
            violation => violation.Code == "outcome.ongoing-reason");
        Assert.Contains(
            StateInvariantValidator.Collect(wonWithoutReason),
            violation => violation.Code == "outcome.missing-reason");
        Assert.Contains(
            StateInvariantValidator.Collect(corpDeckOut),
            violation => violation.Code == "outcome.deck-out-winner");
    }

    [Fact]
    public void CorpAndRunnerPhasesRequireTheirMatchingActiveSide()
    {
        GameState valid = GameState.CreateNew(18);
        GameState corpPhaseWithRunner = Rebuild(
            valid,
            turn: new TurnState(PlayerSide.Runner, TurnPhase.Corp));
        GameState runnerPhaseWithCorp = Rebuild(
            valid,
            turn: new TurnState(PlayerSide.Corp, TurnPhase.Runner));

        Assert.Contains(
            StateInvariantValidator.Collect(corpPhaseWithRunner),
            violation => violation.Code == "turn.phase-side-mismatch");
        Assert.Contains(
            StateInvariantValidator.Collect(runnerPhaseWithCorp),
            violation => violation.Code == "turn.phase-side-mismatch");
    }

    [Fact]
    public void SnapshotCreationRejectsAnInvalidSourceState()
    {
        GameState valid = GameState.CreateNew(19);
        ZoneState runnerRig = valid.Zones.Single(zone => zone.Kind == ZoneKind.RunnerRig);
        GameState invalid = Rebuild(valid, zones: valid.Zones.Where(zone => zone != runnerRig));

        Assert.Throws<StateInvariantException>(() => invalid.CreateSnapshot());
        Assert.Throws<StateInvariantException>(() => GameSnapshot.From(invalid));
    }

    private static GameState Rebuild(
        GameState state,
        IEnumerable<ZoneState>? zones = null,
        IdAllocatorState? allocator = null,
        TurnState? turn = null,
        GameOutcome? outcome = null) =>
        new(
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            state.Players,
            state.Cards,
            zones ?? state.Zones,
            state.Servers,
            turn ?? state.Turn,
            outcome ?? state.Outcome,
            state.RandomState,
            allocator ?? state.Allocator);
}

public sealed class DeterministicShuffleTests
{
    [Fact]
    public void FixedSeedMatchesGoldenPermutation()
    {
        var values = new List<string> { "A", "B", "C", "D", "E", "F", "G" };
        var random = new DeterministicRandom(42);

        random.Shuffle(values);

        Assert.Equal(["C", "E", "G", "A", "D", "B", "F"], values);
        Assert.Equal(13064056694810536104UL, random.State);
    }

    [Fact]
    public void ShuffleConsumesRestorableRngState()
    {
        var first = new DeterministicRandom(12345);
        var values = new List<int> { 0, 1, 2, 3, 4 };
        first.Shuffle(values);
        DeterministicRandom restored = DeterministicRandom.Restore(first.State);

        Assert.Equal(first.NextUInt64(), restored.NextUInt64());
    }

    [Fact]
    public void ZeroIsAValidSeedAndState()
    {
        var first = new DeterministicRandom(0);
        var restored = DeterministicRandom.Restore(0);

        Assert.Equal(first.NextUInt64(), restored.NextUInt64());
    }

    [Fact]
    public void StateCanCrossZeroAndStillBeRestored()
    {
        const ulong increment = 0x9E3779B97F4A7C15UL;
        var random = new DeterministicRandom(unchecked(0UL - increment));

        _ = random.NextUInt64();
        Assert.Equal(0UL, random.State);

        DeterministicRandom restored = DeterministicRandom.Restore(random.State);
        Assert.Equal(random.NextUInt64(), restored.NextUInt64());
    }
}

using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

public enum ZoneKind
{
    CorpDeck,
    CorpHand,
    CorpDiscard,
    CorpScore,
    RunnerStack,
    RunnerGrip,
    RunnerHeap,
    RunnerRig,
    RunnerScore,
    ServerIce,
    ServerRoot,
}

/// <summary>The stable topology kind of a Corp server.</summary>
public enum ServerKind
{
    Headquarters,
    RAndD,
    Archives,
    Remote,
}

public enum TurnPhase
{
    Setup,
    Corp,
    Runner,
    GameOver,
}

public enum GameOutcomeKind
{
    Ongoing,
    Won,
}

public enum GameWinReason
{
    AgendaPoints,
    DeckOut,
}

public sealed record PlayerState
{
    [JsonConstructor]
    public PlayerState(
        PlayerId id,
        PlayerSide side,
        int credits = 0,
        int clicks = 0,
        int agendaPoints = 0)
    {
        Id = id;
        Side = side;
        Credits = credits;
        Clicks = clicks;
        AgendaPoints = agendaPoints;
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Id { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide Side { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Credits { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Clicks { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int AgendaPoints { get; private set; }
}

public sealed record CardInstanceState
{
    [JsonConstructor]
    public CardInstanceState(
        CardInstanceId id,
        CardDefinitionId definitionId,
        ZoneId zoneId,
        PlayerSide ownerSide,
        bool faceUp = false,
        bool rezzed = false,
        int advancementCounters = 0)
    {
        if (!Enum.IsDefined(ownerSide))
        {
            throw new ArgumentOutOfRangeException(nameof(ownerSide), "Every card instance must have a valid owner side.");
        }

        Id = id;
        DefinitionId = definitionId;
        ZoneId = zoneId;
        OwnerSide = ownerSide;
        FaceUp = faceUp;
        Rezzed = rezzed;
        AdvancementCounters = advancementCounters;
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId Id { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardDefinitionId DefinitionId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneId ZoneId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide OwnerSide { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public bool FaceUp { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public bool Rezzed { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int AdvancementCounters { get; private set; }
}

public sealed record ZoneState
{
    [JsonConstructor]
    public ZoneState(
        ZoneId id,
        ZoneKind kind,
        PlayerSide? ownerSide = null,
        ServerId? serverId = null,
        IReadOnlyList<CardInstanceId>? cardIds = null)
    {
        Id = id;
        Kind = kind;
        OwnerSide = ownerSide;
        ServerId = serverId;
        CardIds = Freeze(cardIds);
    }

    [JsonInclude]
    [JsonRequired]
    public ZoneId Id { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneKind Kind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide? OwnerSide { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ServerId? ServerId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<CardInstanceId> CardIds { get; private set; }

    [JsonIgnore]
    public IReadOnlyList<CardInstanceId> Cards => CardIds;

    private static ReadOnlyCollection<CardInstanceId> Freeze(IEnumerable<CardInstanceId>? values) =>
        new ReadOnlyCollection<CardInstanceId>((values ?? []).ToArray());
}

public sealed record ServerState
{
    [JsonConstructor]
    public ServerState(
        ServerId id,
        ServerKind kind,
        ZoneId iceZoneId,
        ZoneId rootZoneId)
    {
        Id = id;
        Kind = kind;
        IceZoneId = iceZoneId;
        RootZoneId = rootZoneId;
    }

    [JsonInclude]
    [JsonRequired]
    public ServerId Id { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ServerKind Kind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneId IceZoneId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneId RootZoneId { get; private set; }
}

public sealed record TurnState
{
    [JsonConstructor]
    public TurnState(
        PlayerSide activeSide,
        TurnPhase phase = TurnPhase.Setup,
        int turnNumber = 1)
    {
        ActiveSide = activeSide;
        Phase = phase;
        TurnNumber = turnNumber;
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide ActiveSide { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public TurnPhase Phase { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int TurnNumber { get; private set; }
}

public sealed record GameOutcome
{
    [JsonConstructor]
    public GameOutcome(
        GameOutcomeKind kind = GameOutcomeKind.Ongoing,
        PlayerSide? winner = null,
        GameWinReason? reason = null)
    {
        Kind = kind;
        Winner = winner;
        Reason = reason;
    }

    [JsonInclude]
    [JsonRequired]
    public GameOutcomeKind Kind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide? Winner { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public GameWinReason? Reason { get; private set; }
}

/// <summary>
/// Complete deterministic engine state. The object is immutable at its public
/// boundary; future reducers create a replacement state and commit it only
/// after validating the replacement. No Godot/runtime service objects belong in
/// this aggregate.
/// </summary>
public sealed class GameState
{
    public const int CurrentSchemaVersion = 1;

    [JsonConstructor]
    public GameState(
        int schemaVersion,
        GameId gameId,
        string rulesVersion,
        string cardDataVersion,
        IReadOnlyList<PlayerState>? players,
        IReadOnlyList<CardInstanceState>? cards,
        IReadOnlyList<ZoneState>? zones,
        IReadOnlyList<ServerState>? servers,
        TurnState? turn,
        GameOutcome? outcome,
        ulong randomState,
        IdAllocatorState? allocator,
        long decisionRevision = 0,
        long eventSequence = 0,
        IReadOnlyList<ResolutionFrame>? resolutionStack = null,
        PendingDecision? pendingDecision = null,
        long actionSequence = 0,
        ulong initialSeed = 0)
    {
        ArgumentNullException.ThrowIfNull(rulesVersion);
        ArgumentNullException.ThrowIfNull(cardDataVersion);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(zones);
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(allocator);

        SchemaVersion = schemaVersion;
        GameId = gameId;
        RulesVersion = rulesVersion;
        CardDataVersion = cardDataVersion;
        Players = FreezeSorted(players, static player => player.Id.Value);
        Cards = FreezeSorted(cards, static card => card.Id.Value);
        Zones = FreezeSorted(zones, static zone => zone.Id.Value);
        Servers = FreezeSorted(
            servers,
            static server => $"{(int)server.Kind:D2}:{server.Id.Value}");
        Turn = turn;
        Outcome = outcome;
        InitialSeed = initialSeed;
        RandomState = randomState;
        Allocator = allocator;
        DecisionRevision = decisionRevision;
        EventSequence = eventSequence;
        ActionSequence = actionSequence;
        ResolutionStack = Freeze(resolutionStack);
        PendingDecision = pendingDecision?.Copy();
    }

    /// <summary>
    /// Convenience constructor for code constructing a complete state in tests
    /// or a setup builder. The attributed constructor above is the JSON contract.
    /// </summary>
    public GameState(
        GameId gameId,
        string rulesVersion,
        string cardDataVersion,
        IEnumerable<PlayerState>? players = null,
        IEnumerable<CardInstanceState>? cards = null,
        IEnumerable<ZoneState>? zones = null,
        IEnumerable<ServerState>? servers = null,
        TurnState? turn = null,
        GameOutcome? outcome = null,
        ulong randomState = 1,
        IdAllocatorState? allocator = null,
        int schemaVersion = CurrentSchemaVersion,
        long decisionRevision = 0,
        long eventSequence = 0,
        IEnumerable<ResolutionFrame>? resolutionStack = null,
        PendingDecision? pendingDecision = null,
        long actionSequence = 0,
        ulong? initialSeed = null)
        : this(
            schemaVersion,
            gameId,
            rulesVersion,
            cardDataVersion,
            players?.ToArray() ?? [],
            cards?.ToArray() ?? [],
            zones?.ToArray() ?? [],
            servers?.ToArray() ?? [],
            turn ?? new TurnState(PlayerSide.Corp),
            outcome ?? new GameOutcome(),
            randomState,
            allocator ?? new IdAllocatorState(),
            decisionRevision,
            eventSequence,
            resolutionStack?.ToArray() ?? [],
            pendingDecision,
            actionSequence,
            initialSeed ?? randomState)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public int SchemaVersion { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public GameId GameId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string RulesVersion { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string CardDataVersion { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<PlayerState> Players { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<CardInstanceState> Cards { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<ZoneState> Zones { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<ServerState> Servers { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public TurnState Turn { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public GameOutcome Outcome { get; private set; }

    /// <summary>The original match seed, retained separately from the stream cursor.</summary>
    [JsonInclude]
    [JsonRequired]
    public ulong InitialSeed { get; private set; }

    /// <summary>The exact stream state, not merely the original seed.</summary>
    [JsonInclude]
    [JsonRequired]
    public ulong RandomState { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IdAllocatorState Allocator { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public long DecisionRevision { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public long EventSequence { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public long ActionSequence { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<ResolutionFrame> ResolutionStack { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PendingDecision? PendingDecision { get; private set; }

    public GameSnapshot CreateSnapshot() => GameSnapshot.From(this);

    [JsonIgnore]
    public GameSnapshot Snapshot => CreateSnapshot();

    public PlayerState GetPlayer(PlayerId id) =>
        Players.FirstOrDefault(player => player.Id == id)
        ?? throw new KeyNotFoundException($"Player '{id}' does not exist in game '{GameId}'.");

    public ZoneState GetZone(ZoneId id) =>
        Zones.FirstOrDefault(zone => zone.Id == id)
        ?? throw new KeyNotFoundException($"Zone '{id}' does not exist in game '{GameId}'.");

    public ZoneState GetZone(ZoneKind kind) =>
        Zones.SingleOrDefault(zone => zone.Kind == kind && zone.ServerId is null)
        ?? throw new KeyNotFoundException($"Zone kind '{kind}' does not identify one non-server zone in game '{GameId}'.");

    public CardInstanceState GetCard(CardInstanceId id) =>
        Cards.FirstOrDefault(card => card.Id == id)
        ?? throw new KeyNotFoundException($"Card instance '{id}' does not exist in game '{GameId}'.");

    public ServerState GetServer(ServerId id) =>
        Servers.FirstOrDefault(server => server.Id == id)
        ?? throw new KeyNotFoundException($"Server '{id}' does not exist in game '{GameId}'.");

    public static GameState CreateNew(
        ulong seed,
        string rulesVersion = "26.03",
        string cardDataVersion = "fixture-1")
    {
        var allocator = new StableIdAllocator();
        GameId gameId = allocator.NextGameId();
        PlayerId corpId = allocator.NextPlayerId();
        PlayerId runnerId = allocator.NextPlayerId();

        var servers = new List<ServerState>();
        var zones = new List<ZoneState>();

        AddServer(ServerKind.Headquarters);
        AddServer(ServerKind.RAndD);
        AddServer(ServerKind.Archives);

        zones.Add(new ZoneState(allocator.NextZoneId(), ZoneKind.CorpDeck, PlayerSide.Corp));
        zones.Add(new ZoneState(allocator.NextZoneId(), ZoneKind.CorpHand, PlayerSide.Corp));
        zones.Add(new ZoneState(allocator.NextZoneId(), ZoneKind.CorpDiscard, PlayerSide.Corp));
        zones.Add(new ZoneState(allocator.NextZoneId(), ZoneKind.CorpScore, PlayerSide.Corp));
        zones.Add(new ZoneState(allocator.NextZoneId(), ZoneKind.RunnerStack, PlayerSide.Runner));
        zones.Add(new ZoneState(allocator.NextZoneId(), ZoneKind.RunnerGrip, PlayerSide.Runner));
        zones.Add(new ZoneState(allocator.NextZoneId(), ZoneKind.RunnerHeap, PlayerSide.Runner));
        zones.Add(new ZoneState(allocator.NextZoneId(), ZoneKind.RunnerRig, PlayerSide.Runner));
        zones.Add(new ZoneState(allocator.NextZoneId(), ZoneKind.RunnerScore, PlayerSide.Runner));

        var state = new GameState(
            gameId,
            rulesVersion,
            cardDataVersion,
            [
                new PlayerState(corpId, PlayerSide.Corp, credits: 5, clicks: 3),
                new PlayerState(runnerId, PlayerSide.Runner, credits: 5, clicks: 4),
            ],
            cards: [],
            zones,
            servers,
            new TurnState(PlayerSide.Corp, TurnPhase.Corp, turnNumber: 1),
            new GameOutcome(),
            seed,
            allocator.State,
            initialSeed: seed);

        StateInvariantValidator.Validate(state);
        return state;

        void AddServer(ServerKind kind)
        {
            ServerId serverId = allocator.NextServerId();
            ZoneId iceZoneId = allocator.NextZoneId();
            ZoneId rootZoneId = allocator.NextZoneId();
            servers.Add(new ServerState(serverId, kind, iceZoneId, rootZoneId));
            zones.Add(new ZoneState(iceZoneId, ZoneKind.ServerIce, PlayerSide.Corp, serverId));
            zones.Add(new ZoneState(rootZoneId, ZoneKind.ServerRoot, PlayerSide.Corp, serverId));
        }
    }

    public static GameState FromSnapshot(GameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        GameState state = snapshot.ToState();
        StateInvariantValidator.Validate(state);
        return state;
    }

    private static ReadOnlyCollection<T> FreezeSorted<T>(IEnumerable<T>? values, Func<T, string> key)
        where T : class
    {
        T[] copy = (values ?? []).ToArray();
        if (copy.Any(static value => value is null))
        {
            throw new ArgumentException("State collections must not contain null entries.", nameof(values));
        }

        Array.Sort(copy, (left, right) =>
        {
            int result = StringComparer.Ordinal.Compare(key(left), key(right));
            return result != 0 ? result : 0;
        });
        return new ReadOnlyCollection<T>(copy);
    }

    private static ReadOnlyCollection<T> Freeze<T>(IEnumerable<T>? values)
        where T : class =>
        new ReadOnlyCollection<T>((values ?? []).Select(value =>
        {
            ArgumentNullException.ThrowIfNull(value);
            return value;
        }).ToArray());
}

/// <summary>
/// Detached complete-state snapshot for clients and diagnostics. This is not a
/// player observation: information filtering belongs to a later boundary.
/// </summary>
public sealed class GameSnapshot
{
    [JsonConstructor]
    public GameSnapshot(
        int schemaVersion,
        GameId gameId,
        string rulesVersion,
        string cardDataVersion,
        IReadOnlyList<PlayerState>? players,
        IReadOnlyList<CardInstanceState>? cards,
        IReadOnlyList<ZoneState>? zones,
        IReadOnlyList<ServerState>? servers,
        TurnState? turn,
        GameOutcome? outcome,
        ulong randomState,
        IdAllocatorState? allocator,
        long decisionRevision = 0,
        long eventSequence = 0,
        IReadOnlyList<ResolutionFrame>? resolutionStack = null,
        PendingDecision? pendingDecision = null,
        long actionSequence = 0,
        ulong initialSeed = 0)
    {
        ArgumentNullException.ThrowIfNull(rulesVersion);
        ArgumentNullException.ThrowIfNull(cardDataVersion);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(zones);
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(allocator);

        SchemaVersion = schemaVersion;
        GameId = gameId;
        RulesVersion = rulesVersion;
        CardDataVersion = cardDataVersion;
        Players = CopyPlayers(players);
        Cards = CopyCards(cards);
        Zones = CopyZones(zones);
        Servers = CopyServers(servers);
        Turn = new TurnState(turn.ActiveSide, turn.Phase, turn.TurnNumber);
        Outcome = new GameOutcome(outcome.Kind, outcome.Winner, outcome.Reason);
        InitialSeed = initialSeed;
        RandomState = randomState;
        Allocator = new IdAllocatorState(allocator.NextSequence);
        DecisionRevision = decisionRevision;
        EventSequence = eventSequence;
        ActionSequence = actionSequence;
        ResolutionStack = CopyFrames(resolutionStack);
        PendingDecision = pendingDecision?.Copy();
    }

    [JsonInclude]
    [JsonRequired]
    public int SchemaVersion { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public GameId GameId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string RulesVersion { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string CardDataVersion { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<PlayerState> Players { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<CardInstanceState> Cards { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<ZoneState> Zones { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<ServerState> Servers { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public TurnState Turn { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public GameOutcome Outcome { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ulong InitialSeed { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ulong RandomState { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IdAllocatorState Allocator { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public long DecisionRevision { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public long EventSequence { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public long ActionSequence { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<ResolutionFrame> ResolutionStack { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PendingDecision? PendingDecision { get; private set; }

    public static GameSnapshot From(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        StateInvariantValidator.Validate(state);
        return new GameSnapshot(
            state.SchemaVersion,
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            state.Players,
            state.Cards,
            state.Zones,
            state.Servers,
            state.Turn,
            state.Outcome,
            state.RandomState,
            state.Allocator,
            state.DecisionRevision,
            state.EventSequence,
            state.ResolutionStack,
            state.PendingDecision,
            state.ActionSequence,
            state.InitialSeed);
    }

    public GameState ToState() => new(
        SchemaVersion,
        GameId,
        RulesVersion,
        CardDataVersion,
        Players,
        Cards,
        Zones,
        Servers,
        Turn,
        Outcome,
        RandomState,
        Allocator,
        DecisionRevision,
        EventSequence,
        ResolutionStack,
        PendingDecision,
        ActionSequence,
        InitialSeed);

    private static ReadOnlyCollection<PlayerState> CopyPlayers(IEnumerable<PlayerState>? values) =>
        new ReadOnlyCollection<PlayerState>((values ?? [])
            .Select(player => CopyPlayer(player))
            .OrderBy(player => player.Id.Value, StringComparer.Ordinal)
            .ToArray());

    private static ReadOnlyCollection<CardInstanceState> CopyCards(IEnumerable<CardInstanceState>? values) =>
        new ReadOnlyCollection<CardInstanceState>((values ?? [])
            .Select(card => CopyCard(card))
            .OrderBy(card => card.Id.Value, StringComparer.Ordinal)
            .ToArray());

    private static ReadOnlyCollection<ZoneState> CopyZones(IEnumerable<ZoneState>? values) =>
        new ReadOnlyCollection<ZoneState>((values ?? [])
            .Select(zone => CopyZone(zone))
            .OrderBy(zone => zone.Id.Value, StringComparer.Ordinal)
            .ToArray());

    private static ReadOnlyCollection<ServerState> CopyServers(IEnumerable<ServerState>? values) =>
        new ReadOnlyCollection<ServerState>((values ?? [])
            .Select(server => CopyServer(server))
            .OrderBy(server => (int)server.Kind)
            .ThenBy(server => server.Id.Value, StringComparer.Ordinal)
            .ToArray());

    private static ReadOnlyCollection<ResolutionFrame> CopyFrames(IEnumerable<ResolutionFrame>? values) =>
        new ReadOnlyCollection<ResolutionFrame>((values ?? [])
            .Select(frame =>
            {
                ArgumentNullException.ThrowIfNull(frame);
                return ResolutionFrameCloner.Copy(frame);
            })
            .ToArray());

    private static PlayerState CopyPlayer(PlayerState? player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return new PlayerState(player.Id, player.Side, player.Credits, player.Clicks, player.AgendaPoints);
    }

    private static CardInstanceState CopyCard(CardInstanceState? card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new CardInstanceState(
            card.Id,
            card.DefinitionId,
            card.ZoneId,
            card.OwnerSide,
            card.FaceUp,
            card.Rezzed,
            card.AdvancementCounters);
    }

    private static ZoneState CopyZone(ZoneState? zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds);
    }

    private static ServerState CopyServer(ServerState? server)
    {
        ArgumentNullException.ThrowIfNull(server);
        return new ServerState(server.Id, server.Kind, server.IceZoneId, server.RootZoneId);
    }
}

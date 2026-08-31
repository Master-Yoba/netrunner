using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Netrunner.Cards;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>
/// Raised when an M2 scenario document is missing, malformed, or contradictory.
/// The message lists every problem with its exact document path.
/// </summary>
public sealed class M2ScenarioException : InvalidOperationException
{
    public M2ScenarioException(IReadOnlyList<string> problems)
        : base("The M2 scenario document is invalid:\n" + string.Join("\n", problems))
    {
    }
}

/// <summary>Player resource placement for an M2 scenario (defaults to the standard 5/3 and 5/4).</summary>
public sealed record M2ScenarioPlayerSpec
{
    [JsonConstructor]
    public M2ScenarioPlayerSpec(PlayerSide side, int credits, int clicks)
    {
        Side = side;
        Credits = credits;
        Clicks = clicks;
    }

    [JsonInclude]
    public PlayerSide Side { get; }

    [JsonInclude]
    public int Credits { get; }

    [JsonInclude]
    public int Clicks { get; }
}

/// <summary>
/// The scripted opening of a curated M2 run: the server the run attacks and
/// the Runner card that initiates it (a registered run event). Run initiation
/// is the engine's setup boundary, so it is a scenario contract consumed by the
/// proof coordinators, not a pending decision.
/// </summary>
public sealed record M2ScenarioRunSpec
{
    [JsonConstructor]
    public M2ScenarioRunSpec(string server, string source, string? breaker = null, int maxPumps = 1)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(source);
        Server = server;
        Source = source;
        Breaker = breaker;
        MaxPumps = maxPumps;
    }

    [JsonInclude]
    public string Server { get; }

    [JsonInclude]
    public string Source { get; }

    /// <summary>
    /// The preferred icebreaker installed in the Runner's rig. CR 26.03 6.5
    /// has no persistent attachment concept, so this is a policy preference:
    /// the coordinators use it whenever it is eligible for the encountered ICE
    /// and fall back to the first other eligible breaker. Validation requires
    /// the run contract to name one.
    /// </summary>
    [JsonInclude]
    public string? Breaker { get; }

    /// <summary>The total number of pumps a coordinator may take across the run.</summary>
    [JsonInclude]
    public int MaxPumps { get; }
}

/// <summary>Explicit turn ownership pinned by an M2 scenario document.</summary>
public sealed record M2ScenarioTurnSpec
{
    [JsonConstructor]
    public M2ScenarioTurnSpec(PlayerSide activeSide, TurnPhase phase, int turnNumber)
    {
        ActiveSide = activeSide;
        Phase = phase;
        TurnNumber = turnNumber;
    }

    [JsonInclude]
    public PlayerSide ActiveSide { get; }

    [JsonInclude]
    public TurnPhase Phase { get; }

    [JsonInclude]
    public int TurnNumber { get; }
}

/// <summary>
/// One explicitly placed card. Non-server zones use the single zone of that
/// kind; server zones additionally require a <c>server</c> reference
/// ("hq", "rd", "archives", or "remote:&lt;index&gt;").
/// </summary>
public sealed record M2ScenarioCardSpec
{
    [JsonConstructor]
    public M2ScenarioCardSpec(
        string id,
        string definitionId,
        ZoneKind zone,
        PlayerSide ownerSide,
        string? server = null,
        bool faceUp = false,
        bool rezzed = false,
        int advancementCounters = 0)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(definitionId);
        Id = id;
        DefinitionId = definitionId;
        Zone = zone;
        OwnerSide = ownerSide;
        Server = server;
        FaceUp = faceUp;
        Rezzed = rezzed;
        AdvancementCounters = advancementCounters;
    }

    [JsonInclude]
    public string Id { get; }

    [JsonInclude]
    public string DefinitionId { get; }

    [JsonInclude]
    public ZoneKind Zone { get; }

    [JsonInclude]
    public PlayerSide OwnerSide { get; }

    public string? Server { get; }

    public bool FaceUp { get; }

    public bool Rezzed { get; }

    public int AdvancementCounters { get; }
}

/// <summary>
/// Checked-in M2 scenario document. Unlike the compact M1 scenario (base zones
/// only), the M2 document places cards on the server topology, pins the turn
/// and player resources, and carries the CR 26.03 citations plus the
/// Jinteki.net cross-check note required of every timing scenario. The document
/// is strict: unknown, missing, or contradictory fields fail with an exact
/// document path.
/// </summary>
public sealed class M2ScenarioDocument
{
    [JsonConstructor]
    public M2ScenarioDocument(
        int schemaVersion,
        string scenarioId,
        ulong seed,
        string rulesVersion,
        string cardDataVersion,
        M2ScenarioTurnSpec turn,
        int remotes = 0,
        M2ScenarioRunSpec? run = null,
        IReadOnlyList<M2ScenarioPlayerSpec>? players = null,
        IReadOnlyList<M2ScenarioCardSpec>? cards = null,
        IReadOnlyList<string>? citations = null,
        string? crossCheckNote = null,
        string? expectedInitialStateHash = null,
        string? behaviorRegistryVersion = null,
        string? behaviorCatalogFingerprint = null)
    {
        ArgumentNullException.ThrowIfNull(scenarioId);
        ArgumentNullException.ThrowIfNull(rulesVersion);
        ArgumentNullException.ThrowIfNull(cardDataVersion);
        ArgumentNullException.ThrowIfNull(turn);
        SchemaVersion = schemaVersion;
        ScenarioId = scenarioId;
        Seed = seed;
        RulesVersion = rulesVersion;
        CardDataVersion = cardDataVersion;
        // D-401 is a deliberate one-time migration: metadata and compiled
        // behavior pins are independent and must be supplied in their own
        // fields. Missing values are retained as invalid input for
        // CollectProblems; they are never inferred from the other pin.
        BehaviorRegistryVersion = behaviorRegistryVersion ?? string.Empty;
        BehaviorCatalogFingerprint = behaviorCatalogFingerprint;
        Turn = turn;
        Remotes = remotes;
        Run = run;
        Players = new ReadOnlyCollection<M2ScenarioPlayerSpec>((players ?? []).ToArray());
        Cards = new ReadOnlyCollection<M2ScenarioCardSpec>((cards ?? []).ToArray());
        Citations = new ReadOnlyCollection<string>((citations ?? []).ToArray());
        CrossCheckNote = crossCheckNote;
        ExpectedInitialStateHash = expectedInitialStateHash;
    }

    public int SchemaVersion { get; }

    [JsonInclude]
    public string ScenarioId { get; }

    [JsonInclude]
    public ulong Seed { get; }

    [JsonInclude]
    public string RulesVersion { get; }

    [JsonInclude]
    public string CardDataVersion { get; }

    /// <summary>
    /// Compiled behavior pin, distinct from authenticated metadata version.
    /// Missing legacy JSON derives the old gateway pin only when its
    /// cardDataVersion carries that legacy value.
    /// </summary>
    [JsonInclude]
    public string BehaviorRegistryVersion { get; }

    /// <summary>
    /// Optional compiled-catalog capability fingerprint. Legacy M2 JSON gets
    /// the Gateway fingerprint when it derives the Gateway behavior pin.
    /// </summary>
    [JsonInclude]
    public string? BehaviorCatalogFingerprint { get; }

    [JsonInclude]
    public M2ScenarioTurnSpec Turn { get; }

    public int Remotes { get; }

    public M2ScenarioRunSpec? Run { get; }

    public IReadOnlyList<M2ScenarioPlayerSpec> Players { get; }

    public IReadOnlyList<M2ScenarioCardSpec> Cards { get; }

    public IReadOnlyList<string> Citations { get; }

    public string? CrossCheckNote { get; }

    public string? ExpectedInitialStateHash { get; }

    /// <summary>Every problem with its exact document path; empty when valid.</summary>
    public IReadOnlyList<string> CollectProblems()
    {
        // M2 callers still use the legacy static lookup/ReplayVerifier APIs;
        // keep this idempotent adapter registration until those callers are
        // fully migrated. The immutable catalog below remains the source for
        // this document's capability checks.
        GatewayBehaviorCatalog.RegisterAll();
        BehaviorCatalog behaviorCatalog = GatewayBehaviorCatalog.Create();
        var problems = new List<string>();

        if (SchemaVersion != EngineCompatibility.ArtifactSchemaVersion)
        {
            problems.Add(
                $"schemaVersion: expected {EngineCompatibility.ArtifactSchemaVersion}, found {SchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(ScenarioId))
        {
            problems.Add("scenarioId: must be a non-empty identifier.");
        }

        if (RulesVersion != EngineCompatibility.RulesVersion)
        {
            problems.Add($"rulesVersion: '{RulesVersion}' is not supported; expected '{EngineCompatibility.RulesVersion}'.");
        }

        if (string.IsNullOrWhiteSpace(CardDataVersion))
        {
            problems.Add("cardDataVersion: must be a non-empty authenticated metadata version.");
        }
        else if (CardDataVersion != EngineCompatibility.GatewayCardDataVersion)
        {
            problems.Add(
                $"cardDataVersion: '{CardDataVersion}' is not the authenticated Gateway metadata snapshot '{EngineCompatibility.GatewayCardDataVersion}'.");
        }

        if (string.IsNullOrWhiteSpace(BehaviorRegistryVersion))
        {
            problems.Add("behaviorRegistryVersion: must be an explicit compiled behavior catalog version.");
        }
        else if (BehaviorRegistryVersion != CardBehaviorSpecCatalog.Version)
        {
            problems.Add(
                $"behaviorRegistryVersion: '{BehaviorRegistryVersion}' is not the Gateway compiled behavior version '{CardBehaviorSpecCatalog.Version}'.");
        }

        string gatewayFingerprint = GatewayBehaviorCatalog.Create().Fingerprint;
        if (!string.Equals(BehaviorCatalogFingerprint, gatewayFingerprint, StringComparison.Ordinal))
        {
            problems.Add(
                $"behaviorCatalogFingerprint: '{BehaviorCatalogFingerprint}' does not match the Gateway behavior catalog fingerprint '{gatewayFingerprint}'.");
        }

        if (Remotes < 0 || Remotes > 8)
        {
            problems.Add($"remotes: must be between 0 and 8, found {Remotes}.");
        }

        M2ScenarioTurnSpec turn = Turn;
        if (!Enum.IsDefined(turn.ActiveSide))
        {
            problems.Add($"turn.activeSide: '{turn.ActiveSide}' is not a valid player side.");
        }

        if (turn.Phase is not (TurnPhase.Corp or TurnPhase.Runner))
        {
            problems.Add($"turn.phase: '{turn.Phase}' is not a playable phase; expected 'corp' or 'runner'.");
        }
        else if (turn.Phase == TurnPhase.Corp && turn.ActiveSide != PlayerSide.Corp
            || turn.Phase == TurnPhase.Runner && turn.ActiveSide != PlayerSide.Runner)
        {
            problems.Add($"turn.phase: phase '{turn.Phase}' requires activeSide '{(turn.Phase == TurnPhase.Corp ? "corp" : "runner")}'.");
        }

        if (turn.TurnNumber < 1)
        {
            problems.Add($"turn.turnNumber: must be at least 1, found {turn.TurnNumber}.");
        }

        var playerSides = new HashSet<PlayerSide>();
        for (int index = 0; index < Players.Count; index++)
        {
            M2ScenarioPlayerSpec player = Players[index];
            string path = $"players[{index}]";
            if (player is null)
            {
                problems.Add($"{path}: player entries must not be null.");
                continue;
            }

            if (!Enum.IsDefined(player.Side))
            {
                problems.Add($"{path}.side: '{player.Side}' is not a valid player side.");
            }
            else if (!playerSides.Add(player.Side))
            {
                problems.Add($"{path}.side: side '{player.Side}' appears more than once.");
            }

            if (player.Credits < 0)
            {
                problems.Add($"{path}.credits: must not be negative, found {player.Credits}.");
            }

            if (player.Clicks < 0)
            {
                problems.Add($"{path}.clicks: must not be negative, found {player.Clicks}.");
            }
        }

        if (Run is not null)
        {
            if (!TryParseServerRef(Run.Server, Remotes, out _))
            {
                problems.Add($"run.server: '{Run.Server}' is not a valid server reference (expected 'hq', 'rd', 'archives', or 'remote:<index>' with index < {Remotes}).");
            }

            M2ScenarioCardSpec? source = Cards.FirstOrDefault(card => card is not null && card.Id == Run.Source);
            if (source is null)
            {
                problems.Add($"run.source: source card '{Run.Source}' is not placed by the scenario.");
            }
            else if (source.OwnerSide != PlayerSide.Runner)
            {
                problems.Add($"run.source: source card '{Run.Source}' is not owned by the Runner.");
            }
            else if (source.Zone != ZoneKind.RunnerGrip)
            {
                problems.Add($"run.source: source card '{Run.Source}' must be placed in RunnerGrip before the run starts.");
            }
            else if (!behaviorCatalog.TryGetRunEvent(new CardDefinitionId(source.DefinitionId), out _))
            {
                problems.Add($"run.source: definition '{source.DefinitionId}' is not a registered run event.");
            }

            M2ScenarioCardSpec? breaker = Run.Breaker is { } breakerId
                ? Cards.FirstOrDefault(card => card is not null && card.Id == breakerId)
                : null;
            if (string.IsNullOrWhiteSpace(Run.Breaker))
            {
                problems.Add("run.breaker: the run contract must name the preferred icebreaker.");
            }
            else if (breaker is null)
            {
                problems.Add($"run.breaker: breaker card '{Run.Breaker}' is not placed by the scenario.");
            }
            else if (breaker.OwnerSide != PlayerSide.Runner)
            {
                problems.Add($"run.breaker: breaker card '{Run.Breaker}' is not owned by the Runner.");
            }
            else if (breaker.Zone != ZoneKind.RunnerRig)
            {
                problems.Add($"run.breaker: breaker card '{Run.Breaker}' is not installed in the Runner's rig.");
            }
            else if (!behaviorCatalog.TryGetIcebreaker(new CardDefinitionId(breaker.DefinitionId), out _))
            {
                problems.Add($"run.breaker: definition '{breaker.DefinitionId}' is not a registered icebreaker.");
            }

            if (Run.MaxPumps < 0)
            {
                problems.Add($"run.maxPumps: must be zero or positive (was {Run.MaxPumps}).");
            }
        }

        var cardIds = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < Cards.Count; index++)
        {
            M2ScenarioCardSpec card = Cards[index];
            string path = $"cards[{index}]";
            if (card is null)
            {
                problems.Add($"{path}: card entries must not be null.");
                continue;
            }

            if (!IsValidStableId(card.Id))
            {
                problems.Add($"{path}.id: '{card.Id}' is not a valid stable identifier (non-empty, at most 128 characters, no whitespace or control characters).");
            }
            else if (!cardIds.Add(card.Id))
            {
                problems.Add($"{path}.id: card instance ID '{card.Id}' occurs more than once.");
            }

            if (!IsValidStableId(card.DefinitionId))
            {
                problems.Add($"{path}.definitionId: '{card.DefinitionId}' is not a valid stable identifier.");
            }
            else if (!IsRegisteredBehavior(new CardDefinitionId(card.DefinitionId), behaviorCatalog))
            {
                problems.Add($"{path}.definitionId: '{card.DefinitionId}' is not registered in the behavior spec catalog.");
            }
            else if (!IsBehaviorCompatibleWithInstalledZone(
                new CardDefinitionId(card.DefinitionId),
                card.Zone,
                behaviorCatalog,
                out string expectedBehavior))
            {
                problems.Add($"{path}.definitionId: '{card.DefinitionId}' is not registered as {expectedBehavior} and cannot be placed in zone '{card.Zone}'.");
            }

            if (!Enum.IsDefined(card.Zone))
            {
                problems.Add($"{path}.zone: '{card.Zone}' is not a supported zone kind.");
            }
            else if (card.Zone == ZoneKind.RunnerPlayArea)
            {
                problems.Add($"{path}.zone: RunnerPlayArea is an active-run zone and cannot be populated by a pre-run scenario.");
            }
            else if (!Enum.IsDefined(card.OwnerSide))
            {
                problems.Add($"{path}.ownerSide: '{card.OwnerSide}' is not a valid player side.");
            }
            else
            {
                PlayerSide expectedOwner = card.Zone is ZoneKind.ServerIce or ZoneKind.ServerRoot
                    ? PlayerSide.Corp
                    : card.Zone switch
                    {
                        ZoneKind.CorpDeck or ZoneKind.CorpHand or ZoneKind.CorpDiscard or ZoneKind.CorpScore => PlayerSide.Corp,
                        _ => PlayerSide.Runner,
                    };
                if (card.OwnerSide != expectedOwner)
                {
                    problems.Add($"{path}.ownerSide: zone '{card.Zone}' requires owner side '{expectedOwner.ToString().ToLowerInvariant()}', found '{card.OwnerSide.ToString().ToLowerInvariant()}'.");
                }
            }

            bool isServerZone = card.Zone is ZoneKind.ServerIce or ZoneKind.ServerRoot;
            if (card.Server is not null)
            {
                if (!isServerZone)
                {
                    problems.Add($"{path}.server: a server reference is only valid for server ice/root zones.");
                }
                else if (!TryParseServerRef(card.Server, Remotes, out _))
                {
                    problems.Add($"{path}.server: '{card.Server}' is not a valid server reference (expected 'hq', 'rd', 'archives', or 'remote:<index>' with index < {Remotes}).");
                }
            }
            else if (isServerZone)
            {
                problems.Add($"{path}.server: a server reference is required for server ice/root zones.");
            }

            if (card.AdvancementCounters < 0)
            {
                problems.Add($"{path}.advancementCounters: must not be negative, found {card.AdvancementCounters}.");
            }
        }

        if (Citations.Count == 0)
        {
            problems.Add("citations: every timing scenario must cite its applicable CR 26.03 rules.");
        }

        for (int index = 0; index < Citations.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(Citations[index]))
            {
                problems.Add($"citations[{index}]: citations must not be blank.");
            }
        }

        if (string.IsNullOrWhiteSpace(CrossCheckNote))
        {
            problems.Add("crossCheckNote: every timing scenario must record its Jinteki.net behavioral cross-check note.");
        }

        if (ExpectedInitialStateHash is not null && !DeterministicHash.IsSha256Hex(ExpectedInitialStateHash))
        {
            problems.Add("expectedInitialStateHash: must be lowercase SHA-256 hex.");
        }

        return problems.AsReadOnly();
    }

    public void Validate()
    {
        IReadOnlyList<string> problems = CollectProblems();
        if (problems.Count > 0)
        {
            throw new M2ScenarioException(problems);
        }
    }

    public static bool TryParseServerRef(string reference, int remotes, out (ServerKind Kind, int RemoteIndex) server)
    {
        server = default;
        switch (reference)
        {
            case "hq":
                server = (ServerKind.Headquarters, -1);
                return true;
            case "rd":
                server = (ServerKind.RAndD, -1);
                return true;
            case "archives":
                server = (ServerKind.Archives, -1);
                return true;
            default:
                if (reference.StartsWith("remote:", StringComparison.Ordinal)
                    && int.TryParse(reference.AsSpan("remote:".Length), out int index)
                    && index >= 0
                    && index < remotes)
                {
                    server = (ServerKind.Remote, index);
                    return true;
                }

                return false;
        }
    }

    internal static bool IsRegisteredBehavior(CardDefinitionId definitionId) =>
        IsRegisteredBehavior(definitionId, CardBehaviorSpecCatalog.Default);

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1859:Change type of parameter 'behaviorCatalog' to 'BehaviorCatalog'",
        Justification = "The interface is the composition boundary for custom match catalogs.")]
    private static bool IsRegisteredBehavior(CardDefinitionId definitionId, IBehaviorCatalog behaviorCatalog) =>
        behaviorCatalog.TryGetIce(definitionId, out _)
        || behaviorCatalog.TryGetIcebreaker(definitionId, out _)
        || behaviorCatalog.TryGetAgenda(definitionId, out _)
        || behaviorCatalog.TryGetAsset(definitionId, out _)
        || behaviorCatalog.TryGetRunEvent(definitionId, out _);

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1859:Change type of parameter 'behaviorCatalog' to 'BehaviorCatalog'",
        Justification = "The interface is the composition boundary for custom match catalogs.")]
    private static bool IsBehaviorCompatibleWithInstalledZone(
        CardDefinitionId definitionId,
        ZoneKind zone,
        IBehaviorCatalog behaviorCatalog,
        out string expectedBehavior)
    {
        switch (zone)
        {
            case ZoneKind.ServerIce:
                expectedBehavior = "ICE";
                return behaviorCatalog.TryGetIce(definitionId, out _);
            case ZoneKind.ServerRoot:
                expectedBehavior = "an asset or agenda";
                return behaviorCatalog.TryGetAsset(definitionId, out _)
                    || behaviorCatalog.TryGetAgenda(definitionId, out _);
            case ZoneKind.RunnerRig:
                expectedBehavior = "an icebreaker";
                return behaviorCatalog.TryGetIcebreaker(definitionId, out _);
            default:
                expectedBehavior = "a behavior compatible with the installed zone";
                return true;
        }
    }

    internal static bool IsValidStableId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            return false;
        }

        foreach (char character in value)
        {
            if (char.IsControl(character) || char.IsWhiteSpace(character))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Deterministic setup builder for M2 scenario documents. It is the only
/// production path from a checked-in M2 scenario to authoritative state; the
/// result always passes the full state invariant validation.
/// </summary>
public static class M2ScenarioSetup
{
    public static GameState Build(M2ScenarioDocument document)
    {
        return Build(document, GatewayBehaviorCatalog.Create());
    }

    /// <summary>
    /// Builds an M2 state using the immutable catalog supplied by composition.
    /// The default overload remains the M2 Gateway adapter.
    /// </summary>
    public static GameState Build(M2ScenarioDocument document, IBehaviorCatalog behaviorCatalog)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        // Compatibility bridge for M2 tests/legacy consumers; all engine
        // operations below use the explicitly supplied immutable catalog.
        GatewayBehaviorCatalog.RegisterAll();
        document.Validate();
        if (!string.Equals(document.BehaviorRegistryVersion, behaviorCatalog.Version, StringComparison.Ordinal)
            || !string.Equals(document.BehaviorCatalogFingerprint, behaviorCatalog.Fingerprint, StringComparison.Ordinal))
        {
            throw new M2ScenarioException([
                $"behaviorCatalogFingerprint: the supplied behavior catalog '{behaviorCatalog.Version}/{behaviorCatalog.Fingerprint}' "
                + $"does not match the scenario pin '{document.BehaviorRegistryVersion}/{document.BehaviorCatalogFingerprint}'."]);
        }

        GameState baseState = GameState.CreateNew(
            document.Seed,
            document.RulesVersion,
            document.CardDataVersion,
            behaviorRegistryVersion: behaviorCatalog.Version,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint);
        var allocator = new StableIdAllocator(baseState.Allocator);

        var servers = baseState.Servers.ToList();
        var zones = baseState.Zones.ToList();
        for (int index = 0; index < document.Remotes; index++)
        {
            ServerId serverId = allocator.NextServerId();
            ZoneId iceZoneId = allocator.NextZoneId();
            ZoneId rootZoneId = allocator.NextZoneId();
            servers.Add(new ServerState(serverId, ServerKind.Remote, iceZoneId, rootZoneId));
            zones.Add(new ZoneState(iceZoneId, ZoneKind.ServerIce, PlayerSide.Corp, serverId));
            zones.Add(new ZoneState(rootZoneId, ZoneKind.ServerRoot, PlayerSide.Corp, serverId));
        }

        var players = baseState.Players.Select(player =>
            document.Players.SingleOrDefault(candidate => candidate.Side == player.Side) is { } adjustment
                ? new PlayerState(player.Id, player.Side, adjustment.Credits, adjustment.Clicks, player.AgendaPoints)
                : player
        ).ToArray();

        var zoneCards = zones.ToDictionary(zone => zone.Id, zone => new List<CardInstanceId>());
        var cards = new List<CardInstanceState>(document.Cards.Count);
        foreach (M2ScenarioCardSpec specification in document.Cards)
        {
            ZoneState zone = ResolveZone(specification);
            cards.Add(new CardInstanceState(
                new CardInstanceId(specification.Id),
                new CardDefinitionId(specification.DefinitionId),
                zone.Id,
                specification.OwnerSide,
                specification.FaceUp,
                specification.Rezzed,
                specification.AdvancementCounters));
            zoneCards[zone.Id].Add(new CardInstanceId(specification.Id));
            if (TryParseCardSequence(specification.Id, out ulong sequence) && sequence >= allocator.State.NextSequence)
            {
                if (sequence == ulong.MaxValue)
                {
                    throw new M2ScenarioException([
                        $"cards: card ID '{specification.Id}' uses the maximum allocatable sequence."]);
                }

                allocator = new StableIdAllocator(sequence + 1);
            }
        }

        var finalZones = zones
            .Select(zone => new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zoneCards[zone.Id]))
            .ToArray();

        var state = new GameState(
            baseState.GameId,
            document.RulesVersion,
            document.CardDataVersion,
            players,
            cards,
            finalZones,
            servers,
            new TurnState(document.Turn.ActiveSide, document.Turn.Phase, document.Turn.TurnNumber),
            baseState.Outcome,
            baseState.RandomState,
            allocator.State,
            schemaVersion: baseState.SchemaVersion,
            initialSeed: baseState.InitialSeed,
            behaviorRegistryVersion: behaviorCatalog.Version,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint);

        StateInvariantValidator.Validate(state, behaviorCatalog);
        if (document.ExpectedInitialStateHash is not null)
        {
            string actualHash = DeterministicHash.ForState(state);
            if (!string.Equals(actualHash, document.ExpectedInitialStateHash, StringComparison.Ordinal))
            {
                throw new M2ScenarioException([
                    $"expectedInitialStateHash: expected '{document.ExpectedInitialStateHash}', but the setup built '{actualHash}'."]);
            }
        }

        return state;

        ZoneState ResolveZone(M2ScenarioCardSpec specification)
        {
            if (specification.Zone is ZoneKind.ServerIce or ZoneKind.ServerRoot)
            {
                // Validation guarantees the reference parses; remotes were
                // appended to the central servers in index order.
                bool serverParsed = M2ScenarioDocument.TryParseServerRef(
                    specification.Server!, document.Remotes, out var parsedServer);
                var (kind, remoteIndex) = serverParsed ? parsedServer : default;
                ServerState server = kind == ServerKind.Remote
                    ? servers[3 + remoteIndex]
                    : servers.Single(candidate => candidate.Kind == kind);
                return zones.Single(zone => zone.Kind == specification.Zone && zone.ServerId == server.Id);
            }

            return zones.Single(zone => zone.Kind == specification.Zone);
        }
    }

    private static bool TryParseCardSequence(string value, out ulong sequence)
    {
        sequence = 0;
        const string prefix = "card:";
        if (!value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> digits = value.AsSpan(prefix.Length);
        if (digits.IsEmpty)
        {
            return false;
        }

        foreach (char digit in digits)
        {
            if (digit is < '0' or > '9')
            {
                return false;
            }
        }

        return ulong.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out sequence);
    }
}

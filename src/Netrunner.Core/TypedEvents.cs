using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

public static class EngineEventKinds
{
    public const string CreditsAdjusted = "credits-adjusted";
    public const string ClicksAdjusted = "clicks-adjusted";
    public const string DamageDealt = "damage-dealt";
    public const string TagsAdjusted = "tags-adjusted";
    public const string MemoryAdjusted = "memory-adjusted";
    public const string ProgramTrashedForMemory = "program-trashed-for-memory";
    public const string CounterAdjusted = "counter-adjusted";
    public const string VirusPurged = "virus-purged";
    public const string LingeringEffectAdded = "lingering-effect-added";
    public const string LingeringEffectRemoved = "lingering-effect-removed";
    public const string LingeringEffectExpired = "lingering-effect-expired";
    public const string TurnStarted = "turn-started";
    public const string TurnEnded = "turn-ended";
    public const string ClickSpent = "click-spent";
    public const string CardMoved = "card-moved";
    public const string CardDrawn = "card-drawn";
    public const string CardInstalled = "card-installed";
    public const string AgendaAdvanced = "agenda-advanced";
    public const string AgendaScored = "agenda-scored";
    public const string AgendaStolen = "agenda-stolen";
    public const string GameWon = "game-won";
    public const string RunInitiated = "run-initiated";
    public const string RunEventPlayed = "run-event-played";
    public const string RunCreditsAdjusted = "run-credits-adjusted";
    public const string RunPriorityPassed = "run-priority-passed";
    public const string RunTimingWindowOpened = "run-timing-window-opened";
    public const string IceApproached = "ice-approached";
    public const string IceRezzed = "ice-rezzed";
    public const string SubroutinesBroken = "subroutines-broken";
    public const string BreakerPumped = "breaker-pumped";
    public const string EncounterInitialized = "encounter-initialized";
    public const string EncounterCompleted = "encounter-completed";
    public const string RunMoved = "run-moved";
    public const string RunSuccessful = "run-successful";
    public const string BreachStarted = "breach-started";
    public const string BreachCandidateChosen = "breach-candidate-chosen";
    public const string CardAccessed = "card-accessed";
    public const string AccessedCardTrashed = "accessed-card-trashed";
    public const string RunEnded = "run-ended";
}

/// <summary>
/// Typed diagnostic/presentation payloads. Events are never replay inputs and
/// contain no dictionaries, object values, or executable references.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(CreditsAdjustedEventPayload), "credits-adjusted")]
[JsonDerivedType(typeof(ClicksAdjustedEventPayload), "clicks-adjusted")]
[JsonDerivedType(typeof(DamageDealtEventPayload), "damage-dealt")]
[JsonDerivedType(typeof(TagsAdjustedEventPayload), "tags-adjusted")]
[JsonDerivedType(typeof(MemoryAdjustedEventPayload), "memory-adjusted")]
[JsonDerivedType(typeof(ProgramTrashedForMemoryEventPayload), "program-trashed-for-memory")]
[JsonDerivedType(typeof(CounterAdjustedEventPayload), "counter-adjusted")]
[JsonDerivedType(typeof(VirusPurgedEventPayload), "virus-purged")]
[JsonDerivedType(typeof(LingeringEffectAddedEventPayload), "lingering-effect-added")]
[JsonDerivedType(typeof(LingeringEffectRemovedEventPayload), "lingering-effect-removed")]
[JsonDerivedType(typeof(LingeringEffectExpiredEventPayload), "lingering-effect-expired")]
[JsonDerivedType(typeof(TurnStartedEventPayload), "turn-started")]
[JsonDerivedType(typeof(TurnEndedEventPayload), "turn-ended")]
[JsonDerivedType(typeof(ClickSpentEventPayload), "click-spent")]
[JsonDerivedType(typeof(CardMovedEventPayload), "card-moved")]
[JsonDerivedType(typeof(CardDrawnEventPayload), "card-drawn")]
[JsonDerivedType(typeof(CardInstalledEventPayload), "card-installed")]
[JsonDerivedType(typeof(AgendaAdvancedEventPayload), "agenda-advanced")]
[JsonDerivedType(typeof(AgendaScoredEventPayload), "agenda-scored")]
[JsonDerivedType(typeof(AgendaStolenEventPayload), "agenda-stolen")]
[JsonDerivedType(typeof(GameWonEventPayload), "game-won")]
[JsonDerivedType(typeof(RunInitiatedEventPayload), "run-initiated")]
[JsonDerivedType(typeof(RunEventPlayedEventPayload), "run-event-played")]
[JsonDerivedType(typeof(RunCreditsAdjustedEventPayload), "run-credits-adjusted")]
[JsonDerivedType(typeof(RunPriorityPassedEventPayload), "run-priority-passed")]
[JsonDerivedType(typeof(RunTimingWindowOpenedEventPayload), "run-timing-window-opened")]
[JsonDerivedType(typeof(IceApproachedEventPayload), "ice-approached")]
[JsonDerivedType(typeof(IceRezzedEventPayload), "ice-rezzed")]
[JsonDerivedType(typeof(SubroutinesBrokenEventPayload), "subroutines-broken")]
[JsonDerivedType(typeof(BreakerPumpedEventPayload), "breaker-pumped")]
[JsonDerivedType(typeof(EncounterInitializedEventPayload), "encounter-initialized")]
[JsonDerivedType(typeof(EncounterCompletedEventPayload), "encounter-completed")]
[JsonDerivedType(typeof(RunMovedEventPayload), "run-moved")]
[JsonDerivedType(typeof(RunSuccessfulEventPayload), "run-successful")]
[JsonDerivedType(typeof(BreachStartedEventPayload), "breach-started")]
[JsonDerivedType(typeof(BreachCandidateChosenEventPayload), "breach-candidate-chosen")]
[JsonDerivedType(typeof(CardAccessedEventPayload), "card-accessed")]
[JsonDerivedType(typeof(AccessedCardTrashedEventPayload), "accessed-card-trashed")]
[JsonDerivedType(typeof(RunEndedEventPayload), "run-ended")]
public abstract record EngineEventPayload
{
    protected EngineEventPayload(int version) => Version = version;

    [JsonInclude]
    [JsonRequired]
    public int Version { get; private set; }
}

public sealed record CreditsAdjustedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public CreditsAdjustedEventPayload(int version, PlayerId target, int delta, int creditsAfter)
        : base(version)
    {
        Target = target;
        Delta = delta;
        CreditsAfter = creditsAfter;
    }

    public CreditsAdjustedEventPayload(PlayerId target, int delta, int creditsAfter)
        : this(1, target, delta, creditsAfter)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Target { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Delta { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int CreditsAfter { get; private set; }
}

public sealed record ClicksAdjustedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public ClicksAdjustedEventPayload(int version, PlayerId target, int delta, int clicksAfter)
        : base(version)
    {
        Target = target;
        Delta = delta;
        ClicksAfter = clicksAfter;
    }

    public ClicksAdjustedEventPayload(PlayerId target, int delta, int clicksAfter)
        : this(1, target, delta, clicksAfter)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Target { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Delta { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int ClicksAfter { get; private set; }
}

public sealed record DamageDealtEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public DamageDealtEventPayload(
        int version,
        PlayerId target,
        DamageKind kind,
        int amount,
        int cardsTrashed,
        int coreDamageAfter,
        bool flatlined)
        : base(version)
    {
        Target = target;
        Kind = kind;
        Amount = amount;
        CardsTrashed = cardsTrashed;
        CoreDamageAfter = coreDamageAfter;
        Flatlined = flatlined;
    }

    public DamageDealtEventPayload(
        PlayerId target,
        DamageKind kind,
        int amount,
        int cardsTrashed,
        int coreDamageAfter,
        bool flatlined)
        : this(1, target, kind, amount, cardsTrashed, coreDamageAfter, flatlined)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Target { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public DamageKind Kind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Amount { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int CardsTrashed { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int CoreDamageAfter { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public bool Flatlined { get; private set; }
}

public sealed record TagsAdjustedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public TagsAdjustedEventPayload(int version, PlayerId target, int delta, int tagsAfter)
        : base(version)
    {
        Target = target;
        Delta = delta;
        TagsAfter = tagsAfter;
    }

    public TagsAdjustedEventPayload(PlayerId target, int delta, int tagsAfter)
        : this(1, target, delta, tagsAfter)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Target { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Delta { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int TagsAfter { get; private set; }
}

public sealed record MemoryAdjustedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public MemoryAdjustedEventPayload(int version, PlayerId target, int baseMemory, int usedMemory)
        : base(version)
    {
        Target = target;
        BaseMemory = baseMemory;
        UsedMemory = usedMemory;
    }

    public MemoryAdjustedEventPayload(PlayerId target, int baseMemory, int usedMemory)
        : this(1, target, baseMemory, usedMemory)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Target { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int BaseMemory { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int UsedMemory { get; private set; }
}

public sealed record ProgramTrashedForMemoryEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public ProgramTrashedForMemoryEventPayload(int version, CardInstanceId cardId, PlayerId owner, int memoryCost)
        : base(version)
    {
        CardId = cardId;
        Owner = owner;
        MemoryCost = memoryCost;
    }

    public ProgramTrashedForMemoryEventPayload(CardInstanceId cardId, PlayerId owner, int memoryCost)
        : this(1, cardId, owner, memoryCost)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Owner { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int MemoryCost { get; private set; }
}

public sealed record CounterAdjustedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public CounterAdjustedEventPayload(int version, CardInstanceId cardId, string counterName, int delta, int countAfter)
        : base(version)
    {
        ArgumentNullException.ThrowIfNull(counterName);
        CardId = cardId;
        CounterName = counterName;
        Delta = delta;
        CountAfter = countAfter;
    }

    public CounterAdjustedEventPayload(CardInstanceId cardId, string counterName, int delta, int countAfter)
        : this(1, cardId, counterName, delta, countAfter)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string CounterName { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Delta { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int CountAfter { get; private set; }
}

public sealed record VirusPurgedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public VirusPurgedEventPayload(int version, int countersRemoved)
        : base(version)
    {
        CountersRemoved = countersRemoved;
    }

    public VirusPurgedEventPayload(int countersRemoved)
        : this(1, countersRemoved)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public int CountersRemoved { get; private set; }
}

public sealed record LingeringEffectAddedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public LingeringEffectAddedEventPayload(int version, RunEffectId effectId, string behaviorId, LingeringEffectExpiry expiry)
        : base(version)
    {
        ArgumentNullException.ThrowIfNull(behaviorId);
        EffectId = effectId;
        BehaviorId = behaviorId;
        Expiry = expiry;
    }

    public LingeringEffectAddedEventPayload(RunEffectId effectId, string behaviorId, LingeringEffectExpiry expiry)
        : this(1, effectId, behaviorId, expiry)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public RunEffectId EffectId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string BehaviorId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public LingeringEffectExpiry Expiry { get; private set; }
}

public sealed record LingeringEffectRemovedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public LingeringEffectRemovedEventPayload(int version, RunEffectId effectId)
        : base(version)
    {
        EffectId = effectId;
    }

    public LingeringEffectRemovedEventPayload(RunEffectId effectId)
        : this(1, effectId)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public RunEffectId EffectId { get; private set; }
}

public sealed record LingeringEffectExpiredEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public LingeringEffectExpiredEventPayload(int version, RunEffectId effectId, LingeringEffectExpiry expiry)
        : base(version)
    {
        EffectId = effectId;
        Expiry = expiry;
    }

    public LingeringEffectExpiredEventPayload(RunEffectId effectId, LingeringEffectExpiry expiry)
        : this(1, effectId, expiry)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public RunEffectId EffectId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public LingeringEffectExpiry Expiry { get; private set; }
}

public sealed record TurnStartedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public TurnStartedEventPayload(int version, PlayerSide activeSide, int turnNumber, int clicks)
        : base(version)
    {
        ActiveSide = activeSide;
        TurnNumber = turnNumber;
        Clicks = clicks;
    }

    public TurnStartedEventPayload(PlayerSide activeSide, int turnNumber, int clicks)
        : this(1, activeSide, turnNumber, clicks)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide ActiveSide { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int TurnNumber { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Clicks { get; private set; }
}

public sealed record TurnEndedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public TurnEndedEventPayload(int version, PlayerSide side, int turnNumber)
        : base(version)
    {
        Side = side;
        TurnNumber = turnNumber;
    }

    public TurnEndedEventPayload(PlayerSide side, int turnNumber)
        : this(1, side, turnNumber)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide Side { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int TurnNumber { get; private set; }
}

public sealed record ClickSpentEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public ClickSpentEventPayload(int version, PlayerSide side, int clicksRemaining)
        : base(version)
    {
        Side = side;
        ClicksRemaining = clicksRemaining;
    }

    public ClickSpentEventPayload(PlayerSide side, int clicksRemaining)
        : this(1, side, clicksRemaining)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide Side { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int ClicksRemaining { get; private set; }
}

public sealed record CardMovedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public CardMovedEventPayload(int version, CardInstanceId cardId, ZoneId fromZoneId, ZoneId toZoneId)
        : base(version)
    {
        CardId = cardId;
        FromZoneId = fromZoneId;
        ToZoneId = toZoneId;
    }

    public CardMovedEventPayload(CardInstanceId cardId, ZoneId fromZoneId, ZoneId toZoneId)
        : this(1, cardId, fromZoneId, toZoneId)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneId FromZoneId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneId ToZoneId { get; private set; }
}

public sealed record CardDrawnEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public CardDrawnEventPayload(int version, PlayerSide side, CardInstanceId cardId, ZoneId fromZoneId, ZoneId toZoneId)
        : base(version)
    {
        Side = side;
        CardId = cardId;
        FromZoneId = fromZoneId;
        ToZoneId = toZoneId;
    }

    public CardDrawnEventPayload(PlayerSide side, CardInstanceId cardId, ZoneId fromZoneId, ZoneId toZoneId)
        : this(1, side, cardId, fromZoneId, toZoneId)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide Side { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneId FromZoneId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneId ToZoneId { get; private set; }
}

public sealed record CardInstalledEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public CardInstalledEventPayload(
        int version,
        PlayerSide side,
        CardInstanceId cardId,
        ZoneId fromZoneId,
        ZoneId toZoneId,
        int cost)
        : base(version)
    {
        Side = side;
        CardId = cardId;
        FromZoneId = fromZoneId;
        ToZoneId = toZoneId;
        Cost = cost;
    }

    public CardInstalledEventPayload(
        PlayerSide side,
        CardInstanceId cardId,
        ZoneId fromZoneId,
        ZoneId toZoneId,
        int cost)
        : this(1, side, cardId, fromZoneId, toZoneId, cost)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide Side { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneId FromZoneId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneId ToZoneId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Cost { get; private set; }
}

public sealed record AgendaAdvancedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public AgendaAdvancedEventPayload(int version, CardInstanceId agendaId, int advancementCounters, int requirement)
        : base(version)
    {
        AgendaId = agendaId;
        AdvancementCounters = advancementCounters;
        Requirement = requirement;
    }

    public AgendaAdvancedEventPayload(CardInstanceId agendaId, int advancementCounters, int requirement)
        : this(1, agendaId, advancementCounters, requirement)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId AgendaId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int AdvancementCounters { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Requirement { get; private set; }
}

public sealed record AgendaScoredEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public AgendaScoredEventPayload(int version, PlayerSide winner, CardInstanceId agendaId, int agendaPoints)
        : base(version)
    {
        Winner = winner;
        AgendaId = agendaId;
        AgendaPoints = agendaPoints;
    }

    public AgendaScoredEventPayload(PlayerSide winner, CardInstanceId agendaId, int agendaPoints)
        : this(1, winner, agendaId, agendaPoints)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide Winner { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId AgendaId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int AgendaPoints { get; private set; }
}

public sealed record AgendaStolenEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public AgendaStolenEventPayload(int version, PlayerSide winner, CardInstanceId agendaId, int agendaPoints)
        : base(version)
    {
        Winner = winner;
        AgendaId = agendaId;
        AgendaPoints = agendaPoints;
    }

    public AgendaStolenEventPayload(PlayerSide winner, CardInstanceId agendaId, int agendaPoints)
        : this(1, winner, agendaId, agendaPoints)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide Winner { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId AgendaId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int AgendaPoints { get; private set; }
}

public sealed record GameWonEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public GameWonEventPayload(int version, PlayerSide winner, GameWinReason reason)
        : base(version)
    {
        Winner = winner;
        Reason = reason;
    }

    public GameWonEventPayload(PlayerSide winner, GameWinReason reason)
        : this(1, winner, reason)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide Winner { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public GameWinReason Reason { get; private set; }
}

public sealed record RunInitiatedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public RunInitiatedEventPayload(int version, RunId runId, ServerId serverId, PlayerId runner, PlayerSide initiatingSide)
        : base(version)
    {
        RunId = runId;
        ServerId = serverId;
        Runner = runner;
        InitiatingSide = initiatingSide;
    }

    public RunInitiatedEventPayload(RunId runId, ServerId serverId, PlayerId runner)
        : this(1, runId, serverId, runner, PlayerSide.Runner)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public RunId RunId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ServerId ServerId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Runner { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide InitiatingSide { get; private set; }
}

/// <summary>
/// A registered run event was played at run initiation: its printed cost was
/// paid and its hosted credits were placed on the event as run-scoped credits.
/// </summary>
public sealed record RunEventPlayedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public RunEventPlayedEventPayload(
        int version,
        RunId runId,
        CardInstanceId eventCardId,
        CardDefinitionId definitionId,
        int cost,
        int hostedCredits,
        PlayerId runner,
        int runnerCreditsAfter)
        : base(version)
    {
        RunId = runId;
        EventCardId = eventCardId;
        DefinitionId = definitionId;
        Cost = cost;
        HostedCredits = hostedCredits;
        Runner = runner;
        RunnerCreditsAfter = runnerCreditsAfter;
    }

    public RunEventPlayedEventPayload(
        RunId runId,
        CardInstanceId eventCardId,
        CardDefinitionId definitionId,
        int cost,
        int hostedCredits,
        PlayerId runner,
        int runnerCreditsAfter)
        : this(1, runId, eventCardId, definitionId, cost, hostedCredits, runner, runnerCreditsAfter)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public RunId RunId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId EventCardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardDefinitionId DefinitionId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Cost { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int HostedCredits { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Runner { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int RunnerCreditsAfter { get; private set; }
}

/// <summary>
/// Run-scoped hosted credits changed: spent toward a run cost (negative
/// delta while a run is live) or expired at run end (delta equal to the
/// negative remaining pool, credits after zero).
/// </summary>
public sealed record RunCreditsAdjustedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public RunCreditsAdjustedEventPayload(int version, RunId runId, int delta, int runCreditsAfter)
        : base(version)
    {
        RunId = runId;
        Delta = delta;
        RunCreditsAfter = runCreditsAfter;
    }

    public RunCreditsAdjustedEventPayload(RunId runId, int delta, int runCreditsAfter)
        : this(1, runId, delta, runCreditsAfter)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public RunId RunId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Delta { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int RunCreditsAfter { get; private set; }
}

public sealed record RunPriorityPassedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public RunPriorityPassedEventPayload(
        int version,
        RunId runId,
        RunPriorityWindowKind window,
        PlayerSide passedBy,
        PlayerSide nextPriorityHolder,
        int consecutivePasses)
        : base(version)
    {
        RunId = runId;
        Window = window;
        PassedBy = passedBy;
        NextPriorityHolder = nextPriorityHolder;
        ConsecutivePasses = consecutivePasses;
    }

    public RunPriorityPassedEventPayload(
        RunId runId,
        RunPriorityWindowKind window,
        PlayerSide passedBy,
        PlayerSide nextPriorityHolder,
        int consecutivePasses)
        : this(1, runId, window, passedBy, nextPriorityHolder, consecutivePasses)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public RunId RunId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public RunPriorityWindowKind Window { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide PassedBy { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide NextPriorityHolder { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int ConsecutivePasses { get; private set; }
}

/// <summary>Diagnostic evidence that a serialized 6.9 decision opportunity opened.</summary>
public sealed record RunTimingWindowOpenedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public RunTimingWindowOpenedEventPayload(
        int version,
        RunId runId,
        RunCrPhase phase,
        RunTimingStep step,
        RunPriorityWindowKind? priorityWindow,
        PlayerSide? priorityHolder)
        : base(version)
    {
        RunId = runId;
        Phase = phase;
        Step = step;
        PriorityWindow = priorityWindow;
        PriorityHolder = priorityHolder;
    }

    public RunTimingWindowOpenedEventPayload(
        RunId runId,
        RunCrPhase phase,
        RunTimingStep step,
        RunPriorityWindowKind? priorityWindow,
        PlayerSide? priorityHolder)
        : this(1, runId, phase, step, priorityWindow, priorityHolder)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public RunId RunId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public RunCrPhase Phase { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public RunTimingStep Step { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public RunPriorityWindowKind? PriorityWindow { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide? PriorityHolder { get; private set; }
}

public sealed record IceApproachedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public IceApproachedEventPayload(int version, RunId runId, CardInstanceId iceId)
        : base(version)
    {
        RunId = runId;
        IceId = iceId;
    }

    public IceApproachedEventPayload(RunId runId, CardInstanceId iceId)
        : this(1, runId, iceId)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public RunId RunId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId IceId { get; private set; }
}

public sealed record IceRezzedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public IceRezzedEventPayload(int version, RunId runId, CardInstanceId iceId)
        : base(version)
    {
        RunId = runId;
        IceId = iceId;
    }

    public IceRezzedEventPayload(RunId runId, CardInstanceId iceId)
        : this(1, runId, iceId)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public RunId RunId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId IceId { get; private set; }
}

public sealed record SubroutinesBrokenEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public SubroutinesBrokenEventPayload(
        int version,
        RunId runId,
        EncounterId encounterId,
        CardInstanceId breakerId,
        int brokenCount)
        : base(version)
    {
        RunId = runId;
        EncounterId = encounterId;
        BreakerId = breakerId;
        BrokenCount = brokenCount;
    }

    public SubroutinesBrokenEventPayload(RunId runId, EncounterId encounterId, CardInstanceId breakerId, int brokenCount)
        : this(1, runId, encounterId, breakerId, brokenCount)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public RunId RunId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public EncounterId EncounterId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId BreakerId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int BrokenCount { get; private set; }
}

public sealed record BreakerPumpedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public BreakerPumpedEventPayload(
        int version,
        RunId runId,
        CardInstanceId breakerId)
        : base(version)
    {
        RunId = runId;
        BreakerId = breakerId;
    }

    public BreakerPumpedEventPayload(RunId runId, CardInstanceId breakerId)
        : this(1, runId, breakerId)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public RunId RunId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId BreakerId { get; private set; }
}

public sealed record EncounterInitializedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public EncounterInitializedEventPayload(
        int version,
        RunId runId,
        EncounterId encounterId,
        CardInstanceId iceId,
        int subroutineCount)
        : base(version)
    {
        RunId = runId;
        EncounterId = encounterId;
        IceId = iceId;
        SubroutineCount = subroutineCount;
    }

    public EncounterInitializedEventPayload(RunId runId, EncounterId encounterId, CardInstanceId iceId, int subroutineCount)
        : this(1, runId, encounterId, iceId, subroutineCount)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public RunId RunId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public EncounterId EncounterId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId IceId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int SubroutineCount { get; private set; }
}

public sealed record EncounterCompletedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public EncounterCompletedEventPayload(int version, RunId runId, EncounterId encounterId)
        : base(version)
    {
        RunId = runId;
        EncounterId = encounterId;
    }

    public EncounterCompletedEventPayload(RunId runId, EncounterId encounterId)
        : this(1, runId, encounterId)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public RunId RunId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public EncounterId EncounterId { get; private set; }
}

public sealed record RunMovedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public RunMovedEventPayload(int version, RunId runId, int fromIceIndex, int toIceIndex, CardInstanceId? nextIceId)
        : base(version)
    {
        RunId = runId;
        FromIceIndex = fromIceIndex;
        ToIceIndex = toIceIndex;
        NextIceId = nextIceId;
    }

    public RunMovedEventPayload(RunId runId, int fromIceIndex, int toIceIndex, CardInstanceId? nextIceId)
        : this(1, runId, fromIceIndex, toIceIndex, nextIceId)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public RunId RunId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int FromIceIndex { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int ToIceIndex { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId? NextIceId { get; private set; }
}

public sealed record RunSuccessfulEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public RunSuccessfulEventPayload(int version, RunId runId, ServerId serverId)
        : base(version)
    {
        RunId = runId;
        ServerId = serverId;
    }

    public RunSuccessfulEventPayload(RunId runId, ServerId serverId)
        : this(1, runId, serverId)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public RunId RunId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ServerId ServerId { get; private set; }
}

/// <summary>The run reached its server and breach access started.</summary>
public sealed record BreachStartedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public BreachStartedEventPayload(
        int version,
        RunId runId,
        ServerId serverId,
        int randomAccessLimit,
        int candidateCount)
        : base(version)
    {
        RunId = runId;
        ServerId = serverId;
        RandomAccessLimit = randomAccessLimit;
        CandidateCount = candidateCount;
    }

    public BreachStartedEventPayload(RunId runId, ServerId serverId, int randomAccessLimit, int candidateCount)
        : this(1, runId, serverId, randomAccessLimit, candidateCount)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public RunId RunId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ServerId ServerId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int RandomAccessLimit { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int CandidateCount { get; private set; }
}

/// <summary>One breach candidate was chosen for the run.</summary>
public sealed record BreachCandidateChosenEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public BreachCandidateChosenEventPayload(
        int version,
        RunId runId,
        ServerId serverId,
        CardInstanceId cardId,
        BreachHiddenCandidateSource? source)
        : base(version)
    {
        RunId = runId;
        ServerId = serverId;
        CardId = cardId;
        Source = source;
    }

    public BreachCandidateChosenEventPayload(RunId runId, ServerId serverId, CardInstanceId cardId, BreachHiddenCandidateSource? source)
        : this(1, runId, serverId, cardId, source)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public RunId RunId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ServerId ServerId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    /// <summary>Null when the chosen candidate was already visible.</summary>
    [JsonInclude]
    [JsonRequired]
    public BreachHiddenCandidateSource? Source { get; private set; }
}

/// <summary>One chosen candidate card was accessed.</summary>
public sealed record CardAccessedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public CardAccessedEventPayload(int version, RunId runId, ServerId serverId, CardInstanceId cardId)
        : base(version)
    {
        RunId = runId;
        ServerId = serverId;
        CardId = cardId;
    }

    public CardAccessedEventPayload(RunId runId, ServerId serverId, CardInstanceId cardId)
        : this(1, runId, serverId, cardId)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public RunId RunId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ServerId ServerId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }
}

/// <summary>An accessed card was trashed, paying its printed trash cost.</summary>
public sealed record AccessedCardTrashedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public AccessedCardTrashedEventPayload(int version, RunId runId, CardInstanceId cardId, int trashCost)
        : base(version)
    {
        RunId = runId;
        CardId = cardId;
        TrashCost = trashCost;
    }

    public AccessedCardTrashedEventPayload(RunId runId, CardInstanceId cardId, int trashCost)
        : this(1, runId, cardId, trashCost)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public RunId RunId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int TrashCost { get; private set; }
}

public sealed record RunEndedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public RunEndedEventPayload(int version, RunId runId, PlayerSide initiatingSide, RunEndReason reason, bool successful)
        : base(version)
    {
        RunId = runId;
        InitiatingSide = initiatingSide;
        Reason = reason;
        Successful = successful;
    }

    public RunEndedEventPayload(RunId runId, PlayerSide initiatingSide, RunEndReason reason, bool successful)
        : this(1, runId, initiatingSide, reason, successful)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public RunId RunId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide InitiatingSide { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public RunEndReason Reason { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public bool Successful { get; private set; }
}

/// <summary>Versioned typed event envelope emitted by deterministic Core.</summary>
public sealed record EngineEvent
{
    [JsonConstructor]
    public EngineEvent(int version, long sequence, string kind, EngineEventPayload payload)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(payload);
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Event envelope version must be positive.");
        }

        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Event sequence must be positive.");
        }

        Version = version;
        Sequence = sequence;
        Kind = kind;
        Payload = payload;
    }

    public EngineEvent(long sequence, string kind, EngineEventPayload payload)
        : this(1, sequence, kind, payload)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public int Version { get; private set; }

    [JsonIgnore]
    public int EventVersion => Version;

    [JsonInclude]
    [JsonRequired]
    public long Sequence { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string Kind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public EngineEventPayload Payload { get; private set; }

    internal EngineEvent Copy() => Payload switch
    {
        CreditsAdjustedEventPayload value => new EngineEvent(Version, Sequence, Kind, new CreditsAdjustedEventPayload(value.Version, value.Target, value.Delta, value.CreditsAfter)),
        ClicksAdjustedEventPayload value => new EngineEvent(Version, Sequence, Kind, new ClicksAdjustedEventPayload(value.Version, value.Target, value.Delta, value.ClicksAfter)),
        DamageDealtEventPayload value => new EngineEvent(Version, Sequence, Kind, new DamageDealtEventPayload(value.Version, value.Target, value.Kind, value.Amount, value.CardsTrashed, value.CoreDamageAfter, value.Flatlined)),
        TagsAdjustedEventPayload value => new EngineEvent(Version, Sequence, Kind, new TagsAdjustedEventPayload(value.Version, value.Target, value.Delta, value.TagsAfter)),
        MemoryAdjustedEventPayload value => new EngineEvent(Version, Sequence, Kind, new MemoryAdjustedEventPayload(value.Version, value.Target, value.BaseMemory, value.UsedMemory)),
        ProgramTrashedForMemoryEventPayload value => new EngineEvent(Version, Sequence, Kind, new ProgramTrashedForMemoryEventPayload(value.Version, value.CardId, value.Owner, value.MemoryCost)),
        CounterAdjustedEventPayload value => new EngineEvent(Version, Sequence, Kind, new CounterAdjustedEventPayload(value.Version, value.CardId, value.CounterName, value.Delta, value.CountAfter)),
        VirusPurgedEventPayload value => new EngineEvent(Version, Sequence, Kind, new VirusPurgedEventPayload(value.Version, value.CountersRemoved)),
        LingeringEffectAddedEventPayload value => new EngineEvent(Version, Sequence, Kind, new LingeringEffectAddedEventPayload(value.Version, value.EffectId, value.BehaviorId, value.Expiry)),
        LingeringEffectRemovedEventPayload value => new EngineEvent(Version, Sequence, Kind, new LingeringEffectRemovedEventPayload(value.Version, value.EffectId)),
        LingeringEffectExpiredEventPayload value => new EngineEvent(Version, Sequence, Kind, new LingeringEffectExpiredEventPayload(value.Version, value.EffectId, value.Expiry)),
        TurnStartedEventPayload value => new EngineEvent(Version, Sequence, Kind, new TurnStartedEventPayload(value.Version, value.ActiveSide, value.TurnNumber, value.Clicks)),
        TurnEndedEventPayload value => new EngineEvent(Version, Sequence, Kind, new TurnEndedEventPayload(value.Version, value.Side, value.TurnNumber)),
        ClickSpentEventPayload value => new EngineEvent(Version, Sequence, Kind, new ClickSpentEventPayload(value.Version, value.Side, value.ClicksRemaining)),
        CardMovedEventPayload value => new EngineEvent(Version, Sequence, Kind, new CardMovedEventPayload(value.Version, value.CardId, value.FromZoneId, value.ToZoneId)),
        CardDrawnEventPayload value => new EngineEvent(Version, Sequence, Kind, new CardDrawnEventPayload(value.Version, value.Side, value.CardId, value.FromZoneId, value.ToZoneId)),
        CardInstalledEventPayload value => new EngineEvent(Version, Sequence, Kind, new CardInstalledEventPayload(value.Version, value.Side, value.CardId, value.FromZoneId, value.ToZoneId, value.Cost)),
        AgendaAdvancedEventPayload value => new EngineEvent(Version, Sequence, Kind, new AgendaAdvancedEventPayload(value.Version, value.AgendaId, value.AdvancementCounters, value.Requirement)),
        AgendaScoredEventPayload value => new EngineEvent(Version, Sequence, Kind, new AgendaScoredEventPayload(value.Version, value.Winner, value.AgendaId, value.AgendaPoints)),
        AgendaStolenEventPayload value => new EngineEvent(Version, Sequence, Kind, new AgendaStolenEventPayload(value.Version, value.Winner, value.AgendaId, value.AgendaPoints)),
        GameWonEventPayload value => new EngineEvent(Version, Sequence, Kind, new GameWonEventPayload(value.Version, value.Winner, value.Reason)),
        RunInitiatedEventPayload value => new EngineEvent(Version, Sequence, Kind, new RunInitiatedEventPayload(value.Version, value.RunId, value.ServerId, value.Runner, value.InitiatingSide)),
        RunEventPlayedEventPayload value => new EngineEvent(Version, Sequence, Kind, new RunEventPlayedEventPayload(value.Version, value.RunId, value.EventCardId, value.DefinitionId, value.Cost, value.HostedCredits, value.Runner, value.RunnerCreditsAfter)),
        RunCreditsAdjustedEventPayload value => new EngineEvent(Version, Sequence, Kind, new RunCreditsAdjustedEventPayload(value.Version, value.RunId, value.Delta, value.RunCreditsAfter)),
        RunPriorityPassedEventPayload value => new EngineEvent(Version, Sequence, Kind, new RunPriorityPassedEventPayload(value.Version, value.RunId, value.Window, value.PassedBy, value.NextPriorityHolder, value.ConsecutivePasses)),
        RunTimingWindowOpenedEventPayload value => new EngineEvent(Version, Sequence, Kind, new RunTimingWindowOpenedEventPayload(value.Version, value.RunId, value.Phase, value.Step, value.PriorityWindow, value.PriorityHolder)),
        IceApproachedEventPayload value => new EngineEvent(Version, Sequence, Kind, new IceApproachedEventPayload(value.Version, value.RunId, value.IceId)),
        IceRezzedEventPayload value => new EngineEvent(Version, Sequence, Kind, new IceRezzedEventPayload(value.Version, value.RunId, value.IceId)),
        SubroutinesBrokenEventPayload value => new EngineEvent(Version, Sequence, Kind, new SubroutinesBrokenEventPayload(value.Version, value.RunId, value.EncounterId, value.BreakerId, value.BrokenCount)),
        BreakerPumpedEventPayload value => new EngineEvent(Version, Sequence, Kind, new BreakerPumpedEventPayload(value.Version, value.RunId, value.BreakerId)),
        EncounterInitializedEventPayload value => new EngineEvent(Version, Sequence, Kind, new EncounterInitializedEventPayload(value.Version, value.RunId, value.EncounterId, value.IceId, value.SubroutineCount)),
        EncounterCompletedEventPayload value => new EngineEvent(Version, Sequence, Kind, new EncounterCompletedEventPayload(value.Version, value.RunId, value.EncounterId)),
        RunMovedEventPayload value => new EngineEvent(Version, Sequence, Kind, new RunMovedEventPayload(value.Version, value.RunId, value.FromIceIndex, value.ToIceIndex, value.NextIceId)),
        RunSuccessfulEventPayload value => new EngineEvent(Version, Sequence, Kind, new RunSuccessfulEventPayload(value.Version, value.RunId, value.ServerId)),
        BreachStartedEventPayload value => new EngineEvent(Version, Sequence, Kind, new BreachStartedEventPayload(value.Version, value.RunId, value.ServerId, value.RandomAccessLimit, value.CandidateCount)),
        BreachCandidateChosenEventPayload value => new EngineEvent(Version, Sequence, Kind, new BreachCandidateChosenEventPayload(value.Version, value.RunId, value.ServerId, value.CardId, value.Source)),
        CardAccessedEventPayload value => new EngineEvent(Version, Sequence, Kind, new CardAccessedEventPayload(value.Version, value.RunId, value.ServerId, value.CardId)),
        AccessedCardTrashedEventPayload value => new EngineEvent(Version, Sequence, Kind, new AccessedCardTrashedEventPayload(value.Version, value.RunId, value.CardId, value.TrashCost)),
        RunEndedEventPayload value => new EngineEvent(Version, Sequence, Kind, new RunEndedEventPayload(value.Version, value.RunId, value.InitiatingSide, value.Reason, value.Successful)),
        _ => throw new InvalidOperationException($"No immutable clone is registered for event payload '{Payload.GetType().Name}'."),
    };
}

public static class EngineEventValidator
{
    public static IReadOnlyList<StateInvariantViolation> Collect(EngineEvent? @event, string path = "event")
    {
        var violations = new List<StateInvariantViolation>();
        if (@event is null)
        {
            violations.Add(new StateInvariantViolation("event.null", path, "Engine events must not be null."));
            return violations.AsReadOnly();
        }

        if (@event.Version != 1)
        {
            Add(violations, "event.unsupported-version", $"{path}.version", $"Event envelope version {@event.Version} is not supported; expected 1.");
        }

        if (@event.Sequence <= 0)
        {
            Add(violations, "event.invalid-sequence", $"{path}.sequence", "Event sequence must be positive.");
        }

        if (!StableIdentifier.TryValidate(@event.Kind))
        {
            Add(violations, "event.invalid-kind", $"{path}.kind", "Event kind must be non-empty and contain no whitespace.");
        }

        if (@event.Payload is null)
        {
            Add(violations, "event.missing-payload", $"{path}.payload", "Engine events must contain a typed payload.");
            return violations.AsReadOnly();
        }

        if (@event.Payload.Version != 1)
        {
            Add(violations, "event.payload-unsupported-version", $"{path}.payload.version", $"Event payload version {@event.Payload.Version} is not supported; expected 1.");
        }

        switch (@event.Payload)
        {
            case CreditsAdjustedEventPayload value:
                RequireKind(@event, EngineEventKinds.CreditsAdjusted, path, violations);
                RequireId(value.Target.Value, $"{path}.payload.target", "player", violations);
                if (value.CreditsAfter < 0)
                {
                    Add(violations, "event.negative-credits", $"{path}.payload.creditsAfter", "Credits after an adjustment must not be negative.");
                }

                break;
            case ClicksAdjustedEventPayload value:
                RequireKind(@event, EngineEventKinds.ClicksAdjusted, path, violations);
                RequireId(value.Target.Value, $"{path}.payload.target", "player", violations);
                if (value.ClicksAfter < 0)
                {
                    Add(violations, "event.negative-clicks", $"{path}.payload.clicksAfter", "Clicks after an adjustment must not be negative.");
                }

                break;
            case DamageDealtEventPayload value:
                RequireKind(@event, EngineEventKinds.DamageDealt, path, violations);
                RequireId(value.Target.Value, $"{path}.payload.target", "player", violations);
                if (!Enum.IsDefined(value.Kind))
                {
                    Add(violations, "event.invalid-damage-kind", $"{path}.payload.kind", $"'{value.Kind}' is not a supported damage kind.");
                }

                RequireNumber(value.Amount, $"{path}.payload.amount", "Damage amount", violations, strictlyPositive: true);
                RequireNumber(value.CardsTrashed, $"{path}.payload.cardsTrashed", "Trashed cards", violations);
                RequireNumber(value.CoreDamageAfter, $"{path}.payload.coreDamageAfter", "Core damage", violations);
                break;
            case TagsAdjustedEventPayload value:
                RequireKind(@event, EngineEventKinds.TagsAdjusted, path, violations);
                RequireId(value.Target.Value, $"{path}.payload.target", "player", violations);
                RequireNumber(value.TagsAfter, $"{path}.payload.tagsAfter", "Tags", violations);
                break;
            case MemoryAdjustedEventPayload value:
                RequireKind(@event, EngineEventKinds.MemoryAdjusted, path, violations);
                RequireId(value.Target.Value, $"{path}.payload.target", "player", violations);
                RequireNumber(value.BaseMemory, $"{path}.payload.baseMemory", "Base memory", violations);
                RequireNumber(value.UsedMemory, $"{path}.payload.usedMemory", "Used memory", violations);
                break;
            case ProgramTrashedForMemoryEventPayload value:
                RequireKind(@event, EngineEventKinds.ProgramTrashedForMemory, path, violations);
                RequireId(value.CardId.Value, $"{path}.payload.cardId", "card", violations);
                RequireId(value.Owner.Value, $"{path}.payload.owner", "player", violations);
                RequireNumber(value.MemoryCost, $"{path}.payload.memoryCost", "Memory cost", violations);
                break;
            case CounterAdjustedEventPayload value:
                RequireKind(@event, EngineEventKinds.CounterAdjusted, path, violations);
                RequireId(value.CardId.Value, $"{path}.payload.cardId", "card", violations);
                RequireId(value.CounterName, $"{path}.payload.counterName", "counter", violations);
                RequireNumber(value.CountAfter, $"{path}.payload.countAfter", "Counter value", violations);
                break;
            case VirusPurgedEventPayload value:
                RequireKind(@event, EngineEventKinds.VirusPurged, path, violations);
                RequireNumber(value.CountersRemoved, $"{path}.payload.countersRemoved", "Purged counters", violations);
                break;
            case LingeringEffectAddedEventPayload value:
                RequireKind(@event, EngineEventKinds.LingeringEffectAdded, path, violations);
                RequireId(value.EffectId.Value, $"{path}.payload.effectId", "effect", violations);
                RequireId(value.BehaviorId, $"{path}.payload.behaviorId", "behavior", violations);
                if (!Enum.IsDefined(value.Expiry))
                {
                    Add(violations, "event.invalid-lingering-expiry", $"{path}.payload.expiry", $"'{value.Expiry}' is not supported.");
                }

                break;
            case LingeringEffectRemovedEventPayload value:
                RequireKind(@event, EngineEventKinds.LingeringEffectRemoved, path, violations);
                RequireId(value.EffectId.Value, $"{path}.payload.effectId", "effect", violations);
                break;
            case LingeringEffectExpiredEventPayload value:
                RequireKind(@event, EngineEventKinds.LingeringEffectExpired, path, violations);
                RequireId(value.EffectId.Value, $"{path}.payload.effectId", "effect", violations);
                if (!Enum.IsDefined(value.Expiry))
                {
                    Add(violations, "event.invalid-lingering-expiry", $"{path}.payload.expiry", $"'{value.Expiry}' is not supported.");
                }

                break;
            case TurnStartedEventPayload value:
                RequireKind(@event, EngineEventKinds.TurnStarted, path, violations);
                RequireSide(value.ActiveSide, $"{path}.payload.activeSide", violations);
                RequireNumber(value.TurnNumber, $"{path}.payload.turnNumber", "Turn number", violations, strictlyPositive: true);
                RequireNumber(value.Clicks, $"{path}.payload.clicks", "Clicks", violations);
                break;
            case TurnEndedEventPayload value:
                RequireKind(@event, EngineEventKinds.TurnEnded, path, violations);
                RequireSide(value.Side, $"{path}.payload.side", violations);
                RequireNumber(value.TurnNumber, $"{path}.payload.turnNumber", "Turn number", violations, strictlyPositive: true);
                break;
            case ClickSpentEventPayload value:
                RequireKind(@event, EngineEventKinds.ClickSpent, path, violations);
                RequireSide(value.Side, $"{path}.payload.side", violations);
                RequireNumber(value.ClicksRemaining, $"{path}.payload.clicksRemaining", "Remaining clicks", violations);
                break;
            case CardMovedEventPayload value:
                RequireKind(@event, EngineEventKinds.CardMoved, path, violations);
                RequireId(value.CardId.Value, $"{path}.payload.cardId", "card", violations);
                RequireId(value.FromZoneId.Value, $"{path}.payload.fromZoneId", "zone", violations);
                RequireId(value.ToZoneId.Value, $"{path}.payload.toZoneId", "zone", violations);
                if (value.FromZoneId == value.ToZoneId)
                {
                    Add(violations, "event.same-zone-move", $"{path}.payload", "A card move must change zones.");
                }

                break;
            case CardDrawnEventPayload value:
                RequireKind(@event, EngineEventKinds.CardDrawn, path, violations);
                RequireSide(value.Side, $"{path}.payload.side", violations);
                RequireId(value.CardId.Value, $"{path}.payload.cardId", "card", violations);
                RequireId(value.FromZoneId.Value, $"{path}.payload.fromZoneId", "zone", violations);
                RequireId(value.ToZoneId.Value, $"{path}.payload.toZoneId", "zone", violations);
                break;
            case CardInstalledEventPayload value:
                RequireKind(@event, EngineEventKinds.CardInstalled, path, violations);
                RequireSide(value.Side, $"{path}.payload.side", violations);
                RequireId(value.CardId.Value, $"{path}.payload.cardId", "card", violations);
                RequireId(value.FromZoneId.Value, $"{path}.payload.fromZoneId", "zone", violations);
                RequireId(value.ToZoneId.Value, $"{path}.payload.toZoneId", "zone", violations);
                RequireNumber(value.Cost, $"{path}.payload.cost", "Install cost", violations);
                break;
            case AgendaAdvancedEventPayload value:
                RequireKind(@event, EngineEventKinds.AgendaAdvanced, path, violations);
                RequireId(value.AgendaId.Value, $"{path}.payload.agendaId", "card", violations);
                RequireNumber(value.AdvancementCounters, $"{path}.payload.advancementCounters", "Advancement counters", violations);
                RequireNumber(value.Requirement, $"{path}.payload.requirement", "Advancement requirement", violations, strictlyPositive: true);
                break;
            case AgendaScoredEventPayload value:
                RequireKind(@event, EngineEventKinds.AgendaScored, path, violations);
                RequireSide(value.Winner, $"{path}.payload.winner", violations);
                RequireId(value.AgendaId.Value, $"{path}.payload.agendaId", "card", violations);
                RequireNumber(value.AgendaPoints, $"{path}.payload.agendaPoints", "Agenda points", violations, strictlyPositive: true);
                break;
            case AgendaStolenEventPayload value:
                RequireKind(@event, EngineEventKinds.AgendaStolen, path, violations);
                RequireSide(value.Winner, $"{path}.payload.winner", violations);
                RequireId(value.AgendaId.Value, $"{path}.payload.agendaId", "card", violations);
                RequireNumber(value.AgendaPoints, $"{path}.payload.agendaPoints", "Agenda points", violations, strictlyPositive: true);
                break;
            case GameWonEventPayload value:
                RequireKind(@event, EngineEventKinds.GameWon, path, violations);
                RequireSide(value.Winner, $"{path}.payload.winner", violations);
                if (!Enum.IsDefined(value.Reason))
                {
                    Add(violations, "event.invalid-reason", $"{path}.payload.reason", $"'{value.Reason}' is not a supported win reason.");
                }

                break;
            case RunInitiatedEventPayload value:
                RequireKind(@event, EngineEventKinds.RunInitiated, path, violations);
                RequireId(value.RunId.Value, $"{path}.payload.runId", "run", violations);
                RequireId(value.ServerId.Value, $"{path}.payload.serverId", "server", violations);
                RequireId(value.Runner.Value, $"{path}.payload.runner", "player", violations);
                RequireSide(value.InitiatingSide, $"{path}.payload.initiatingSide", violations);
                break;
            case RunEventPlayedEventPayload value:
                RequireKind(@event, EngineEventKinds.RunEventPlayed, path, violations);
                RequireId(value.RunId.Value, $"{path}.payload.runId", "run", violations);
                RequireId(value.EventCardId.Value, $"{path}.payload.eventCardId", "card", violations);
                RequireId(value.DefinitionId.Value, $"{path}.payload.definitionId", "card", violations);
                RequireNumber(value.Cost, $"{path}.payload.cost", "Event cost", violations);
                RequireNumber(value.HostedCredits, $"{path}.payload.hostedCredits", "Hosted credits", violations);
                RequireId(value.Runner.Value, $"{path}.payload.runner", "player", violations);
                RequireNumber(value.RunnerCreditsAfter, $"{path}.payload.runnerCreditsAfter", "Runner credits after", violations);
                break;
            case RunCreditsAdjustedEventPayload value:
                RequireKind(@event, EngineEventKinds.RunCreditsAdjusted, path, violations);
                RequireId(value.RunId.Value, $"{path}.payload.runId", "run", violations);
                RequireNumber(value.RunCreditsAfter, $"{path}.payload.runCreditsAfter", "Run credits after", violations);
                break;
            case RunPriorityPassedEventPayload value:
                RequireKind(@event, EngineEventKinds.RunPriorityPassed, path, violations);
                RequireId(value.RunId.Value, $"{path}.payload.runId", "run", violations);
                if (!Enum.IsDefined(value.Window))
                {
                    Add(violations, "event.invalid-window", $"{path}.payload.window", $"'{value.Window}' is not a supported run priority window.");
                }

                RequireSide(value.PassedBy, $"{path}.payload.passedBy", violations);
                RequireSide(value.NextPriorityHolder, $"{path}.payload.nextPriorityHolder", violations);
                RequireNumber(value.ConsecutivePasses, $"{path}.payload.consecutivePasses", "Consecutive passes", violations, strictlyPositive: true);
                break;
            case RunTimingWindowOpenedEventPayload value:
                RequireKind(@event, EngineEventKinds.RunTimingWindowOpened, path, violations);
                RequireId(value.RunId.Value, $"{path}.payload.runId", "run", violations);
                if (!Enum.IsDefined(value.Phase))
                {
                    Add(violations, "event.invalid-run-phase", $"{path}.payload.phase", $"'{value.Phase}' is not a supported run phase.");
                }

                if (!Enum.IsDefined(value.Step))
                {
                    Add(violations, "event.invalid-run-step", $"{path}.payload.step", $"'{value.Step}' is not a supported run timing step.");
                }

                if (value.PriorityWindow.HasValue && !Enum.IsDefined(value.PriorityWindow.Value))
                {
                    Add(violations, "event.invalid-window", $"{path}.payload.priorityWindow", $"'{value.PriorityWindow}' is not a supported run priority window.");
                }

                if (value.PriorityHolder.HasValue)
                {
                    RequireSide(value.PriorityHolder.Value, $"{path}.payload.priorityHolder", violations);
                }

                break;
            case IceApproachedEventPayload value:
                RequireKind(@event, EngineEventKinds.IceApproached, path, violations);
                RequireId(value.RunId.Value, $"{path}.payload.runId", "run", violations);
                RequireId(value.IceId.Value, $"{path}.payload.iceId", "card", violations);
                break;
            case IceRezzedEventPayload value:
                RequireKind(@event, EngineEventKinds.IceRezzed, path, violations);
                RequireId(value.RunId.Value, $"{path}.payload.runId", "run", violations);
                RequireId(value.IceId.Value, $"{path}.payload.iceId", "card", violations);
                break;
            case SubroutinesBrokenEventPayload value:
                RequireKind(@event, EngineEventKinds.SubroutinesBroken, path, violations);
                RequireId(value.RunId.Value, $"{path}.payload.runId", "run", violations);
                RequireId(value.EncounterId.Value, $"{path}.payload.encounterId", "encounter", violations);
                RequireId(value.BreakerId.Value, $"{path}.payload.breakerId", "card", violations);
                RequireNumber(value.BrokenCount, $"{path}.payload.brokenCount", "Broken subroutine count", violations, strictlyPositive: true);
                break;
            case BreakerPumpedEventPayload value:
                RequireKind(@event, EngineEventKinds.BreakerPumped, path, violations);
                RequireId(value.RunId.Value, $"{path}.payload.runId", "run", violations);
                RequireId(value.BreakerId.Value, $"{path}.payload.breakerId", "card", violations);
                break;
            case EncounterInitializedEventPayload value:
                RequireKind(@event, EngineEventKinds.EncounterInitialized, path, violations);
                RequireId(value.RunId.Value, $"{path}.payload.runId", "run", violations);
                RequireId(value.EncounterId.Value, $"{path}.payload.encounterId", "encounter", violations);
                RequireId(value.IceId.Value, $"{path}.payload.iceId", "card", violations);
                RequireNumber(value.SubroutineCount, $"{path}.payload.subroutineCount", "Subroutine count", violations);
                break;
            case EncounterCompletedEventPayload value:
                RequireKind(@event, EngineEventKinds.EncounterCompleted, path, violations);
                RequireId(value.RunId.Value, $"{path}.payload.runId", "run", violations);
                RequireId(value.EncounterId.Value, $"{path}.payload.encounterId", "encounter", violations);
                break;
            case RunMovedEventPayload value:
                RequireKind(@event, EngineEventKinds.RunMoved, path, violations);
                RequireId(value.RunId.Value, $"{path}.payload.runId", "run", violations);
                RequireNumber(value.FromIceIndex, $"{path}.payload.fromIceIndex", "Previous ICE index", violations);
                if (value.ToIceIndex < -1)
                {
                    Add(violations, "event.invalid-index", $"{path}.payload.toIceIndex", "Next ICE index must be -1 or nonnegative.");
                }

                if (value.NextIceId.HasValue)
                {
                    RequireId(value.NextIceId.Value.Value, $"{path}.payload.nextIceId", "card", violations);
                }

                break;
            case RunSuccessfulEventPayload value:
                RequireKind(@event, EngineEventKinds.RunSuccessful, path, violations);
                RequireId(value.RunId.Value, $"{path}.payload.runId", "run", violations);
                RequireId(value.ServerId.Value, $"{path}.payload.serverId", "server", violations);
                break;
            case BreachStartedEventPayload value:
                RequireKind(@event, EngineEventKinds.BreachStarted, path, violations);
                RequireId(value.RunId.Value, $"{path}.payload.runId", "run", violations);
                RequireId(value.ServerId.Value, $"{path}.payload.serverId", "server", violations);
                RequireNumber(value.RandomAccessLimit, $"{path}.payload.randomAccessLimit", "Random access limit", violations);
                RequireNumber(value.CandidateCount, $"{path}.payload.candidateCount", "Candidate count", violations);
                break;
            case BreachCandidateChosenEventPayload value:
                RequireKind(@event, EngineEventKinds.BreachCandidateChosen, path, violations);
                RequireId(value.RunId.Value, $"{path}.payload.runId", "run", violations);
                RequireId(value.ServerId.Value, $"{path}.payload.serverId", "server", violations);
                RequireId(value.CardId.Value, $"{path}.payload.cardId", "card", violations);
                break;
            case CardAccessedEventPayload value:
                RequireKind(@event, EngineEventKinds.CardAccessed, path, violations);
                RequireId(value.RunId.Value, $"{path}.payload.runId", "run", violations);
                RequireId(value.ServerId.Value, $"{path}.payload.serverId", "server", violations);
                RequireId(value.CardId.Value, $"{path}.payload.cardId", "card", violations);
                break;
            case AccessedCardTrashedEventPayload value:
                RequireKind(@event, EngineEventKinds.AccessedCardTrashed, path, violations);
                RequireId(value.RunId.Value, $"{path}.payload.runId", "run", violations);
                RequireId(value.CardId.Value, $"{path}.payload.cardId", "card", violations);
                RequireNumber(value.TrashCost, $"{path}.payload.trashCost", "Trash cost", violations);
                break;
            case RunEndedEventPayload value:
                RequireKind(@event, EngineEventKinds.RunEnded, path, violations);
                RequireId(value.RunId.Value, $"{path}.payload.runId", "run", violations);
                RequireSide(value.InitiatingSide, $"{path}.payload.initiatingSide", violations);
                if (!Enum.IsDefined(value.Reason))
                {
                    Add(violations, "event.invalid-run-end-reason", $"{path}.payload.reason", $"'{value.Reason}' is not a supported run-end reason.");
                }

                break;
            default:
                Add(violations, "event.unknown-payload", $"{path}.payload", $"Event payload type '{@event.Payload.GetType().Name}' is not registered.");
                break;
        }

        return violations.AsReadOnly();
    }

    public static void Validate(EngineEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        IReadOnlyList<StateInvariantViolation> violations = Collect(@event);
        if (violations.Count > 0)
        {
            throw new StateInvariantException(violations);
        }
    }

    public static IReadOnlyList<EngineEvent> FreezeBatch(IEnumerable<EngineEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        EngineEvent[] copy = events.Select(static @event =>
        {
            ArgumentNullException.ThrowIfNull(@event);
            Validate(@event);
            return @event.Copy();
        }).ToArray();

        long previous = 0;
        foreach (EngineEvent @event in copy)
        {
            if (@event.Sequence <= previous)
            {
                throw new StateInvariantException([new StateInvariantViolation(
                    "event.nonmonotonic-sequence",
                    "events",
                    "Event sequences must be strictly increasing within a batch.")]);
            }

            previous = @event.Sequence;
        }

        return new ReadOnlyCollection<EngineEvent>(copy);
    }

    private static void RequireKind(EngineEvent @event, string expected, string path, List<StateInvariantViolation> violations)
    {
        if (!string.Equals(@event.Kind, expected, StringComparison.Ordinal))
        {
            Add(violations, "event.kind-payload-mismatch", $"{path}.kind", $"Payload requires event kind '{expected}'.");
        }
    }

    private static void RequireId(string? value, string path, string label, List<StateInvariantViolation> violations)
    {
        if (!StableIdentifier.TryValidate(value))
        {
            Add(violations, "event.invalid-id", path, $"Event {label} ID must be non-empty and valid.");
        }
    }

    private static void RequireSide(PlayerSide side, string path, List<StateInvariantViolation> violations)
    {
        if (side is not (PlayerSide.Corp or PlayerSide.Runner))
        {
            Add(violations, "event.invalid-side", path, $"'{side}' is not a valid player side.");
        }
    }

    private static void RequireNumber(
        int value,
        string path,
        string label,
        List<StateInvariantViolation> violations,
        bool strictlyPositive = false)
    {
        if (value < 0 || strictlyPositive && value == 0)
        {
            Add(violations, "event.invalid-number", path, $"{label} must be {(strictlyPositive ? "positive" : "nonnegative")}.");
        }
    }

    private static void Add(List<StateInvariantViolation> violations, string code, string path, string message) =>
        violations.Add(new StateInvariantViolation(code, path, message));
}

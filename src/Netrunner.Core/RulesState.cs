using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

/// <summary>Damage families supported by the M2 rules primitives.</summary>
public enum DamageKind
{
    Net,
    Meat,
    Core,
}

/// <summary>Explicit cleanup boundary for a game-level lingering effect.</summary>
public enum LingeringEffectExpiry
{
    Encounter,
    Run,
    Turn,
}

/// <summary>
/// Per-player M2 resources that are not part of the M1 turn economy.  Keeping
/// this in the optional rules aggregate leaves no-run M1 state bytes intact.
/// </summary>
public sealed record PlayerRulesState
{
    [JsonConstructor]
    public PlayerRulesState(
        PlayerId playerId,
        int baseHandSize,
        int coreDamage,
        int tags,
        int baseMemory,
        int usedMemory)
    {
        PlayerId = playerId;
        BaseHandSize = baseHandSize;
        CoreDamage = coreDamage;
        Tags = tags;
        BaseMemory = baseMemory;
        UsedMemory = usedMemory;
    }

    public PlayerRulesState(PlayerId playerId)
        : this(playerId, 5, 0, 0, 4, 0)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId PlayerId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int BaseHandSize { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int CoreDamage { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Tags { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int BaseMemory { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int UsedMemory { get; private set; }

    [JsonIgnore]
    public int MaximumHandSize => BaseHandSize - CoreDamage;

    internal PlayerRulesState Copy() => new(
        PlayerId,
        BaseHandSize,
        CoreDamage,
        Tags,
        BaseMemory,
        UsedMemory);
}

/// <summary>Materialized memory usage for one installed program.</summary>
public sealed record ProgramMemoryState
{
    [JsonConstructor]
    public ProgramMemoryState(CardInstanceId cardId, PlayerId owner, int memoryCost)
    {
        CardId = cardId;
        Owner = owner;
        MemoryCost = memoryCost;
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

    internal ProgramMemoryState Copy() => new(CardId, Owner, MemoryCost);
}

/// <summary>A named, non-negative counter attached to a physical card.</summary>
public sealed record CardCounterState
{
    [JsonConstructor]
    public CardCounterState(CardInstanceId cardId, string counterName, int count)
    {
        ArgumentNullException.ThrowIfNull(counterName);
        CardId = cardId;
        CounterName = counterName;
        Count = count;
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string CounterName { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Count { get; private set; }

    internal CardCounterState Copy() => new(CardId, CounterName, Count);
}

/// <summary>
/// A typed game-level lingering effect placeholder. Its compiled behavior is
/// selected by BehaviorId outside serialized state; no delegate or payload is
/// stored here. Turn expiry intentionally lives at game level so it survives
/// run cleanup.
/// </summary>
public sealed record GameLingeringEffect
{
    [JsonConstructor]
    public GameLingeringEffect(
        int version,
        RunEffectId effectId,
        string behaviorId,
        CardInstanceId? sourceCardId,
        LingeringEffectExpiry expiry)
    {
        ArgumentNullException.ThrowIfNull(behaviorId);
        Version = version;
        EffectId = effectId;
        BehaviorId = behaviorId;
        SourceCardId = sourceCardId;
        Expiry = expiry;
    }

    public GameLingeringEffect(
        RunEffectId effectId,
        string behaviorId,
        CardInstanceId? sourceCardId,
        LingeringEffectExpiry expiry)
        : this(1, effectId, behaviorId, sourceCardId, expiry)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public int Version { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public RunEffectId EffectId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string BehaviorId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId? SourceCardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public LingeringEffectExpiry Expiry { get; private set; }

    internal GameLingeringEffect Copy() => new(Version, EffectId, BehaviorId, SourceCardId, Expiry);
}

/// <summary>
/// Optional M2 rules aggregate. It is intentionally absent from M1/no-run
/// states and deeply detached whenever it crosses a state boundary.
/// </summary>
public sealed record RulesState
{
    [JsonConstructor]
    public RulesState(
        int version,
        IReadOnlyList<PlayerRulesState> players,
        IReadOnlyList<ProgramMemoryState> programs,
        IReadOnlyList<CardCounterState> counters,
        IReadOnlyList<GameLingeringEffect> lingeringEffects,
        IReadOnlyList<TriggerRegistration>? triggers = null,
        IReadOnlyList<InterruptRegistration>? interrupts = null)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(programs);
        ArgumentNullException.ThrowIfNull(counters);
        ArgumentNullException.ThrowIfNull(lingeringEffects);
        Version = version;
        Players = new ReadOnlyCollection<PlayerRulesState>(players.Select(static value =>
        {
            ArgumentNullException.ThrowIfNull(value);
            return value.Copy();
        }).ToArray());
        Programs = new ReadOnlyCollection<ProgramMemoryState>(programs.Select(static value =>
        {
            ArgumentNullException.ThrowIfNull(value);
            return value.Copy();
        }).ToArray());
        Counters = new ReadOnlyCollection<CardCounterState>(counters.Select(static value =>
        {
            ArgumentNullException.ThrowIfNull(value);
            return value.Copy();
        }).ToArray());
        LingeringEffects = new ReadOnlyCollection<GameLingeringEffect>(lingeringEffects.Select(static value =>
        {
            ArgumentNullException.ThrowIfNull(value);
            return value.Copy();
        }).ToArray());
        Triggers = new ReadOnlyCollection<TriggerRegistration>((triggers ?? []).Select(static value =>
        {
            ArgumentNullException.ThrowIfNull(value);
            return value.Copy();
        }).ToArray());
        Interrupts = new ReadOnlyCollection<InterruptRegistration>((interrupts ?? []).Select(static value =>
        {
            ArgumentNullException.ThrowIfNull(value);
            return value.Copy();
        }).ToArray());
    }

    public RulesState(
        IReadOnlyList<PlayerRulesState> players,
        IReadOnlyList<ProgramMemoryState>? programs = null,
        IReadOnlyList<CardCounterState>? counters = null,
        IReadOnlyList<GameLingeringEffect>? lingeringEffects = null,
        IReadOnlyList<TriggerRegistration>? triggers = null,
        IReadOnlyList<InterruptRegistration>? interrupts = null)
        : this(1, players, programs ?? [], counters ?? [], lingeringEffects ?? [], triggers ?? [], interrupts ?? [])
    {
    }

    [JsonInclude]
    [JsonRequired]
    public int Version { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<PlayerRulesState> Players { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<ProgramMemoryState> Programs { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<CardCounterState> Counters { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<GameLingeringEffect> LingeringEffects { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<TriggerRegistration> Triggers { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<InterruptRegistration> Interrupts { get; private set; }

    public static RulesState CreateDefault(IEnumerable<PlayerState> players)
    {
        ArgumentNullException.ThrowIfNull(players);
        return new RulesState(players.Select(player => new PlayerRulesState(
            player.Id,
            baseHandSize: 5,
            coreDamage: 0,
            tags: 0,
            baseMemory: player.Side == PlayerSide.Runner ? 4 : 0,
            usedMemory: 0)).ToArray());
    }

    internal RulesState Copy() => new(Version, Players, Programs, Counters, LingeringEffects, Triggers, Interrupts);
}

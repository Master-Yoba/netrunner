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
/// The rules meaning of a named card counter.  Generic counters retain the
/// existing name-based behavior; the two named meanings are reserved for the
/// typed virus and hosted-credit primitives.
/// </summary>
public enum CardCounterPurpose
{
    Generic,
    Virus,
    HostedCredit,
}

/// <summary>
/// The kind of cost for which a hosted credit counter may be spent.
/// </summary>
public enum HostedCreditSpendRestriction
{
    CardEffectOnly,
    AbilityCost,
    RunCost,
    AnyCost,

    /// <summary>
    /// Spendable only for the uses its card declares
    /// (<see cref="CardBehaviorDefinition.HostedCreditUses"/>): recurring
    /// credits for trash costs, credits to install job resources, and so on.
    /// </summary>
    DeclaredUseOnly,
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

/// <summary>
/// VP-CARD-AS-AGENDA: a non-agenda card added to a score area as an agenda (CR 26.03 10.1.3): the points it is worth there
/// and whether it may be forfeited (Myōshu, Word on the Street). It was neither scored nor stolen.
/// </summary>
public sealed record ScoreAreaAgenda
{
    [JsonConstructor]
    public ScoreAreaAgenda(CardInstanceId cardId, int points, bool cannotForfeit)
    {
        CardId = cardId;
        Points = points;
        CannotForfeit = cannotForfeit;
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Points { get; private set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CannotForfeit { get; private set; }
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

/// <summary>
/// A named, positive counter attached to a physical card. Zero-count
/// counters are omitted from serialized state and are represented by the
/// zero result of <see cref="CardCounterRules"/> transitions.
/// </summary>
public sealed record CardCounterState
{
    /// <summary>
    /// Creates a legacy named counter. The reserved name <c>virus</c> receives
    /// its typed virus purpose; all other names use the generic purpose. The
    /// reserved name <c>credit</c> must be created through
    /// <see cref="CreateHostedCredit"/> (or the explicit hosted-credit
    /// constructor).
    /// </summary>
    public CardCounterState(CardInstanceId cardId, string counterName, int count)
        : this(
            cardId,
            counterName,
            count,
            string.Equals(counterName, "virus", StringComparison.Ordinal)
                ? CardCounterPurpose.Virus
                : CardCounterPurpose.Generic,
            spendRestriction: null)
    {
    }

    /// <summary>Creates a counter with an explicit typed purpose.</summary>
    [JsonConstructor]
    public CardCounterState(
        CardInstanceId cardId,
        string counterName,
        int count,
        CardCounterPurpose purpose,
        HostedCreditSpendRestriction? spendRestriction)
    {
        if (!StableIdentifier.TryValidate(counterName))
        {
            throw new ArgumentException(
                "Counter name must be a stable nonblank identifier.",
                nameof(counterName));
        }

        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                count,
                "Counter count must be positive; zero-count counters are omitted.");
        }

        if (!Enum.IsDefined(purpose))
        {
            throw new ArgumentOutOfRangeException(nameof(purpose));
        }

        if (spendRestriction is { } restriction && !Enum.IsDefined(restriction))
        {
            throw new ArgumentOutOfRangeException(nameof(spendRestriction));
        }

        ValidatePurposeNameRestriction(counterName, purpose, spendRestriction);
        CardId = cardId;
        CounterName = counterName;
        Count = count;
        Purpose = purpose;
        SpendRestriction = spendRestriction;
    }

    /// <summary>Creates an explicitly typed hosted-credit pool.</summary>
    public CardCounterState(
        CardInstanceId cardId,
        int count,
        HostedCreditSpendRestriction spendRestriction = HostedCreditSpendRestriction.CardEffectOnly)
        : this(
            cardId,
            "credit",
            count,
            CardCounterPurpose.HostedCredit,
            spendRestriction)
    {
    }

    /// <summary>Creates a hosted-credit pool named <c>credit</c>.</summary>
    public static CardCounterState CreateHostedCredit(
        CardInstanceId cardId,
        int count,
        HostedCreditSpendRestriction spendRestriction = HostedCreditSpendRestriction.CardEffectOnly) =>
        new(cardId, count, spendRestriction);

    /// <summary>Alias for <see cref="CreateHostedCredit"/>.</summary>
    public static CardCounterState HostedCredit(
        CardInstanceId cardId,
        int count,
        HostedCreditSpendRestriction spendRestriction = HostedCreditSpendRestriction.CardEffectOnly) =>
        CreateHostedCredit(cardId, count, spendRestriction);

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string CounterName { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Count { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardCounterPurpose Purpose { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public HostedCreditSpendRestriction? SpendRestriction { get; private set; }

    internal CardCounterState Copy() => new(
        CardId,
        CounterName,
        Count,
        Purpose,
        SpendRestriction);

    internal static bool IsValidPurposeNameRestriction(
        string? counterName,
        CardCounterPurpose purpose,
        HostedCreditSpendRestriction? spendRestriction)
    {
        if (!StableIdentifier.TryValidate(counterName)
            || !Enum.IsDefined(purpose)
            || (spendRestriction is { } restriction && !Enum.IsDefined(restriction)))
        {
            return false;
        }

        return purpose switch
        {
            CardCounterPurpose.Generic =>
                !string.Equals(counterName, "virus", StringComparison.Ordinal)
                && !string.Equals(counterName, "credit", StringComparison.Ordinal)
                && spendRestriction is null,
            CardCounterPurpose.Virus =>
                string.Equals(counterName, "virus", StringComparison.Ordinal)
                && spendRestriction is null,
            CardCounterPurpose.HostedCredit =>
                string.Equals(counterName, "credit", StringComparison.Ordinal)
                && spendRestriction is not null,
            _ => false,
        };
    }

    private static void ValidatePurposeNameRestriction(
        string counterName,
        CardCounterPurpose purpose,
        HostedCreditSpendRestriction? spendRestriction)
    {
        if (!IsValidPurposeNameRestriction(counterName, purpose, spendRestriction))
        {
            throw new ArgumentException(
                "Counter purpose, name, and hosted-credit spend restriction do not form a valid combination.",
                nameof(purpose));
        }
    }
}

/// <summary>
/// Pure arithmetic and legality helpers for card counters. These helpers do
/// not mutate a <see cref="CardCounterState"/>; a zero result means the caller
/// should omit the counter from the next RulesState aggregate.
/// </summary>
public static class CardCounterRules
{
    /// <summary>
    /// Returns the count after loading a hosted-credit pool. A generic or
    /// virus counter cannot be silently converted into hosted credits.
    /// </summary>
    public static int Load(CardCounterState? counter, int amount)
    {
        if (counter is not null && counter.Purpose != CardCounterPurpose.HostedCredit)
        {
            throw new InvalidOperationException(
                $"Counter '{counter.CounterName}' is not a hosted-credit pool.");
        }

        return Add(counter?.Count ?? 0, amount);
    }

    /// <summary>Returns the count after placing additional counters.</summary>
    public static int Place(CardCounterState? counter, int amount) =>
        Add(counter?.Count ?? 0, amount);

    /// <summary>Returns the count after taking counters, without spend restrictions.</summary>
    public static int Take(CardCounterState counter, int amount)
    {
        ArgumentNullException.ThrowIfNull(counter);
        return Remove(counter.Count, amount);
    }

    /// <summary>
    /// Returns whether a hosted-credit counter can be spent for the supplied
    /// concrete cost kind. Generic and virus counters are not credit pools;
    /// <see cref="HostedCreditSpendRestriction.CardEffectOnly"/> describes a
    /// placement source and is never a spend permission. An AnyCost pool
    /// matches either concrete ability or run cost.
    /// </summary>
    public static bool CanSpend(
        CardCounterState counter,
        HostedCreditSpendRestriction costKind)
    {
        ArgumentNullException.ThrowIfNull(counter);
        if (!Enum.IsDefined(costKind))
        {
            throw new ArgumentOutOfRangeException(nameof(costKind));
        }

        if (counter.Purpose != CardCounterPurpose.HostedCredit
            || costKind is HostedCreditSpendRestriction.CardEffectOnly
                or HostedCreditSpendRestriction.AnyCost)
        {
            return false;
        }

        return counter.SpendRestriction is { } restriction
            && (restriction == HostedCreditSpendRestriction.AnyCost
                || restriction == costKind);
    }

    /// <summary>Alias for <see cref="CanSpend"/> used by legality projections.</summary>
    public static bool IsSpendLegal(
        CardCounterState counter,
        HostedCreditSpendRestriction costKind) =>
        CanSpend(counter, costKind);

    /// <summary>
    /// Returns the count after spending counters for a cost kind. Unlike
    /// <see cref="Take(CardCounterState, int)"/>, this enforces a hosted-credit
    /// spend restriction.
    /// </summary>
    public static int Spend(
        CardCounterState counter,
        int amount,
        HostedCreditSpendRestriction costKind)
    {
        ArgumentNullException.ThrowIfNull(counter);
        if (!CanSpend(counter, costKind))
        {
            throw new InvalidOperationException(
                $"Counter '{counter.CounterName}' cannot be spent for '{costKind}'.");
        }

        return Remove(counter.Count, amount);
    }

    private static int Add(int currentCount, int amount)
    {
        ValidateCurrentCount(currentCount);
        ValidateAmount(amount);

        long next = checked((long)currentCount + amount);
        if (next > int.MaxValue)
        {
            throw new OverflowException("The resulting card-counter count exceeds Int32.MaxValue.");
        }

        return (int)next;
    }

    private static int Remove(int currentCount, int amount)
    {
        ValidateCurrentCount(currentCount);
        ValidateAmount(amount);

        long next = checked((long)currentCount - amount);
        if (next < 0)
        {
            throw new InvalidOperationException("The card-counter count cannot underflow.");
        }

        return (int)next;
    }

    private static void ValidateCurrentCount(int currentCount)
    {
        if (currentCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentCount),
                currentCount,
                "A card-counter count cannot be negative.");
        }
    }

    private static void ValidateAmount(int amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "A card-counter transition amount must be positive.");
        }
    }
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
        IReadOnlyList<InterruptRegistration>? interrupts = null,
        int badPublicity = 0,
        IReadOnlyList<ScoreAreaAgenda>? scoredAsAgendas = null)
    {
        // PR #152's review: a null entry is an invalid state, rejected here as the other collections' are.
        ScoredAsAgendas = scoredAsAgendas is { Count: > 0 }
            ? new ReadOnlyCollection<ScoreAreaAgenda>(scoredAsAgendas.Select(static entry =>
                entry ?? throw new ArgumentException("Cards scored as agendas must not contain null entries.", nameof(scoredAsAgendas))).ToArray())
            : null;
        ArgumentNullException.ThrowIfNull(players);
        if (badPublicity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(badPublicity), "Bad publicity cannot be negative.");
        }

        BadPublicity = badPublicity;
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

    /// <summary>
    /// VP-BAD-PUBLICITY: the bad publicity counters on the Corp (CR 26.03 1.9.5d, 10.6). Counters hosted on a card are card
    /// counters instead, with no effect while hosted (CR 1.13.3). Omitted while zero, so states without it keep their bytes.
    /// </summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int BadPublicity { get; private set; }

    /// <summary>VP-CARD-AS-AGENDA: the cards added to a score area as agendas; omitted while there are none.</summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ScoreAreaAgenda>? ScoredAsAgendas { get; private set; }

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

    internal RulesState Copy() => new(Version, Players, Programs, Counters, LingeringEffects, Triggers, Interrupts, BadPublicity, ScoredAsAgendas);
}

using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

/// <summary>CR 26.03 section 6.9 phase represented by a live run.</summary>
public enum RunCrPhase
{
    Initiation,
    Approach,
    Encounter,
    Movement,
    Success,
    RunEnd,
}

/// <summary>
/// The bounded automatic/decision steps used by <see cref="RunTimingFrame"/>.
/// The names intentionally mirror the CR 26.03 6.9 timing matrix.
/// </summary>
public enum RunTimingStep
{
    // 6.9.1e: the initiation paid/rez window (P + non-ICE R).
    InitiationPaidWindow,
    // 6.9.2a: approach the outermost remaining ICE.
    Approach,
    // 6.9.2b: ordinary alternating priority; the Corp may rez the approached ICE.
    ApproachPaidWindow,
    // 6.9.3a: initialize the encounter.
    EncounterInitialization,
    // 6.9.3b: the encounter paid window (P).
    EncounterPaidWindow,
    // 6.9.3c and following encounter processing. Slice 1 has no subroutines.
    Encounter,
    // 6.9.4b: the movement paid window (P).
    MovementPaidWindow,
    // 6.9.4c: the Runner-only jack-out/continue decision.
    JackOutDecision,
    // 6.9.4d: continuing moves inward automatically.
    MoveInward,
    // 6.9.4e: paid/non-ICE-rez window after movement (P + non-ICE R).
    AfterMovePaidWindow,
    // 6.9.5a: success is declared without a paid window.
    Success,
    // 6.9.5b: the nested breach boundary that starts candidate selection.
    BreachBoundary,
    // 7.5.4 and 7.3.4: the Runner picks one candidate to access.
    BreachCandidateSelection,
    // 7.2.2: the Runner's mid-access window; an accessed agenda offers the
    // pass only (no mid-access ability is modeled in the M2 card set) and
    // 7.2.3 steals it automatically on progression.
    BreachAccessAbility,
    // 6.9.6: cleanup at run end.
    RunEnd,
}

/// <summary>Kind of alternating priority window currently represented.</summary>
public enum RunPriorityWindowKind
{
    InitiationPaid,
    ApproachPaid,
    EncounterPaid,
    MovementPaid,
    AfterMovePaid,
}

public enum RunEndReason
{
    JackedOut,
    BreachCompleted,
    EndedByRule,
}

public enum RunAccessState
{
    NotStarted,
    InProgress,
    Completed,
}

public enum RunEncounterStatus
{
    Initialized,
    Complete,
}

public enum RunLingeringEffectExpiry
{
    Encounter,
    Run,
}

/// <summary>The hidden Corp pool a random breach candidate is drawn from.</summary>
public enum BreachHiddenCandidateSource
{
    CorpHand,
    CorpDeck,
}

/// <summary>
/// Explicit priority-window state.  The capability flags are persisted so a
/// restored decision cannot silently acquire an ability that was not legal at
/// the point it was issued.
/// </summary>
public sealed record RunPriorityState
{
    [JsonConstructor]
    public RunPriorityState(
        int version,
        RunPriorityWindowKind window,
        PlayerSide priorityHolder,
        int consecutivePasses,
        bool corpMayRezApproachedIce,
        bool corpMayRezNonIce)
    {
        Version = version;
        Window = window;
        PriorityHolder = priorityHolder;
        ConsecutivePasses = consecutivePasses;
        CorpMayRezApproachedIce = corpMayRezApproachedIce;
        CorpMayRezNonIce = corpMayRezNonIce;
    }

    public RunPriorityState(
        RunPriorityWindowKind window,
        PlayerSide priorityHolder,
        bool corpMayRezApproachedIce = false,
        bool corpMayRezNonIce = false)
        : this(
            1,
            window,
            priorityHolder,
            0,
            corpMayRezApproachedIce,
            corpMayRezNonIce)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public int Version { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public RunPriorityWindowKind Window { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide PriorityHolder { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int ConsecutivePasses { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public bool CorpMayRezApproachedIce { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public bool CorpMayRezNonIce { get; private set; }

    internal RunPriorityState Copy() => new(
        Version,
        Window,
        PriorityHolder,
        ConsecutivePasses,
        CorpMayRezApproachedIce,
        CorpMayRezNonIce);
}

public sealed record RunSubroutineState
{
    [JsonConstructor]
    public RunSubroutineState(
        int version,
        SubroutineId id,
        int ordinal,
        bool broken,
        bool resolved)
    {
        Version = version;
        Id = id;
        Ordinal = ordinal;
        Broken = broken;
        Resolved = resolved;
    }

    public RunSubroutineState(SubroutineId id, int ordinal)
        : this(1, id, ordinal, false, false)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public int Version { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public SubroutineId Id { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Ordinal { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public bool Broken { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public bool Resolved { get; private set; }

    internal RunSubroutineState Copy() => new(Version, Id, Ordinal, Broken, Resolved);
}

public sealed record RunEncounterState
{
    [JsonConstructor]
    public RunEncounterState(
        int version,
        EncounterId id,
        CardInstanceId iceId,
        RunEncounterStatus status,
        IReadOnlyList<RunSubroutineState> subroutines)
    {
        ArgumentNullException.ThrowIfNull(subroutines);
        Version = version;
        Id = id;
        IceId = iceId;
        Status = status;
        Subroutines = new ReadOnlyCollection<RunSubroutineState>(subroutines.Select(static value =>
        {
            ArgumentNullException.ThrowIfNull(value);
            return value.Copy();
        }).ToArray());
    }

    public RunEncounterState(EncounterId id, CardInstanceId iceId, IReadOnlyList<RunSubroutineState> subroutines)
        : this(3, id, iceId, RunEncounterStatus.Initialized, subroutines)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public int Version { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public EncounterId Id { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId IceId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public RunEncounterStatus Status { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<RunSubroutineState> Subroutines { get; private set; }

    internal RunEncounterState Copy() => new(Version, Id, IceId, Status, Subroutines);
}

/// <summary>
/// A serializable run-scoped lingering effect placeholder.  Slice 1 does not
/// execute effects, but retains their typed expiry boundary for later slices.
/// </summary>
public sealed record RunLingeringEffect
{
    [JsonConstructor]
    public RunLingeringEffect(
        int version,
        RunEffectId id,
        string behaviorId,
        CardInstanceId? sourceCardId,
        RunLingeringEffectExpiry expiry)
    {
        ArgumentNullException.ThrowIfNull(behaviorId);
        Version = version;
        Id = id;
        BehaviorId = behaviorId;
        SourceCardId = sourceCardId;
        Expiry = expiry;
    }

    public RunLingeringEffect(
        RunEffectId id,
        string behaviorId,
        CardInstanceId? sourceCardId,
        RunLingeringEffectExpiry expiry)
        : this(1, id, behaviorId, sourceCardId, expiry)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public int Version { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public RunEffectId Id { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string BehaviorId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId? SourceCardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public RunLingeringEffectExpiry Expiry { get; private set; }

    internal RunLingeringEffect Copy() => new(Version, Id, BehaviorId, SourceCardId, Expiry);
}

/// <summary>
/// A run-scoped increase to the number of hidden central cards that may be
/// accessed during the breach. The value is snapshotted when the run starts;
/// later source-counter changes cannot alter an active run.
/// </summary>
public sealed record RunBreachAccessModifier
{
    [JsonConstructor]
    public RunBreachAccessModifier(
        CardInstanceId sourceCardId,
        ServerId attackedServerId,
        int delta,
        string reasonKey)
    {
        if (!StableIdentifier.TryValidate(sourceCardId.Value))
        {
            throw new ArgumentException("A breach modifier requires a stable source card ID.", nameof(sourceCardId));
        }

        if (!StableIdentifier.TryValidate(attackedServerId.Value))
        {
            throw new ArgumentException("A breach modifier requires a stable attacked server ID.", nameof(attackedServerId));
        }

        if (delta <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(delta), "A breach modifier delta must be positive.");
        }

        if (!StableIdentifier.TryValidate(reasonKey))
        {
            throw new ArgumentException("A breach modifier requires a stable reason key.", nameof(reasonKey));
        }

        SourceCardId = sourceCardId;
        AttackedServerId = attackedServerId;
        Delta = delta;
        ReasonKey = reasonKey;
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId SourceCardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ServerId AttackedServerId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Delta { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string ReasonKey { get; private set; }

    internal RunBreachAccessModifier Copy() => new(SourceCardId, AttackedServerId, Delta, ReasonKey);
}

/// <summary>
/// Authoritative, optional state for one run.  It is omitted entirely when no
/// run is active, preserving M1 state bytes and hashes for default games.
/// </summary>
public sealed record RunState
{
    [JsonConstructor]
    public RunState(
        int version,
        RunId id,
        ServerId attackedServer,
        PlayerSide initiatingSide,
        PlayerId initiatingPlayer,
        CardInstanceId? initiatingSource,
        RunCrPhase phase,
        RunTimingStep step,
        int currentIceIndex,
        CardInstanceId? currentIceId,
        bool successful,
        bool unsuccessful,
        RunEndReason? endReason,
        RunPriorityState? priority,
        RunEncounterState? encounter,
        IReadOnlyList<CardInstanceId> breachCandidates,
        int randomAccessLimit,
        IReadOnlyList<CardInstanceId> chosenAccessCards,
        IReadOnlyList<CardInstanceId> accessedCards,
        RunAccessState accessState,
        int runCredits,
        CardInstanceId? activeRunEventId,
        IReadOnlyList<RunLingeringEffect> lingeringEffects,
        IReadOnlyList<RunBreachAccessModifier>? breachAccessModifiers = null)
    {
        ArgumentNullException.ThrowIfNull(breachCandidates);
        ArgumentNullException.ThrowIfNull(chosenAccessCards);
        ArgumentNullException.ThrowIfNull(accessedCards);
        ArgumentNullException.ThrowIfNull(lingeringEffects);
        Version = version;
        Id = id;
        AttackedServer = attackedServer;
        InitiatingSide = initiatingSide;
        InitiatingPlayer = initiatingPlayer;
        InitiatingSource = initiatingSource;
        Phase = phase;
        Step = step;
        CurrentIceIndex = currentIceIndex;
        CurrentIceId = currentIceId;
        Successful = successful;
        Unsuccessful = unsuccessful;
        EndReason = endReason;
        Priority = priority?.Copy();
        Encounter = encounter?.Copy();
        BreachCandidates = FreezeIds(breachCandidates);
        RandomAccessLimit = randomAccessLimit;
        ChosenAccessCards = FreezeIds(chosenAccessCards);
        AccessedCards = FreezeIds(accessedCards);
        AccessState = accessState;
        RunCredits = runCredits;
        ActiveRunEventId = activeRunEventId;
        LingeringEffects = new ReadOnlyCollection<RunLingeringEffect>(lingeringEffects.Select(static value =>
        {
            ArgumentNullException.ThrowIfNull(value);
            return value.Copy();
        }).ToArray());
        BreachAccessModifiers = breachAccessModifiers is { Count: > 0 }
            ? new ReadOnlyCollection<RunBreachAccessModifier>(breachAccessModifiers.Select(static value =>
            {
                ArgumentNullException.ThrowIfNull(value);
                return value.Copy();
            }).ToArray())
            : null;
    }

    public RunState(
        RunId id,
        ServerId attackedServer,
        PlayerSide initiatingSide,
        PlayerId initiatingPlayer,
        int currentIceIndex,
        CardInstanceId? currentIceId,
        RunTimingStep step,
        RunPriorityState? priority = null,
        CardInstanceId? initiatingSource = null,
        int runCredits = 0,
        CardInstanceId? activeRunEventId = null,
        IReadOnlyList<RunBreachAccessModifier>? breachAccessModifiers = null)
        : this(
            1,
            id,
            attackedServer,
            initiatingSide,
            initiatingPlayer,
            initiatingSource,
            PhaseFor(step),
            step,
            currentIceIndex,
            currentIceId,
            false,
            false,
            null,
            priority,
            null,
            [],
            0,
            [],
            [],
            RunAccessState.NotStarted,
            runCredits,
            activeRunEventId,
            [],
            breachAccessModifiers)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public int Version { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public RunId Id { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ServerId AttackedServer { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide InitiatingSide { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerId InitiatingPlayer { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId? InitiatingSource { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public RunCrPhase Phase { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public RunTimingStep Step { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int CurrentIceIndex { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId? CurrentIceId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public bool Successful { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public bool Unsuccessful { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public RunEndReason? EndReason { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public RunPriorityState? Priority { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public RunEncounterState? Encounter { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<CardInstanceId> BreachCandidates { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int RandomAccessLimit { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<CardInstanceId> ChosenAccessCards { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<CardInstanceId> AccessedCards { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public RunAccessState AccessState { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int RunCredits { get; private set; }

    /// <summary>
    /// The run event played to start this run, kept faceup in the public
    /// RunnerPlayArea for the run's duration and trashed to the public heap
    /// faceup at run end.
    /// CR 26.03 1.10.3c/1.10.4/8.6.7. Null when the run started without an
    /// event; a run plays at most one event.
    /// </summary>
    [JsonInclude]
    [JsonRequired]
    public CardInstanceId? ActiveRunEventId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<RunLingeringEffect> LingeringEffects { get; private set; }

    /// <summary>
    /// Immutable run-start snapshots that increase hidden central access. The
    /// field is omitted from legacy/basic/event runs when no modifier exists.
    /// </summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<RunBreachAccessModifier>? BreachAccessModifiers { get; private set; }

    /// <summary>
    /// The card currently being accessed: the last chosen access card that is
    /// not yet in the accessed list, or null when access is complete.
    /// Sequential access guarantees at most one such card; the last is used
    /// for safety.
    /// </summary>
    internal CardInstanceId? CurrentAccessCard
    {
        get
        {
            for (int index = ChosenAccessCards.Count - 1; index >= 0; index--)
            {
                if (!AccessedCards.Contains(ChosenAccessCards[index]))
                {
                    return ChosenAccessCards[index];
                }
            }

            return null;
        }
    }

    internal RunState Copy() => new(
        Version,
        Id,
        AttackedServer,
        InitiatingSide,
        InitiatingPlayer,
        InitiatingSource,
        Phase,
        Step,
        CurrentIceIndex,
        CurrentIceId,
        Successful,
        Unsuccessful,
        EndReason,
        Priority,
        Encounter,
        BreachCandidates,
        RandomAccessLimit,
        ChosenAccessCards,
        AccessedCards,
        AccessState,
        RunCredits,
        ActiveRunEventId,
        LingeringEffects,
        BreachAccessModifiers);

    /// <summary>
    /// Creates the next immutable timing cursor while retaining the run's
    /// ordered candidate/access/effect collections.  Reducers use this rather
    /// than mutating a restored run in place.
    /// </summary>
    internal RunState WithTiming(
        RunTimingStep step,
        int currentIceIndex,
        CardInstanceId? currentIceId,
        bool successful,
        bool unsuccessful,
        RunEndReason? endReason,
        RunPriorityState? priority,
        RunEncounterState? encounter,
        RunAccessState accessState) => new(
            Version,
            Id,
            AttackedServer,
            InitiatingSide,
            InitiatingPlayer,
            InitiatingSource,
            PhaseFor(step),
            step,
            currentIceIndex,
            currentIceId,
            successful,
            unsuccessful,
            endReason,
            priority,
            encounter,
            BreachCandidates,
            RandomAccessLimit,
            ChosenAccessCards,
            AccessedCards,
            accessState,
            RunCredits,
            ActiveRunEventId,
            LingeringEffects,
            BreachAccessModifiers);

    /// <summary>
    /// Replaces the run's encounter while retaining every other field; the
    /// timing step and its phase are preserved unchanged.
    /// </summary>
    /// <summary>
    /// Replaces the run's lingering effects while retaining every other field;
    /// the timing step and its phase are preserved unchanged.
    /// </summary>
    internal RunState WithLingeringEffects(IReadOnlyList<RunLingeringEffect> lingeringEffects) => new(
        Version,
        Id,
        AttackedServer,
        InitiatingSide,
        InitiatingPlayer,
        InitiatingSource,
        PhaseFor(Step),
        Step,
        CurrentIceIndex,
        CurrentIceId,
        Successful,
        Unsuccessful,
        EndReason,
        Priority,
        Encounter,
        BreachCandidates,
        RandomAccessLimit,
        ChosenAccessCards,
        AccessedCards,
        AccessState,
        RunCredits,
        ActiveRunEventId,
        lingeringEffects,
        BreachAccessModifiers);

    /// <summary>
    /// Replaces the run's run-scoped hosted credits while retaining every
    /// other field; the timing step and its phase are preserved unchanged.
    /// </summary>
    internal RunState WithRunCredits(int runCredits) => new(
        Version,
        Id,
        AttackedServer,
        InitiatingSide,
        InitiatingPlayer,
        InitiatingSource,
        PhaseFor(Step),
        Step,
        CurrentIceIndex,
        CurrentIceId,
        Successful,
        Unsuccessful,
        EndReason,
        Priority,
        Encounter,
        BreachCandidates,
        RandomAccessLimit,
        ChosenAccessCards,
        AccessedCards,
        AccessState,
        runCredits,
        ActiveRunEventId,
        LingeringEffects,
        BreachAccessModifiers);

    /// <summary>
    /// Replaces the live run priority cursor while retaining the exact run
    /// timing/source/access state. Ability completion uses this boundary to
    /// clear consecutive passes without advancing the run step.
    /// </summary>
    internal RunState WithPriority(RunPriorityState? priority) => new(
        Version,
        Id,
        AttackedServer,
        InitiatingSide,
        InitiatingPlayer,
        InitiatingSource,
        PhaseFor(Step),
        Step,
        CurrentIceIndex,
        CurrentIceId,
        Successful,
        Unsuccessful,
        EndReason,
        priority,
        Encounter,
        BreachCandidates,
        RandomAccessLimit,
        ChosenAccessCards,
        AccessedCards,
        AccessState,
        RunCredits,
        ActiveRunEventId,
        LingeringEffects,
        BreachAccessModifiers);

    internal RunState WithEncounter(RunEncounterState encounter) => new(
        Version,
        Id,
        AttackedServer,
        InitiatingSide,
        InitiatingPlayer,
        InitiatingSource,
        PhaseFor(Step),
        Step,
        CurrentIceIndex,
        CurrentIceId,
        Successful,
        Unsuccessful,
        EndReason,
        Priority,
        encounter,
        BreachCandidates,
        RandomAccessLimit,
        ChosenAccessCards,
        AccessedCards,
        AccessState,
        RunCredits,
        ActiveRunEventId,
        LingeringEffects,
        BreachAccessModifiers);

    /// <summary>
    /// Replaces the run's breach candidate/access fields while retaining every
    /// other field; the timing step and its phase are preserved unchanged.
    /// </summary>
    internal RunState WithBreach(
        RunAccessState accessState,
        IReadOnlyList<CardInstanceId> candidates,
        int randomAccessLimit,
        IReadOnlyList<CardInstanceId> chosen,
        IReadOnlyList<CardInstanceId> accessed) => new(
        Version,
        Id,
        AttackedServer,
        InitiatingSide,
        InitiatingPlayer,
        InitiatingSource,
        PhaseFor(Step),
        Step,
        CurrentIceIndex,
        CurrentIceId,
        Successful,
        Unsuccessful,
        EndReason,
        Priority,
        Encounter,
        FreezeIds(candidates),
        randomAccessLimit,
        FreezeIds(chosen),
        FreezeIds(accessed),
        accessState,
        RunCredits,
        ActiveRunEventId,
        LingeringEffects,
        BreachAccessModifiers);

    private static ReadOnlyCollection<CardInstanceId> FreezeIds(IEnumerable<CardInstanceId> values) =>
        new(values.ToArray());

    /// <summary>
    /// Returns the currently available hidden cards for a multi-access
    /// breach.  Physical cards remain in their source zone while accessed, so
    /// a card already recorded by this run must not be offered again.
    /// </summary>
    internal static IReadOnlyList<CardInstanceId> AvailableHiddenBreachCards(
        ZoneState? hiddenZone,
        IReadOnlyList<CardInstanceId> accessedCards) =>
        hiddenZone is null
            ? []
            : hiddenZone.CardIds
                .Where(cardId => !accessedCards.Contains(cardId))
                .ToArray();

    /// <summary>
    /// The hidden pool a random breach candidate is drawn from: the Corp hand
    /// for Headquarters or the R&amp;D deck for R&amp;D, only while the random
    /// access limit remains and an unaccessed card remains in the pool; null
    /// otherwise.
    /// </summary>
    internal static BreachHiddenCandidateSource? HiddenBreachCandidate(
        ServerKind kind,
        ZoneState? hiddenZone,
        int randomAccessLimit,
        IReadOnlyList<CardInstanceId>? accessedCards = null) =>
        randomAccessLimit > 0
            && AvailableHiddenBreachCards(hiddenZone, accessedCards ?? []) is { Count: > 0 }
            && kind is ServerKind.Headquarters or ServerKind.RAndD
            ? kind == ServerKind.Headquarters
                ? BreachHiddenCandidateSource.CorpHand
                : BreachHiddenCandidateSource.CorpDeck
            : null;

    private static RunCrPhase PhaseFor(RunTimingStep step) => step switch
    {
        RunTimingStep.InitiationPaidWindow => RunCrPhase.Initiation,
        RunTimingStep.Approach or RunTimingStep.ApproachPaidWindow => RunCrPhase.Approach,
        RunTimingStep.EncounterInitialization or RunTimingStep.EncounterPaidWindow or RunTimingStep.Encounter => RunCrPhase.Encounter,
        RunTimingStep.MovementPaidWindow or RunTimingStep.JackOutDecision or RunTimingStep.MoveInward or RunTimingStep.AfterMovePaidWindow => RunCrPhase.Movement,
        RunTimingStep.Success
            or RunTimingStep.BreachBoundary
            or RunTimingStep.BreachCandidateSelection
            or RunTimingStep.BreachAccessAbility => RunCrPhase.Success,
        RunTimingStep.RunEnd => RunCrPhase.RunEnd,
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, "Unknown run timing step."),
    };
}

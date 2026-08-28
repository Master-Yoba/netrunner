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
    // 6.9.5b: a nested breach boundary; access is implemented later.
    BreachBoundary,
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
    PlaceholderBoundary,
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
        : this(1, id, iceId, RunEncounterStatus.Initialized, subroutines)
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
        IReadOnlyList<RunLingeringEffect> lingeringEffects)
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
        LingeringEffects = new ReadOnlyCollection<RunLingeringEffect>(lingeringEffects.Select(static value =>
        {
            ArgumentNullException.ThrowIfNull(value);
            return value.Copy();
        }).ToArray());
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
        CardInstanceId? initiatingSource = null)
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
            0,
            [])
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

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<RunLingeringEffect> LingeringEffects { get; private set; }

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
        LingeringEffects);

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
            LingeringEffects);

    private static ReadOnlyCollection<CardInstanceId> FreezeIds(IEnumerable<CardInstanceId> values) =>
        new(values.ToArray());

    private static RunCrPhase PhaseFor(RunTimingStep step) => step switch
    {
        RunTimingStep.InitiationPaidWindow => RunCrPhase.Initiation,
        RunTimingStep.Approach or RunTimingStep.ApproachPaidWindow => RunCrPhase.Approach,
        RunTimingStep.EncounterInitialization or RunTimingStep.EncounterPaidWindow or RunTimingStep.Encounter => RunCrPhase.Encounter,
        RunTimingStep.MovementPaidWindow or RunTimingStep.JackOutDecision or RunTimingStep.MoveInward or RunTimingStep.AfterMovePaidWindow => RunCrPhase.Movement,
        RunTimingStep.Success or RunTimingStep.BreachBoundary => RunCrPhase.Success,
        RunTimingStep.RunEnd => RunCrPhase.RunEnd,
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, "Unknown run timing step."),
    };
}

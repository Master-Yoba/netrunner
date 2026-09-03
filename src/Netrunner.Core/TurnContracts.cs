using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

/// <summary>
/// The fine-grained phase represented by a real CR 5 turn.  <see
/// cref="TurnPhase"/> remains the side-level compatibility projection used by
/// M1/M2 scenarios; this value is populated only by the real-turn shell.
/// </summary>
public enum TurnStep
{
    None,
    CorpDraw,
    CorpAction,
    CorpDiscard,
    RunnerAction,
    RunnerDiscard,
}

/// <summary>CR 9.2 capabilities available in a turn paid-ability window.</summary>
public enum TurnPriorityWindowKind
{
    Paid,
    PaidAndRez,
    PaidRezAndScore,
}

/// <summary>
/// Serializable cursor for CR 5.6/5.7.  The cursor is deliberately explicit:
/// a loaded state resumes at the exact formal step and cannot infer a skipped
/// mandatory draw, discard, or paid window from default fields.
/// </summary>
public enum TurnFrameStage
{
    GainAllottedClicks,
    BeforeTurnPriority,
    FormalTurnBegin,
    MandatoryDraw,
    ActionPriority,
    AwaitingAction,
    ActionPhaseEnd,
    DiscardSelection,
    DiscardPriority,
    LoseLeftoverClicks,
    FormalTurnEnd,
}

/// <summary>
/// Explicit facts for one side's current or immediately preceding turn.  The
/// fields are intentionally data-only so card behavior can query facts later
/// without persisting delegates or deriving meaning from event text.
/// </summary>
public sealed record TurnFactsState
{
    [JsonConstructor]
    public TurnFactsState(
        int version,
        PlayerSide side,
        int turnNumber,
        int clicksAllotted,
        int clicksLost,
        bool turnBegan,
        bool mandatoryDrawPerformed,
        bool actionPhaseEnded,
        bool discardPhaseEnded,
        IReadOnlyList<CardInstanceId>? installedCardIds = null,
        bool successfulRun = false)
    {
        Version = version;
        Side = side;
        TurnNumber = turnNumber;
        ClicksAllotted = clicksAllotted;
        ClicksLost = clicksLost;
        TurnBegan = turnBegan;
        MandatoryDrawPerformed = mandatoryDrawPerformed;
        ActionPhaseEnded = actionPhaseEnded;
        DiscardPhaseEnded = discardPhaseEnded;
        InstalledCardIds = new ReadOnlyCollection<CardInstanceId>((installedCardIds ?? []).Distinct().ToArray());
        SuccessfulRun = successfulRun;
    }

    public TurnFactsState(PlayerSide side, int turnNumber, int clicksAllotted)
        : this(1, side, turnNumber, clicksAllotted, 0, false, false, false, false)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public int Version { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide Side { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int TurnNumber { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int ClicksAllotted { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int ClicksLost { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public bool TurnBegan { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public bool MandatoryDrawPerformed { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public bool ActionPhaseEnded { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public bool DiscardPhaseEnded { get; private set; }

    /// <summary>Cards installed during this side's current turn.</summary>
    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<CardInstanceId> InstalledCardIds { get; private set; }

    /// <summary>Whether this side has completed a successful run this turn.</summary>
    [JsonInclude]
    [JsonRequired]
    public bool SuccessfulRun { get; private set; }

    internal TurnFactsState MarkTurnBegan() => New(
        turnBegan: true,
        mandatoryDrawPerformed: MandatoryDrawPerformed,
        actionPhaseEnded: ActionPhaseEnded,
        discardPhaseEnded: DiscardPhaseEnded,
        clicksLost: ClicksLost,
        installedCardIds: InstalledCardIds,
        successfulRun: SuccessfulRun);

    internal TurnFactsState MarkMandatoryDraw() => New(
        turnBegan: TurnBegan,
        mandatoryDrawPerformed: true,
        actionPhaseEnded: ActionPhaseEnded,
        discardPhaseEnded: DiscardPhaseEnded,
        clicksLost: ClicksLost,
        installedCardIds: InstalledCardIds,
        successfulRun: SuccessfulRun);

    internal TurnFactsState MarkActionPhaseEnded() => New(
        turnBegan: TurnBegan,
        mandatoryDrawPerformed: MandatoryDrawPerformed,
        actionPhaseEnded: true,
        discardPhaseEnded: DiscardPhaseEnded,
        clicksLost: ClicksLost,
        installedCardIds: InstalledCardIds,
        successfulRun: SuccessfulRun);

    internal TurnFactsState MarkDiscardPhaseEnded(int clicksLost) => New(
        turnBegan: TurnBegan,
        mandatoryDrawPerformed: MandatoryDrawPerformed,
        actionPhaseEnded: ActionPhaseEnded,
        discardPhaseEnded: true,
        clicksLost: clicksLost,
        installedCardIds: InstalledCardIds,
        successfulRun: SuccessfulRun);

    internal TurnFactsState MarkInstalled(CardInstanceId cardId) => New(
        turnBegan: TurnBegan,
        mandatoryDrawPerformed: MandatoryDrawPerformed,
        actionPhaseEnded: ActionPhaseEnded,
        discardPhaseEnded: DiscardPhaseEnded,
        clicksLost: ClicksLost,
        installedCardIds: InstalledCardIds.Append(cardId).ToArray(),
        successfulRun: SuccessfulRun);

    internal TurnFactsState MarkSuccessfulRun() => New(
        turnBegan: TurnBegan,
        mandatoryDrawPerformed: MandatoryDrawPerformed,
        actionPhaseEnded: ActionPhaseEnded,
        discardPhaseEnded: DiscardPhaseEnded,
        clicksLost: ClicksLost,
        installedCardIds: InstalledCardIds,
        successfulRun: true);

    internal TurnFactsState Copy() => New(
        turnBegan: TurnBegan,
        mandatoryDrawPerformed: MandatoryDrawPerformed,
        actionPhaseEnded: ActionPhaseEnded,
        discardPhaseEnded: DiscardPhaseEnded,
        clicksLost: ClicksLost,
        installedCardIds: InstalledCardIds,
        successfulRun: SuccessfulRun);

    private TurnFactsState New(
        bool turnBegan,
        bool mandatoryDrawPerformed,
        bool actionPhaseEnded,
        bool discardPhaseEnded,
        int clicksLost,
        IReadOnlyList<CardInstanceId>? installedCardIds = null,
        bool successfulRun = false) => new(
        Version,
        Side,
        TurnNumber,
        ClicksAllotted,
        clicksLost,
        turnBegan,
        mandatoryDrawPerformed,
        actionPhaseEnded,
        discardPhaseEnded,
        installedCardIds,
        successfulRun);
}

/// <summary>
/// Side-specific current/last-turn facts.  Keeping both sides explicit avoids
/// the ambiguous phrase "last turn" at the Corp/Runner boundary and gives
/// later card behavior a stable query surface.
/// </summary>
public sealed record TurnHistoryState
{
    [JsonConstructor]
    public TurnHistoryState(
        int version,
        TurnFactsState? corpThisTurn,
        TurnFactsState? corpLastTurn,
        TurnFactsState? runnerThisTurn,
        TurnFactsState? runnerLastTurn)
    {
        Version = version;
        CorpThisTurn = corpThisTurn?.Copy();
        CorpLastTurn = corpLastTurn?.Copy();
        RunnerThisTurn = runnerThisTurn?.Copy();
        RunnerLastTurn = runnerLastTurn?.Copy();
    }

    public TurnHistoryState()
        : this(1, null, null, null, null)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public int Version { get; private set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TurnFactsState? CorpThisTurn { get; private set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TurnFactsState? CorpLastTurn { get; private set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TurnFactsState? RunnerThisTurn { get; private set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TurnFactsState? RunnerLastTurn { get; private set; }

    public TurnFactsState? ThisTurn(PlayerSide side) => side == PlayerSide.Corp ? CorpThisTurn : RunnerThisTurn;

    public TurnFactsState? LastTurn(PlayerSide side) => side == PlayerSide.Corp ? CorpLastTurn : RunnerLastTurn;

    internal TurnHistoryState Start(PlayerSide side, int turnNumber, int clicksAllotted)
    {
        TurnFactsState next = new(side, turnNumber, clicksAllotted);
        return side == PlayerSide.Corp
            ? new(Version, next, CorpThisTurn, RunnerThisTurn, RunnerLastTurn)
            : new(Version, CorpThisTurn, CorpLastTurn, next, RunnerThisTurn);
    }

    internal TurnHistoryState MarkTurnBegan(PlayerSide side)
    {
        TurnFactsState current = RequireCurrent(side);
        return ReplaceCurrent(side, current.MarkTurnBegan());
    }

    internal TurnHistoryState MarkMandatoryDraw(PlayerSide side)
    {
        TurnFactsState current = RequireCurrent(side);
        return ReplaceCurrent(side, current.MarkMandatoryDraw());
    }

    internal TurnHistoryState MarkActionPhaseEnded(PlayerSide side)
    {
        TurnFactsState current = RequireCurrent(side);
        return ReplaceCurrent(side, current.MarkActionPhaseEnded());
    }

    internal TurnHistoryState MarkDiscardPhaseEnded(PlayerSide side, int clicksLost)
    {
        TurnFactsState current = RequireCurrent(side);
        return ReplaceCurrent(side, current.MarkDiscardPhaseEnded(clicksLost));
    }

    internal TurnHistoryState MarkInstalled(PlayerSide side, CardInstanceId cardId)
    {
        TurnFactsState current = RequireCurrent(side);
        return ReplaceCurrent(side, current.MarkInstalled(cardId));
    }

    internal TurnHistoryState MarkSuccessfulRun(PlayerSide side)
    {
        TurnFactsState current = RequireCurrent(side);
        return ReplaceCurrent(side, current.MarkSuccessfulRun());
    }

    internal TurnHistoryState Copy() => new(Version, CorpThisTurn, CorpLastTurn, RunnerThisTurn, RunnerLastTurn);

    private TurnFactsState RequireCurrent(PlayerSide side) => ThisTurn(side)
        ?? throw new InvalidOperationException($"No current '{side}' turn facts are available.");

    private TurnHistoryState ReplaceCurrent(PlayerSide side, TurnFactsState facts) => side == PlayerSide.Corp
        ? new(Version, facts, CorpLastTurn, RunnerThisTurn, RunnerLastTurn)
        : new(Version, CorpThisTurn, CorpLastTurn, facts, RunnerLastTurn);
}

/// <summary>
/// CR 5 turn cursor.  Paid-window capabilities and pass state are persisted
/// with the cursor so save/load cannot silently grant rez/score permissions.
/// </summary>
public sealed record TurnFrame : ResolutionFrame
{
    [JsonConstructor]
    public TurnFrame(
        int version,
        string behaviorId,
        PlayerSide activeSide,
        int turnNumber,
        TurnFrameStage stage,
        TurnPriorityWindowKind? priorityWindow = null,
        PlayerSide? priorityHolder = null,
        int consecutivePasses = 0,
        bool forcedActionEnd = false)
        : base(version, behaviorId)
    {
        ActiveSide = activeSide;
        TurnNumber = turnNumber;
        Stage = stage;
        PriorityWindow = priorityWindow;
        PriorityHolder = priorityHolder;
        ConsecutivePasses = consecutivePasses;
        ForcedActionEnd = forcedActionEnd;
    }

    public TurnFrame(
        PlayerSide activeSide,
        int turnNumber = 1,
        TurnFrameStage stage = TurnFrameStage.GainAllottedClicks)
        : this(
            1,
            ResolutionBehaviorIds.Turn,
            activeSide,
            turnNumber,
            stage)
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
    public TurnFrameStage Stage { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public TurnPriorityWindowKind? PriorityWindow { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide? PriorityHolder { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int ConsecutivePasses { get; private set; }

    /// <summary>Set by an effect that directly ends the active action phase.</summary>
    [JsonInclude]
    [JsonRequired]
    public bool ForcedActionEnd { get; private set; }

    [JsonIgnore]
    public bool AllowsPaidAbilities => PriorityWindow is TurnPriorityWindowKind.Paid
        or TurnPriorityWindowKind.PaidAndRez
        or TurnPriorityWindowKind.PaidRezAndScore;

    [JsonIgnore]
    public bool AllowsRez => PriorityWindow is TurnPriorityWindowKind.PaidAndRez
        or TurnPriorityWindowKind.PaidRezAndScore;

    [JsonIgnore]
    public bool AllowsScore => PriorityWindow == TurnPriorityWindowKind.PaidRezAndScore;

    [JsonIgnore]
    public TurnStep Step => ActiveSide switch
    {
        PlayerSide.Corp when Stage is TurnFrameStage.GainAllottedClicks
            or TurnFrameStage.BeforeTurnPriority
            or TurnFrameStage.FormalTurnBegin
            or TurnFrameStage.MandatoryDraw => TurnStep.CorpDraw,
        PlayerSide.Corp when Stage is TurnFrameStage.ActionPriority
            or TurnFrameStage.AwaitingAction
            or TurnFrameStage.ActionPhaseEnd => TurnStep.CorpAction,
        PlayerSide.Corp => TurnStep.CorpDiscard,
        PlayerSide.Runner when Stage is TurnFrameStage.GainAllottedClicks
            or TurnFrameStage.BeforeTurnPriority
            or TurnFrameStage.FormalTurnBegin => TurnStep.RunnerAction,
        PlayerSide.Runner when Stage is TurnFrameStage.ActionPriority
            or TurnFrameStage.AwaitingAction
            or TurnFrameStage.ActionPhaseEnd => TurnStep.RunnerAction,
        _ => TurnStep.RunnerDiscard,
    };
}

/// <summary>
/// Owner-private, multi-card discard decision.  The candidate and selected
/// IDs are frozen in the frame; a restored prompt therefore cannot acquire a
/// different hand after another reducer has run.
/// </summary>
public sealed record TurnDiscardFrame : ResolutionFrame
{
    [JsonConstructor]
    public TurnDiscardFrame(
        int version,
        string behaviorId,
        PlayerId owner,
        int requiredCount,
        IReadOnlyList<CardInstanceId> candidates,
        IReadOnlyList<CardInstanceId>? selected = null)
        : base(version, behaviorId)
    {
        Owner = owner;
        RequiredCount = requiredCount;
        ArgumentNullException.ThrowIfNull(candidates);
        Candidates = Freeze(candidates);
        Selected = Freeze(selected);
    }

    public TurnDiscardFrame(PlayerId owner, int requiredCount, IReadOnlyList<CardInstanceId> candidates)
        : this(1, ResolutionBehaviorIds.TurnDiscard, owner, requiredCount, candidates)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Owner { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int RequiredCount { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<CardInstanceId> Candidates { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<CardInstanceId> Selected { get; private set; }

    internal TurnDiscardFrame Select(CardInstanceId cardId) => new(
        Version,
        BehaviorId,
        Owner,
        RequiredCount,
        Candidates,
        Selected.Append(cardId).ToArray());

    private static ReadOnlyCollection<CardInstanceId> Freeze(IEnumerable<CardInstanceId>? values) =>
        new((values ?? []).ToArray());
}

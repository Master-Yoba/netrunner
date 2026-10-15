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
/// Canonical once-per-turn identity for a behavior-owned ability.  The source
/// card instance is part of the key: two physical copies of the same printed
/// definition do not share a usage limit.
/// </summary>
public sealed record AbilityUseKey
{
    [JsonConstructor]
    public AbilityUseKey(CardInstanceId sourceCardId, string abilityKey)
    {
        if (!StableIdentifier.TryValidate(sourceCardId.Value))
        {
            throw new ArgumentException("An ability-use key requires a stable source card ID.", nameof(sourceCardId));
        }

        if (!StableIdentifier.TryValidate(abilityKey))
        {
            throw new ArgumentException("An ability-use key requires a stable ability key.", nameof(abilityKey));
        }

        SourceCardId = sourceCardId;
        AbilityKey = abilityKey;
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId SourceCardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string AbilityKey { get; private set; }
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
        bool successfulRun = false,
        IReadOnlyList<AbilityUseKey>? usedAbilityKeys = null,
        IReadOnlyList<ServerId>? attemptedRunServerIds = null,
        IReadOnlyList<ServerId>? successfulRunServerIds = null,
        IReadOnlyList<CardInstanceId>? accessedCardOccurrenceIds = null,
        IReadOnlyList<CardInstanceId>? trashedCardOccurrenceIds = null,
        IReadOnlyList<AgendaTurnChange>? agendaTurnChanges = null,
        IReadOnlyList<TagTurnChange>? tagTurnChanges = null,
        IReadOnlyList<FirstXUseKey>? firstXUseKeys = null,
        IReadOnlyList<CardInstanceId>? programInstallOccurrenceIds = null,
        IReadOnlyList<ServerId>? hqBreachOccurrenceServerIds = null,
        IReadOnlyList<OccurrenceId>? basicDrawOccurrenceIds = null,
        IReadOnlyList<TurnEventCount>? eventCounts = null,
        IReadOnlyList<DeferredOccurrence>? deferredOccurrences = null,
        int nextTurnExtraClicks = 0,
        IReadOnlyList<CardInstanceId>? handSizeDiscardIds = null,
        IReadOnlyList<CardInstanceId>? freeInstallIds = null)
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
        UsedAbilityKeys = new ReadOnlyCollection<AbilityUseKey>((usedAbilityKeys ?? [])
            .Select(value =>
            {
                ArgumentNullException.ThrowIfNull(value);
                return new AbilityUseKey(value.SourceCardId, value.AbilityKey);
            })
            .ToArray());
        AttemptedRunServerIds = new ReadOnlyCollection<ServerId>((attemptedRunServerIds ?? []).ToArray());
        SuccessfulRunServerIds = new ReadOnlyCollection<ServerId>((successfulRunServerIds ?? []).ToArray());
        AccessedCardOccurrenceIds = new ReadOnlyCollection<CardInstanceId>((accessedCardOccurrenceIds ?? []).ToArray());
        TrashedCardOccurrenceIds = new ReadOnlyCollection<CardInstanceId>((trashedCardOccurrenceIds ?? []).ToArray());
        AgendaTurnChanges = new ReadOnlyCollection<AgendaTurnChange>((agendaTurnChanges ?? [])
            .Select(value =>
            {
                ArgumentNullException.ThrowIfNull(value);
                return new AgendaTurnChange(value.CardId, value.BeneficiarySide, value.Points, value.Kind);
            })
            .ToArray());
        TagTurnChanges = new ReadOnlyCollection<TagTurnChange>((tagTurnChanges ?? [])
            .Select(value =>
            {
                ArgumentNullException.ThrowIfNull(value);
                return new TagTurnChange(value.AffectedPlayerId, value.Delta);
            })
            .ToArray());
        FirstXUseKeys = new ReadOnlyCollection<FirstXUseKey>((firstXUseKeys ?? [])
            .Select(value =>
            {
                ArgumentNullException.ThrowIfNull(value);
                return new FirstXUseKey(value.SourceCardId, value.Key, value.Count);
            })
            .ToArray());
        ProgramInstallOccurrenceIds = new ReadOnlyCollection<CardInstanceId>(
            (programInstallOccurrenceIds ?? []).ToArray());
        HqBreachOccurrenceServerIds = new ReadOnlyCollection<ServerId>(
            (hqBreachOccurrenceServerIds ?? []).ToArray());
        BasicDrawOccurrenceIds = new ReadOnlyCollection<OccurrenceId>(
            (basicDrawOccurrenceIds ?? []).ToArray());
        TurnEventCount[] counts = (eventCounts ?? [])
            .Select(static value =>
            {
                ArgumentNullException.ThrowIfNull(value);
                return new TurnEventCount(value.Kind, value.Count);
            })
            .OrderBy(static value => value.Kind)
            .ToArray();
        if (counts.Select(static value => value.Kind).Distinct().Count() != counts.Length)
        {
            throw new ArgumentException("Turn event counts must not repeat a kind.", nameof(eventCounts));
        }

        EventCounts = counts.Length == 0 ? null : new ReadOnlyCollection<TurnEventCount>(counts);
        DeferredOccurrence[] deferred = (deferredOccurrences ?? [])
            .Select(static value =>
            {
                ArgumentNullException.ThrowIfNull(value);
                return new DeferredOccurrence(value.Kind, value.Controller, value.SubjectCardId);
            })
            .ToArray();
        DeferredOccurrences = deferred.Length == 0 ? null : new ReadOnlyCollection<DeferredOccurrence>(deferred);
        ArgumentOutOfRangeException.ThrowIfNegative(nextTurnExtraClicks);
        NextTurnExtraClicks = nextTurnExtraClicks;
        HandSizeDiscardIds = handSizeDiscardIds is null or { Count: 0 }
            ? null
            : new ReadOnlyCollection<CardInstanceId>(handSizeDiscardIds.Distinct().ToArray());
        FreeInstallIds = freeInstallIds is null or { Count: 0 }
            ? null
            : new ReadOnlyCollection<CardInstanceId>(freeInstallIds.Distinct().ToArray());
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

    /// <summary>
    /// Runner program installs completed during this formal turn, in event
    /// order. Unlike <see cref="InstalledCardIds"/>, this is an occurrence
    /// ledger and intentionally preserves repeated physical IDs.
    /// </summary>
    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<CardInstanceId> ProgramInstallOccurrenceIds { get; private set; }

    /// <summary>
    /// Actual HQ breaches recorded at the authoritative breach boundary for
    /// this formal turn. This is an occurrence ledger: repeated HQ breaches
    /// are intentionally retained in event order rather than deduplicated.
    /// </summary>
    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<ServerId> HqBreachOccurrenceServerIds { get; private set; }

    /// <summary>
    /// Every accepted real click-for-card draw action in this formal turn.
    /// The list is global to the turn facts, so installing a source after the
    /// first draw cannot re-arm its declaration.
    /// </summary>
    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<OccurrenceId> BasicDrawOccurrenceIds { get; private set; }

    /// <summary>Whether this side has completed a successful run this turn.</summary>
    [JsonInclude]
    [JsonRequired]
    public bool SuccessfulRun { get; private set; }

    /// <summary>
    /// Physical source/ability pairs used during this side's current turn.
    /// The list is serialized even when empty so schema-3 states cannot hide
    /// usage semantics behind an omitted compatibility field.
    /// </summary>
    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<AbilityUseKey> UsedAbilityKeys { get; private set; }

    /// <summary>Servers on which this side attempted a run, in first-seen order.</summary>
    [JsonInclude]
    public IReadOnlyList<ServerId> AttemptedRunServerIds { get; private set; }

    /// <summary>Servers on which this side completed a successful run, in first-seen order.</summary>
    [JsonInclude]
    public IReadOnlyList<ServerId> SuccessfulRunServerIds { get; private set; }

    /// <summary>Cards accessed during this turn; repeated accesses remain ordered occurrences.</summary>
    [JsonInclude]
    public IReadOnlyList<CardInstanceId> AccessedCardOccurrenceIds { get; private set; }

    /// <summary>Cards trashed during this turn; repeated trashes remain ordered occurrences.</summary>
    [JsonInclude]
    public IReadOnlyList<CardInstanceId> TrashedCardOccurrenceIds { get; private set; }

    /// <summary>Typed agenda score/steal changes observed during this turn.</summary>
    [JsonInclude]
    public IReadOnlyList<AgendaTurnChange> AgendaTurnChanges { get; private set; }

    /// <summary>Typed tag deltas observed during this turn.</summary>
    [JsonInclude]
    public IReadOnlyList<TagTurnChange> TagTurnChanges { get; private set; }

    /// <summary>Counted first-X usage keys observed during this turn.</summary>
    [JsonInclude]
    public IReadOnlyList<FirstXUseKey> FirstXUseKeys { get; private set; }

    /// <summary>
    /// Typed counts of turn events (actions started, operations and events
    /// played, ...). Omitted while empty, so states without these facts keep
    /// their historical bytes.
    /// </summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<TurnEventCount>? EventCounts { get; private set; }

    /// <summary>
    /// Occurrences raised mid-action (a tag removed, credits gained through a
    /// card ability, an agenda forfeited) that open their trigger checkpoint
    /// once the action completes, in order. Omitted while empty.
    /// </summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DeferredOccurrence>? DeferredOccurrences { get; private set; }

    /// <summary>EL-HAND-SIZE-DISCARD: the cards discarded this turn to reach maximum hand size.</summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CardInstanceId>? HandSizeDiscardIds { get; private set; }

    /// <summary>EL-HOSTED-FACEUP: the cards this side installed this turn without spending credits (Bling).</summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CardInstanceId>? FreeInstallIds { get; private set; }

    internal TurnFactsState WithFreeInstall(CardInstanceId cardId) => New(
        turnBegan: TurnBegan,
        mandatoryDrawPerformed: MandatoryDrawPerformed,
        actionPhaseEnded: ActionPhaseEnded,
        discardPhaseEnded: DiscardPhaseEnded,
        clicksLost: ClicksLost,
        installedCardIds: InstalledCardIds,
        successfulRun: SuccessfulRun,
        usedAbilityKeys: UsedAbilityKeys,
        attemptedRunServerIds: AttemptedRunServerIds,
        successfulRunServerIds: SuccessfulRunServerIds,
        accessedCardOccurrenceIds: AccessedCardOccurrenceIds,
        trashedCardOccurrenceIds: TrashedCardOccurrenceIds,
        agendaTurnChanges: AgendaTurnChanges,
        tagTurnChanges: TagTurnChanges,
        firstXUseKeys: FirstXUseKeys,
        freeInstallIds: (FreeInstallIds ?? []).Append(cardId).ToArray());

    internal TurnFactsState WithHandSizeDiscards(IReadOnlyList<CardInstanceId> discarded) => New(
        turnBegan: TurnBegan,
        mandatoryDrawPerformed: MandatoryDrawPerformed,
        actionPhaseEnded: ActionPhaseEnded,
        discardPhaseEnded: DiscardPhaseEnded,
        clicksLost: ClicksLost,
        installedCardIds: InstalledCardIds,
        successfulRun: SuccessfulRun,
        usedAbilityKeys: UsedAbilityKeys,
        attemptedRunServerIds: AttemptedRunServerIds,
        successfulRunServerIds: SuccessfulRunServerIds,
        accessedCardOccurrenceIds: AccessedCardOccurrenceIds,
        trashedCardOccurrenceIds: TrashedCardOccurrenceIds,
        agendaTurnChanges: AgendaTurnChanges,
        tagTurnChanges: TagTurnChanges,
        firstXUseKeys: FirstXUseKeys,
        handSizeDiscardIds: discarded);

    /// <summary>Extra clicks this side is allotted at the start of its next turn ("+1 allotted click").</summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int NextTurnExtraClicks { get; private set; }

    internal TurnFactsState WithDeferredOccurrences(IReadOnlyList<DeferredOccurrence> deferred) => New(
        turnBegan: TurnBegan,
        mandatoryDrawPerformed: MandatoryDrawPerformed,
        actionPhaseEnded: ActionPhaseEnded,
        discardPhaseEnded: DiscardPhaseEnded,
        clicksLost: ClicksLost,
        installedCardIds: InstalledCardIds,
        successfulRun: SuccessfulRun,
        usedAbilityKeys: UsedAbilityKeys,
        attemptedRunServerIds: AttemptedRunServerIds,
        successfulRunServerIds: SuccessfulRunServerIds,
        accessedCardOccurrenceIds: AccessedCardOccurrenceIds,
        trashedCardOccurrenceIds: TrashedCardOccurrenceIds,
        agendaTurnChanges: AgendaTurnChanges,
        tagTurnChanges: TagTurnChanges,
        firstXUseKeys: FirstXUseKeys,
        deferredOccurrences: deferred);

    internal TurnFactsState WithNextTurnExtraClicks(int extra) => New(
        turnBegan: TurnBegan,
        mandatoryDrawPerformed: MandatoryDrawPerformed,
        actionPhaseEnded: ActionPhaseEnded,
        discardPhaseEnded: DiscardPhaseEnded,
        clicksLost: ClicksLost,
        installedCardIds: InstalledCardIds,
        successfulRun: SuccessfulRun,
        usedAbilityKeys: UsedAbilityKeys,
        attemptedRunServerIds: AttemptedRunServerIds,
        successfulRunServerIds: SuccessfulRunServerIds,
        accessedCardOccurrenceIds: AccessedCardOccurrenceIds,
        trashedCardOccurrenceIds: TrashedCardOccurrenceIds,
        agendaTurnChanges: AgendaTurnChanges,
        tagTurnChanges: TagTurnChanges,
        firstXUseKeys: FirstXUseKeys,
        nextTurnExtraClicks: extra);

    /// <summary>How many times an event happened this turn.</summary>
    public int EventCount(TurnEventKind kind) =>
        EventCounts?.FirstOrDefault(value => value.Kind == kind)?.Count ?? 0;

    internal TurnFactsState RecordEvent(TurnEventKind kind)
    {
        TurnEventCount[] counts = (EventCounts ?? [])
            .Where(value => value.Kind != kind)
            .Append(new TurnEventCount(kind, checked(EventCount(kind) + 1)))
            .ToArray();
        return New(
            turnBegan: TurnBegan,
            mandatoryDrawPerformed: MandatoryDrawPerformed,
            actionPhaseEnded: ActionPhaseEnded,
            discardPhaseEnded: DiscardPhaseEnded,
            clicksLost: ClicksLost,
            installedCardIds: InstalledCardIds,
            successfulRun: SuccessfulRun,
            usedAbilityKeys: UsedAbilityKeys,
            attemptedRunServerIds: AttemptedRunServerIds,
            successfulRunServerIds: SuccessfulRunServerIds,
            accessedCardOccurrenceIds: AccessedCardOccurrenceIds,
            trashedCardOccurrenceIds: TrashedCardOccurrenceIds,
            agendaTurnChanges: AgendaTurnChanges,
            tagTurnChanges: TagTurnChanges,
            firstXUseKeys: FirstXUseKeys,
            eventCounts: counts);
    }

    internal TurnFactsState MarkTurnBegan() => New(
        turnBegan: true,
        mandatoryDrawPerformed: MandatoryDrawPerformed,
        actionPhaseEnded: ActionPhaseEnded,
        discardPhaseEnded: DiscardPhaseEnded,
        clicksLost: ClicksLost,
        installedCardIds: InstalledCardIds,
        successfulRun: SuccessfulRun,
        usedAbilityKeys: UsedAbilityKeys,
        attemptedRunServerIds: AttemptedRunServerIds,
        successfulRunServerIds: SuccessfulRunServerIds,
        accessedCardOccurrenceIds: AccessedCardOccurrenceIds,
        trashedCardOccurrenceIds: TrashedCardOccurrenceIds,
        agendaTurnChanges: AgendaTurnChanges,
        tagTurnChanges: TagTurnChanges,
        firstXUseKeys: FirstXUseKeys);

    internal TurnFactsState MarkMandatoryDraw() => New(
        turnBegan: TurnBegan,
        mandatoryDrawPerformed: true,
        actionPhaseEnded: ActionPhaseEnded,
        discardPhaseEnded: DiscardPhaseEnded,
        clicksLost: ClicksLost,
        installedCardIds: InstalledCardIds,
        successfulRun: SuccessfulRun,
        usedAbilityKeys: UsedAbilityKeys,
        attemptedRunServerIds: AttemptedRunServerIds,
        successfulRunServerIds: SuccessfulRunServerIds,
        accessedCardOccurrenceIds: AccessedCardOccurrenceIds,
        trashedCardOccurrenceIds: TrashedCardOccurrenceIds,
        agendaTurnChanges: AgendaTurnChanges,
        tagTurnChanges: TagTurnChanges,
        firstXUseKeys: FirstXUseKeys);

    internal TurnFactsState MarkActionPhaseEnded() => New(
        turnBegan: TurnBegan,
        mandatoryDrawPerformed: MandatoryDrawPerformed,
        actionPhaseEnded: true,
        discardPhaseEnded: DiscardPhaseEnded,
        clicksLost: ClicksLost,
        installedCardIds: InstalledCardIds,
        successfulRun: SuccessfulRun,
        usedAbilityKeys: UsedAbilityKeys,
        attemptedRunServerIds: AttemptedRunServerIds,
        successfulRunServerIds: SuccessfulRunServerIds,
        accessedCardOccurrenceIds: AccessedCardOccurrenceIds,
        trashedCardOccurrenceIds: TrashedCardOccurrenceIds,
        agendaTurnChanges: AgendaTurnChanges,
        tagTurnChanges: TagTurnChanges,
        firstXUseKeys: FirstXUseKeys);

    internal TurnFactsState MarkDiscardPhaseEnded(int clicksLost) => New(
        turnBegan: TurnBegan,
        mandatoryDrawPerformed: MandatoryDrawPerformed,
        actionPhaseEnded: ActionPhaseEnded,
        discardPhaseEnded: true,
        clicksLost: clicksLost,
        installedCardIds: InstalledCardIds,
        successfulRun: SuccessfulRun,
        usedAbilityKeys: UsedAbilityKeys,
        attemptedRunServerIds: AttemptedRunServerIds,
        successfulRunServerIds: SuccessfulRunServerIds,
        accessedCardOccurrenceIds: AccessedCardOccurrenceIds,
        trashedCardOccurrenceIds: TrashedCardOccurrenceIds,
        agendaTurnChanges: AgendaTurnChanges,
        tagTurnChanges: TagTurnChanges,
        firstXUseKeys: FirstXUseKeys);

    internal TurnFactsState MarkInstalled(CardInstanceId cardId) => New(
        turnBegan: TurnBegan,
        mandatoryDrawPerformed: MandatoryDrawPerformed,
        actionPhaseEnded: ActionPhaseEnded,
        discardPhaseEnded: DiscardPhaseEnded,
        clicksLost: ClicksLost,
        installedCardIds: InstalledCardIds.Append(cardId).ToArray(),
        successfulRun: SuccessfulRun,
        usedAbilityKeys: UsedAbilityKeys,
        attemptedRunServerIds: AttemptedRunServerIds,
        successfulRunServerIds: SuccessfulRunServerIds,
        accessedCardOccurrenceIds: AccessedCardOccurrenceIds,
        trashedCardOccurrenceIds: TrashedCardOccurrenceIds,
        agendaTurnChanges: AgendaTurnChanges,
        tagTurnChanges: TagTurnChanges,
        firstXUseKeys: FirstXUseKeys);

    /// <summary>
    /// Records one successfully completed Runner program install. The caller
    /// chooses the active formal-turn facts; this method does not infer an
    /// ability source or deduplicate repeated occurrences.
    /// </summary>
    internal TurnFactsState RecordProgramInstall(CardInstanceId cardId) =>
        WithFactCollections(
            programInstallOccurrenceIds: ProgramInstallOccurrenceIds.Append(cardId).ToArray());

    /// <summary>
    /// Records one actual HQ breach at BeginBreach. Repeated server IDs are
    /// preserved so callers can determine whether the current occurrence was
    /// the first HQ breach before appending it.
    /// </summary>
    internal TurnFactsState RecordHqBreach(ServerId serverId) =>
        WithFactCollections(
            hqBreachOccurrenceServerIds: HqBreachOccurrenceServerIds.Append(serverId).ToArray());

    /// <summary>Records one accepted real basic-draw occurrence.</summary>
    internal TurnFactsState RecordBasicDraw(OccurrenceId occurrenceId) =>
        WithFactCollections(
            basicDrawOccurrenceIds: BasicDrawOccurrenceIds.Append(occurrenceId).ToArray());

    /// <summary>Records a run attempt once, preserving first-seen server order.</summary>
    internal TurnFactsState MarkRunAttempted(ServerId serverId)
    {
        if (AttemptedRunServerIds.Contains(serverId))
        {
            return this;
        }

        return WithFactCollections(
            attemptedRunServerIds: AttemptedRunServerIds.Append(serverId).ToArray());
    }

    /// <summary>
    /// Records a successful run. A successful server is also necessarily an
    /// attempted server, and both lists retain first-seen order.
    /// </summary>
    internal TurnFactsState MarkSuccessfulRun(ServerId serverId)
    {
        IReadOnlyList<ServerId> attempted = AttemptedRunServerIds.Contains(serverId)
            ? AttemptedRunServerIds
            : AttemptedRunServerIds.Append(serverId).ToArray();
        IReadOnlyList<ServerId> successful = SuccessfulRunServerIds.Contains(serverId)
            ? SuccessfulRunServerIds
            : SuccessfulRunServerIds.Append(serverId).ToArray();

        return WithFactCollections(
            successfulRun: true,
            attemptedRunServerIds: attempted,
            successfulRunServerIds: successful);
    }

    /// <summary>Records a card-access occurrence; repeated IDs remain ordered.</summary>
    internal TurnFactsState RecordAccess(CardInstanceId cardId) => WithFactCollections(
        accessedCardOccurrenceIds: AccessedCardOccurrenceIds.Append(cardId).ToArray());

    /// <summary>Records a card-trash occurrence; repeated IDs remain ordered.</summary>
    internal TurnFactsState RecordTrash(CardInstanceId cardId) => WithFactCollections(
        trashedCardOccurrenceIds: TrashedCardOccurrenceIds.Append(cardId).ToArray());

    internal TurnFactsState RecordAgendaChange(AgendaTurnChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return WithFactCollections(
            agendaTurnChanges: AgendaTurnChanges
                .Append(new AgendaTurnChange(change.CardId, change.BeneficiarySide, change.Points, change.Kind))
                .ToArray());
    }

    internal TurnFactsState RecordAgendaChange(
        CardInstanceId cardId,
        PlayerSide beneficiarySide,
        int points,
        AgendaTurnChangeKind kind) => RecordAgendaChange(
            new AgendaTurnChange(cardId, beneficiarySide, points, kind));

    internal TurnFactsState RecordTagChange(TagTurnChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return WithFactCollections(
            tagTurnChanges: TagTurnChanges
                .Append(new TagTurnChange(change.AffectedPlayerId, change.Delta))
                .ToArray());
    }

    internal TurnFactsState RecordTagChange(PlayerId affectedPlayerId, int delta) => RecordTagChange(
        new TagTurnChange(affectedPlayerId, delta));

    /// <summary>
    /// Adds a first-X use count for a source/key pair. Counts are checked for
    /// overflow and an existing pair is updated in place.
    /// </summary>
    internal TurnFactsState ConsumeFirstX(
        CardInstanceId sourceCardId,
        string key,
        int amount = 1)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "A first-X consumption amount must be positive.");
        }

        int existingIndex = -1;
        for (int index = 0; index < FirstXUseKeys.Count; index++)
        {
            FirstXUseKey candidate = FirstXUseKeys[index];
            if (candidate.SourceCardId == sourceCardId
                && string.Equals(candidate.Key, key, StringComparison.Ordinal))
            {
                existingIndex = index;
                break;
            }
        }
        if (existingIndex < 0)
        {
            return WithFactCollections(
                firstXUseKeys: FirstXUseKeys
                    .Append(new FirstXUseKey(sourceCardId, key, amount))
                    .ToArray());
        }

        FirstXUseKey existing = FirstXUseKeys[existingIndex];
        int nextCount = checked(existing.Count + amount);
        FirstXUseKey[] updated = FirstXUseKeys.ToArray();
        updated[existingIndex] = new FirstXUseKey(sourceCardId, key, nextCount);
        return WithFactCollections(firstXUseKeys: updated);
    }

    /// <summary>Returns the accumulated first-X count for a source/key pair.</summary>
    internal int GetFirstXCount(CardInstanceId sourceCardId, string key) => FirstXUseKeys
        .Where(value => value.SourceCardId == sourceCardId
            && string.Equals(value.Key, key, StringComparison.Ordinal))
        .Select(value => value.Count)
        .FirstOrDefault();

    internal TurnFactsState MarkSuccessfulRun() => New(
        turnBegan: TurnBegan,
        mandatoryDrawPerformed: MandatoryDrawPerformed,
        actionPhaseEnded: ActionPhaseEnded,
        discardPhaseEnded: DiscardPhaseEnded,
        clicksLost: ClicksLost,
        installedCardIds: InstalledCardIds,
        successfulRun: true,
        usedAbilityKeys: UsedAbilityKeys,
        attemptedRunServerIds: AttemptedRunServerIds,
        successfulRunServerIds: SuccessfulRunServerIds,
        accessedCardOccurrenceIds: AccessedCardOccurrenceIds,
        trashedCardOccurrenceIds: TrashedCardOccurrenceIds,
        agendaTurnChanges: AgendaTurnChanges,
        tagTurnChanges: TagTurnChanges,
        firstXUseKeys: FirstXUseKeys);

    private TurnFactsState WithFactCollections(
        bool? successfulRun = null,
        IReadOnlyList<ServerId>? attemptedRunServerIds = null,
        IReadOnlyList<ServerId>? successfulRunServerIds = null,
        IReadOnlyList<CardInstanceId>? accessedCardOccurrenceIds = null,
        IReadOnlyList<CardInstanceId>? trashedCardOccurrenceIds = null,
        IReadOnlyList<AgendaTurnChange>? agendaTurnChanges = null,
        IReadOnlyList<TagTurnChange>? tagTurnChanges = null,
        IReadOnlyList<FirstXUseKey>? firstXUseKeys = null,
        IReadOnlyList<CardInstanceId>? programInstallOccurrenceIds = null,
        IReadOnlyList<ServerId>? hqBreachOccurrenceServerIds = null,
        IReadOnlyList<OccurrenceId>? basicDrawOccurrenceIds = null) => New(
        turnBegan: TurnBegan,
        mandatoryDrawPerformed: MandatoryDrawPerformed,
        actionPhaseEnded: ActionPhaseEnded,
        discardPhaseEnded: DiscardPhaseEnded,
        clicksLost: ClicksLost,
        installedCardIds: InstalledCardIds,
        successfulRun: successfulRun ?? SuccessfulRun,
        usedAbilityKeys: UsedAbilityKeys,
        attemptedRunServerIds: attemptedRunServerIds ?? AttemptedRunServerIds,
        successfulRunServerIds: successfulRunServerIds ?? SuccessfulRunServerIds,
        accessedCardOccurrenceIds: accessedCardOccurrenceIds ?? AccessedCardOccurrenceIds,
        trashedCardOccurrenceIds: trashedCardOccurrenceIds ?? TrashedCardOccurrenceIds,
        agendaTurnChanges: agendaTurnChanges ?? AgendaTurnChanges,
        tagTurnChanges: tagTurnChanges ?? TagTurnChanges,
        firstXUseKeys: firstXUseKeys ?? FirstXUseKeys,
        programInstallOccurrenceIds: programInstallOccurrenceIds ?? ProgramInstallOccurrenceIds,
        hqBreachOccurrenceServerIds: hqBreachOccurrenceServerIds ?? HqBreachOccurrenceServerIds,
        basicDrawOccurrenceIds: basicDrawOccurrenceIds ?? BasicDrawOccurrenceIds);

    internal bool HasUsedAbility(CardInstanceId sourceCardId, string abilityKey) =>
        UsedAbilityKeys.Any(value => value.SourceCardId == sourceCardId
            && string.Equals(value.AbilityKey, abilityKey, StringComparison.Ordinal));

    internal TurnFactsState MarkAbilityUsed(CardInstanceId sourceCardId, string abilityKey)
    {
        if (HasUsedAbility(sourceCardId, abilityKey))
        {
            return this;
        }

        return New(
            turnBegan: TurnBegan,
            mandatoryDrawPerformed: MandatoryDrawPerformed,
            actionPhaseEnded: ActionPhaseEnded,
            discardPhaseEnded: DiscardPhaseEnded,
            clicksLost: ClicksLost,
            installedCardIds: InstalledCardIds,
            successfulRun: SuccessfulRun,
            usedAbilityKeys: UsedAbilityKeys.Append(new AbilityUseKey(sourceCardId, abilityKey)).ToArray(),
            attemptedRunServerIds: AttemptedRunServerIds,
            successfulRunServerIds: SuccessfulRunServerIds,
            accessedCardOccurrenceIds: AccessedCardOccurrenceIds,
            trashedCardOccurrenceIds: TrashedCardOccurrenceIds,
            agendaTurnChanges: AgendaTurnChanges,
            tagTurnChanges: TagTurnChanges,
            firstXUseKeys: FirstXUseKeys);
    }

    private TurnFactsState New(
        bool turnBegan,
        bool mandatoryDrawPerformed,
        bool actionPhaseEnded,
        bool discardPhaseEnded,
        int clicksLost,
        IReadOnlyList<CardInstanceId>? installedCardIds = null,
        bool successfulRun = false,
        IReadOnlyList<AbilityUseKey>? usedAbilityKeys = null,
        IReadOnlyList<ServerId>? attemptedRunServerIds = null,
        IReadOnlyList<ServerId>? successfulRunServerIds = null,
        IReadOnlyList<CardInstanceId>? accessedCardOccurrenceIds = null,
        IReadOnlyList<CardInstanceId>? trashedCardOccurrenceIds = null,
        IReadOnlyList<AgendaTurnChange>? agendaTurnChanges = null,
        IReadOnlyList<TagTurnChange>? tagTurnChanges = null,
        IReadOnlyList<FirstXUseKey>? firstXUseKeys = null,
        IReadOnlyList<CardInstanceId>? programInstallOccurrenceIds = null,
        IReadOnlyList<ServerId>? hqBreachOccurrenceServerIds = null,
        IReadOnlyList<OccurrenceId>? basicDrawOccurrenceIds = null,
        IReadOnlyList<TurnEventCount>? eventCounts = null,
        IReadOnlyList<DeferredOccurrence>? deferredOccurrences = null,
        int? nextTurnExtraClicks = null,
        IReadOnlyList<CardInstanceId>? handSizeDiscardIds = null,
        IReadOnlyList<CardInstanceId>? freeInstallIds = null) => new(
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
        successfulRun,
        usedAbilityKeys,
        attemptedRunServerIds,
        successfulRunServerIds,
        accessedCardOccurrenceIds,
        trashedCardOccurrenceIds,
        agendaTurnChanges,
        tagTurnChanges,
        firstXUseKeys,
        programInstallOccurrenceIds ?? ProgramInstallOccurrenceIds,
        hqBreachOccurrenceServerIds ?? HqBreachOccurrenceServerIds,
        basicDrawOccurrenceIds ?? BasicDrawOccurrenceIds,
        eventCounts ?? EventCounts,
        deferredOccurrences ?? DeferredOccurrences,
        nextTurnExtraClicks ?? NextTurnExtraClicks,
        handSizeDiscardIds ?? HandSizeDiscardIds,
        freeInstallIds ?? FreeInstallIds);

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
        // Turn facts are immutable once built (their constructor freezes its own copies), so a history holds them as
        // given (S5.0: copying them on every state was about 5% of a simulated step).
        Version = version;
        CorpThisTurn = corpThisTurn;
        CorpLastTurn = corpLastTurn;
        RunnerThisTurn = runnerThisTurn;
        RunnerLastTurn = runnerLastTurn;
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

    internal TurnHistoryState RecordProgramInstall(PlayerSide side, CardInstanceId cardId)
    {
        TurnFactsState current = RequireCurrent(side);
        return ReplaceCurrent(side, current.RecordProgramInstall(cardId));
    }

    internal TurnHistoryState RecordHqBreach(PlayerSide side, ServerId serverId)
    {
        TurnFactsState current = RequireCurrent(side);
        return ReplaceCurrent(side, current.RecordHqBreach(serverId));
    }

    internal TurnHistoryState RecordBasicDraw(PlayerSide side, OccurrenceId occurrenceId)
    {
        TurnFactsState current = RequireCurrent(side);
        return ReplaceCurrent(side, current.RecordBasicDraw(occurrenceId));
    }

    internal TurnHistoryState MarkRunAttempted(PlayerSide side, ServerId serverId)
    {
        TurnFactsState current = RequireCurrent(side);
        return ReplaceCurrent(side, current.MarkRunAttempted(serverId));
    }

    internal TurnHistoryState MarkSuccessfulRun(PlayerSide side, ServerId serverId)
    {
        TurnFactsState current = RequireCurrent(side);
        return ReplaceCurrent(side, current.MarkSuccessfulRun(serverId));
    }

    internal TurnHistoryState MarkSuccessfulRun(PlayerSide side)
    {
        TurnFactsState current = RequireCurrent(side);
        return ReplaceCurrent(side, current.MarkSuccessfulRun());
    }

    internal TurnHistoryState RecordAccess(PlayerSide side, CardInstanceId cardId)
    {
        TurnFactsState current = RequireCurrent(side);
        return ReplaceCurrent(side, current.RecordAccess(cardId));
    }

    internal TurnHistoryState RecordTrash(PlayerSide side, CardInstanceId cardId)
    {
        TurnFactsState current = RequireCurrent(side);
        return ReplaceCurrent(side, current.RecordTrash(cardId));
    }

    internal TurnHistoryState RecordAgendaChange(PlayerSide side, AgendaTurnChange change)
    {
        TurnFactsState current = RequireCurrent(side);
        return ReplaceCurrent(side, current.RecordAgendaChange(change));
    }

    internal TurnHistoryState RecordAgendaChange(
        PlayerSide side,
        CardInstanceId cardId,
        PlayerSide beneficiarySide,
        int points,
        AgendaTurnChangeKind kind) => RecordAgendaChange(
            side,
            new AgendaTurnChange(cardId, beneficiarySide, points, kind));

    internal TurnHistoryState RecordTagChange(PlayerSide side, TagTurnChange change)
    {
        TurnFactsState current = RequireCurrent(side);
        return ReplaceCurrent(side, current.RecordTagChange(change));
    }

    internal TurnHistoryState RecordTagChange(PlayerSide side, PlayerId affectedPlayerId, int delta) => RecordTagChange(
        side,
        new TagTurnChange(affectedPlayerId, delta));

    internal TurnHistoryState ConsumeFirstX(
        PlayerSide side,
        CardInstanceId sourceCardId,
        string key,
        int amount = 1)
    {
        TurnFactsState current = RequireCurrent(side);
        return ReplaceCurrent(side, current.ConsumeFirstX(sourceCardId, key, amount));
    }

    internal int GetFirstXCount(PlayerSide side, CardInstanceId sourceCardId, string key) => RequireCurrent(side)
        .GetFirstXCount(sourceCardId, key);

    internal TurnHistoryState MarkAbilityUsed(
        PlayerSide side,
        CardInstanceId sourceCardId,
        string abilityKey)
    {
        TurnFactsState current = RequireCurrent(side);
        return ReplaceCurrent(side, current.MarkAbilityUsed(sourceCardId, abilityKey));
    }

    internal TurnHistoryState WithDeferredOccurrences(PlayerSide side, IReadOnlyList<DeferredOccurrence> deferred) =>
        ReplaceCurrent(side, RequireCurrent(side).WithDeferredOccurrences(deferred));

    internal TurnHistoryState WithHandSizeDiscards(PlayerSide side, IReadOnlyList<CardInstanceId> discarded) =>
        ReplaceCurrent(side, RequireCurrent(side).WithHandSizeDiscards(discarded));

    internal TurnHistoryState WithFreeInstall(PlayerSide side, CardInstanceId cardId) =>
        ReplaceCurrent(side, RequireCurrent(side).WithFreeInstall(cardId));

    internal TurnHistoryState WithNextTurnExtraClicks(PlayerSide side, int extra) =>
        ReplaceCurrent(side, RequireCurrent(side).WithNextTurnExtraClicks(extra));

    internal TurnHistoryState RecordEvent(PlayerSide side, TurnEventKind kind)
    {
        TurnFactsState current = ThisTurn(side)
            ?? throw new InvalidOperationException($"No current {side} turn facts are available.");
        return ReplaceCurrent(side, current.RecordEvent(kind));
    }

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

/// <summary>A typed turn event counted by <see cref="TurnFactsState.EventCounts"/>.</summary>
public enum TurnEventKind
{
    /// <summary>The active player began an action (a click action of the action phase).</summary>
    ActionStarted,

    /// <summary>The Corp played an operation.</summary>
    OperationPlayed,

    /// <summary>The Runner played an event.</summary>
    EventPlayed,

    /// <summary>The Runner stole an agenda or trashed a Corp card.</summary>
    CorpCardStolenOrTrashed,

    /// <summary>The Runner trashed an installed Corp card.</summary>
    InstalledCorpCardTrashed,

    /// <summary>An agenda was scored or stolen.</summary>
    AgendaScoredOrStolen,

    /// <summary>A run began.</summary>
    RunBegan,

    /// <summary>One of the Runner's tags was removed.</summary>
    TagRemoved,

    /// <summary>The Corp gained credits through an ability on an agenda or operation.</summary>
    CardCreditGain,

    /// <summary>VP-BAD-PUBLICITY: the Corp took bad publicity.</summary>
    BadPublicityTaken,

    /// <summary>VP-SCORING-FACTS: the Corp scored an agenda.</summary>
    AgendaScored,

    /// <summary>VP-SCORING-FACTS: the Corp scored an agenda it had not installed this turn (Myōshu).</summary>
    AgendaScoredNotInstalledThisTurn,

    /// <summary>VP-RUN-FACTS: a piece of ICE was rezzed (Underdome Irregulars).</summary>
    IceRezzed,

    /// <summary>VP-CLICK-COST-MODIFIER: the Corp played a Double operation (Synchrocyclotron).</summary>
    DoubleOperationPlayed,
}

/// <summary>
/// An occurrence raised in the middle of an action whose trigger checkpoint
/// opens once the action completes (CR 9.1: triggers wait for the current
/// ability to finish resolving).
/// </summary>
public sealed record DeferredOccurrence
{
    [JsonConstructor]
    public DeferredOccurrence(OccurrenceKind kind, PlayerSide controller, CardInstanceId? subjectCardId = null)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(controller))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Kind = kind;
        Controller = controller;
        SubjectCardId = subjectCardId;
    }

    [JsonInclude]
    [JsonRequired]
    public OccurrenceKind Kind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide Controller { get; private set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CardInstanceId? SubjectCardId { get; private set; }
}

/// <summary>How many times one typed event happened during a turn.</summary>
public sealed record TurnEventCount
{
    [JsonConstructor]
    public TurnEventCount(TurnEventKind kind, int count)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        Kind = kind;
        Count = count;
    }

    [JsonInclude]
    [JsonRequired]
    public TurnEventKind Kind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Count { get; private set; }
}

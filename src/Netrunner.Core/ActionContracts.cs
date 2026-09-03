using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

public enum DecisionKind
{
    ChooseNumber,
    ChooseCard,
    ToyTurn,
    TurnTiming,
    TurnAction,
    InstallChoice,
    TurnDiscard,
    RunTiming,
    MemoryCleanup,
    TriggerChoice,
    TriggerOrdering,
    InterruptChoice,
    BehaviorChoice,
}

public enum ActionKind
{
    ChooseNumber,
    ChooseCard,
    GainCredit,
    DrawCard,
    PlayCard,
    StartRun,
    InstallCard,
    AdvanceAgenda,
    AdvanceCard,
    ScoreAgenda,
    RemoveTag,
    TrashTaggedResource,
    PurgeVirusCounters,
    StealAgenda,
    EndTurn,
    PassPriority,
    RezCard,
    RezIce,
    BreakSubroutines,
    PumpBreaker,
    JackOut,
    ContinueRun,
    TrashProgram,
    UseTrigger,
    DeclineTrigger,
    OrderTriggers,
    UsePrevention,
    DeclinePrevention,
    UseReplacement,
    DeclineReplacement,
    Legacy,
    ChooseBreachCandidate,
    TrashAccessedCard,
    NoAccessAction,
    ChooseBehavior,
    UseAbility,
}

/// <summary>
/// Strongly typed, pre-materialized action data. Callers submit only the
/// ActionId; these parameters are authored by Core when the decision is built.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(NumberChoiceParameters), "number")]
[JsonDerivedType(typeof(CardChoiceParameters), "card")]
[JsonDerivedType(typeof(PlayCardParameters), "play-card")]
[JsonDerivedType(typeof(RunServerParameters), "run-server")]
[JsonDerivedType(typeof(RezCardParameters), "rez-card")]
[JsonDerivedType(typeof(AdvanceCardParameters), "advance-card")]
[JsonDerivedType(typeof(ScoreAgendaParameters), "score-agenda")]
[JsonDerivedType(typeof(RemoveTagParameters), "remove-tag")]
[JsonDerivedType(typeof(TrashTaggedResourceParameters), "trash-tagged-resource")]
[JsonDerivedType(typeof(PurgeVirusCountersParameters), "purge-virus-counters")]
[JsonDerivedType(typeof(InstallCardParameters), "install")]
[JsonDerivedType(typeof(RunCardPaymentParameters), "run-card-payment")]
[JsonDerivedType(typeof(RunIceParameters), "run-ice")]
[JsonDerivedType(typeof(RunBreakerParameters), "run-breaker")]
[JsonDerivedType(typeof(RunBreakParameters), "run-break")]
[JsonDerivedType(typeof(NoActionParameters), "none")]
[JsonDerivedType(typeof(TriggerChoiceParameters), "trigger")]
[JsonDerivedType(typeof(TriggerOrderParameters), "trigger-order")]
[JsonDerivedType(typeof(PreventionChoiceParameters), "prevention")]
[JsonDerivedType(typeof(ReplacementChoiceParameters), "replacement")]
[JsonDerivedType(typeof(LegacyActionParameters), "legacy")]
[JsonDerivedType(typeof(BreachCandidateParameters), "breach-candidate")]
[JsonDerivedType(typeof(BehaviorChoiceParameters), "behavior-choice")]
[JsonDerivedType(typeof(AbilityActionParameters), "ability")]
public abstract record ActionParameters;

public sealed record NumberChoiceParameters([property: JsonRequired] int Value) : ActionParameters;

public sealed record CardChoiceParameters([property: JsonRequired] CardInstanceId CardId) : ActionParameters;

/// <summary>
/// Engine-issued immutable payload for the Runner's standard tag-removal
/// action. Both costs are frozen in the materialized action; callers submit
/// only the issued action ID.
/// </summary>
public sealed record RemoveTagParameters : ActionParameters
{
    [JsonConstructor]
    public RemoveTagParameters(int clickCost, int creditCost)
    {
        ClickCost = clickCost;
        CreditCost = creditCost;
    }

    [JsonInclude]
    [JsonRequired]
    public int ClickCost { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int CreditCost { get; private set; }
}

/// <summary>
/// Engine-issued immutable payload for the Corp's tagged Runner-resource
/// trash action. The physical target and both costs are frozen in the
/// materialized action; callers submit only the issued action ID.
/// </summary>
public sealed record TrashTaggedResourceParameters : ActionParameters
{
    [JsonConstructor]
    public TrashTaggedResourceParameters(CardInstanceId cardId, int clickCost, int creditCost)
    {
        CardId = cardId;
        ClickCost = clickCost;
        CreditCost = creditCost;
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int ClickCost { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int CreditCost { get; private set; }
}

/// <summary>
/// Engine-issued immutable payload for the Corp's global virus-counter
/// purge. The click cost is frozen in the materialized action; callers submit
/// only the issued action ID.
/// </summary>
public sealed record PurgeVirusCountersParameters : ActionParameters
{
    [JsonConstructor]
    public PurgeVirusCountersParameters(int clickCost) => ClickCost = clickCost;

    [JsonInclude]
    [JsonRequired]
    public int ClickCost { get; private set; }
}

/// <summary>
/// Engine-issued immutable payload for ordinary card play. The action binds
/// the physical card and its frozen printed credit cost; callers submit only
/// the materialized action ID.
/// </summary>
public sealed record PlayCardParameters : ActionParameters
{
    [JsonConstructor]
    public PlayCardParameters(CardInstanceId cardId, int creditCost)
    {
        if (creditCost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(creditCost), "Play credit cost must not be negative.");
        }

        CardId = cardId;
        CreditCost = creditCost;
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int CreditCost { get; private set; }
}

/// <summary>
/// Engine-issued immutable payload for a basic Runner run. The server ID is
/// the complete action payload; basic runs cost one click and no credits or
/// link, so no mutable payment data belongs on the action.
/// </summary>
public sealed record RunServerParameters : ActionParameters
{
    [JsonConstructor]
    public RunServerParameters(ServerId serverId) => ServerId = serverId;

    [JsonInclude]
    [JsonRequired]
    public ServerId ServerId { get; private set; }
}

/// <summary>
/// Engine-issued immutable payload for a runtime-catalog-backed rez action.
/// The printed cost is frozen with the physical card identity so a restored
/// or forged decision cannot change payment semantics.
/// </summary>
public sealed record RezCardParameters : ActionParameters
{
    [JsonConstructor]
    public RezCardParameters(CardInstanceId cardId, int creditCost)
    {
        if (creditCost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(creditCost), "Rez credit cost must not be negative.");
        }

        CardId = cardId;
        CreditCost = creditCost;
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int CreditCost { get; private set; }
}

/// <summary>
/// Engine-issued immutable payload for the real Corp advance action. The
/// action always costs exactly one credit; the frozen cost remains in the
/// payload so a restored or forged decision cannot change payment semantics.
/// </summary>
public sealed record AdvanceCardParameters : ActionParameters
{
    [JsonConstructor]
    public AdvanceCardParameters(CardInstanceId cardId, int creditCost)
    {
        CardId = cardId;
        CreditCost = creditCost;
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int CreditCost { get; private set; }
}

/// <summary>
/// Engine-issued immutable payload for the Corp score option in a paid,
/// rez, and score timing window. Both printed values are bound to the
/// injected runtime card catalog and are never supplied by the reducer
/// caller independently of the issued action.
/// </summary>
public sealed record ScoreAgendaParameters : ActionParameters
{
    [JsonConstructor]
    public ScoreAgendaParameters(
        CardInstanceId cardId,
        int agendaPoints,
        int advancementRequirement)
    {
        CardId = cardId;
        AgendaPoints = agendaPoints;
        AdvancementRequirement = advancementRequirement;
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int AgendaPoints { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int AdvancementRequirement { get; private set; }
}

/// <summary>
/// The physical destination family for a real CR install action.  A server
/// destination is completed by <see cref="InstallCardParameters.ServerId"/>
/// or the explicit <see cref="InstallCardParameters.CreateNewRemote"/>
/// marker; callers never provide a zone ID.
/// </summary>
public enum InstallDestinationKind
{
    RunnerRig,
    ServerRoot,
    ServerIce,
}

/// <summary>
/// Engine-issued immutable install payload.  Unlike the legacy
/// <see cref="CardChoiceParameters"/>, this DTO freezes the source card,
/// typed destination, server/new-remote choice, calculated credit cost, and
/// the bounded replacement-selection protocol.  The action submission still
/// contains only the issued action ID.
/// </summary>
public sealed record InstallCardParameters : ActionParameters
{
    [JsonConstructor]
    public InstallCardParameters(
        CardInstanceId cardId,
        InstallDestinationKind destinationKind,
        ServerId? serverId,
        bool createNewRemote,
        int creditCost,
        IReadOnlyList<CardInstanceId>? candidateReplacementCardIds = null,
        IReadOnlyList<CardInstanceId>? requiredReplacementCardIds = null,
        IReadOnlyList<CardInstanceId>? selectedReplacementCardIds = null,
        bool finalizeSelection = true,
        CostQuote? costQuote = null)
    {
        if (!Enum.IsDefined(destinationKind))
        {
            throw new ArgumentOutOfRangeException(nameof(destinationKind));
        }

        if (creditCost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(creditCost), "Install credit cost must not be negative.");
        }

        if (costQuote is not null && costQuote.EffectiveCost != creditCost)
        {
            throw new ArgumentException(
                "The install credit cost must equal the effective cost in the frozen quote.",
                nameof(creditCost));
        }

        bool serverDestination = destinationKind is InstallDestinationKind.ServerRoot or InstallDestinationKind.ServerIce;
        if (destinationKind == InstallDestinationKind.RunnerRig && (serverId.HasValue || createNewRemote))
        {
            throw new ArgumentException("Runner-rig installs must not identify a server or a new remote.", nameof(serverId));
        }

        if (serverDestination && serverId.HasValue == createNewRemote)
        {
            throw new ArgumentException(
                "A server install must identify exactly one existing server or the explicit new-remote marker.",
                nameof(serverId));
        }

        CardId = cardId;
        DestinationKind = destinationKind;
        ServerId = serverId;
        CreateNewRemote = createNewRemote;
        CreditCost = creditCost;
        CandidateReplacementCardIds = Freeze(candidateReplacementCardIds);
        RequiredReplacementCardIds = Freeze(requiredReplacementCardIds);
        SelectedReplacementCardIds = Freeze(selectedReplacementCardIds);
        FinalizeSelection = finalizeSelection;
        CostQuote = costQuote;
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public InstallDestinationKind DestinationKind { get; private set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ServerId? ServerId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public bool CreateNewRemote { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int CreditCost { get; private set; }

    /// <summary>Frozen candidates for the bounded like-card/program selection.</summary>
    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<CardInstanceId> CandidateReplacementCardIds { get; private set; }

    /// <summary>Frozen candidates that must be selected to satisfy replacement rules.</summary>
    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<CardInstanceId> RequiredReplacementCardIds { get; private set; }

    /// <summary>Cards selected so far by the incremental install protocol.</summary>
    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<CardInstanceId> SelectedReplacementCardIds { get; private set; }

    /// <summary>
    /// False means the action advances the frozen selection; true submits the
    /// exact selected set and commits the install.
    /// </summary>
    [JsonInclude]
    [JsonRequired]
    public bool FinalizeSelection { get; private set; }

    /// <summary>
    /// Optional detached cost explanation. It is omitted for unmodified
    /// installs so legacy action JSON remains byte-compatible.
    /// </summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CostQuote? CostQuote { get; private set; }

    private static ReadOnlyCollection<CardInstanceId> Freeze(IEnumerable<CardInstanceId>? values) =>
        new((values ?? []).ToArray());
}

/// <summary>Typed target for the CR 6.9 approached-ICE rez action.</summary>
public sealed record RunIceParameters([property: JsonRequired] CardInstanceId IceId) : ActionParameters;

/// <summary>
/// Typed target for the CR 3.9.5 encounter icebreaker pump action: the rig
/// breaker whose strength is raised for the current encounter. The exact
/// hosted-credit portion of the payment is materialized by Core per CR
/// 1.10.3c.
/// </summary>
public sealed record RunBreakerParameters : ActionParameters
{
    [JsonConstructor]
    public RunBreakerParameters(CardInstanceId breakerId, int hostedCredits = 0)
    {
        if (hostedCredits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hostedCredits), "Hosted credits spent must not be negative.");
        }

        BreakerId = breakerId;
        HostedCredits = hostedCredits;
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId BreakerId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int HostedCredits { get; private set; }

    [JsonIgnore]
    public int RunCredits => HostedCredits;
}

/// <summary>
/// Typed target and quantity for the CR 9.8 encounter subroutine break
/// action: the named rig icebreaker and how many subroutines it breaks.
/// The quantity is bounded by the number of unbroken subroutines at the moment
/// the action is materialized. The breaker's printed break limit applies to
/// this interface ability activation only, not cumulatively across the
/// encounter. The exact hosted-credit portion is materialized by Core.
/// </summary>
public sealed record RunBreakParameters : ActionParameters
{
    [JsonConstructor]
    public RunBreakParameters(
        CardInstanceId breakerId,
        int quantity,
        int hostedCredits = 0,
        CostQuote? costQuote = null)
    {
        if (quantity < 1)
        {
            throw new ArgumentException("The break quantity must be at least 1.", nameof(quantity));
        }

        if (hostedCredits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hostedCredits), "Hosted credits spent must not be negative.");
        }

        if (costQuote is not null && costQuote.EffectiveCost < 0)
        {
            throw new ArgumentException("A break quote must have a nonnegative effective cost.", nameof(costQuote));
        }

        BreakerId = breakerId;
        Quantity = quantity;
        HostedCredits = hostedCredits;
        CostQuote = costQuote;
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId BreakerId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Quantity { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int HostedCredits { get; private set; }

    /// <summary>
    /// Optional detached cost explanation. It is omitted for unmodified
    /// breaks so legacy action JSON remains byte-compatible.
    /// </summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CostQuote? CostQuote { get; private set; }

    [JsonIgnore]
    public int RunCredits => HostedCredits;
}

/// <summary>
/// Typed accessed-card basic-trash action. Core materializes one action for
/// each legal division between the Runner wallet and hosted run credits.
/// </summary>
public sealed record RunCardPaymentParameters : ActionParameters
{
    [JsonConstructor]
    public RunCardPaymentParameters(CardInstanceId cardId, int hostedCredits = 0)
    {
        if (hostedCredits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hostedCredits), "Hosted credits spent must not be negative.");
        }

        CardId = cardId;
        HostedCredits = hostedCredits;
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int HostedCredits { get; private set; }

    [JsonIgnore]
    public int RunCredits => HostedCredits;
}

/// <summary>
/// Typed marker payload for an action whose complete meaning is defined by its
/// materialized action kind. It is deliberately distinct from the legacy
/// string payload, which is not accepted by authoritative decisions.
/// </summary>
public sealed record NoActionParameters : ActionParameters;

/// <summary>Typed target for using or declining one materialized trigger.</summary>
public sealed record TriggerChoiceParameters([property: JsonRequired] TriggerId TriggerId) : ActionParameters;

/// <summary>Explicit selection of the next trigger in a simultaneous group.</summary>
public sealed record TriggerOrderParameters : ActionParameters
{
    [JsonConstructor]
    public TriggerOrderParameters(TriggerId triggerId)
    {
        TriggerId = triggerId;
    }

    [JsonInclude]
    [JsonRequired]
    public TriggerId TriggerId { get; private set; }
}

/// <summary>Typed prevention choice; amount is materialized by Core.</summary>
public sealed record PreventionChoiceParameters(
    [property: JsonRequired] InterruptId InterruptId,
    [property: JsonRequired] int Amount) : ActionParameters;

/// <summary>Typed replacement choice for one materialized interrupt.</summary>
public sealed record ReplacementChoiceParameters([property: JsonRequired] InterruptId InterruptId) : ActionParameters;

/// <summary>
/// Kept only for the pre-M1 observation compatibility constructor. It is not
/// accepted in an authoritative PendingDecision.
/// </summary>
public sealed record LegacyActionParameters([property: JsonRequired] string Kind) : ActionParameters;

/// <summary>
/// Selection of one candidate during a breach: either the explicit card the
/// Runner chooses from the visible pool, or the hidden source Core must draw
/// from. Exactly one of the two must be present.
/// </summary>
public sealed record BreachCandidateParameters : ActionParameters
{
    [JsonConstructor]
    public BreachCandidateParameters(CardInstanceId? cardId, BreachHiddenCandidateSource? source)
    {
        bool hasCard = cardId.HasValue;
        bool hasSource = source.HasValue;
        if (hasCard == hasSource)
        {
            throw new ArgumentException(
                "Exactly one of the candidate card ID or the hidden candidate source must be provided.",
                nameof(cardId));
        }

        CardId = cardId;
        Source = source;
    }

    /// <summary>Convenience selection of one visible candidate card.</summary>
    public BreachCandidateParameters(CardInstanceId cardId)
        : this(cardId, null)
    {
    }

    /// <summary>Convenience selection of a hidden candidate to draw from a source.</summary>
    public BreachCandidateParameters(BreachHiddenCandidateSource source)
        : this(null, source)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId? CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public BreachHiddenCandidateSource? Source { get; private set; }
}

/// <summary>
/// A complete selection for one materialized generic card-behavior choice.
/// Option keys represent mode/order options; card IDs represent frozen hidden
/// or public candidates. Empty lists are the explicit skip for an optional
/// up-to-one choice.
/// </summary>
public sealed record BehaviorChoiceParameters : ActionParameters
{
    [JsonConstructor]
    public BehaviorChoiceParameters(
        string choiceKey,
        IReadOnlyList<string>? optionKeys = null,
        IReadOnlyList<CardInstanceId>? cardIds = null,
        bool finalizeSelection = true)
    {
        if (!StableIdentifier.TryValidate(choiceKey))
        {
            throw new ArgumentException("A behavior choice key must be stable.", nameof(choiceKey));
        }

        ChoiceKey = choiceKey;
        OptionKeys = new ReadOnlyCollection<string>((optionKeys ?? []).ToArray());
        CardIds = new ReadOnlyCollection<CardInstanceId>((cardIds ?? []).ToArray());
        FinalizeSelection = finalizeSelection;
    }

    public BehaviorChoiceParameters(string choiceKey, string optionKey)
        : this(choiceKey, [optionKey], null, true)
    {
    }

    public BehaviorChoiceParameters(string choiceKey, IEnumerable<CardInstanceId> cardIds)
        : this(choiceKey, null, cardIds?.ToArray(), true)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public string ChoiceKey { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<string> OptionKeys { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<CardInstanceId> CardIds { get; private set; }

    /// <summary>
    /// Large any-number/up-to choices use a bounded toggle protocol. A
    /// non-final action adds one card to the frozen partial selection; the
    /// final action submits the accumulated set for normal cardinality
    /// validation. Existing complete-choice payloads default to true.
    /// </summary>
    [JsonInclude]
    [JsonRequired]
    public bool FinalizeSelection { get; private set; }
}

/// <summary>
/// Engine-issued payload for one paid-ability activation.  The payload is a
/// detached snapshot: callers submit its action ID and cannot replace source,
/// costs, targets, usage facts, or catalog pins.
/// </summary>
public sealed record AbilityActionParameters : ActionParameters
{
    [JsonConstructor]
    public AbilityActionParameters(
        CardInstanceId sourceCardId,
        CardDefinitionId definitionId,
        string abilityKey,
        BehaviorAbilityTiming timing,
        BehaviorAbilityActivation activation,
        IReadOnlyList<BehaviorCost> costs,
        IReadOnlyList<CardInstanceId> targetCardIds,
        AbilityUsageSnapshot usage,
        string catalogFingerprint,
        string definitionFingerprint,
        ZoneId sourceZoneId,
        ZoneKind sourceZoneKind)
    {
        if (!StableIdentifier.TryValidate(sourceCardId.Value))
        {
            throw new ArgumentException("An ability action requires a stable source card ID.", nameof(sourceCardId));
        }

        if (!StableIdentifier.TryValidate(definitionId.Value))
        {
            throw new ArgumentException("An ability action requires a stable definition ID.", nameof(definitionId));
        }

        if (!StableIdentifier.TryValidate(sourceZoneId.Value))
        {
            throw new ArgumentException("An ability action requires a stable source zone ID.", nameof(sourceZoneId));
        }

        if (!Enum.IsDefined(sourceZoneKind))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceZoneKind));
        }

        if (!StableIdentifier.TryValidate(abilityKey))
        {
            throw new ArgumentException(
                "An ability action requires a stable ability key.",
                nameof(abilityKey));
        }

        if (!Enum.IsDefined(timing))
        {
            throw new ArgumentOutOfRangeException(nameof(timing));
        }

        if (!Enum.IsDefined(activation))
        {
            throw new ArgumentOutOfRangeException(nameof(activation));
        }

        if (!DeterministicHash.IsSha256Hex(catalogFingerprint))
        {
            throw new ArgumentException(
                "An ability action catalog fingerprint must be lowercase SHA-256 hex.",
                nameof(catalogFingerprint));
        }

        if (!DeterministicHash.IsSha256Hex(definitionFingerprint))
        {
            throw new ArgumentException(
                "An ability action definition fingerprint must be lowercase SHA-256 hex.",
                nameof(definitionFingerprint));
        }

        ArgumentNullException.ThrowIfNull(usage);
        SourceCardId = sourceCardId;
        DefinitionId = definitionId;
        AbilityKey = abilityKey;
        Timing = timing;
        Activation = activation;
        Costs = Freeze(costs, nameof(costs));
        TargetCardIds = FreezeIds(targetCardIds);
        Usage = new AbilityUsageSnapshot(usage.Limit, usage.UsesThisTurn);
        CatalogFingerprint = catalogFingerprint;
        DefinitionFingerprint = definitionFingerprint;
        SourceZoneId = sourceZoneId;
        SourceZoneKind = sourceZoneKind;
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId SourceCardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardDefinitionId DefinitionId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneId SourceZoneId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneKind SourceZoneKind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string AbilityKey { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public BehaviorAbilityTiming Timing { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public BehaviorAbilityActivation Activation { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<BehaviorCost> Costs { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<CardInstanceId> TargetCardIds { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public AbilityUsageSnapshot Usage { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string CatalogFingerprint { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string DefinitionFingerprint { get; private set; }

    private static ReadOnlyCollection<T> Freeze<T>(IReadOnlyList<T> values, string parameterName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(values);
        T[] copy = values.ToArray();
        if (copy.Any(static value => value is null))
        {
            throw new ArgumentException(
                "Ability action collections must not contain null entries.",
                parameterName);
        }

        return new ReadOnlyCollection<T>(copy);
    }

    private static ReadOnlyCollection<CardInstanceId> FreezeIds(IReadOnlyList<CardInstanceId> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new ReadOnlyCollection<CardInstanceId>(values.ToArray());
    }
}

public sealed record LegalAction
{
    [JsonConstructor]
    public LegalAction(
        ActionId id,
        ActionKind kind,
        string description,
        ActionParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(parameters);
        Id = id;
        Kind = kind;
        Description = description;
        Parameters = parameters;
    }

    /// <summary>Compatibility overload for the M0 observation fixture.</summary>
    public LegalAction(ActionId id, string kind, string description)
        : this(id, ActionKind.Legacy, description, new LegacyActionParameters(kind))
    {
        ArgumentNullException.ThrowIfNull(kind);
    }

    [JsonInclude]
    [JsonRequired]
    public ActionId Id { get; private set; }

    [JsonIgnore]
    public ActionId ActionId => Id;

    [JsonInclude]
    [JsonRequired]
    public ActionKind Kind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string Description { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ActionParameters Parameters { get; private set; }
}

/// <summary>
/// A decision revision is part of the authoritative state. A caller cannot
/// add parameters or targets to this submission after Core materializes it.
/// </summary>
public sealed record ActionSubmission
{
    [JsonConstructor]
    public ActionSubmission(
        PlayerId actor,
        DecisionId decisionId,
        long revision,
        ActionId actionId)
    {
        Actor = actor;
        DecisionId = decisionId;
        Revision = revision;
        ActionId = actionId;
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Actor { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public DecisionId DecisionId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public long Revision { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ActionId ActionId { get; private set; }
}

/// <summary>
/// Evidence for one committed action. The record is deliberately outside
/// GameState because its resulting state hash would otherwise hash itself.
/// </summary>
public sealed record AcceptedActionRecord
{
    [JsonConstructor]
    public AcceptedActionRecord(
        long sequence,
        ActionSubmission submission,
        string resultingStateHash,
        string resultingEventBatchHash)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(resultingStateHash);
        ArgumentNullException.ThrowIfNull(resultingEventBatchHash);
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Accepted action sequence must be positive.");
        }

        if (!DeterministicHash.IsSha256Hex(resultingStateHash))
        {
            throw new ArgumentException("Resulting state hash must be lowercase SHA-256 hex.", nameof(resultingStateHash));
        }

        if (!DeterministicHash.IsSha256Hex(resultingEventBatchHash))
        {
            throw new ArgumentException("Resulting event-batch hash must be lowercase SHA-256 hex.", nameof(resultingEventBatchHash));
        }

        Sequence = sequence;
        Submission = submission;
        ResultingStateHash = resultingStateHash;
        ResultingEventBatchHash = resultingEventBatchHash;
    }

    [JsonInclude]
    [JsonRequired]
    public long Sequence { get; private set; }

    [JsonIgnore]
    public long ActionSequence => Sequence;

    [JsonInclude]
    [JsonRequired]
    public ActionSubmission Submission { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string ResultingStateHash { get; private set; }

    [JsonIgnore]
    public string StateHash => ResultingStateHash;

    [JsonInclude]
    [JsonRequired]
    public string ResultingEventBatchHash { get; private set; }

    [JsonIgnore]
    public string EventBatchHash => ResultingEventBatchHash;
}

public sealed class PendingDecision
{
    [JsonConstructor]
    public PendingDecision(
        DecisionId id,
        long revision,
        PlayerId owner,
        DecisionKind kind,
        string prompt,
        IReadOnlyList<LegalAction> legalActions)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(legalActions);
        if (revision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision), "Decision revision must be positive.");
        }

        Id = id;
        Revision = revision;
        Owner = owner;
        Kind = kind;
        Prompt = prompt;
        LegalActions = new ReadOnlyCollection<LegalAction>(legalActions
            .Select(CloneAction)
            .ToArray());
    }

    [JsonInclude]
    [JsonRequired]
    public DecisionId Id { get; private set; }

    [JsonIgnore]
    public DecisionId DecisionId => Id;

    [JsonInclude]
    [JsonRequired]
    public long Revision { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Owner { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public DecisionKind Kind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string Prompt { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<LegalAction> LegalActions { get; private set; }

    internal PendingDecision Copy() =>
        new(Id, Revision, Owner, Kind, Prompt, LegalActions);

    private static LegalAction CloneAction(LegalAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new LegalAction(action.Id, action.Kind, action.Description, CloneParameters(action.Parameters));
    }

    private static ActionParameters CloneParameters(ActionParameters parameters) => parameters switch
    {
        NumberChoiceParameters numberChoice => new NumberChoiceParameters(numberChoice.Value),
        CardChoiceParameters cardChoice => new CardChoiceParameters(cardChoice.CardId),
        RemoveTagParameters removeTag => new RemoveTagParameters(removeTag.ClickCost, removeTag.CreditCost),
        TrashTaggedResourceParameters trashTaggedResource => new TrashTaggedResourceParameters(
            trashTaggedResource.CardId,
            trashTaggedResource.ClickCost,
            trashTaggedResource.CreditCost),
        PurgeVirusCountersParameters purgeVirusCounters => new PurgeVirusCountersParameters(purgeVirusCounters.ClickCost),
        PlayCardParameters playCard => new PlayCardParameters(playCard.CardId, playCard.CreditCost),
        RunServerParameters runServer => new RunServerParameters(runServer.ServerId),
        RezCardParameters rezCard => new RezCardParameters(rezCard.CardId, rezCard.CreditCost),
        AdvanceCardParameters advanceCard => new AdvanceCardParameters(advanceCard.CardId, advanceCard.CreditCost),
        ScoreAgendaParameters scoreAgenda => new ScoreAgendaParameters(
            scoreAgenda.CardId,
            scoreAgenda.AgendaPoints,
            scoreAgenda.AdvancementRequirement),
        InstallCardParameters install => new InstallCardParameters(
            install.CardId,
            install.DestinationKind,
            install.ServerId,
            install.CreateNewRemote,
            install.CreditCost,
            install.CandidateReplacementCardIds,
            install.RequiredReplacementCardIds,
            install.SelectedReplacementCardIds,
            install.FinalizeSelection,
            install.CostQuote),
        RunCardPaymentParameters payment => new RunCardPaymentParameters(payment.CardId, payment.HostedCredits),
        RunIceParameters runIce => new RunIceParameters(runIce.IceId),
        RunBreakerParameters breaker => new RunBreakerParameters(breaker.BreakerId, breaker.HostedCredits),
        RunBreakParameters runBreak => new RunBreakParameters(
            runBreak.BreakerId,
            runBreak.Quantity,
            runBreak.HostedCredits,
            runBreak.CostQuote),
        NoActionParameters => new NoActionParameters(),
        TriggerChoiceParameters trigger => new TriggerChoiceParameters(trigger.TriggerId),
        TriggerOrderParameters order => new TriggerOrderParameters(order.TriggerId),
        PreventionChoiceParameters prevention => new PreventionChoiceParameters(prevention.InterruptId, prevention.Amount),
        ReplacementChoiceParameters replacement => new ReplacementChoiceParameters(replacement.InterruptId),
        LegacyActionParameters legacy => new LegacyActionParameters(legacy.Kind),
        BreachCandidateParameters breachCandidate => new BreachCandidateParameters(breachCandidate.CardId, breachCandidate.Source),
        BehaviorChoiceParameters behaviorChoice => new BehaviorChoiceParameters(
            behaviorChoice.ChoiceKey,
            behaviorChoice.OptionKeys,
            behaviorChoice.CardIds,
            behaviorChoice.FinalizeSelection),
        AbilityActionParameters ability => new AbilityActionParameters(
            ability.SourceCardId,
            ability.DefinitionId,
            ability.AbilityKey,
            ability.Timing,
            ability.Activation,
            ability.Costs,
            ability.TargetCardIds,
            ability.Usage,
            ability.CatalogFingerprint,
            ability.DefinitionFingerprint,
            ability.SourceZoneId,
            ability.SourceZoneKind),
        _ => throw new InvalidOperationException(
            $"No immutable clone is registered for action parameter type '{parameters.GetType().Name}'."),
    };
}

public enum ApplyStatus
{
    Accepted,
    Rejected,
}

public enum ApplyRejectionCode
{
    TerminalState,
    NoPendingDecision,
    WrongOwner,
    StaleDecisionId,
    StaleRevision,
    UnknownAction,
    InvalidInputState,
    ResolutionFailure,
}

public sealed record ApplyRejection(
    ApplyRejectionCode Code,
    string Message)
{
    [JsonIgnore]
    public ApplyRejectionCode Reason => Code;
}

public sealed class ApplyResult
{
    private static readonly IReadOnlyList<EngineEvent> EmptyEvents =
        Array.AsReadOnly(Array.Empty<EngineEvent>());

    private ApplyResult(
        ApplyStatus status,
        GameState state,
        ApplyRejection? rejection,
        IReadOnlyList<EngineEvent> events,
        AcceptedActionRecord? acceptedAction)
    {
        Status = status;
        State = state;
        Rejection = rejection;
        Events = events;
        AcceptedAction = acceptedAction;
    }

    public ApplyStatus Status { get; }

    public bool Accepted => Status == ApplyStatus.Accepted;

    public bool IsAccepted => Accepted;

    public bool IsRejected => !Accepted;

    public GameState State { get; }

    public ApplyRejection? Rejection { get; }

    public ApplyRejection? Error => Rejection;

    [JsonIgnore]
    public ApplyRejectionCode? RejectionCode => Rejection?.Code;

    public IReadOnlyList<EngineEvent> Events { get; }

    public AcceptedActionRecord? AcceptedAction { get; }

    [JsonIgnore]
    public AcceptedActionRecord? ActionRecord => AcceptedAction;

    internal static ApplyResult Accept(
        GameState state,
        IReadOnlyList<EngineEvent> events,
        AcceptedActionRecord acceptedAction) =>
        new(ApplyStatus.Accepted, state, null, events, acceptedAction);

    internal static ApplyResult Reject(GameState state, ApplyRejectionCode code, string message) =>
        new(ApplyStatus.Rejected, state, new ApplyRejection(code, message), EmptyEvents, null);
}

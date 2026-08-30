using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

public enum DecisionKind
{
    ChooseNumber,
    ChooseCard,
    ToyTurn,
    RunTiming,
    MemoryCleanup,
    TriggerChoice,
    TriggerOrdering,
    InterruptChoice,
}

public enum ActionKind
{
    ChooseNumber,
    ChooseCard,
    GainCredit,
    DrawCard,
    InstallCard,
    AdvanceAgenda,
    ScoreAgenda,
    StealAgenda,
    EndTurn,
    PassPriority,
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
}

/// <summary>
/// Strongly typed, pre-materialized action data. Callers submit only the
/// ActionId; these parameters are authored by Core when the decision is built.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(NumberChoiceParameters), "number")]
[JsonDerivedType(typeof(CardChoiceParameters), "card")]
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
public abstract record ActionParameters;

public sealed record NumberChoiceParameters([property: JsonRequired] int Value) : ActionParameters;

public sealed record CardChoiceParameters([property: JsonRequired] CardInstanceId CardId) : ActionParameters;

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
    public RunBreakParameters(CardInstanceId breakerId, int quantity, int hostedCredits = 0)
    {
        if (quantity < 1)
        {
            throw new ArgumentException("The break quantity must be at least 1.", nameof(quantity));
        }

        if (hostedCredits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hostedCredits), "Hosted credits spent must not be negative.");
        }

        BreakerId = breakerId;
        Quantity = quantity;
        HostedCredits = hostedCredits;
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
        RunCardPaymentParameters payment => new RunCardPaymentParameters(payment.CardId, payment.HostedCredits),
        RunIceParameters runIce => new RunIceParameters(runIce.IceId),
        RunBreakerParameters breaker => new RunBreakerParameters(breaker.BreakerId, breaker.HostedCredits),
        RunBreakParameters runBreak => new RunBreakParameters(runBreak.BreakerId, runBreak.Quantity, runBreak.HostedCredits),
        NoActionParameters => new NoActionParameters(),
        TriggerChoiceParameters trigger => new TriggerChoiceParameters(trigger.TriggerId),
        TriggerOrderParameters order => new TriggerOrderParameters(order.TriggerId),
        PreventionChoiceParameters prevention => new PreventionChoiceParameters(prevention.InterruptId, prevention.Amount),
        ReplacementChoiceParameters replacement => new ReplacementChoiceParameters(replacement.InterruptId),
        LegacyActionParameters legacy => new LegacyActionParameters(legacy.Kind),
        BreachCandidateParameters breachCandidate => new BreachCandidateParameters(breachCandidate.CardId, breachCandidate.Source),
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

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

/// <summary>
/// The read-only behavior view consumed by an engine session.  Card metadata
/// and rules behavior are deliberately separate: this catalog contains only
/// compiled behavior contracts and never owns card text or a metadata loader.
/// </summary>
public interface IBehaviorCatalog
{
    /// <summary>Build/version label for the compiled behavior set.</summary>
    string Version { get; }

    /// <summary>Alias used by serialized state and replay contracts.</summary>
    string BehaviorRegistryVersion { get; }

    /// <summary>
    /// Deterministic SHA-256 fingerprint of the compiled registrations.  The
    /// fingerprint is useful when two catalogs intentionally share a version
    /// label while carrying different composition (for example in tests).
    /// </summary>
    string Fingerprint { get; }

    string BehaviorRegistryFingerprint { get; }

    IReadOnlyCollection<CardDefinitionId> DefinitionIds { get; }

    IReadOnlyCollection<CardDefinitionId> AllDefinitionIds { get; }

    IReadOnlyCollection<IceBehaviorSpec> IceSpecs { get; }

    IReadOnlyCollection<IcebreakerBehaviorSpec> IcebreakerSpecs { get; }

    IReadOnlyCollection<AssetBehaviorSpec> AssetSpecs { get; }

    IReadOnlyCollection<AgendaBehaviorSpec> AgendaSpecs { get; }

    IReadOnlyCollection<RunEventBehaviorSpec> RunEventSpecs { get; }

    IReadOnlyCollection<CardBehaviorDefinition> GenericSpecs { get; }

    bool TryGetIce(CardDefinitionId definitionId, out IceBehaviorSpec spec);

    bool TryGetIcebreaker(CardDefinitionId definitionId, out IcebreakerBehaviorSpec spec);

    bool TryGetAsset(CardDefinitionId definitionId, out AssetBehaviorSpec spec);

    bool TryGetAgenda(CardDefinitionId definitionId, out AgendaBehaviorSpec spec);

    bool TryGetRunEvent(CardDefinitionId definitionId, out RunEventBehaviorSpec spec);

    bool TryGet(CardDefinitionId definitionId, out CardBehaviorDefinition spec);

    /// <summary>Returns the stable fingerprint of one generic definition.</summary>
    string GetDefinitionFingerprint(CardDefinitionId definitionId);

    /// <summary>
    /// Optional compiled card-class metadata for typed hidden-zone filters.
    /// Catalogs that do not carry card-class registrations fail closed.
    /// </summary>
    bool TryGetCardType(CardDefinitionId definitionId, out BehaviorCardType cardType)
    {
        cardType = default;
        return false;
    }

    /// <summary>
    /// Optional printed subtypes registered by composition for value filters
    /// that run without runtime metadata (a strength that counts fracters).
    /// Catalogs without registrations fail closed.
    /// </summary>
    bool TryGetCardSubtypes(CardDefinitionId definitionId, out IReadOnlyList<string> subtypes)
    {
        subtypes = [];
        return false;
    }

    /// <summary>
    /// Whether the engine records typed turn events (actions started,
    /// operations played) for this catalog's cards. Off for catalogs that
    /// never read them, so their states keep their historical bytes.
    /// </summary>
    bool TracksTurnEvents => false;

    /// <summary>
    /// VP.2: whether the engine records this turn event, which only a catalog with a card that reads it does (Vantage
    /// Point's scoring and run facts), so other catalogs' states keep their historical bytes.
    /// </summary>
    bool RecordsTurnEvent(TurnEventKind kind) => false;
}

/// <summary>
/// A typed cost used by a composable compiled card behavior.  The first slice
/// defines the contract; later card batches add concrete instruction users.
/// </summary>
public sealed record BehaviorCost
{
    [JsonConstructor]
    public BehaviorCost(
        BehaviorCostKind kind,
        int amount = 1,
        string? counterName = null,
        string? additionalKey = null,
        BehaviorTarget? additionalTarget = null,
        BehaviorAdditionalCostKind? additionalKind = null,
        string? creditSourceSubtype = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (kind == BehaviorCostKind.Credits && amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "A Credits behavior cost amount must not be negative.");
        }

        if (kind is BehaviorCostKind.Clicks
            or BehaviorCostKind.LoseClick
            or BehaviorCostKind.Counter
            or BehaviorCostKind.Tags
            or BehaviorCostKind.HostedCredits
            or BehaviorCostKind.RemoveSelfFromGame
            or BehaviorCostKind.Additional)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(
                amount,
                1,
                "A non-credit behavior cost amount must be positive.");
        }

        if ((kind is BehaviorCostKind.TrashSelf or BehaviorCostKind.RemoveSelfFromGame)
            && amount != 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "A self-movement behavior cost must have exactly one unit.");
        }

        if (kind == BehaviorCostKind.Counter
            && !StableIdentifier.TryValidate(counterName))
        {
            throw new ArgumentException("Counter costs require a stable counter name.", nameof(counterName));
        }

        if (kind == BehaviorCostKind.Additional
            && !StableIdentifier.TryValidate(additionalKey))
        {
            throw new ArgumentException(
                "Additional costs require a stable additional-cost key.",
                nameof(additionalKey));
        }

        if (additionalKind is { } operation && !Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(nameof(additionalKind));
        }

        if (creditSourceSubtype is not null
            && (kind != BehaviorCostKind.Credits || !StableIdentifier.TryValidate(creditSourceSubtype)))
        {
            throw new ArgumentException(
                "Only a credit cost may restrict its credits to cards of a subtype, named by a stable identifier.",
                nameof(creditSourceSubtype));
        }

        CreditSourceSubtype = creditSourceSubtype;

        if (kind != BehaviorCostKind.Counter && counterName is not null)
        {
            throw new ArgumentException(
                "Only counter costs may carry a counter name.",
                nameof(counterName));
        }

        if (kind != BehaviorCostKind.Additional && additionalKey is not null)
        {
            throw new ArgumentException(
                "Only additional costs may carry an additional-cost key.",
                nameof(additionalKey));
        }

        if (kind != BehaviorCostKind.Additional && additionalTarget is not null)
        {
            throw new ArgumentException(
                "Only additional costs may carry an additional-cost target.",
                nameof(additionalTarget));
        }

        if (kind != BehaviorCostKind.Additional && additionalKind is not null)
        {
            throw new ArgumentException(
                "Only additional costs may carry an additional-cost operation.",
                nameof(additionalKind));
        }

        // The historical Additional shape was intentionally inert and is
        // retained for old compatibility fixtures. A structured additional
        // cost carries an explicit operation and an explicitly owned zone
        // target; the pair is never inferred from a string key.
        if (kind == BehaviorCostKind.Additional
            && additionalKind is null
            && additionalTarget is not null)
        {
            throw new ArgumentException(
                "A structured additional cost target requires a typed operation.",
                nameof(additionalKind));
        }

        if (kind == BehaviorCostKind.Additional
            && additionalKind is not null
            && !IsStructuredCardTrash(additionalTarget))
        {
            throw new ArgumentException(
                "A card-trash additional cost requires a ZoneCards target with an explicit owner and no reveal.",
                nameof(additionalTarget));
        }

        Kind = kind;
        Amount = amount;
        CounterName = counterName;
        AdditionalKey = additionalKey;
        AdditionalTarget = additionalTarget;
        AdditionalKind = additionalKind;
    }

    public BehaviorCostKind Kind { get; }

    public int Amount { get; }

    public string? CounterName { get; }

    /// <summary>
    /// Stable key for a card-specific additional cost.  It is intentionally
    /// inert in Core until a later slice supplies the corresponding reducer.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AdditionalKey { get; }

    /// <summary>
    /// Typed source/target descriptor for a structured additional cost. The
    /// descriptor is deliberately separate from the ability's effect target
    /// list: paying a cost must not retarget the effect itself.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BehaviorTarget? AdditionalTarget { get; }

    /// <summary>Typed operation implemented by a structured additional cost.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BehaviorAdditionalCostKind? AdditionalKind { get; }

    /// <summary>
    /// VP-STEALTH-CREDITS: a credit cost payable only with credits from cards of this printed subtype ("stealth": Corsair,
    /// Lampades, Baker), never from the Runner's pool or the bad publicity fund. Null for an ordinary credit cost.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CreditSourceSubtype { get; }

    private static bool IsStructuredCardTrash(BehaviorTarget? target) =>
        target is
        {
            Kind: BehaviorTargetKind.ZoneCards,
            Filter: not null,
            Reveal: false,
        }
        && target.Filter.OwnerSide is not null;

}

public enum BehaviorCostKind
{
    Credits,
    Clicks,
    Tags,
    Counter,
    TrashSelf,
    LoseClick,
    Additional,
    HostedCredits,
    RemoveSelfFromGame,
}

/// <summary>Typed operation represented by a structured additional cost.</summary>
public enum BehaviorAdditionalCostKind
{
    TrashCards,
}

/// <summary>A serializable condition term in a compiled behavior contract.</summary>
public sealed record BehaviorCondition
{
    [JsonConstructor]
    public BehaviorCondition(
        BehaviorConditionKind kind,
        string? fact = null,
        int? value = null,
        PlayerSide? subjectSide = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (subjectSide is { } side && !Enum.IsDefined(side))
        {
            throw new ArgumentOutOfRangeException(nameof(subjectSide));
        }

        if (subjectSide is not null
            && kind is not (BehaviorConditionKind.Tagged
                or BehaviorConditionKind.NotTagged
                or BehaviorConditionKind.ClicksRemainingAtLeast
                or BehaviorConditionKind.TurnEventCountIs
                or BehaviorConditionKind.TurnEventCountAtLeast
                or BehaviorConditionKind.CreditsAtLeast))
        {
            throw new ArgumentException(
                "An explicit subject side is valid only for tag, click, credit and turn-event conditions.",
                nameof(subjectSide));
        }

        if (kind is BehaviorConditionKind.PlayedFromZone or BehaviorConditionKind.SourceInZone or BehaviorConditionKind.SourceNotInZone
            && (fact is null || !Enum.TryParse<BehaviorZone>(fact, ignoreCase: false, out _) || value is not null))
        {
            throw new ArgumentException("A zone condition names one typed zone.", nameof(fact));
        }

        if (kind == BehaviorConditionKind.ZoneCountAtLeast
            && (fact is null || !Enum.TryParse<BehaviorZone>(fact, ignoreCase: false, out _) || value is null or < 0))
        {
            throw new ArgumentException("A zone-count condition names one typed zone and a nonnegative count.", nameof(fact));
        }

        if (kind == BehaviorConditionKind.CreditsAtLeast && (subjectSide is null || value is null or < 0 || fact is not null))
        {
            throw new ArgumentException("A credit condition names a side and a nonnegative amount.", nameof(value));
        }

        if (kind == BehaviorConditionKind.BadPublicityAtLeast && (value is null or < 0 || fact is not null))
        {
            throw new ArgumentException("A bad publicity condition names a nonnegative amount.", nameof(value));
        }

        if (kind == BehaviorConditionKind.Encountering && (value is not null || fact is not null && !StableIdentifier.TryValidate(fact)))
        {
            throw new ArgumentException("An encounter condition may name one ICE subtype.", nameof(fact));
        }

        if (kind == BehaviorConditionKind.ChoseCards && (!StableIdentifier.TryValidate(fact) || value is not null))
        {
            throw new ArgumentException("A chose-cards condition names its stable choice key.", nameof(fact));
        }

        if (kind is BehaviorConditionKind.TurnEventCountIs or BehaviorConditionKind.TurnEventCountAtLeast
            && (fact is null || !Enum.TryParse<TurnEventKind>(fact, ignoreCase: false, out _)
                || value is null or < 0))
        {
            throw new ArgumentException(
                "A turn-event condition names a typed turn event and a nonnegative count.",
                nameof(fact));
        }

        if (kind is BehaviorConditionKind.FactEquals or BehaviorConditionKind.CounterAtLeast)
        {
            if (string.IsNullOrWhiteSpace(fact))
            {
                throw new ArgumentException(
                    "This condition kind requires a fact or counter name.",
                    nameof(fact));
            }
        }

        if ((kind is BehaviorConditionKind.Tagged or BehaviorConditionKind.NotTagged)
            && fact is not null)
        {
            throw new ArgumentException(
                "Tagged conditions use the single standard tag state and do not accept a fact identifier.",
                nameof(fact));
        }

        if (kind == BehaviorConditionKind.ClicksRemainingAtLeast)
        {
            if (value is null || value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "A clicks-remaining condition requires a nonnegative threshold.");
            }

            if (fact is not null)
            {
                throw new ArgumentException(
                    "A clicks-remaining condition does not accept a fact identifier.",
                    nameof(fact));
            }
        }

        if ((kind is BehaviorConditionKind.Tagged or BehaviorConditionKind.NotTagged)
            && value is not null)
        {
            throw new ArgumentException(
                "A tag condition does not accept a numeric value.",
                nameof(value));
        }

        if ((kind is BehaviorConditionKind.InstalledThisTurn
                or BehaviorConditionKind.SuccessfulRunThisTurn
                or BehaviorConditionKind.FoundCard
                or BehaviorConditionKind.SuccessfulRunDuringRunnerLastTurn
                or BehaviorConditionKind.RunEventActive
                or BehaviorConditionKind.PlayedThisTurn
                or BehaviorConditionKind.NoActionFinishedThisTurn
                or BehaviorConditionKind.SourceScoredThisTurn
                or BehaviorConditionKind.SuccessfulRunOnEveryCentralThisTurn
                or BehaviorConditionKind.RunAgainstSourceServer
                or BehaviorConditionKind.RunAgainstAnotherServer)
            && (fact is not null || value is not null))
        {
            throw new ArgumentException(
                "This condition kind does not accept a fact or numeric value.",
                fact is not null ? nameof(fact) : nameof(value));
        }

        if (kind is BehaviorConditionKind.ThreatLevelAtLeast
                or BehaviorConditionKind.RunnerTagsAtLeast
                or BehaviorConditionKind.ZoneCountAtMost
                or BehaviorConditionKind.UnusedMemoryAtMost
                or BehaviorConditionKind.UnusedMemoryAtLeast
                or BehaviorConditionKind.SourceAdvancementAtLeast
            && value is null or < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "A threshold condition requires a nonnegative value.");
        }

        if (kind == BehaviorConditionKind.ZoneCountAtMost
            ? fact is null || !Enum.TryParse<BehaviorZone>(fact, ignoreCase: false, out _)
            : kind is BehaviorConditionKind.ThreatLevelAtLeast
                or BehaviorConditionKind.RunnerTagsAtLeast
                or BehaviorConditionKind.UnusedMemoryAtMost
                or BehaviorConditionKind.UnusedMemoryAtLeast
                && fact is not null)
        {
            throw new ArgumentException(
                "A zone-count condition names one typed zone; other threshold conditions take no fact.",
                nameof(fact));
        }

        Fact = fact;
        Kind = kind;
        SubjectSide = subjectSide;
        Value = value;
    }

    public static BehaviorCondition RunEventActive() => new(BehaviorConditionKind.RunEventActive);

    public static BehaviorCondition ThreatLevelAtLeast(int threshold) =>
        new(BehaviorConditionKind.ThreatLevelAtLeast, value: threshold);

    public static BehaviorCondition RunnerTagsAtLeast(int threshold) =>
        new(BehaviorConditionKind.RunnerTagsAtLeast, value: threshold);

    /// <summary>
    /// The active turn's count of a typed event is exactly the value ("the
    /// first time each turn" is a count of 1), optionally only during one
    /// side's turn.
    /// </summary>
    public static BehaviorCondition TurnEventCountIs(TurnEventKind kind, int count, PlayerSide? duringTurnOf = null) =>
        new(BehaviorConditionKind.TurnEventCountIs, fact: kind.ToString(), value: count, subjectSide: duringTurnOf);

    /// <summary>VP-SCORING-FACTS: the active turn's count of a typed event is at least the value.</summary>
    public static BehaviorCondition TurnEventCountAtLeast(TurnEventKind kind, int count, PlayerSide? duringTurnOf = null) =>
        new(BehaviorConditionKind.TurnEventCountAtLeast, fact: kind.ToString(), value: count, subjectSide: duringTurnOf);

    /// <summary>VP-SCORING-FACTS: the source agenda was scored this turn.</summary>
    public static BehaviorCondition SourceScoredThisTurn() => new(BehaviorConditionKind.SourceScoredThisTurn);

    /// <summary>VP-BAD-PUBLICITY: the Corp has at least the given bad publicity.</summary>
    public static BehaviorCondition BadPublicityAtLeast(int minimum) =>
        new(BehaviorConditionKind.BadPublicityAtLeast, value: minimum);

    /// <summary>VP-ENCOUNTER-SUBTYPE: the Runner is encountering ICE with the subtype ("the barrier you are encountering").</summary>
    public static BehaviorCondition EncounteringSubtype(string subtype) =>
        new(BehaviorConditionKind.Encountering, fact: subtype);

    /// <summary>A player (by default the behavior controller) has at least the value in clicks.</summary>
    public static BehaviorCondition ClicksAtLeast(int threshold, PlayerSide? side) =>
        new(BehaviorConditionKind.ClicksRemainingAtLeast, value: threshold, subjectSide: side);

    public static BehaviorCondition SourceIn(BehaviorZone zone) =>
        new(BehaviorConditionKind.SourceInZone, fact: zone.ToString());

    public static BehaviorCondition SourceNotIn(BehaviorZone zone) =>
        new(BehaviorConditionKind.SourceNotInZone, fact: zone.ToString());

    public static BehaviorCondition SourceHostsAtLeast(int minimum) =>
        new(BehaviorConditionKind.SourceHostsAtLeast, value: minimum);

    public static BehaviorCondition ChosenTrashCostIs(string choiceKey, int cost) =>
        new(BehaviorConditionKind.ChosenTrashCostIs, fact: choiceKey, value: cost);

    public static BehaviorCondition RezzedSubtypeInAttackedServer(string subtype) =>
        new(BehaviorConditionKind.RezzedSubtypeInAttackedServer, fact: subtype);

    public static BehaviorCondition RezzedInstalledCopy(CardDefinitionId definitionId) =>
        new(BehaviorConditionKind.RezzedInstalledCopy, fact: definitionId.Value);

    public static BehaviorCondition CreditsAtLeast(PlayerSide side, int minimum) =>
        new(BehaviorConditionKind.CreditsAtLeast, value: minimum, subjectSide: side);

    public static BehaviorCondition ZoneCountAtLeast(BehaviorZone zone, int minimum) =>
        new(BehaviorConditionKind.ZoneCountAtLeast, fact: zone.ToString(), value: minimum);

    public static BehaviorCondition Chose(string choiceKey) =>
        new(BehaviorConditionKind.ChoseCards, fact: choiceKey);

    public static BehaviorCondition PlayedFrom(BehaviorZone zone) =>
        new(BehaviorConditionKind.PlayedFromZone, fact: zone.ToString());

    public static BehaviorCondition SourceAdvancementAtLeast(int threshold) =>
        new(BehaviorConditionKind.SourceAdvancementAtLeast, value: threshold);

    public static BehaviorCondition ZoneCountAtMost(BehaviorZone zone, int maximum) =>
        new(BehaviorConditionKind.ZoneCountAtMost, fact: zone.ToString(), value: maximum);

    public BehaviorConditionKind Kind { get; }

    public string? Fact { get; }

    /// <summary>
    /// Optional player whose standard tag state is tested by a Tagged or
    /// NotTagged condition. When omitted, evaluation remains relative to the
    /// behavior controller for compatibility with the original contract.
    /// </summary>
    public PlayerSide? SubjectSide { get; }

    public int? Value { get; }

    /// <summary>Creates a condition requiring the standard tag state to be present.</summary>
    public static BehaviorCondition Tagged(PlayerSide? subjectSide = null) =>
        new(BehaviorConditionKind.Tagged, subjectSide: subjectSide);

    /// <summary>Creates a condition requiring the standard tag state to be absent.</summary>
    public static BehaviorCondition NotTagged(PlayerSide? subjectSide = null) =>
        new(BehaviorConditionKind.NotTagged, subjectSide: subjectSide);

    /// <summary>Creates a condition over the acting player's remaining clicks.</summary>
    public static BehaviorCondition ClicksRemainingAtLeast(int threshold) =>
        new(BehaviorConditionKind.ClicksRemainingAtLeast, value: threshold);

    /// <summary>Creates an installed-this-turn fact condition.</summary>
    public static BehaviorCondition InstalledThisTurn() =>
        new(BehaviorConditionKind.InstalledThisTurn);

    /// <summary>Creates a successful-run-this-turn fact condition.</summary>
    public static BehaviorCondition SuccessfulRunThisTurn() =>
        new(BehaviorConditionKind.SuccessfulRunThisTurn);

    /// <summary>Creates a condition requiring the latest search to find a card.</summary>
    public static BehaviorCondition FoundCard() =>
        new(BehaviorConditionKind.FoundCard);
}

public enum BehaviorConditionKind
{
    FactEquals,
    CounterAtLeast,
    Installed,
    Encountering,
    SuccessfulRun,
    SourceController,
    Tagged,
    NotTagged,
    ClicksRemainingAtLeast,
    InstalledThisTurn,
    SuccessfulRunThisTurn,
    FoundCard,
    SuccessfulRunDuringRunnerLastTurn,

    /// <summary>EL-RUN-EVENT-ACTIVE: the current run was started by a run event.</summary>
    RunEventActive,

    /// <summary>EL-THREAT: the threat level (the higher score) is at least the value.</summary>
    ThreatLevelAtLeast,

    /// <summary>EL-TAG-COUNT: the Runner has at least the value in tags.</summary>
    RunnerTagsAtLeast,

    /// <summary>
    /// A zone of the source controller (named by the fact, a
    /// <see cref="BehaviorZone"/> name) holds at most the value in cards.
    /// </summary>
    ZoneCountAtMost,

    /// <summary>The Runner's unused memory is at most the value (0: memory is full).</summary>
    UnusedMemoryAtMost,

    /// <summary>The Runner's unused memory is at least the value.</summary>
    UnusedMemoryAtLeast,

    /// <summary>The source controller has played an operation or event this turn.</summary>
    PlayedThisTurn,

    /// <summary>EL-ACTION-NOT-FINISHED: the active player has not finished an action this turn.</summary>
    NoActionFinishedThisTurn,

    /// <summary>The behavior's source card hosts at least the value in advancement counters.</summary>
    SourceAdvancementAtLeast,

    /// <summary>
    /// The active turn's count of the typed turn event named by the fact is
    /// exactly the value; a subject side restricts it to that side's turn.
    /// </summary>
    TurnEventCountIs,

    /// <summary>The operation or event resolving was played from the zone the fact names (Petty Cash).</summary>
    PlayedFromZone,

    /// <summary>The behavior's source card is in the zone the fact names (Byte! accessed in R&amp;D).</summary>
    SourceInZone,

    /// <summary>The behavior's source card is not in the zone the fact names.</summary>
    SourceNotInZone,

    /// <summary>A zone of the source controller (named by the fact) holds at least the value in cards.</summary>
    ZoneCountAtLeast,

    /// <summary>The choice the fact names chose at least one card ("if you do", Touch-ups).</summary>
    ChoseCards,

    /// <summary>A run is in progress ("Use this ability only during a run": Proprionegation).</summary>
    RunInProgress,

    /// <summary>The subject side's credit pool holds at least the value ("if the Corp has 15[credit] or more": Fransofia Ward).</summary>
    CreditsAtLeast,

    /// <summary>
    /// A rezzed installed card other than the source has the definition the
    /// fact names (Mycoweb: "a rezzed sentry", "another rezzed code gate").
    /// </summary>
    RezzedInstalledCopy,

    /// <summary>
    /// A rezzed Corp card with the subtype the fact names is in the root of,
    /// or protecting, the attacked server (LEO Construction: a bioroid).
    /// </summary>
    RezzedSubtypeInAttackedServer,

    /// <summary>The source hosts at least the value in cards faceup (Detente: "add 2 hosted cards to HQ").</summary>
    SourceHostsAtLeast,

    /// <summary>
    /// The card bound to the choice key the fact names has an access trash
    /// cost equal to the value (Detente's access: the trash option per cost).
    /// </summary>
    ChosenTrashCostIs,

    /// <summary>VP-SCORING-FACTS: the active turn's count of a typed event is at least the value (Hype Machine, Myōshu).</summary>
    TurnEventCountAtLeast,

    /// <summary>VP-SCORING-FACTS: the source agenda was scored this turn (Witch Hunt).</summary>
    SourceScoredThisTurn,

    /// <summary>VP-RUN-FACTS: this turn the Runner made a successful run on HQ, on R&amp;D and on Archives (Chain Reaction).</summary>
    SuccessfulRunOnEveryCentralThisTurn,

    /// <summary>VP-BAD-PUBLICITY: the Corp has at least the value in bad publicity (Luana Campos, Scapegoat).</summary>
    BadPublicityAtLeast,

    /// <summary>VP-ATTACKED-SERVER: a run is in progress against the source's server (Event Horizon).</summary>
    RunAgainstSourceServer,

    /// <summary>
    /// VP-ATTACKED-SERVER: a run is in progress against a server other than the source's (The Red Room, Sacrifice Zone
    /// Expansion).
    /// </summary>
    RunAgainstAnotherServer,
}

/// <summary>A typed target selector; IDs are resolved by Core at runtime.</summary>
public sealed record BehaviorTarget
{
    [JsonConstructor]
    public BehaviorTarget(
        BehaviorTargetKind kind,
        string? selector = null,
        BehaviorZone? zone = null,
        BehaviorCardFilter? filter = null,
        bool reveal = false)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (kind == BehaviorTargetKind.NamedCard && string.IsNullOrWhiteSpace(selector))
        {
            throw new ArgumentException("Named-card targets require a selector.", nameof(selector));
        }

        if (kind is BehaviorTargetKind.ZoneCard or BehaviorTargetKind.ZoneCards)
        {
            if (zone is null)
            {
                // Preserve the contract's historical validation category so
                // callers can distinguish an unbound target from a null
                // runtime object without depending on ArgumentNullException.
                throw new ArgumentException("Zone-card targets require a typed zone.", nameof(zone));
            }

            if (!Enum.IsDefined(zone.Value))
            {
                throw new ArgumentOutOfRangeException(nameof(zone));
            }
        }
        else if (kind is BehaviorTargetKind.InstalledCard or BehaviorTargetKind.InstalledCardNotThisTurn)
        {
            if (zone is not null || reveal)
            {
                throw new ArgumentException(
                    "Installed-card targets may not carry a zone or reveal flag.",
                    nameof(zone));
            }

            // Installed selectors use owner and printed card class. Hidden
            // definition selectors remain zone-search vocabulary; a subtype
            // may name only public cards: Runner cards, or rezzed Corp cards
            // (EL-INSTALLED-SUBTYPE: Bumi's trojan, LEO's rezzed bioroid).
            if (filter is not null
                && (filter.DefinitionId is not null
                    || filter.Subtype is not null
                        && filter.OwnerSide != PlayerSide.Runner
                        && filter.Rezzed != true))
            {
                throw new ArgumentException(
                    "Installed-card targets may carry only owner-side, card-class and public subtype filters.",
                    nameof(filter));
            }
        }
        else if (kind == BehaviorTargetKind.AccessedCard)
        {
            // EL-ACCESS-FILTER: an access ability may name the kind of card it
            // affects ("the non-agenda card you are accessing"). Like installed
            // targets, only owner-side and card-class filters are allowed.
            if (zone is not null || reveal)
            {
                throw new ArgumentException(
                    "Accessed-card targets may not carry a zone or reveal flag.",
                    nameof(zone));
            }

            if (filter is not null
                && (filter.DefinitionId is not null
                    || filter.Subtype is not null
                    || filter.Rezzed is not null))
            {
                throw new ArgumentException(
                    "Accessed-card targets may carry only owner-side and card-class filters.",
                    nameof(filter));
            }
        }
        else if (zone is not null || filter is not null || reveal)
        {
            throw new ArgumentException(
                "Only zone-card targets may carry a zone, filter, or reveal flag.",
                nameof(zone));
        }

        Kind = kind;
        Selector = selector;
        Zone = zone;
        Filter = filter;
        Reveal = reveal;
    }

    /// <summary>
    /// Creates a target over one or more cards in a typed zone. The optional
    /// filter is data, not an executable predicate; reveal is explicit so a
    /// hidden-zone selection cannot accidentally leak information.
    /// </summary>
    public BehaviorTarget(
        BehaviorTargetKind kind,
        BehaviorZone zone,
        BehaviorCardFilter? filter = null,
        bool reveal = false)
        : this(kind, null, zone, filter, reveal)
    {
        if (kind is not (BehaviorTargetKind.ZoneCard or BehaviorTargetKind.ZoneCards))
        {
            throw new ArgumentException(
                "A zone descriptor target must use a zone-card target kind.",
                nameof(kind));
        }
    }

    public BehaviorTargetKind Kind { get; }

    public string? Selector { get; }

    /// <summary>The selector of an installed-card target that excludes the behavior's own source ("your other installed cards").</summary>
    public const string OtherSelector = "other";

    /// <summary>Whether this installed-card target excludes the behavior's source.</summary>
    [JsonIgnore]
    public bool IsOther => string.Equals(Selector, OtherSelector, StringComparison.Ordinal);

    /// <summary>
    /// EL-COPY-OF-ACCESSED: an installed-card target over only copies (same
    /// definition) of a faceup card in Archives (Charm Offensive, after a
    /// successful Archives run accessed them all).
    /// </summary>
    public const string CopyOfArchivesSelector = "copy-of-archives";

    /// <summary>
    /// EL-ATTACKED-SERVER: an installed-card target over only cards in the root
    /// of, or protecting, the attacked server (LEO Construction).
    /// </summary>
    public const string AttackedServerSelector = "attacked-server";

    [JsonIgnore]
    public bool IsInAttackedServer => string.Equals(Selector, AttackedServerSelector, StringComparison.Ordinal);

    /// <summary>EL-MOD-HOST: an installed-card target over only cards without the AI subtype.</summary>
    public const string NotAiSelector = "not-ai";

    [JsonIgnore]
    public bool IsNotAi => string.Equals(Selector, NotAiSelector, StringComparison.Ordinal);

    [JsonIgnore]
    public bool IsCopyOfArchives => string.Equals(Selector, CopyOfArchivesSelector, StringComparison.Ordinal);

    /// <summary>EL-HOSTED-FACEUP: a zone target over only the cards hosted on the behavior's source.</summary>
    public const string HostedOnSourceSelector = "hosted-on-source";

    [JsonIgnore]
    public bool IsHostedOnSource => string.Equals(Selector, HostedOnSourceSelector, StringComparison.Ordinal);

    /// <summary>The cards of a hosted-faceup zone hosted on the behavior's source.</summary>
    public static BehaviorTarget HostedOnSource(PlayerSide owner, BehaviorCardType cardType = BehaviorCardType.Any) =>
        new(BehaviorTargetKind.ZoneCards, HostedOnSourceSelector, BehaviorZone.HostedFaceUp,
            new BehaviorCardFilter(cardType, ownerSide: owner));

    /// <summary>EL-HAND-SIZE-DISCARD: a zone target over only the cards discarded this turn to reach maximum hand size.</summary>
    public const string HandSizeDiscardsSelector = "hand-size-discards";

    [JsonIgnore]
    public bool IsHandSizeDiscards => string.Equals(Selector, HandSizeDiscardsSelector, StringComparison.Ordinal);

    private const string TopSelectorPrefix = "top:";

    /// <summary>EL-LOOK-TOP: a zone target over only the top cards of a deck ("look at the top 3 cards of R&amp;D").</summary>
    public static BehaviorTarget TopOfZone(BehaviorZone zone, int count, BehaviorCardFilter? filter = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return new BehaviorTarget(BehaviorTargetKind.ZoneCards, TopSelectorPrefix + count.ToString(System.Globalization.CultureInfo.InvariantCulture), zone, filter);
    }

    /// <summary>How many top cards of its zone a top-of-zone target covers, or null for the whole zone.</summary>
    [JsonIgnore]
    public int? TopCount => Selector is { } selector
        && selector.StartsWith(TopSelectorPrefix, StringComparison.Ordinal)
        && int.TryParse(selector.AsSpan(TopSelectorPrefix.Length), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out int count)
        && count > 0
            ? count
            : null;

    /// <summary>The zone used by a <see cref="BehaviorTargetKind.ZoneCard"/> target.</summary>
    public BehaviorZone? Zone { get; }

    /// <summary>Optional typed filter for a zone target.</summary>
    public BehaviorCardFilter? Filter { get; }

    /// <summary>Whether selecting the target explicitly reveals it.</summary>
    public bool Reveal { get; }

    public static BehaviorTarget CardInZone(
        BehaviorZone zone,
        BehaviorCardFilter? filter = null,
        bool reveal = false) =>
        new(BehaviorTargetKind.ZoneCard, zone, filter, reveal);

    public static BehaviorTarget CardsInZone(
        BehaviorZone zone,
        BehaviorCardFilter? filter = null,
        bool reveal = false) =>
        new(BehaviorTargetKind.ZoneCards, zone, filter, reveal);

    /// <summary>
    /// Creates the typed installed-card target used by effects such as
    /// Seamless Launch. Core resolves it against the current turn's
    /// serializable installation facts.
    /// </summary>
    public static BehaviorTarget InstalledCardNotThisTurn(string? selector = null) =>
        new(BehaviorTargetKind.InstalledCardNotThisTurn, selector);

    /// <summary>
    /// Creates an installed-card target constrained to one owning side. The
    /// side filter is intentionally explicit so a Corp behavior cannot
    /// accidentally target a Runner card (or vice versa).
    /// </summary>
    public static BehaviorTarget InstalledCardNotThisTurn(PlayerSide ownerSide, string? selector = null) =>
        new(
            BehaviorTargetKind.InstalledCardNotThisTurn,
            selector,
            filter: new BehaviorCardFilter(ownerSide: ownerSide));
}

public enum BehaviorTargetKind
{
    Self,
    Controller,
    Opponent,
    InstalledCard,
    InstalledCardNotThisTurn,
    NamedCard,
    Server,
    AccessedCard,
    ChosenCard,
    ZoneCard,
    ZoneCards,
}

/// <summary>
/// Immutable declaration for a behavior-owned click that starts a normal
/// Runner run.  The declaration is intentionally structural: it names the
/// server kind and, optionally, a source counter whose value is snapshotted
/// into the run's breach-access modifiers at initiation.
/// </summary>
public sealed record BehaviorRunOperand
{
    [JsonConstructor]
    public BehaviorRunOperand(
        ServerKind serverKind,
        bool requireNotRunThisTurn = false,
        string? sourceCounterName = null,
        int? breachAccessDeltaPerCounter = null,
        string? breachAccessReasonKey = null)
    {
        if (!Enum.IsDefined(serverKind)
            || serverKind is not (ServerKind.Headquarters or ServerKind.RAndD or ServerKind.Archives))
        {
            throw new ArgumentOutOfRangeException(
                nameof(serverKind),
                "A click-run operand must name an existing central server kind.");
        }

        if (sourceCounterName is null
            && (breachAccessDeltaPerCounter is not null || breachAccessReasonKey is not null))
        {
            throw new ArgumentException(
                "A breach-access scale and reason require a source counter name.",
                nameof(sourceCounterName));
        }

        if (sourceCounterName is not null && !StableIdentifier.TryValidate(sourceCounterName))
        {
            throw new ArgumentException(
                "A run source counter name must be a stable identifier.",
                nameof(sourceCounterName));
        }

        if (breachAccessDeltaPerCounter is null && breachAccessReasonKey is not null)
        {
            throw new ArgumentException(
                "A breach-access reason requires a positive per-counter delta.",
                nameof(breachAccessReasonKey));
        }

        if (sourceCounterName is not null && breachAccessDeltaPerCounter is null)
        {
            throw new ArgumentException(
                "A source counter name requires a breach-access scale and reason.",
                nameof(breachAccessDeltaPerCounter));
        }

        if (breachAccessDeltaPerCounter is not null
            && breachAccessDeltaPerCounter <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(breachAccessDeltaPerCounter),
                "A breach-access scale must be positive.");
        }

        if (breachAccessDeltaPerCounter is not null
            && !StableIdentifier.TryValidate(breachAccessReasonKey))
        {
            throw new ArgumentException(
                "A breach-access scale requires a stable reason key.",
                nameof(breachAccessReasonKey));
        }

        ServerKind = serverKind;
        RequireNotRunThisTurn = requireNotRunThisTurn;
        SourceCounterName = sourceCounterName;
        BreachAccessDeltaPerCounter = breachAccessDeltaPerCounter;
        BreachAccessReasonKey = breachAccessReasonKey;
    }

    public ServerKind ServerKind { get; }

    public bool RequireNotRunThisTurn { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceCounterName { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BreachAccessDeltaPerCounter { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BreachAccessReasonKey { get; }
}

/// <summary>One inert, typed instruction in a compiled behavior program.</summary>
public sealed record BehaviorInstruction
{
    /// <summary>VP-CARD-AS-AGENDA: the argument of a card added as an agenda that can't be forfeited (Word on the Street).</summary>
    public const string CannotForfeitArgument = "cannot-forfeit";

    [JsonConstructor]
    public BehaviorInstruction(
        BehaviorInstructionKind kind,
        int amount = 0,
        string? argument = null,
        BehaviorTarget? target = null,
        BehaviorZone? sourceZone = null,
        BehaviorZone? destinationZone = null,
        BehaviorCardFilter? filter = null,
        BehaviorCondition? condition = null,
        BehaviorChoiceSpec? choice = null,
        BehaviorBranchSpec? branch = null,
        bool optional = false,
        HostedCreditSpendRestriction? spendRestriction = null,
        BehaviorRunOperand? runOperand = null,
        BehaviorAmountOperand? amountOperand = null,
        BehaviorPlayerSelector? recipient = null,
        DamageKind? damageKind = null,
        BehaviorDamageAmountOperand? damageAmountOperand = null,
        string? recordAs = null,
        BehaviorInstallCostRule? costRule = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        // VP-ALLOTTED-CLICKS: "-1 allotted click for their next turn" (Caveat Emptor) is a negative grant.
        if (kind is not (BehaviorInstructionKind.ModifyEncounteredIceStrength
            or BehaviorInstructionKind.ModifySourceStrength
            or BehaviorInstructionKind.AddSelfToScoreAreaAsAgenda
            or BehaviorInstructionKind.GainAllottedClicksNextTurn))
        {
            ArgumentOutOfRangeException.ThrowIfNegative(amount);
        }

        if (kind == BehaviorInstructionKind.AddSelfToScoreAreaAsAgenda && argument is not (null or CannotForfeitArgument))
        {
            throw new ArgumentException("A card added as an agenda takes only the cannot-forfeit argument.", nameof(argument));
        }

        Kind = kind;
        Amount = amount;
        Argument = argument;
        Target = target;
        SourceZone = sourceZone;
        DestinationZone = destinationZone;
        Filter = filter;
        Condition = condition;
        Choice = choice;
        Branch = branch;
        Optional = optional;
        SpendRestriction = NormalizeSpendRestriction(kind, spendRestriction);
        RunOperand = runOperand;
        AmountOperand = amountOperand;
        DamageKind = damageKind;
        DamageAmountOperand = damageAmountOperand;
        Recipient = recipient == BehaviorPlayerSelector.Controller
            ? null
            : recipient;
        RecordAs = recordAs;
        CostRule = costRule;
        ValidateOperands();
    }

    public BehaviorInstructionKind Kind { get; }

    public int Amount { get; }

    public string? Argument { get; }

    /// <summary>Optional typed card/player target used by the instruction.</summary>
    public BehaviorTarget? Target { get; }

    /// <summary>Optional typed source zone for movement/search instructions.</summary>
    public BehaviorZone? SourceZone { get; }

    /// <summary>Optional typed destination zone for movement instructions.</summary>
    public BehaviorZone? DestinationZone { get; }

    /// <summary>Optional typed filter for hidden-zone search.</summary>
    public BehaviorCardFilter? Filter { get; }

    /// <summary>Optional condition for a conditional instruction such as click loss.</summary>
    public BehaviorCondition? Condition { get; }

    /// <summary>Choice declaration consumed by a <see cref="BehaviorInstructionKind.Choose"/>.</summary>
    public BehaviorChoiceSpec? Choice { get; }

    /// <summary>Branch declaration consumed by a <see cref="BehaviorInstructionKind.Branch"/>.</summary>
    public BehaviorBranchSpec? Branch { get; }

    /// <summary>Whether this instruction opens an optional install decision.</summary>
    public bool Optional { get; }

    /// <summary>
    /// Optional restriction for a hosted-credit pool instruction. Loading and
    /// placing credits are card effects by default, while take instructions
    /// deliberately do not carry a spend restriction.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HostedCreditSpendRestriction? SpendRestriction { get; }

    /// <summary>
    /// Optional typed run declaration. It is present only on a final
    /// <see cref="BehaviorInstructionKind.StartRun"/> instruction.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BehaviorRunOperand? RunOperand { get; }

    /// <summary>
    /// Optional typed dynamic amount for a credit-gain instruction. A
    /// non-null operand is mutually exclusive with the instruction's static
    /// <see cref="Amount"/> and is re-evaluated from authoritative counters
    /// at execution time.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BehaviorAmountOperand? AmountOperand { get; }

    /// <summary>
    /// Optional player recipient for AddTags. Omitted retains the historical
    /// controller recipient; explicit ActingPlayer/Runner/Corp values make
    /// the subject choice structural and save/load stable.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BehaviorPlayerSelector? Recipient { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DamageKind? DamageKind { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BehaviorDamageAmountOperand? DamageAmountOperand { get; }

    /// <summary>
    /// EL-AMOUNT: a key under which the instruction records the amount it
    /// actually applied (credits lost, tags removed), for a later
    /// <see cref="BehaviorAmountKind.Recorded"/> amount.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RecordAs { get; }

    /// <summary>
    /// EL-BEHAVIOR-INSTALL: how a zone install pays. Set only on an
    /// InstallCard instruction whose target is a zone of cards; its amount is
    /// then a credit discount.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BehaviorInstallCostRule? CostRule { get; }

    /// <summary>Whether this is an install from a zone chosen in its own decision.</summary>
    [JsonIgnore]
    public bool IsZoneInstall =>
        Kind == BehaviorInstructionKind.InstallCard
        && Target is { Kind: BehaviorTargetKind.ZoneCard or BehaviorTargetKind.ZoneCards };

    private void ValidateOperands()
    {
        if (CostRule is { } rule
            && (!Enum.IsDefined(rule) || !IsZoneInstall))
        {
            throw new ArgumentException(
                "Only an InstallCard instruction over a zone of cards may declare a cost rule.",
                nameof(CostRule));
        }

        if (RecordAs is not null
            && (!StableIdentifier.TryValidate(RecordAs)
                || Kind is not (BehaviorInstructionKind.LoseCredits or BehaviorInstructionKind.RemoveTags)))
        {
            throw new ArgumentException(
                "Only credit-loss and tag-removal instructions may record a stable amount key.",
                nameof(RecordAs));
        }

        if (DamageKind is { } damageKind && !Enum.IsDefined(damageKind))
        {
            throw new ArgumentOutOfRangeException(nameof(DamageKind));
        }

        if (Kind == BehaviorInstructionKind.DealDamage)
        {
            if (DamageKind is null)
            {
                throw new ArgumentException("Damage instructions require a typed damage kind.", nameof(DamageKind));
            }
            if (DamageAmountOperand is not null && Amount != 0)
            {
                throw new ArgumentException("Damage uses either a static or dynamic amount.", nameof(Amount));
            }
        }
        else if (DamageKind is not null || DamageAmountOperand is not null)
        {
            throw new ArgumentException("Damage operands require a damage instruction.", nameof(DamageKind));
        }

        if (Recipient is { } recipient && !Enum.IsDefined(recipient))
        {
            throw new ArgumentOutOfRangeException(nameof(Recipient));
        }

        // A damage instruction may name the Runner as recipient: the Runner suffers it, so the Runner is responsible
        // for it, not the Corp (rules 10.4.1; Semak-samun).
        if (Recipient is not null
            && Kind is not (BehaviorInstructionKind.AddTags
                or BehaviorInstructionKind.GainCredits
                or BehaviorInstructionKind.LoseCredits
                or BehaviorInstructionKind.LoseClicks
                or BehaviorInstructionKind.GainAllottedClicksNextTurn)
            && !(Kind == BehaviorInstructionKind.DealDamage && Recipient == BehaviorPlayerSelector.Runner))
        {
            throw new ArgumentException(
                "A player recipient is valid only for tag and credit instructions, and the Runner for damage it suffers.",
                nameof(Recipient));
        }

        if (SpendRestriction is { } spendRestriction && !Enum.IsDefined(spendRestriction))
        {
            throw new ArgumentOutOfRangeException(nameof(SpendRestriction));
        }

        if (Kind == BehaviorInstructionKind.StartRun)
        {
            if (RunOperand is null)
            {
                throw new ArgumentNullException(
                    nameof(RunOperand),
                    "StartRun instructions require a typed run operand.");
            }

            if (Amount != 0 || Argument is not null)
            {
                throw new ArgumentException(
                    "StartRun instructions carry only their typed run operand.",
                    nameof(Amount));
            }
        }

        if (Kind is BehaviorInstructionKind.ModifyEncounteredIceStrength
            or BehaviorInstructionKind.ModifySourceStrength)
        {
            if (Amount == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(Amount),
                    "Encountered-ICE strength modifiers require a nonzero amount.");
            }

            if (!StableIdentifier.TryValidate(Argument))
            {
                throw new ArgumentException(
                    "Encountered-ICE strength modifiers require a stable reason key.",
                    nameof(Argument));
            }

            if (Target is not null
                || SourceZone is not null
                || DestinationZone is not null
                || Filter is not null
                || Condition is not null
                || Choice is not null
                || Branch is not null
                || Optional
                || SpendRestriction is not null
                || RunOperand is not null
                || AmountOperand is not null)
            {
                throw new ArgumentException(
                    "Encountered-ICE strength modifiers carry only a signed amount and reason key.",
                    nameof(Argument));
            }

            return;
        }
        if (RunOperand is not null && Kind != BehaviorInstructionKind.StartRun)
        {
            throw new ArgumentException(
                "A typed run operand is valid only for StartRun instructions.",
                nameof(RunOperand));
        }

        if (AmountOperand is not null
            && Kind is not (BehaviorInstructionKind.GainCredits
                or BehaviorInstructionKind.LoseCredits
                or BehaviorInstructionKind.Draw
                or BehaviorInstructionKind.RemoveTags
                or BehaviorInstructionKind.AddCounter
                or BehaviorInstructionKind.AdvanceCard
                or BehaviorInstructionKind.AddTags))
        {
            throw new ArgumentException(
                "A dynamic amount operand is valid only for credit, draw, counter, advancement and tag instructions.",
                nameof(AmountOperand));
        }

        if (AmountOperand is not null && Amount != 0)
        {
            throw new ArgumentException(
                "An instruction must use either a static amount or a dynamic amount operand, not both.",
                nameof(Amount));
        }

        bool hostedCreditInstruction = Kind is BehaviorInstructionKind.LoadHostedCredits
            or BehaviorInstructionKind.PlaceHostedCredits
            or BehaviorInstructionKind.TakeHostedCredits
            or BehaviorInstructionKind.TakeUpToHostedCredits
            or BehaviorInstructionKind.TakeAllHostedCredits;
        if (!hostedCreditInstruction && SpendRestriction is not null)
        {
            throw new ArgumentException(
                "A hosted-credit spend restriction is valid only for hosted-credit instructions.",
                nameof(SpendRestriction));
        }

        switch (Kind)
        {
            case BehaviorInstructionKind.LoadHostedCredits:
            case BehaviorInstructionKind.PlaceHostedCredits:
            case BehaviorInstructionKind.TakeHostedCredits:
            case BehaviorInstructionKind.TakeUpToHostedCredits:
                if (Amount <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(Amount),
                        "Hosted-credit load, place, and take instructions require a positive amount.");
                }

                if (Kind is (BehaviorInstructionKind.TakeHostedCredits
                    or BehaviorInstructionKind.TakeUpToHostedCredits)
                    && SpendRestriction is not null)
                {
                    throw new ArgumentException(
                        "Hosted-credit take instructions do not carry a spend restriction.",
                        nameof(SpendRestriction));
                }

                break;
            case BehaviorInstructionKind.TakeAllHostedCredits:
                if (Amount != 0)
                {
                    throw new ArgumentException(
                        "TakeAllHostedCredits instructions must not carry an amount.",
                        nameof(Amount));
                }

                if (SpendRestriction is not null)
                {
                    throw new ArgumentException(
                        "TakeAllHostedCredits instructions do not carry a spend restriction.",
                        nameof(SpendRestriction));
                }

                break;
        }

        if (SourceZone is { } sourceZone && !Enum.IsDefined(sourceZone))
        {
            throw new ArgumentOutOfRangeException(nameof(SourceZone));
        }

        if (DestinationZone is { } destinationZone && !Enum.IsDefined(destinationZone))
        {
            throw new ArgumentOutOfRangeException(nameof(DestinationZone));
        }

        if ((Kind is BehaviorInstructionKind.GainCredits
                or BehaviorInstructionKind.Draw
                or BehaviorInstructionKind.LoseClicks
                or BehaviorInstructionKind.AdvanceCard
                or BehaviorInstructionKind.RemoveTags
                or BehaviorInstructionKind.TakeBadPublicity
                or BehaviorInstructionKind.RemoveBadPublicity
                or BehaviorInstructionKind.RemoveAdvancementCounters)
            && AmountOperand is null
            && Amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Amount),
                "This executable numeric instruction requires a positive amount.");
        }

        if (Kind is BehaviorInstructionKind.AddCounter or BehaviorInstructionKind.RemoveCounter)
        {
            if (Amount <= 0 && AmountOperand is null)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(Amount),
                    "A counter instruction requires a positive amount.");
            }

            if (Target is not null
                && Target.Kind is not (BehaviorTargetKind.Self or BehaviorTargetKind.ChosenCard))
            {
                throw new ArgumentException(
                    "Counter instructions may target Self or an engine-proven ChosenCard binding.",
                    nameof(Target));
            }

            if (Target is { Kind: BehaviorTargetKind.ChosenCard }
                && !StableIdentifier.TryValidate(Target.Selector))
            {
                throw new ArgumentException(
                    "A chosen counter target requires a stable binding selector.",
                    nameof(Target));
            }
        }

        if (Kind == BehaviorInstructionKind.Search)
        {
            RequireZone(SourceZone, nameof(SourceZone),
                BehaviorZone.Deck,
                BehaviorZone.Headquarters,
                BehaviorZone.Archives,
                BehaviorZone.Stack);
            if (Filter is null)
            {
                throw new ArgumentNullException(nameof(Filter), "Search instructions require a typed card filter.");
            }

            if (Target is null)
            {
                throw new ArgumentNullException(
                    nameof(Target),
                    "Search instructions require an explicit candidate target descriptor.");
            }

            if (Target.Kind is not (BehaviorTargetKind.ZoneCard or BehaviorTargetKind.ZoneCards))
            {
                throw new ArgumentException(
                    "Search instructions must target a zone-card candidate descriptor.",
                    nameof(Target));
            }

            if (Target.Zone != SourceZone)
            {
                throw new ArgumentException(
                    "Search target and source must name the same zone.",
                    nameof(Target));
            }

            if (SourceZone is BehaviorZone.Deck or BehaviorZone.Stack && Target.Reveal)
            {
                throw new ArgumentException(
                    "Deck and Stack searches must keep the selection hidden; reveal with an explicit Reveal instruction after any shuffle.",
                    nameof(Target));
            }

            if (!BehaviorContractStructure.AreEqual(Target.Filter, Filter))
            {
                throw new ArgumentException(
                    "Search target and instruction filters must match structurally.",
                    nameof(Filter));
            }

            if (DestinationZone is not null)
            {
                throw new ArgumentException(
                    "Search instructions do not carry a destination zone.",
                    nameof(DestinationZone));
            }
        }

        if (Kind is BehaviorInstructionKind.Reveal
            or BehaviorInstructionKind.ShuffleIntoDeck
            or BehaviorInstructionKind.AddToHand
            or BehaviorInstructionKind.TrashCard
            or BehaviorInstructionKind.InstallCard
            or BehaviorInstructionKind.AdvanceCard
            or BehaviorInstructionKind.RemoveAdvancementCounters
            or BehaviorInstructionKind.SwapInstalledIce)
        {
            if (Target is null)
            {
                throw new ArgumentNullException(nameof(Target), $"{Kind} instructions require a typed target.");
            }
        }

        switch (Kind)
        {
            case BehaviorInstructionKind.Reveal:
                break;
            case BehaviorInstructionKind.ShuffleIntoDeck:
                if (SourceZone is { } shuffleSource)
                {
                    // VP.3: the rig too, for an installed Runner card shuffled into the stack (Scapegoat).
                    RequireZone(
                        shuffleSource,
                        nameof(SourceZone),
                        BehaviorZone.Deck,
                        BehaviorZone.Headquarters,
                        BehaviorZone.Archives,
                        BehaviorZone.Stack,
                        BehaviorZone.Grip,
                        BehaviorZone.Rig);
                }

                RequireDestination(nameof(DestinationZone), BehaviorZone.Deck, BehaviorZone.Stack);
                break;
            case BehaviorInstructionKind.AddToHand:
                if (SourceZone is { } addSource)
                {
                    RequireZone(
                        addSource,
                        nameof(SourceZone),
                        BehaviorZone.Deck,
                        BehaviorZone.Headquarters,
                        BehaviorZone.Archives,
                        BehaviorZone.Stack,
                        BehaviorZone.Heap,
                        BehaviorZone.HostedFaceUp);
                }

                RequireDestination(nameof(DestinationZone), BehaviorZone.Headquarters, BehaviorZone.Grip);
                break;
            case BehaviorInstructionKind.TrashCard:
                break;
            case BehaviorInstructionKind.InstallCard:
                if (SourceZone is { } installSource)
                {
                    RequireZone(
                        installSource,
                        nameof(SourceZone),
                        BehaviorZone.Headquarters,
                        BehaviorZone.Grip,
                        BehaviorZone.Archives,
                        BehaviorZone.Stack,
                        BehaviorZone.Deck,
                        BehaviorZone.Heap,
                        BehaviorZone.ScoreArea,
                        BehaviorZone.HostedFaceUp);
                }

                if (IsZoneInstall)
                {
                    // A zone install chooses its card and destination in one
                    // decision; a destination zone only narrows it (ServerRoot:
                    // a remote server's root; ServerIce: protecting a server).
                    if (Target!.Zone != SourceZone)
                    {
                        throw new ArgumentException("A zone install's target and source must name the same zone.", nameof(Target));
                    }

                    if (DestinationZone is { } narrowed
                        && narrowed is not (BehaviorZone.ServerRoot or BehaviorZone.ServerIce or BehaviorZone.Rig))
                    {
                        throw new ArgumentException("A zone install may narrow its destination to a server root, server ice or the rig.", nameof(DestinationZone));
                    }

                    break;
                }

                RequireDestination(
                    nameof(DestinationZone),
                    BehaviorZone.Rig,
                    BehaviorZone.ServerIce,
                    BehaviorZone.ServerRoot,
                    BehaviorZone.PlayArea);
                break;
            case BehaviorInstructionKind.AdvanceCard:
                if (Target?.Kind is not (BehaviorTargetKind.InstalledCard or BehaviorTargetKind.ChosenCard))
                {
                    throw new ArgumentException(
                        "Advance instructions must target an installed card or a chosen card binding.",
                        nameof(Target));
                }

                break;
        }

        if (Kind == BehaviorInstructionKind.MoveCard
            && (Target is not null || DestinationZone is not null || SourceZone is not null)
            && (Target is null || DestinationZone is null))
        {
            throw new ArgumentException(
                "Typed move instructions require both a target and destination zone.",
                nameof(Target));
        }

        if (Kind == BehaviorInstructionKind.Choose && Choice is null)
        {
            throw new ArgumentNullException(
                nameof(Choice),
                "Choose instructions require a typed choice declaration.");
        }

        if (Choice is not null && Kind != BehaviorInstructionKind.Choose)
        {
            throw new ArgumentException(
                "A choice declaration is valid only on a choose instruction.",
                nameof(Choice));
        }

        if (Kind == BehaviorInstructionKind.Branch && Branch is null)
        {
            throw new ArgumentNullException(
                nameof(Branch),
                "Branch instructions require a typed branch declaration.");
        }

        if (Branch is not null && Kind != BehaviorInstructionKind.Branch)
        {
            throw new ArgumentException(
                "A branch declaration is valid only on a branch instruction.",
                nameof(Branch));
        }

        if (Optional && Kind != BehaviorInstructionKind.InstallCard)
        {
            throw new ArgumentException(
                "Only install instructions may be optional.",
                nameof(Optional));
        }

        if (Kind != BehaviorInstructionKind.LoseClicks && Condition is not null)
        {
            throw new ArgumentException(
                "Instruction conditions are currently supported only for click loss.",
                nameof(Condition));
        }

        if (Kind != BehaviorInstructionKind.Search && Filter is not null)
        {
            throw new ArgumentException(
                "Typed filters are currently supported only for search instructions.",
                nameof(Filter));
        }

        if (Kind != BehaviorInstructionKind.MoveCard
            && Kind != BehaviorInstructionKind.Search
            && Kind != BehaviorInstructionKind.ShuffleIntoDeck
            && Kind != BehaviorInstructionKind.AddToHand
            && Kind != BehaviorInstructionKind.InstallCard
            && Kind != BehaviorInstructionKind.ShuffleSearchedDeck
            && Kind != BehaviorInstructionKind.ShuffleDeck
            && !(Kind == BehaviorInstructionKind.PlayCard && SourceZone is null
                && DestinationZone is null or BehaviorZone.RemovedFromGame)
            && (SourceZone is not null || DestinationZone is not null))
        {
            throw new ArgumentException(
                "Source and destination zones are not valid for this instruction kind.",
                nameof(SourceZone));
        }

        if (Kind == BehaviorInstructionKind.ShuffleDeck
            && (SourceZone is not (BehaviorZone.Deck or BehaviorZone.Stack)
                || DestinationZone is not null || Target is not null || Amount != 0))
            throw new ArgumentException("A deck shuffle names only its Deck or Stack source zone.");

        if (Kind == BehaviorInstructionKind.ShuffleSearchedDeck
            && (SourceZone is not (BehaviorZone.Deck or BehaviorZone.Stack)
                || DestinationZone is not null || Target is not null || Amount != 0))
            throw new ArgumentException("A search shuffle requires only its searched Deck or Stack source zone.");

        if (Kind is not (BehaviorInstructionKind.Reveal
            or BehaviorInstructionKind.Search
            or BehaviorInstructionKind.ShuffleIntoDeck
            or BehaviorInstructionKind.AddToHand
            or BehaviorInstructionKind.AddCounter
            or BehaviorInstructionKind.TrashCard
            or BehaviorInstructionKind.InstallCard
            or BehaviorInstructionKind.AdvanceCard
            or BehaviorInstructionKind.MoveCard
            or BehaviorInstructionKind.SwapInstalledIce
            or BehaviorInstructionKind.RezIgnoringCosts
            or BehaviorInstructionKind.Choose
            or BehaviorInstructionKind.RemoveCounter
            or BehaviorInstructionKind.ForfeitAgenda
            or BehaviorInstructionKind.ProhibitScoringTargetThisTurn
            or BehaviorInstructionKind.Sabotage
            or BehaviorInstructionKind.PlaceOnTopOfDeck
            or BehaviorInstructionKind.PlayCard
            or BehaviorInstructionKind.TurnFaceUp
            or BehaviorInstructionKind.HostFaceUp
            or BehaviorInstructionKind.Derez
            or BehaviorInstructionKind.RezPayingLess
            or BehaviorInstructionKind.MoveSourceToServerOf
            or BehaviorInstructionKind.SwapApproachedIce
            or BehaviorInstructionKind.HostSourceOnTarget
            or BehaviorInstructionKind.ChooseAtRandom
            or BehaviorInstructionKind.StealAgenda)
            && Target is not null)
        {
            throw new ArgumentException(
                "Typed targets are not valid for this instruction kind.",
                nameof(Target));
        }
    }

    private static HostedCreditSpendRestriction? NormalizeSpendRestriction(
        BehaviorInstructionKind kind,
        HostedCreditSpendRestriction? spendRestriction) =>
        kind is BehaviorInstructionKind.LoadHostedCredits or BehaviorInstructionKind.PlaceHostedCredits
            ? spendRestriction ?? HostedCreditSpendRestriction.CardEffectOnly
            : spendRestriction;

    private void RequireDestination(string parameterName, params BehaviorZone[] allowed)
    {
        if (DestinationZone is not { } destination)
        {
            throw new ArgumentNullException(
                parameterName,
                $"{Kind} instructions require a destination zone.");
        }

        if (!allowed.Contains(destination))
        {
            throw new ArgumentException(
                $"Destination zone '{destination}' is not valid for {Kind}.");
        }
    }

    private static void RequireZone(BehaviorZone? zone, string parameterName, params BehaviorZone[] allowed)
    {
        if (zone is null || !allowed.Contains(zone.Value))
        {
            throw new ArgumentException(
                $"Zone '{zone?.ToString() ?? "<none>"}' is not valid for this instruction.",
                parameterName);
        }
    }
}

public enum BehaviorInstructionKind
{
    GainCredits,
    LoseCredits,
    GainClicks,
    Draw,
    MoveCard,
    AddCounter,
    DealDamage,
    AddTags,
    StartRun,
    Choose,
    Branch,
    EndRun,
    TrashSelf,
    NoOp,
    LoseClicks,
    Search,
    Reveal,
    ShuffleIntoDeck,
    AddToHand,
    TrashCard,
    InstallCard,
    AdvanceCard,
    LoadHostedCredits,
    PlaceHostedCredits,
    TakeHostedCredits,
    TakeUpToHostedCredits,
    TakeAllHostedCredits,
    ModifyEncounteredIceStrength,
    GainCreditsPerAccess,
    SwapInstalledIce,
    ProhibitScoringThisTurn,
    RezIgnoringCosts,
    ShuffleSearchedDeck,

    /// <summary>
    /// EL-REMOVE-TAGS: removes up to the amount of the Runner's tags, as an
    /// effect, recording how many were removed.
    /// </summary>
    RemoveTags,

    /// <summary>
    /// EL-SELF-STRENGTH: raises the source icebreaker's own strength for the
    /// current encounter, from an encounter-paid ability ("Hosted virus
    /// counter: +2 strength"). Shares the encounter-strength provenance of
    /// <see cref="ModifyEncounteredIceStrength"/>.
    /// </summary>
    ModifySourceStrength,

    /// <summary>EL-FLIP: turns the source identity to its other face.</summary>
    FlipSelf,

    /// <summary>Removes up to the amount of the named counter (the argument) from the target, by default the source.</summary>
    RemoveCounter,

    /// <summary>EL-FORFEIT: forfeits the chosen agenda from the controller's score area.</summary>
    ForfeitAgenda,

    /// <summary>The recipient gets the amount as extra allotted clicks at the start of their next turn.</summary>
    GainAllottedClicksNextTurn,

    /// <summary>The Corp cannot score the targeted card this turn.</summary>
    ProhibitScoringTargetThisTurn,

    /// <summary>
    /// EL-SABOTAGE: the Corp trashes the amount in cards of their choice from
    /// HQ and/or the top of R&amp;D.
    /// </summary>
    Sabotage,

    /// <summary>Moves the targeted card to the top of its owner's deck.</summary>
    PlaceOnTopOfDeck,

    /// <summary>Shuffles the controller's deck (the source zone), after an install from it.</summary>
    ShuffleDeck,

    /// <summary>EL-RECURRING: refills the source's recurring credits ("when you install / rez this").</summary>
    RefillRecurringCredits,

    /// <summary>EL-FACEUP-AGENDA: turns the target installed card faceup without rezzing it (BANGUN).</summary>
    TurnFaceUp,

    /// <summary>EL-HOSTED-FACEUP: hosts the target cards faceup on the source, not installed.</summary>
    HostFaceUp,

    /// <summary>
    /// Puts every card this behavior has set aside back where it came from now,
    /// rather than when the behavior completes ("reveal all but 1 card in HQ",
    /// then install from HQ: Peer Review).
    /// </summary>
    ReturnSetAside,

    /// <summary>
    /// EL-NESTED-PLAY: plays the targeted operation or event (a chosen card,
    /// or the source itself) from inside this behavior; a RemovedFromGame
    /// destination removes it from the game after it resolves.
    /// </summary>
    PlayCard,

    /// <summary>The Runner accesses the amount in additional cards in the coming breach (the argument is a reason key).</summary>
    AddBreachAccess,

    /// <summary>EL-PURGE: purges every virus counter (Flyswatter), as the basic purge action does.</summary>
    PurgeVirusCounters,

    /// <summary>EL-DEREZ: derezzes the targeted installed Corp cards; they turn facedown (Maglectric Rapid).</summary>
    Derez,

    /// <summary>EL-BYPASS: the Runner bypasses the encountered ICE (Fransofia Ward).</summary>
    BypassEncounteredIce,

    /// <summary>EL-REZ-PAYING-LESS: rezzes the targeted installed ICE paying the amount less (Mycoweb).</summary>
    RezPayingLess,

    /// <summary>EL-MOVE-UPGRADE: moves the installed source upgrade to the root of the targeted card's server (Mercia B4LL4RD).</summary>
    MoveSourceToServerOf,

    /// <summary>EL-MOD-HOST: hosts the source mod on the targeted installed non-AI icebreaker (GAMEDRAGON™ Pro).</summary>
    HostSourceOnTarget,

    /// <summary>
    /// EL-RANDOM: binds the amount of cards chosen at random from the target
    /// to the key the argument names (Detente: a card from HQ at random).
    /// </summary>
    ChooseAtRandom,

    /// <summary>EL-ACCESS-OUTSIDE-RUN: the Runner steals the targeted agenda (Detente's access).</summary>
    StealAgenda,

    /// <summary>
    /// EL-SWAP-APPROACHED-ICE: swaps the ICE being approached with the
    /// targeted ICE from HQ or Archives, which is installed unrezzed in its
    /// position (Mitra Aman).
    /// </summary>
    SwapApproachedIce,

    /// <summary>
    /// EL-MOVE-RUNNER: the Runner moves to the outermost position of the
    /// server of the kind the argument names, which becomes the attacked
    /// server (Proprionegation: Archives).
    /// </summary>
    MoveRunnerToServer,

    /// <summary>VP-BAD-PUBLICITY: the Corp takes the amount in bad publicity (CR 26.03 10.6).</summary>
    TakeBadPublicity,

    /// <summary>VP-BAD-PUBLICITY: the Corp removes up to the amount of its bad publicity.</summary>
    RemoveBadPublicity,

    /// <summary>
    /// VP-CARD-AS-AGENDA: the source card goes to the recipient's score area as an agenda worth the amount, which may be
    /// negative (CR 26.03 10.1.3; Myōshu, Word on the Street). The argument
    /// <see cref="BehaviorInstruction.CannotForfeitArgument"/> marks it as one that can't be forfeited.
    /// </summary>
    AddSelfToScoreAreaAsAgenda,

    /// <summary>
    /// VP-SPEND-ADVANCEMENT: removes up to the amount of advancement counters from each targeted installed card (Sacrifice
    /// Zone Expansion: "remove 1 hosted advancement counter").
    /// </summary>
    RemoveAdvancementCounters,

    // Short aliases keep the contract pleasant for callers while the longer
    // names remain unambiguous in serialized/fingerprint output.
    Shuffle = ShuffleIntoDeck,
    Trash = TrashCard,
    Install = InstallCard,
    Advance = AdvanceCard,
}

/// <summary>Structural player recipient for player-targeted behavior effects.</summary>
public enum BehaviorPlayerSelector
{
    Controller,
    ActingPlayer,
    Corp,
    Runner,
}

/// <summary>A static value modifier with a typed condition and reason key.</summary>
public sealed record BehaviorStaticModifier
{
    [JsonConstructor]
    public BehaviorStaticModifier(
        string valueKey,
        int delta,
        string? reasonKey = null,
        BehaviorStaticModifierKind? kind = null)
    {
        if (string.IsNullOrWhiteSpace(valueKey))
        {
            throw new ArgumentException("A modifier value key is required.", nameof(valueKey));
        }

        if (kind is { } typedKind)
        {
            if (!Enum.IsDefined(typedKind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            string canonicalValueKey = ValueKeyFor(typedKind);
            if (!string.Equals(valueKey, canonicalValueKey, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"A typed static modifier kind '{typedKind}' must use value key '{canonicalValueKey}'.",
                    nameof(valueKey));
            }
        }

        ValueKey = valueKey;
        Delta = delta;
        ReasonKey = reasonKey;
        Kind = kind;
    }

    public BehaviorStaticModifier(string valueKey, int delta, string? reasonKey = null)
        : this(valueKey, delta, reasonKey, kind: null)
    {
    }

    public BehaviorStaticModifier(
        BehaviorStaticModifierKind kind,
        int delta,
        string? reasonKey = null)
        : this(ValueKeyFor(kind), delta, reasonKey, kind)
    {
    }

    public string ValueKey { get; }

    public int Delta { get; }

    public string? ReasonKey { get; }

    /// <summary>The typed modifier kind, when this uses the declarative vocabulary.</summary>
    public BehaviorStaticModifierKind? Kind { get; }

    private static string ValueKeyFor(BehaviorStaticModifierKind kind)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        return kind switch
        {
            BehaviorStaticModifierKind.MemoryUnits => "memory-units",
            BehaviorStaticModifierKind.MaximumHandSize => "maximum-hand-size",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }
}

/// <summary>Whether a trigger is scoped to its source, controller, or either player.</summary>
public enum BehaviorTriggerScope
{
    Self,
    Controller,
    Any,
}

/// <summary>Optional outcome filter for a RunEnded catalog trigger.</summary>
public enum RunOutcomeRequirement
{
    Successful,
    Unsuccessful,
}

/// <summary>
/// Immutable typed trigger declaration. A trigger's instruction list is the
/// complete behavior executed when its declared occurrence is dispatched.
/// </summary>
/// <summary>What a card's declared-use hosted credits pay for.</summary>
public enum BehaviorHostedCreditUseKind
{
    /// <summary>Trash costs of accessed cards (Azimat).</summary>
    TrashCost,

    /// <summary>Install costs of Runner resources with one of the subtypes (Open Market).</summary>
    InstallResource,

    /// <summary>Rez costs of assets in the root of, and ICE protecting, the card's own server (Mahkota Langit Grid).</summary>
    RezInServer,
}

/// <summary>
/// VP-SCORE-STEAL-COST: the card goes to the Corp's score area as an agenda worth the points as an additional cost to score
/// (Word on the Street: -1, can't be forfeited, only for agendas installed this turn).
/// </summary>
public sealed record BehaviorScoreCostAsAgenda(int Points, bool CannotForfeit, bool OnlyAgendasInstalledThisTurn);

/// <summary>One declared use of a card's hosted credits.</summary>
public sealed record BehaviorHostedCreditUse
{
    public BehaviorHostedCreditUse(BehaviorHostedCreditUseKind kind, IReadOnlyList<string>? subtypes = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Kind = kind;
        Subtypes = new ReadOnlyCollection<string>((subtypes ?? []).ToArray());
        if (kind == BehaviorHostedCreditUseKind.InstallResource && Subtypes.Count == 0)
        {
            throw new ArgumentException("A resource-install use names the resource subtypes it pays for.", nameof(subtypes));
        }
    }

    public BehaviorHostedCreditUseKind Kind { get; }

    public IReadOnlyList<string> Subtypes { get; }

    public bool Equals(BehaviorHostedCreditUse? other) =>
        other is not null && Kind == other.Kind && Subtypes.SequenceEqual(other.Subtypes, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Kind, Subtypes.Count);
}

/// <summary>EL-FLIP: the two faces of a double-sided identity.</summary>
public enum BehaviorFace
{
    Front,
    Back,
}

public sealed class BehaviorTrigger : IEquatable<BehaviorTrigger>
{
    [JsonConstructor]
    public BehaviorTrigger(
        string key,
        OccurrenceKind occurrence,
        TriggerMode mode,
        BehaviorTriggerScope scope,
        BehaviorCardFilter? subjectCardFilter,
        IReadOnlyList<BehaviorInstruction> instructions,
        RunOutcomeRequirement? runOutcomeRequirement = null,
        ServerKind? serverKindRequirement = null,
        bool requireSourceOnAttackedServer = false,
        bool requireSourceInstalled = false,
        bool activeWhileUnrezzed = false,
        bool persistent = false,
        bool requireAgendaStolenDuringRun = false,
        PlayerSide? occurrenceControllerSide = null,
        bool oncePerTurn = false,
        BehaviorTriggerCondition? condition = null,
        BehaviorCondition? requirement = null,
        BehaviorFace? face = null,
        bool activeWhileAccessed = false)
    {
        if (!StableIdentifier.TryValidate(key))
        {
            throw new ArgumentException(
                "A behavior trigger key must be a stable identifier.",
                nameof(key));
        }

        Requirement = requirement;
        if (face is { } declaredFace && !Enum.IsDefined(declaredFace))
        {
            throw new ArgumentOutOfRangeException(nameof(face));
        }

        Face = face;

        if (!Enum.IsDefined(occurrence))
        {
            throw new ArgumentOutOfRangeException(nameof(occurrence));
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (!Enum.IsDefined(scope))
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }

        if (scope == BehaviorTriggerScope.Self && !IsCardBearing(occurrence))
        {
            throw new ArgumentException(
                $"Self-scoped triggers require a card-bearing occurrence, not '{occurrence}'.",
                nameof(scope));
        }

        if (subjectCardFilter is not null && !IsCardBearing(occurrence))
        {
            throw new ArgumentException(
                $"A subject card filter requires a card-bearing occurrence, not '{occurrence}'.",
                nameof(subjectCardFilter));
        }

        if (runOutcomeRequirement is { } outcomeRequirement
            && !Enum.IsDefined(outcomeRequirement))
        {
            throw new ArgumentOutOfRangeException(nameof(runOutcomeRequirement));
        }

        if (runOutcomeRequirement is not null && occurrence != OccurrenceKind.RunEnded)
        {
            throw new ArgumentException(
                "A run outcome requirement is valid only for RunEnded triggers.",
                nameof(runOutcomeRequirement));
        }

        if (serverKindRequirement is { } requiredServerKind
            && (!Enum.IsDefined(requiredServerKind)
                || occurrence is not (OccurrenceKind.RunSuccessful
                    or OccurrenceKind.RunEnded
                    or OccurrenceKind.ServerApproached
                    or OccurrenceKind.IceEncountered
                    or OccurrenceKind.IceApproached
                    // VP-REZ-SERVER-KIND: the server of the rezzed card (Grubber: "if it is protecting a central server").
                    or OccurrenceKind.CardRezzed)))
        {
            throw new ArgumentException(
                "A server-kind requirement is valid only for run server occurrences and must name a defined server kind.",
                nameof(serverKindRequirement));
        }

        if (requireSourceOnAttackedServer
            && occurrence is not (OccurrenceKind.ServerApproached or OccurrenceKind.IceEncountered or OccurrenceKind.IceApproached or OccurrenceKind.CardRezzed or OccurrenceKind.RunEnded or OccurrenceKind.AgendaScoredOrStolen
                or OccurrenceKind.CreditsSpentFromOutsidePool or OccurrenceKind.RunSuccessful))
        {
            throw new ArgumentException(
                "Requiring the source to occupy the attacked server is valid only for ServerApproached, IceEncountered, or CardRezzed triggers.",
                nameof(requireSourceOnAttackedServer));
        }

        ArgumentNullException.ThrowIfNull(instructions);
        if (instructions.Count == 0)
        {
            throw new ArgumentException(
                "A behavior trigger must declare at least one instruction.",
                nameof(instructions));
        }

        if (occurrenceControllerSide is { } side && !Enum.IsDefined(side)) throw new ArgumentOutOfRangeException(nameof(occurrenceControllerSide));
        if (condition is { } predicate && !Enum.IsDefined(predicate)) throw new ArgumentOutOfRangeException(nameof(condition));
        OccurrenceControllerSide = occurrenceControllerSide;
        OncePerTurn = oncePerTurn;
        Condition = condition;
        Key = key;
        Occurrence = occurrence;
        Mode = mode;
        Scope = scope;
        SubjectCardFilter = subjectCardFilter;
        Instructions = Freeze(instructions);
        BehaviorRunOperandValidation.RequireNoStartRun(Instructions, nameof(instructions));
        RunOutcomeRequirement = runOutcomeRequirement;
        ServerKindRequirement = serverKindRequirement;
        RequireSourceOnAttackedServer = requireSourceOnAttackedServer;
        RequireSourceInstalled = requireSourceInstalled;
        ActiveWhileUnrezzed = activeWhileUnrezzed;
        if (activeWhileAccessed && (occurrence != OccurrenceKind.CardAccessed || scope != BehaviorTriggerScope.Self))
        {
            throw new ArgumentException("An access-time ability is a self-scoped CardAccessed trigger.", nameof(activeWhileAccessed));
        }

        ActiveWhileAccessed = activeWhileAccessed;
        if ((persistent || requireAgendaStolenDuringRun) && occurrence != OccurrenceKind.RunEnded)
            throw new ArgumentException("Persistent run-end conditions require RunEnded.");
        Persistent = persistent;
        RequireAgendaStolenDuringRun = requireAgendaStolenDuringRun;
        if (activeWhileUnrezzed && (!requireSourceInstalled || occurrence != OccurrenceKind.CardAccessed || scope != BehaviorTriggerScope.Self))
        {
            throw new ArgumentException("Unrezzed triggers require an installed self-access declaration.", nameof(activeWhileUnrezzed));
        }
    }

    public string Key { get; }

    public OccurrenceKind Occurrence { get; }

    public PlayerSide? OccurrenceControllerSide { get; }

    public bool OncePerTurn { get; }

    public BehaviorTriggerCondition? Condition { get; }

    /// <summary>
    /// A structured "if" the trigger needs to meet its condition (CR 9.6),
    /// judged against its source card when the occurrence happens: the
    /// threat level, a counter or advancement count on the source, a zone
    /// size or a turn fact. Null for unconditional triggers.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BehaviorCondition? Requirement { get; }

    /// <summary>EL-FLIP: the face of a double-sided identity this ability is printed on; null for both.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BehaviorFace? Face { get; }

    public TriggerMode Mode { get; }

    public BehaviorTriggerScope Scope { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BehaviorCardFilter? SubjectCardFilter { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RunOutcomeRequirement? RunOutcomeRequirement { get; }

    /// <summary>
    /// Optional server topology predicate for a run occurrence. The engine
    /// resolves the occurrence's stable server ID through the current server
    /// topology; it never infers this value from card text or a source title.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ServerKind? ServerKindRequirement { get; }

    /// <summary>
    /// When true, catalog materialization requires the active source's
    /// installed zone to carry the occurrence's attacked server ID.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RequireSourceOnAttackedServer { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RequireSourceInstalled { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ActiveWhileUnrezzed { get; }

    /// <summary>
    /// An ability active while its own card is being accessed, wherever it is
    /// except Archives ("when the Runner accesses this asset anywhere except
    /// in Archives", Byte!).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ActiveWhileAccessed { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Persistent { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RequireAgendaStolenDuringRun { get; }

    public IReadOnlyList<BehaviorInstruction> Instructions { get; }

    public bool Equals(BehaviorTrigger? other) =>
        other is not null
        && string.Equals(Key, other.Key, StringComparison.Ordinal)
        && Occurrence == other.Occurrence
        && OccurrenceControllerSide == other.OccurrenceControllerSide
        && OncePerTurn == other.OncePerTurn
        && Condition == other.Condition
        && Requirement == other.Requirement
        && Face == other.Face
        && Mode == other.Mode
        && Scope == other.Scope
        && BehaviorContractStructure.AreEqual(SubjectCardFilter, other.SubjectCardFilter)
        && RunOutcomeRequirement == other.RunOutcomeRequirement
        && ServerKindRequirement == other.ServerKindRequirement
        && RequireSourceOnAttackedServer == other.RequireSourceOnAttackedServer
        && RequireSourceInstalled == other.RequireSourceInstalled
        && ActiveWhileUnrezzed == other.ActiveWhileUnrezzed
        && ActiveWhileAccessed == other.ActiveWhileAccessed
        && Persistent == other.Persistent
        && RequireAgendaStolenDuringRun == other.RequireAgendaStolenDuringRun
        && BehaviorContractStructure.SequenceEquals(
            Instructions,
            other.Instructions,
            BehaviorContractStructure.AreEqual);

    public override bool Equals(object? obj) => Equals(obj as BehaviorTrigger);

    public override int GetHashCode() =>
        HashCode.Combine(
            HashCode.Combine(
                Key,
                Occurrence,
                Mode,
                Scope,
                SubjectCardFilter,
                RunOutcomeRequirement,
                ServerKindRequirement,
                RequireSourceOnAttackedServer),
            Instructions.Count,
            RequireSourceInstalled,
            ActiveWhileUnrezzed, Persistent, RequireAgendaStolenDuringRun);

    private static bool IsCardBearing(OccurrenceKind occurrence) => occurrence is
        OccurrenceKind.CardInstalled
        or OccurrenceKind.CardRezzed
        or OccurrenceKind.IceEncountered
        or OccurrenceKind.IceApproached
        or OccurrenceKind.RunSuccessful
        or OccurrenceKind.RunEnded
        or OccurrenceKind.AgendaScoredOrStolen
        or OccurrenceKind.CardAccessed
        or OccurrenceKind.HostedCreditPoolEmptied
        or OccurrenceKind.CardAdvancedFromZero
        or OccurrenceKind.AccessedCardTrashed
        or OccurrenceKind.AgendaForfeited
        or OccurrenceKind.FirstCardCreditGainThisTurn
        or OccurrenceKind.FirstOperationPlayedThisTurn
        or OccurrenceKind.FirstEventPlayedThisTurn
        or OccurrenceKind.CardAdvanced;

    private static ReadOnlyCollection<BehaviorInstruction> Freeze(
        IEnumerable<BehaviorInstruction> values)
    {
        BehaviorInstruction[] copy = values.ToArray();
        if (copy.Any(static value => value is null))
        {
            throw new ArgumentException(
                "Behavior trigger instructions must not contain null entries.",
                nameof(values));
        }

        return new ReadOnlyCollection<BehaviorInstruction>(copy);
    }
}

/// <summary>
/// Narrow escape hatch for a genuinely unique timing rule.  The handler ID,
/// rather than an executable delegate, is what may be persisted or referenced
/// by a behavior definition.  The handler itself is supplied by composition.
/// </summary>
public interface IBehaviorExtensionHandler
{
    string HandlerId { get; }
}

/// <summary>
/// Composable compiled behavior contract.  Rules text and metadata are not
/// interpreted by this type; all values are authored, typed, and frozen by the
/// behavior compiler/catalog builder.
/// </summary>
public sealed class CardBehaviorDefinition
{
    [JsonConstructor]
    public CardBehaviorDefinition(
        CardDefinitionId definitionId,
        PlayerSide controller,
        IReadOnlyList<BehaviorCost>? costs = null,
        IReadOnlyList<BehaviorCondition>? conditions = null,
        IReadOnlyList<BehaviorTarget>? targets = null,
        IReadOnlyList<BehaviorInstruction>? instructions = null,
        IReadOnlyList<BehaviorStaticModifier>? staticModifiers = null,
        IReadOnlyList<BehaviorTrigger>? triggers = null,
        string? extensionHandlerId = null,
        IReadOnlyList<BehaviorPaidAbility>? paidAbilities = null,
        IReadOnlyList<BehaviorCostModifier>? costModifiers = null,
        IReadOnlyList<BehaviorValueModifier>? valueModifiers = null,
        IReadOnlyList<BehaviorFirstProgramInstallCostModifier>? firstProgramInstallCostModifiers = null,
        IReadOnlyList<BehaviorFirstHqBreachAccessModifier>? firstHqBreachAccessModifiers = null,
        IReadOnlyList<BehaviorFirstBasicDrawReplacement>? firstBasicDrawReplacements = null,
        bool canBeAdvanced = false,
        HostingBehaviorSpec? hosting = null,
        bool runnerCannotTrash = false,
        IReadOnlyList<BehaviorHostedCreditUse>? hostedCreditUses = null,
        int recurringCredits = 0,
        int rootAssetTrashCostIncrease = 0,
        IReadOnlyList<BehaviorCondition>? rezRequirements = null,
        bool hostedCardsPlayAsGrip = false,
        int rezForfeitDiscount = 0,
        int hostIcebreakerStrength = 0,
        int stealCredits = 0,
        BehaviorScoreCostAsAgenda? scoreCostAsAgenda = null,
        int firstDoubleOperationClickDiscount = 0,
        bool returnsHostedBadPublicity = false,
        IReadOnlyList<ServerKind>? installServerKinds = null)
    {
        if (installServerKinds is { Count: > 0 } && (controller != PlayerSide.Corp
            || installServerKinds.Any(static kind => !Enum.IsDefined(kind)) || installServerKinds.Distinct().Count() != installServerKinds.Count))
        {
            throw new ArgumentException("Only a Corp card limits its install to distinct, defined server kinds.", nameof(installServerKinds));
        }

        InstallServerKinds = installServerKinds is { Count: > 0 } ? installServerKinds.ToArray() : null;
        if (returnsHostedBadPublicity && controller != PlayerSide.Corp)
        {
            throw new ArgumentException("Only a Corp card hosts bad publicity.", nameof(returnsHostedBadPublicity));
        }

        ReturnsHostedBadPublicity = returnsHostedBadPublicity;
        if (firstDoubleOperationClickDiscount < 0 || firstDoubleOperationClickDiscount > 0 && controller != PlayerSide.Corp)
        {
            throw new ArgumentException("Only a Corp card lowers a Double operation's click cost, by a nonnegative amount.",
                nameof(firstDoubleOperationClickDiscount));
        }

        FirstDoubleOperationClickDiscount = firstDoubleOperationClickDiscount;
        ScoreCostAsAgenda = scoreCostAsAgenda;
        if (stealCredits < 0 || stealCredits > 0 && controller != PlayerSide.Corp)
        {
            throw new ArgumentException("Only a Corp card adds a nonnegative credit cost to stealing.", nameof(stealCredits));
        }

        StealCredits = stealCredits;
        DefinitionId = definitionId;
        ArgumentOutOfRangeException.ThrowIfNegative(hostIcebreakerStrength);
        if (hostIcebreakerStrength > 0 && controller != PlayerSide.Runner)
        {
            throw new ArgumentException("Only Runner mods host on icebreakers.", nameof(hostIcebreakerStrength));
        }

        HostIcebreakerStrength = hostIcebreakerStrength;
        ArgumentOutOfRangeException.ThrowIfNegative(rezForfeitDiscount);
        if (rezForfeitDiscount > 0 && controller != PlayerSide.Corp)
        {
            throw new ArgumentException("Only Corp cards are rezzed.", nameof(rezForfeitDiscount));
        }

        RezForfeitDiscount = rezForfeitDiscount;
        HostedCardsPlayAsGrip = hostedCardsPlayAsGrip;
        if (hostedCardsPlayAsGrip && controller != PlayerSide.Runner)
        {
            throw new ArgumentException("Only Runner cards host cards playable as if in the grip.", nameof(hostedCardsPlayAsGrip));
        }

        RezRequirements = rezRequirements is null or { Count: 0 }
            ? null
            : new ReadOnlyCollection<BehaviorCondition>(rezRequirements.ToArray());
        ArgumentOutOfRangeException.ThrowIfNegative(recurringCredits);
        ArgumentOutOfRangeException.ThrowIfNegative(rootAssetTrashCostIncrease);
        RootAssetTrashCostIncrease = rootAssetTrashCostIncrease;
        RecurringCredits = recurringCredits;
        HostedCreditUses = hostedCreditUses is null or { Count: 0 }
            ? null
            : new ReadOnlyCollection<BehaviorHostedCreditUse>(hostedCreditUses.ToArray());
        if (!Enum.IsDefined(controller))
        {
            throw new ArgumentOutOfRangeException(nameof(controller));
        }

        Controller = controller;
        CanBeAdvanced = canBeAdvanced;
        Hosting = hosting;
        RunnerCannotTrash = runnerCannotTrash;
        if (runnerCannotTrash && controller != PlayerSide.Corp)
        {
            throw new ArgumentException("Only Corp cards can forbid the Runner trashing them.", nameof(runnerCannotTrash));
        }

        if (canBeAdvanced && controller != PlayerSide.Corp)
        {
            throw new ArgumentException("Only Corp cards can permit advancement.", nameof(canBeAdvanced));
        }
        Costs = Freeze(costs);
        if (Costs.Count(static cost => cost.Kind is BehaviorCostKind.TrashSelf or BehaviorCostKind.RemoveSelfFromGame) > 1)
        {
            throw new ArgumentException(
                "A behavior may not declare more than one self-movement cost.",
                nameof(costs));
        }
        Conditions = Freeze(conditions);
        Targets = Freeze(targets);
        Instructions = Freeze(instructions);
        BehaviorRunOperandValidation.RequireNoStartRun(Instructions, nameof(instructions));
        StaticModifiers = Freeze(staticModifiers);
        Triggers = Freeze(triggers);
        var triggerKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (BehaviorTrigger trigger in Triggers)
        {
            if (!triggerKeys.Add(trigger.Key))
            {
                throw new ArgumentException(
                    $"Behavior trigger key '{trigger.Key}' is declared more than once.",
                    nameof(triggers));
            }
        }

        PaidAbilities = FreezePaidAbilities(paidAbilities);
        CostModifiers = FreezeCostModifiers(costModifiers);
        ValueModifiers = FreezeValueModifiers(valueModifiers);
        FirstProgramInstallCostModifiers = FreezeFirstProgramInstallCostModifiers(firstProgramInstallCostModifiers);
        FirstHqBreachAccessModifiers = FreezeFirstHqBreachAccessModifiers(firstHqBreachAccessModifiers);
        FirstBasicDrawReplacements = FreezeFirstBasicDrawReplacements(firstBasicDrawReplacements);
        if (!string.IsNullOrWhiteSpace(extensionHandlerId))
        {
            ExtensionHandlerId = extensionHandlerId;
        }
    }

    public CardDefinitionId DefinitionId { get; }

    public PlayerSide Controller { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CanBeAdvanced { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HostingBehaviorSpec? Hosting { get; }

    /// <summary>The Runner cannot trash this card while it is rezzed (Kessleroid).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RunnerCannotTrash { get; }

    /// <summary>
    /// EL-PERSISTENT: while this rezzed card is in a server's root, the trash
    /// cost of each asset in that root rises by this amount; the increase
    /// persists through the run in which the Runner trashes this card.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int RootAssetTrashCostIncrease { get; }

    /// <summary>
    /// What the Corp must be able to pay as an additional cost to rez this
    /// card, any one of them (Plutus: an agenda to forfeit, or 3 HQ cards).
    /// The cost itself is paid by the card's "when rezzed" ability.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BehaviorCondition>? RezRequirements { get; }

    /// <summary>
    /// EL-HOSTED-FACEUP: the Runner can play or install cards hosted faceup on
    /// this installed card as if they were in the grip (Bling).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HostedCardsPlayAsGrip { get; }

    /// <summary>
    /// EL-REZ-COST-PAYMENT: the Corp can forfeit 1 agenda as it rezzes this
    /// card to pay for this much of its rez cost (Biawak).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int RezForfeitDiscount { get; }

    /// <summary>
    /// EL-MOD-HOST: this hardware hosts on an installed non-AI icebreaker, which
    /// gets this much strength and keeps its pumped strength for the rest of the
    /// run (GAMEDRAGON™ Pro). Zero for every other card.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int HostIcebreakerStrength { get; }

    /// <summary>EL-RECURRING: hosted credits refilled to this amount before each of the controller's turns.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int RecurringCredits { get; }

    /// <summary>What this card's hosted credits can be spent on (Azimat, Open Market, Mahkota Langit Grid).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BehaviorHostedCreditUse>? HostedCreditUses { get; }

    /// <summary>
    /// VP-SCORE-STEAL-COST: while the card is active, stealing any agenda costs the Runner this many more credits
    /// (Magistrate Revontulet).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int StealCredits { get; }

    /// <summary>
    /// VP-SCORE-STEAL-COST: while the card is active, scoring an agenda (one installed this turn, when so declared) costs
    /// the Corp adding this card to its score area as an agenda (Word on the Street).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BehaviorScoreCostAsAgenda? ScoreCostAsAgenda { get; }

    /// <summary>
    /// VP-CLICK-COST-MODIFIER: while the card is active, the first Double operation the Corp plays each turn costs this
    /// many clicks less (Synchrocyclotron).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int FirstDoubleOperationClickDiscount { get; }

    /// <summary>
    /// VP-UNINSTALL-INTERRUPT: when the card would be uninstalled, the Corp takes the bad publicity hosted on it
    /// (<see cref="HostedBadPublicityCounter"/>; Luana Campos).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ReturnsHostedBadPublicity { get; }

    /// <summary>
    /// VP-INSTALL-SERVER-KIND: the kinds of server the card may be installed in ("Central server only": The Red Room;
    /// "HQ or R&amp;D only": Flagship). Null for no limit.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ServerKind>? InstallServerKinds { get; }

    /// <summary>VP-BAD-PUBLICITY: the name of the counter for bad publicity hosted on a card.</summary>
    public const string HostedBadPublicityCounter = "bad-publicity";

    public IReadOnlyList<BehaviorCost> Costs { get; }

    public IReadOnlyList<BehaviorCondition> Conditions { get; }

    public IReadOnlyList<BehaviorTarget> Targets { get; }

    public IReadOnlyList<BehaviorInstruction> Instructions { get; }

    public IReadOnlyList<BehaviorStaticModifier> StaticModifiers { get; }

    public IReadOnlyList<BehaviorTrigger> Triggers { get; }

    public string? ExtensionHandlerId { get; }

    /// <summary>
    /// Immutable paid-ability declarations.  The declarations are data-only;
    /// materialization and execution are owned by later engine checkpoints.
    /// </summary>
    public IReadOnlyList<BehaviorPaidAbility> PaidAbilities { get; }

    /// <summary>
    /// Immutable self-cost adjustments projected only while this definition's
    /// physical source is the card being paid for.  The list is optional in
    /// the JSON contract so definitions that predate conditional costs keep
    /// their canonical byte shape.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BehaviorCostModifier>? CostModifiers { get; }

    /// <summary>
    /// Generic calculated-value declarations owned by this physical card's
    /// behavior definition. Null is preserved for legacy definitions so their
    /// JSON and compatibility fingerprints remain stable.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BehaviorValueModifier>? ValueModifiers { get; }

    /// <summary>
    /// Optional installed-source declarations that reduce the first Runner
    /// program install in the active formal turn. Null preserves the legacy
    /// generic-definition JSON shape and fingerprint input.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BehaviorFirstProgramInstallCostModifier>? FirstProgramInstallCostModifiers { get; }

    /// <summary>
    /// Optional installed-source declarations that add access on the first HQ
    /// breach of the active formal turn. Null preserves the legacy generic
    /// definition JSON shape and fingerprint input.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BehaviorFirstHqBreachAccessModifier>? FirstHqBreachAccessModifiers { get; }

    /// <summary>
    /// Optional installed-source declarations that replace the first real
    /// basic draw action of the formal turn. Null preserves the legacy generic
    /// definition JSON shape and fingerprint input.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BehaviorFirstBasicDrawReplacement>? FirstBasicDrawReplacements { get; }

    private static ReadOnlyCollection<T> Freeze<T>(IEnumerable<T>? values)
        where T : class
    {
        T[] copy = (values ?? []).ToArray();
        if (copy.Any(static value => value is null))
        {
            throw new ArgumentException("Behavior contract collections must not contain null entries.", nameof(values));
        }

        return new ReadOnlyCollection<T>(copy);
    }

    private static ReadOnlyCollection<BehaviorPaidAbility> FreezePaidAbilities(
        IEnumerable<BehaviorPaidAbility>? paidAbilities)
    {
        BehaviorPaidAbility[] copy = (paidAbilities ?? []).ToArray();
        if (copy.Any(static value => value is null))
        {
            throw new ArgumentException(
                "Paid ability declarations must not contain null entries.",
                nameof(paidAbilities));
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (BehaviorPaidAbility declaration in copy)
        {
            if (!keys.Add(declaration.Key))
            {
                throw new ArgumentException(
                    $"Paid ability key '{declaration.Key}' is declared more than once.",
                    nameof(paidAbilities));
            }
        }

        return new ReadOnlyCollection<BehaviorPaidAbility>(copy);
    }

    private static ReadOnlyCollection<BehaviorCostModifier>? FreezeCostModifiers(
        IEnumerable<BehaviorCostModifier>? costModifiers)
    {
        if (costModifiers is null)
        {
            return null;
        }

        BehaviorCostModifier[] copy = costModifiers.ToArray();
        if (copy.Length == 0)
        {
            return null;
        }

        if (copy.Any(static value => value is null))
        {
            throw new ArgumentException(
                "Cost modifier declarations must not contain null entries.",
                nameof(costModifiers));
        }

        return new ReadOnlyCollection<BehaviorCostModifier>(copy);
    }

    private static ReadOnlyCollection<BehaviorValueModifier>? FreezeValueModifiers(
        IEnumerable<BehaviorValueModifier>? valueModifiers)
    {
        if (valueModifiers is null)
        {
            return null;
        }

        BehaviorValueModifier[] copy = valueModifiers.ToArray();
        if (copy.Length == 0)
        {
            return null;
        }

        if (copy.Any(static value => value is null))
        {
            throw new ArgumentException(
                "Value modifier declarations must not contain null entries.",
                nameof(valueModifiers));
        }

        return new ReadOnlyCollection<BehaviorValueModifier>(copy);
    }

    private static ReadOnlyCollection<BehaviorFirstProgramInstallCostModifier>? FreezeFirstProgramInstallCostModifiers(
        IEnumerable<BehaviorFirstProgramInstallCostModifier>? modifiers)
    {
        if (modifiers is null)
        {
            return null;
        }

        BehaviorFirstProgramInstallCostModifier[] copy = modifiers.ToArray();
        if (copy.Length == 0)
        {
            return null;
        }

        if (copy.Any(static value => value is null))
        {
            throw new ArgumentException(
                "First-program install cost modifier declarations must not contain null entries.",
                nameof(modifiers));
        }

        if (copy.Select(static modifier => modifier.ReasonKey).Distinct(StringComparer.Ordinal).Count() != copy.Length)
        {
            throw new ArgumentException(
                "First-program install cost modifier declarations must use a unique reason key per source.",
                nameof(modifiers));
        }

        return new ReadOnlyCollection<BehaviorFirstProgramInstallCostModifier>(copy);
    }

    private static ReadOnlyCollection<BehaviorFirstHqBreachAccessModifier>? FreezeFirstHqBreachAccessModifiers(
        IEnumerable<BehaviorFirstHqBreachAccessModifier>? modifiers)
    {
        if (modifiers is null)
        {
            return null;
        }

        BehaviorFirstHqBreachAccessModifier[] copy = modifiers.ToArray();
        if (copy.Length == 0)
        {
            return null;
        }

        if (copy.Any(static value => value is null))
        {
            throw new ArgumentException(
                "First-HQ-breach access modifier declarations must not contain null entries.",
                nameof(modifiers));
        }

        if (copy.Select(static modifier => modifier.ReasonKey).Distinct(StringComparer.Ordinal).Count() != copy.Length)
        {
            throw new ArgumentException(
                "First-HQ-breach access modifier declarations must use a unique reason key per source.",
                nameof(modifiers));
        }

        return new ReadOnlyCollection<BehaviorFirstHqBreachAccessModifier>(copy);
    }

    private static ReadOnlyCollection<BehaviorFirstBasicDrawReplacement>? FreezeFirstBasicDrawReplacements(
        IEnumerable<BehaviorFirstBasicDrawReplacement>? replacements)
    {
        if (replacements is null)
        {
            return null;
        }

        BehaviorFirstBasicDrawReplacement[] copy = replacements.ToArray();
        if (copy.Length == 0)
        {
            return null;
        }

        if (copy.Any(static value => value is null))
        {
            throw new ArgumentException(
                "First-basic-draw replacement declarations must not contain null entries.",
                nameof(replacements));
        }

        if (copy.Select(static replacement => replacement.ReasonKey).Distinct(StringComparer.Ordinal).Count() != copy.Length)
        {
            throw new ArgumentException(
                "First-basic-draw replacement declarations must use a unique reason key per source.",
                nameof(replacements));
        }

        return new ReadOnlyCollection<BehaviorFirstBasicDrawReplacement>(copy);
    }
}

/// <summary>
/// Immutable behavior catalog.  Use <see cref="BehaviorCatalogBuilder"/> to
/// compose one; once built, all collections and lookups are detached and safe
/// to share between parallel engine sessions.
/// </summary>
public sealed class BehaviorCatalog : IBehaviorCatalog, IBehaviorCardClassificationCatalog
{
    /// <summary>
    /// Immutable empty catalog used by rules-only compatibility overloads.
    /// Behavior-bearing sessions must compose and inject their catalog.
    /// </summary>
    public static BehaviorCatalog Empty { get; } =
        new BehaviorCatalogBuilder(EngineCompatibility.BehaviorRegistryVersion).Build();

    private readonly IReadOnlyDictionary<CardDefinitionId, IceBehaviorSpec> _ice;
    private readonly IReadOnlyDictionary<CardDefinitionId, IcebreakerBehaviorSpec> _icebreakers;
    private readonly IReadOnlyDictionary<CardDefinitionId, AssetBehaviorSpec> _assets;
    private readonly IReadOnlyDictionary<CardDefinitionId, AgendaBehaviorSpec> _agendas;
    private readonly IReadOnlyDictionary<CardDefinitionId, RunEventBehaviorSpec> _runEvents;
    private readonly IReadOnlyDictionary<CardDefinitionId, CardBehaviorDefinition> _generic;
    private readonly ReadOnlyDictionary<CardDefinitionId, BehaviorCardType> _cardTypes;
    private readonly ReadOnlyDictionary<CardDefinitionId, IReadOnlyList<string>> _cardSubtypes;
    private readonly ConcurrentDictionary<CardDefinitionId, string> _definitionFingerprints = new();
    private readonly HashSet<TurnEventKind> _recordedTurnEvents;

    /// <summary>VP.2: the opt-in turn events this catalog's cards read (see <see cref="IBehaviorCatalog.RecordsTurnEvent"/>).</summary>
    public IReadOnlyCollection<TurnEventKind> RecordedTurnEvents => _recordedTurnEvents;

    public bool RecordsTurnEvent(TurnEventKind kind) => _recordedTurnEvents.Contains(kind);

    internal BehaviorCatalog(
        string version,
        IReadOnlyDictionary<CardDefinitionId, IceBehaviorSpec> ice,
        IReadOnlyDictionary<CardDefinitionId, IcebreakerBehaviorSpec> icebreakers,
        IReadOnlyDictionary<CardDefinitionId, AssetBehaviorSpec> assets,
        IReadOnlyDictionary<CardDefinitionId, AgendaBehaviorSpec> agendas,
        IReadOnlyDictionary<CardDefinitionId, RunEventBehaviorSpec> runEvents,
        IReadOnlyDictionary<CardDefinitionId, CardBehaviorDefinition> generic,
        IReadOnlyDictionary<CardDefinitionId, BehaviorCardType>? cardTypes = null,
        IReadOnlyDictionary<CardDefinitionId, IReadOnlyList<string>>? cardSubtypes = null,
        bool tracksTurnEvents = false,
        IReadOnlyCollection<TurnEventKind>? recordedTurnEvents = null)
    {
        Version = ValidateVersion(version);
        TracksTurnEvents = tracksTurnEvents;
        _recordedTurnEvents = recordedTurnEvents is { Count: > 0 } ? recordedTurnEvents.ToHashSet() : [];
        BehaviorRegistryVersion = Version;
        _ice = Freeze(ice);
        _icebreakers = Freeze(icebreakers);
        _assets = Freeze(assets);
        _agendas = Freeze(agendas);
        _runEvents = Freeze(runEvents);
        _generic = Freeze(generic);
        _cardTypes = FreezeCardTypes(cardTypes ?? new Dictionary<CardDefinitionId, BehaviorCardType>());
        _cardSubtypes = new ReadOnlyDictionary<CardDefinitionId, IReadOnlyList<string>>(
            (cardSubtypes ?? new Dictionary<CardDefinitionId, IReadOnlyList<string>>())
                .ToDictionary(static pair => pair.Key, static pair => (IReadOnlyList<string>)pair.Value.ToArray()));
        ValidateTypedFamilyConflicts(_ice, _icebreakers, _assets, _agendas, _runEvents);
        ValidateClassificationBindings(
            _ice,
            _icebreakers,
            _assets,
            _agendas,
            _runEvents,
            _generic,
            _cardTypes);
        Fingerprint = BehaviorCatalogFingerprint.Compute(
            Version,
            _ice,
            _icebreakers,
            _assets,
            _agendas,
            _runEvents,
            _generic,
            _cardTypes,
            _cardSubtypes,
            tracksTurnEvents);
    }

    public string Version { get; }

    /// <inheritdoc />
    public bool TracksTurnEvents { get; }

    public string BehaviorRegistryVersion { get; }

    public string Fingerprint { get; }

    public string BehaviorRegistryFingerprint => Fingerprint;

    /// <summary>ICE IDs, retained as the M2 compatibility meaning.</summary>
    public IReadOnlyCollection<CardDefinitionId> DefinitionIds =>
        new ReadOnlyCollection<CardDefinitionId>(_ice.Keys.ToArray());

    public IReadOnlyCollection<CardDefinitionId> AllDefinitionIds =>
        new ReadOnlyCollection<CardDefinitionId>(_ice.Keys
                .Concat(_icebreakers.Keys)
                .Concat(_assets.Keys)
                .Concat(_agendas.Keys)
                .Concat(_runEvents.Keys)
                .Concat(_generic.Keys)
                .Distinct()
                .OrderBy(static id => id.Value, StringComparer.Ordinal)
                .ToArray());

    public IReadOnlyCollection<IceBehaviorSpec> IceSpecs =>
        new ReadOnlyCollection<IceBehaviorSpec>(_ice.Values.ToArray());

    public IReadOnlyCollection<IcebreakerBehaviorSpec> IcebreakerSpecs =>
        new ReadOnlyCollection<IcebreakerBehaviorSpec>(_icebreakers.Values.ToArray());

    public IReadOnlyCollection<AssetBehaviorSpec> AssetSpecs =>
        new ReadOnlyCollection<AssetBehaviorSpec>(_assets.Values.ToArray());

    public IReadOnlyCollection<AgendaBehaviorSpec> AgendaSpecs =>
        new ReadOnlyCollection<AgendaBehaviorSpec>(_agendas.Values.ToArray());

    public IReadOnlyCollection<RunEventBehaviorSpec> RunEventSpecs =>
        new ReadOnlyCollection<RunEventBehaviorSpec>(_runEvents.Values.ToArray());

    public IReadOnlyCollection<CardBehaviorDefinition> GenericSpecs =>
        new ReadOnlyCollection<CardBehaviorDefinition>(_generic.Values.ToArray());

    public bool TryGetIce(CardDefinitionId definitionId, out IceBehaviorSpec spec) =>
        _ice.TryGetValue(definitionId, out spec!);

    public bool TryGetIcebreaker(CardDefinitionId definitionId, out IcebreakerBehaviorSpec spec) =>
        _icebreakers.TryGetValue(definitionId, out spec!);

    public bool TryGetAsset(CardDefinitionId definitionId, out AssetBehaviorSpec spec) =>
        _assets.TryGetValue(definitionId, out spec!);

    public bool TryGetAgenda(CardDefinitionId definitionId, out AgendaBehaviorSpec spec) =>
        _agendas.TryGetValue(definitionId, out spec!);

    public bool TryGetRunEvent(CardDefinitionId definitionId, out RunEventBehaviorSpec spec) =>
        _runEvents.TryGetValue(definitionId, out spec!);

    public bool TryGet(CardDefinitionId definitionId, out CardBehaviorDefinition spec) =>
        _generic.TryGetValue(definitionId, out spec!);

    /// <summary>
    /// The stable fingerprint of everything this catalog registers for one card, in every family
    /// (typed specs, generic definition, card type and subtypes). It changes exactly when that card's
    /// behavior does, so a change can be scoped to the deck pools that hold the card (scripts/affected-pools.sh).
    /// </summary>
    public string GetCardBehaviorFingerprint(CardDefinitionId definitionId) =>
        BehaviorCatalogFingerprint.ComputeCard(
            definitionId, _ice, _icebreakers, _assets, _agendas, _runEvents, _generic, _cardTypes, _cardSubtypes);

    /// <summary>
    /// Returns the stable fingerprint of one generic definition. The catalog and its definitions are immutable, so each
    /// fingerprint is computed once (bot plan S0.4: recomputing it for every issued ability action was a sixth of a
    /// planner's time).
    /// </summary>
    public string GetDefinitionFingerprint(CardDefinitionId definitionId)
    {
        if (_definitionFingerprints.TryGetValue(definitionId, out string? fingerprint))
        {
            return fingerprint;
        }

        if (!_generic.TryGetValue(definitionId, out CardBehaviorDefinition? definition))
        {
            throw new KeyNotFoundException($"Generic behavior definition '{definitionId}' is not registered.");
        }

        return _definitionFingerprints.GetOrAdd(definitionId, BehaviorCatalogFingerprint.ComputeDefinition(definition));
    }

    public bool TryGetCardType(CardDefinitionId definitionId, out BehaviorCardType cardType) =>
        _cardTypes.TryGetValue(definitionId, out cardType);

    public bool TryGetCardSubtypes(CardDefinitionId definitionId, out IReadOnlyList<string> subtypes) =>
        _cardSubtypes.TryGetValue(definitionId, out subtypes!);

    /// <summary>The definitions with registered printed subtypes.</summary>
    public IReadOnlyCollection<CardDefinitionId> SubtypedDefinitionIds =>
        new ReadOnlyCollection<CardDefinitionId>(_cardSubtypes.Keys.ToArray());

    /// <summary>
    /// Builds an immutable catalog containing every typed and generic contract
    /// for the requested definition IDs. The source version is retained, so
    /// a scenario can pin only the behavior capabilities it references while
    /// still consuming a larger composition catalog.
    /// </summary>
    public static BehaviorCatalog CreateSlice(
        IBehaviorCatalog source,
        IEnumerable<CardDefinitionId> definitionIds)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(definitionIds);

        var builder = new BehaviorCatalogBuilder(source.Version);
        if (source.TracksTurnEvents)
        {
            builder.TrackTurnEvents();
        }

        foreach (CardDefinitionId definitionId in definitionIds.Distinct())
        {
            if (source.TryGetIce(definitionId, out IceBehaviorSpec? ice))
            {
                builder.AddIce(ice);
            }

            if (source.TryGetIcebreaker(definitionId, out IcebreakerBehaviorSpec? icebreaker))
            {
                builder.AddIcebreaker(icebreaker);
            }

            if (source.TryGetAsset(definitionId, out AssetBehaviorSpec? asset))
            {
                builder.AddAsset(asset);
            }

            if (source.TryGetAgenda(definitionId, out AgendaBehaviorSpec? agenda))
            {
                builder.AddAgenda(agenda);
            }

            if (source.TryGetRunEvent(definitionId, out RunEventBehaviorSpec? runEvent))
            {
                builder.AddRunEvent(runEvent);
            }

            if (source.TryGet(definitionId, out CardBehaviorDefinition? generic))
            {
                builder.Add(generic);
            }

            if (source is IBehaviorCardClassificationCatalog classifications
                && classifications.TryGetCardType(definitionId, out BehaviorCardType cardType))
            {
                builder.AddCardType(definitionId, cardType);
            }

            if (source.TryGetCardSubtypes(definitionId, out IReadOnlyList<string> subtypes))
            {
                builder.AddCardSubtypes(definitionId, subtypes);
            }
        }

        return builder.Build();
    }

    /// <summary>Returns a definition-ID slice of this immutable catalog.</summary>
    public BehaviorCatalog CreateSlice(IEnumerable<CardDefinitionId> definitionIds) =>
        CreateSlice(this, definitionIds);

    private static string ValidateVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("A behavior catalog version is required.", nameof(version));
        }

        if (!StableIdentifier.TryValidate(version))
        {
            throw new ArgumentException("A behavior catalog version must be a stable identifier.", nameof(version));
        }

        return version;
    }

    private static ReadOnlyDictionary<CardDefinitionId, T> Freeze<T>(IReadOnlyDictionary<CardDefinitionId, T> source)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        var copy = new Dictionary<CardDefinitionId, T>();
        foreach ((CardDefinitionId id, T value) in source)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!copy.TryAdd(id, value))
            {
                throw new InvalidOperationException($"Behavior definition '{id}' occurs more than once.");
            }
        }

        return new ReadOnlyDictionary<CardDefinitionId, T>(copy);
    }

    private static ReadOnlyDictionary<CardDefinitionId, BehaviorCardType> FreezeCardTypes(
        IReadOnlyDictionary<CardDefinitionId, BehaviorCardType> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var copy = new Dictionary<CardDefinitionId, BehaviorCardType>();
        foreach ((CardDefinitionId id, BehaviorCardType value) in source)
        {
            if (!copy.TryAdd(id, value))
            {
                throw new InvalidOperationException($"Card classification '{id}' occurs more than once.");
            }
        }

        return new ReadOnlyDictionary<CardDefinitionId, BehaviorCardType>(copy);
    }

    private static void ValidateTypedFamilyConflicts<TIce, TBreaker, TAsset, TAgenda, TRunEvent>(
        IReadOnlyDictionary<CardDefinitionId, TIce> ice,
        IReadOnlyDictionary<CardDefinitionId, TBreaker> icebreakers,
        IReadOnlyDictionary<CardDefinitionId, TAsset> assets,
        IReadOnlyDictionary<CardDefinitionId, TAgenda> agendas,
        IReadOnlyDictionary<CardDefinitionId, TRunEvent> runEvents)
        where TIce : class
        where TBreaker : class
        where TAsset : class
        where TAgenda : class
        where TRunEvent : class
    {
        var familiesByDefinition = new Dictionary<CardDefinitionId, string>();
        Check("ICE", ice.Keys);
        Check("icebreaker", icebreakers.Keys);
        Check("asset", assets.Keys);
        Check("agenda", agendas.Keys);
        Check("run event", runEvents.Keys);

        void Check(string family, IEnumerable<CardDefinitionId> definitionIds)
        {
            foreach (CardDefinitionId definitionId in definitionIds)
            {
                if (familiesByDefinition.TryGetValue(definitionId, out string? existingFamily))
                {
                    throw new InvalidOperationException(
                        $"Card definition '{definitionId}' cannot be registered as both {existingFamily} and {family} behavior.");
                }

                familiesByDefinition.Add(definitionId, family);
            }
        }
    }

    private static void ValidateClassificationBindings<TIce, TBreaker, TAsset, TAgenda, TRunEvent>(
        IReadOnlyDictionary<CardDefinitionId, TIce> ice,
        IReadOnlyDictionary<CardDefinitionId, TBreaker> icebreakers,
        IReadOnlyDictionary<CardDefinitionId, TAsset> assets,
        IReadOnlyDictionary<CardDefinitionId, TAgenda> agendas,
        IReadOnlyDictionary<CardDefinitionId, TRunEvent> runEvents,
        IReadOnlyDictionary<CardDefinitionId, CardBehaviorDefinition> generic,
        IReadOnlyDictionary<CardDefinitionId, BehaviorCardType> cardTypes)
        where TIce : class
        where TBreaker : class
        where TAsset : class
        where TAgenda : class
        where TRunEvent : class
    {
        foreach (CardDefinitionId definitionId in cardTypes.Keys)
        {
            if (!ice.ContainsKey(definitionId)
                && !icebreakers.ContainsKey(definitionId)
                && !assets.ContainsKey(definitionId)
                && !agendas.ContainsKey(definitionId)
                && !runEvents.ContainsKey(definitionId)
                && !generic.ContainsKey(definitionId))
            {
                throw new InvalidOperationException(
                    $"Card classification '{definitionId}' requires a corresponding typed or generic behavior registration.");
            }
        }
    }
}

/// <summary>
/// Validating composer for immutable behavior catalogs.  All registration
/// methods are additive and return the same builder for fluent composition.
/// </summary>
public sealed class BehaviorCatalogBuilder
{
    private readonly Dictionary<CardDefinitionId, IceBehaviorSpec> _ice = new();
    private readonly Dictionary<CardDefinitionId, IcebreakerBehaviorSpec> _icebreakers = new();
    private readonly Dictionary<CardDefinitionId, AssetBehaviorSpec> _assets = new();
    private readonly Dictionary<CardDefinitionId, AgendaBehaviorSpec> _agendas = new();
    private readonly Dictionary<CardDefinitionId, RunEventBehaviorSpec> _runEvents = new();
    private readonly Dictionary<CardDefinitionId, CardBehaviorDefinition> _generic = new();
    private readonly Dictionary<CardDefinitionId, BehaviorCardType> _cardTypes = new();
    private readonly Dictionary<CardDefinitionId, IReadOnlyList<string>> _cardSubtypes = new();
    private bool _tracksTurnEvents;
    private readonly HashSet<TurnEventKind> _recordedTurnEvents = [];
    private readonly Dictionary<CardDefinitionId, string> _typedFamilies = new();

    public BehaviorCatalogBuilder(string version)
    {
        Version = version;
        _ = new BehaviorCatalog(version, new Dictionary<CardDefinitionId, IceBehaviorSpec>(),
            new Dictionary<CardDefinitionId, IcebreakerBehaviorSpec>(),
            new Dictionary<CardDefinitionId, AssetBehaviorSpec>(),
            new Dictionary<CardDefinitionId, AgendaBehaviorSpec>(),
            new Dictionary<CardDefinitionId, RunEventBehaviorSpec>(),
            new Dictionary<CardDefinitionId, CardBehaviorDefinition>());
    }

    public BehaviorCatalogBuilder(string version, IBehaviorCatalog baseCatalog)
        : this(version)
    {
        ArgumentNullException.ThrowIfNull(baseCatalog);
        AddIce(baseCatalog.IceSpecs);
        AddIcebreaker(baseCatalog.IcebreakerSpecs);
        AddAsset(baseCatalog.AssetSpecs);
        AddAgenda(baseCatalog.AgendaSpecs);
        AddRunEvent(baseCatalog.RunEventSpecs);
        Add(baseCatalog.GenericSpecs);
        if (baseCatalog is IBehaviorCardClassificationCatalog classifications)
        {
            foreach (CardDefinitionId definitionId in baseCatalog.AllDefinitionIds)
            {
                if (classifications.TryGetCardType(definitionId, out BehaviorCardType cardType))
                {
                    AddCardType(definitionId, cardType);
                }
            }
        }

        _tracksTurnEvents = baseCatalog.TracksTurnEvents;
        if (baseCatalog is BehaviorCatalog withEvents)
        {
            _recordedTurnEvents.UnionWith(withEvents.RecordedTurnEvents);
        }
        if (baseCatalog is BehaviorCatalog compiled)
        {
            foreach (CardDefinitionId definitionId in compiled.SubtypedDefinitionIds)
            {
                compiled.TryGetCardSubtypes(definitionId, out IReadOnlyList<string> subtypes);
                AddCardSubtypes(definitionId, subtypes);
            }
        }
    }

    public string Version { get; }

    public BehaviorCatalogBuilder AddIce(IceBehaviorSpec spec) => AddTyped(_ice, spec, "ICE");

    public BehaviorCatalogBuilder AddIce(IEnumerable<IceBehaviorSpec> specs) => AddMany(specs, AddIce);

    public BehaviorCatalogBuilder AddIcebreaker(IcebreakerBehaviorSpec spec) => AddTyped(_icebreakers, spec, "icebreaker");

    public BehaviorCatalogBuilder AddIcebreaker(IEnumerable<IcebreakerBehaviorSpec> specs) => AddMany(specs, AddIcebreaker);

    public BehaviorCatalogBuilder AddAsset(AssetBehaviorSpec spec) => AddTyped(_assets, spec, "asset");

    public BehaviorCatalogBuilder AddAsset(IEnumerable<AssetBehaviorSpec> specs) => AddMany(specs, AddAsset);

    public BehaviorCatalogBuilder AddAgenda(AgendaBehaviorSpec spec) => AddTyped(_agendas, spec, "agenda");

    public BehaviorCatalogBuilder AddAgenda(IEnumerable<AgendaBehaviorSpec> specs) => AddMany(specs, AddAgenda);

    public BehaviorCatalogBuilder AddRunEvent(RunEventBehaviorSpec spec) => AddTyped(_runEvents, spec, "run event");

    public BehaviorCatalogBuilder AddRunEvent(IEnumerable<RunEventBehaviorSpec> specs) => AddMany(specs, AddRunEvent);

    /// <summary>
    /// Adds a generic behavior contract. Generic contracts are an intentional
    /// additive overlay: a card may retain a legacy typed behavior while a
    /// later batch contributes generic triggers/modifiers for the same ID.
    /// Typed families themselves may not reuse an ID across families.
    /// </summary>
    public BehaviorCatalogBuilder Add(CardBehaviorDefinition spec) => AddTo(_generic, spec, "generic behavior");

    public BehaviorCatalogBuilder Add(IEnumerable<CardBehaviorDefinition> specs) => AddMany(specs, Add);

    /// <summary>
    /// Registers a compiled card class for typed hidden-zone filters. This is
    /// deliberately separate from generic behavior, but every classification
    /// must accompany a typed or generic executable registration before the
    /// catalog can be built.
    /// </summary>
    public BehaviorCatalogBuilder AddCardType(CardDefinitionId definitionId, BehaviorCardType cardType)
    {
        if (!StableIdentifier.TryValidate(definitionId.Value))
        {
            throw new ArgumentException("A card classification requires a stable definition ID.", nameof(definitionId));
        }

        if (!Enum.IsDefined(cardType))
        {
            throw new ArgumentOutOfRangeException(nameof(cardType));
        }

        if (_cardTypes.TryGetValue(definitionId, out BehaviorCardType existing)
            && existing != cardType)
        {
            throw new InvalidOperationException(
                $"Card definition '{definitionId}' is already classified as '{existing}', not '{cardType}'.");
        }

        _cardTypes[definitionId] = cardType;
        return this;
    }

    /// <summary>
    /// Registers a definition's printed subtypes for value filters that run
    /// without runtime metadata. The definition needs an executable
    /// registration of its own or in a composed base catalog.
    /// </summary>
    public BehaviorCatalogBuilder AddCardSubtypes(CardDefinitionId definitionId, IReadOnlyList<string> subtypes)
    {
        ArgumentNullException.ThrowIfNull(subtypes);
        if (subtypes.Count == 0 || subtypes.Any(static subtype => !StableIdentifier.TryValidate(subtype)))
        {
            throw new ArgumentException("Registered subtypes must be stable non-empty identifiers.", nameof(subtypes));
        }

        _cardSubtypes[definitionId] = subtypes.ToArray();
        return this;
    }

    /// <summary>Opts the catalog into typed turn-event tracking.</summary>
    public BehaviorCatalogBuilder TrackTurnEvents()
    {
        _tracksTurnEvents = true;
        return this;
    }

    /// <summary>VP.2: opts the catalog into recording turn events that only some cards read.</summary>
    public BehaviorCatalogBuilder RecordTurnEvents(params TurnEventKind[] kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        _tracksTurnEvents = true;
        _recordedTurnEvents.UnionWith(kinds);
        return this;
    }

    public BehaviorCatalog Build() => new(
        Version,
        _ice,
        _icebreakers,
        _assets,
        _agendas,
        _runEvents,
        _generic,
        _cardTypes,
        _cardSubtypes,
        _tracksTurnEvents,
        _recordedTurnEvents);

    private BehaviorCatalogBuilder AddTyped<T>(
        Dictionary<CardDefinitionId, T> map,
        T spec,
        string family)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(spec);
        CardDefinitionId definitionId = DefinitionIdOf(spec);
        if (_typedFamilies.TryGetValue(definitionId, out string? existingFamily)
            && !string.Equals(existingFamily, family, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Card definition '{definitionId}' cannot be registered as both {existingFamily} and {family} behavior.");
        }

        AddTo(map, spec, family);
        _typedFamilies.TryAdd(definitionId, family);
        return this;
    }

    private BehaviorCatalogBuilder AddMany<T>(IEnumerable<T> specs, Func<T, BehaviorCatalogBuilder> add)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(specs);
        foreach (T spec in specs)
        {
            add(spec);
        }

        return this;
    }

    private BehaviorCatalogBuilder AddTo<T>(Dictionary<CardDefinitionId, T> map, T spec, string family)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(spec);
        CardDefinitionId definitionId = DefinitionIdOf(spec, family);

        if (map.TryGetValue(definitionId, out T? existing))
        {
            if (!BehaviorCatalogStructure.AreEqual(existing, spec))
            {
                throw new InvalidOperationException(
                    $"Card definition '{definitionId}' is already registered with a different {family} behavior.");
            }

            return this;
        }

        map.Add(definitionId, spec);
        return this;
    }

    private static CardDefinitionId DefinitionIdOf<T>(T spec, string? family = null)
        where T : class => spec switch
        {
            IceBehaviorSpec ice => ice.DefinitionId,
            IcebreakerBehaviorSpec breaker => breaker.DefinitionId,
            AssetBehaviorSpec asset => asset.DefinitionId,
            AgendaBehaviorSpec agenda => agenda.DefinitionId,
            RunEventBehaviorSpec runEvent => runEvent.DefinitionId,
            CardBehaviorDefinition generic => generic.DefinitionId,
            _ => throw new ArgumentException(
                $"Unsupported {family ?? "behavior"} contract type '{typeof(T).Name}'.",
                nameof(spec)),
        };
}

internal static class BehaviorCatalogStructure
{
    public static bool AreEqual<T>(T left, T right)
        where T : class =>
        (left, right) switch
        {
            (IceBehaviorSpec a, IceBehaviorSpec b) => a.DefinitionId == b.DefinitionId
                && a.Strength == b.Strength
                && a.IceType == b.IceType
                && a.RezCost == b.RezCost
                && string.Equals(a.BreakableOnlyBySubtype, b.BreakableOnlyBySubtype, StringComparison.Ordinal)
                && SequenceEquals(a.Subroutines, b.Subroutines)
                && SequenceEquals(a.StrengthModifiers, b.StrengthModifiers)
                && BehaviorContractStructure.SequenceEquals(
                    a.ValueModifiers ?? [],
                    b.ValueModifiers ?? [],
                    BehaviorContractStructure.AreEqual),
            (IcebreakerBehaviorSpec a, IcebreakerBehaviorSpec b) => a.DefinitionId == b.DefinitionId
                && a.BaseStrength == b.BaseStrength
                && a.InterfaceType == b.InterfaceType
                && a.BreakCost == b.BreakCost
                && a.BreakLimit == b.BreakLimit
                && a.PumpCost == b.PumpCost
                && a.PumpStrength == b.PumpStrength
                && BehaviorContractStructure.AreEqual(a.DynamicPump, b.DynamicPump)
                && BehaviorContractStructure.SequenceEquals(
                    a.ValueModifiers ?? [],
                    b.ValueModifiers ?? [],
                    BehaviorContractStructure.AreEqual),
            (AssetBehaviorSpec a, AssetBehaviorSpec b) => a.DefinitionId == b.DefinitionId && a.TrashCost == b.TrashCost,
            (AgendaBehaviorSpec a, AgendaBehaviorSpec b) => a.DefinitionId == b.DefinitionId && a.AgendaPoints == b.AgendaPoints,
            (RunEventBehaviorSpec a, RunEventBehaviorSpec b) => a.DefinitionId == b.DefinitionId
                && a.Cost == b.Cost
                && a.HostedCredits == b.HostedCredits
                && BehaviorContractStructure.SequenceEquals(
                    a.RezCostModifiers ?? [],
                    b.RezCostModifiers ?? [],
                    static (left, right) => left.Equals(right))
                && BehaviorContractStructure.SequenceEquals(
                    a.AllowedTargetServers ?? [],
                    b.AllowedTargetServers ?? [],
                    static (left, right) => left == right)
                && a.SuccessDraw == b.SuccessDraw
                && a.BreachAccessModifier == b.BreachAccessModifier
                && a.AdditionalClicks == b.AdditionalClicks
                && a.RedirectApproachTo == b.RedirectApproachTo
                && a.PreventFirstEndRun == b.PreventFirstEndRun,
            (CardBehaviorDefinition a, CardBehaviorDefinition b) => a.DefinitionId == b.DefinitionId
                && a.Controller == b.Controller
                && a.CanBeAdvanced == b.CanBeAdvanced
                && a.RunnerCannotTrash == b.RunnerCannotTrash
                && a.RecurringCredits == b.RecurringCredits
                && a.RootAssetTrashCostIncrease == b.RootAssetTrashCostIncrease
                && SequenceEquals(a.RezRequirements ?? [], b.RezRequirements ?? [])
                && a.HostedCardsPlayAsGrip == b.HostedCardsPlayAsGrip
                && a.RezForfeitDiscount == b.RezForfeitDiscount
                && a.HostIcebreakerStrength == b.HostIcebreakerStrength
                && a.StealCredits == b.StealCredits
                && a.ScoreCostAsAgenda == b.ScoreCostAsAgenda
                && a.FirstDoubleOperationClickDiscount == b.FirstDoubleOperationClickDiscount
                && a.ReturnsHostedBadPublicity == b.ReturnsHostedBadPublicity
                && SequenceEquals(a.InstallServerKinds ?? [], b.InstallServerKinds ?? [])
                && SequenceEquals(a.HostedCreditUses ?? [], b.HostedCreditUses ?? [])
                && a.Hosting == b.Hosting
                && SequenceEquals(a.Costs, b.Costs)
                && SequenceEquals(a.Conditions, b.Conditions)
                && BehaviorContractStructure.SequenceEquals(
                    a.Targets,
                    b.Targets,
                    BehaviorContractStructure.AreEqual)
                && BehaviorContractStructure.SequenceEquals(
                    a.Instructions,
                    b.Instructions,
                    BehaviorContractStructure.AreEqual)
                && BehaviorContractStructure.SequenceEquals(
                    a.StaticModifiers,
                    b.StaticModifiers,
                    BehaviorContractStructure.AreEqual)
                && BehaviorContractStructure.SequenceEquals(
                    a.Triggers,
                    b.Triggers,
                    static (left, right) => left.Equals(right))
                && BehaviorContractStructure.SequenceEquals(
                    a.PaidAbilities,
                    b.PaidAbilities,
                    static (left, right) => left.Equals(right))
                && BehaviorContractStructure.SequenceEquals(
                    a.CostModifiers ?? [],
                    b.CostModifiers ?? [],
                    static (left, right) => left.Equals(right))
                && BehaviorContractStructure.SequenceEquals(
                    a.ValueModifiers ?? [],
                    b.ValueModifiers ?? [],
                    BehaviorContractStructure.AreEqual)
                && BehaviorContractStructure.SequenceEquals(
                    a.FirstProgramInstallCostModifiers ?? [],
                    b.FirstProgramInstallCostModifiers ?? [],
                    static (left, right) => left.Equals(right))
                && BehaviorContractStructure.SequenceEquals(
                    a.FirstHqBreachAccessModifiers ?? [],
                    b.FirstHqBreachAccessModifiers ?? [],
                    static (left, right) => left.Equals(right))
                && BehaviorContractStructure.SequenceEquals(
                    a.FirstBasicDrawReplacements ?? [],
                    b.FirstBasicDrawReplacements ?? [],
                    static (left, right) => left.Equals(right))
                && a.ExtensionHandlerId == b.ExtensionHandlerId,
            _ => false,
        };

    public static bool SequenceEquals<T>(IReadOnlyList<T> left, IReadOnlyList<T> right)
        where T : notnull => left.SequenceEqual(right);

}

internal static class BehaviorCatalogFingerprint
{
    internal static string ComputeDefinition(CardBehaviorDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var builder = new StringBuilder();
        builder.Append("definition|").Append(definition.DefinitionId.Value).Append('|');
        AppendGeneric(builder, definition);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    public static string Compute(
        string version,
        IReadOnlyDictionary<CardDefinitionId, IceBehaviorSpec> ice,
        IReadOnlyDictionary<CardDefinitionId, IcebreakerBehaviorSpec> icebreakers,
        IReadOnlyDictionary<CardDefinitionId, AssetBehaviorSpec> assets,
        IReadOnlyDictionary<CardDefinitionId, AgendaBehaviorSpec> agendas,
        IReadOnlyDictionary<CardDefinitionId, RunEventBehaviorSpec> runEvents,
        IReadOnlyDictionary<CardDefinitionId, CardBehaviorDefinition> generic,
        IReadOnlyDictionary<CardDefinitionId, BehaviorCardType>? cardTypes = null,
        IReadOnlyDictionary<CardDefinitionId, IReadOnlyList<string>>? cardSubtypes = null,
        bool tracksTurnEvents = false)
    {
        var builder = new StringBuilder();
        builder.Append("version\n").Append(version).Append('\n');
        AppendFamily(builder, "ice", ice, AppendIce);
        AppendFamily(builder, "icebreaker", icebreakers, AppendIcebreaker);
        AppendFamily(builder, "asset", assets, AppendAsset);
        AppendFamily(builder, "agenda", agendas, AppendAgenda);
        AppendFamily(builder, "run-event", runEvents, AppendRunEvent);
        AppendFamily(builder, "generic", generic, AppendGeneric);
        if (cardTypes is not null)
        {
            AppendFamily(builder, "card-type", cardTypes, AppendCardType);
        }

        // Emitted only when present, so catalogs without subtype
        // registrations keep their historical fingerprint bytes.
        if (tracksTurnEvents)
        {
            builder.Append("tracks-turn-events\n");
        }

        if (cardSubtypes is { Count: > 0 })
        {
            AppendFamily(builder, "card-subtypes", cardSubtypes,
                static (target, subtypes) => target.Append(string.Join(',', subtypes)));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    internal static string ComputeCard(
        CardDefinitionId id,
        IReadOnlyDictionary<CardDefinitionId, IceBehaviorSpec> ice,
        IReadOnlyDictionary<CardDefinitionId, IcebreakerBehaviorSpec> icebreakers,
        IReadOnlyDictionary<CardDefinitionId, AssetBehaviorSpec> assets,
        IReadOnlyDictionary<CardDefinitionId, AgendaBehaviorSpec> agendas,
        IReadOnlyDictionary<CardDefinitionId, RunEventBehaviorSpec> runEvents,
        IReadOnlyDictionary<CardDefinitionId, CardBehaviorDefinition> generic,
        IReadOnlyDictionary<CardDefinitionId, BehaviorCardType> cardTypes,
        IReadOnlyDictionary<CardDefinitionId, IReadOnlyList<string>> cardSubtypes)
    {
        var builder = new StringBuilder();
        builder.Append("card\n").Append(id.Value).Append('\n');
        AppendFamily(builder, "ice", Only(ice, id), AppendIce);
        AppendFamily(builder, "icebreaker", Only(icebreakers, id), AppendIcebreaker);
        AppendFamily(builder, "asset", Only(assets, id), AppendAsset);
        AppendFamily(builder, "agenda", Only(agendas, id), AppendAgenda);
        AppendFamily(builder, "run-event", Only(runEvents, id), AppendRunEvent);
        AppendFamily(builder, "generic", Only(generic, id), AppendGeneric);
        AppendFamily(builder, "card-type", Only(cardTypes, id), AppendCardType);
        AppendFamily(builder, "card-subtypes", Only(cardSubtypes, id),
            static (target, subtypes) => target.Append(string.Join(',', subtypes)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static Dictionary<CardDefinitionId, T> Only<T>(IReadOnlyDictionary<CardDefinitionId, T> values, CardDefinitionId id)
        where T : notnull =>
        values.TryGetValue(id, out T? value) ? new() { [id] = value } : [];

    private static void AppendFamily<T>(
        StringBuilder builder,
        string family,
        IReadOnlyDictionary<CardDefinitionId, T> values,
        Action<StringBuilder, T> append)
        where T : notnull
    {
        foreach ((CardDefinitionId id, T value) in values.OrderBy(static pair => pair.Key.Value, StringComparer.Ordinal))
        {
            builder.Append(family).Append('|').Append(id.Value).Append('|');
            append(builder, value);
            builder.Append('\n');
        }
    }

    private static void AppendIce(StringBuilder builder, IceBehaviorSpec value)
    {
        builder.Append(value.Strength).Append('|').Append(value.IceType).Append('|').Append(value.RezCost);
        foreach (IceSubroutineSpec subroutine in value.Subroutines)
        {
            builder.Append('|').Append(subroutine.GetType().Name).Append(':').Append(subroutine);
        }

        foreach (IceStrengthModifierSpec modifier in value.StrengthModifiers)
        {
            builder.Append('|').Append(modifier.Delta).Append(':').Append(modifier.Condition);
        }

        if (value.ValueModifiers is { Count: > 0 })
        {
            AppendSequence(builder, value.ValueModifiers, AppendValueModifier);
        }

        if (value.BreakableOnlyBySubtype is { } breakerSubtype)
        {
            builder.Append("|breakable-only-by=").Append(breakerSubtype);
        }

    }

    private static void AppendIcebreaker(StringBuilder builder, IcebreakerBehaviorSpec value)
    {
        builder.Append(value.BaseStrength).Append('|').Append(value.InterfaceType).Append('|')
            .Append(value.BreakCost).Append('|').Append(value.BreakLimit).Append('|')
            .Append(value.PumpCost).Append('|').Append(value.PumpStrength);
        if (value.DynamicPump is { } dynamicPump)
        {
            builder.Append("|dynamic-pump=");
            AppendDynamicPump(builder, dynamicPump);
        }

        if (value.ValueModifiers is { Count: > 0 })
        {
            AppendSequence(builder, value.ValueModifiers, AppendValueModifier);
        }

    }

    private static void AppendAsset(StringBuilder builder, AssetBehaviorSpec value) => builder.Append(value.TrashCost);

    private static void AppendAgenda(StringBuilder builder, AgendaBehaviorSpec value)
    {
        builder.Append(value.AgendaPoints);
        // Vantage Point's declarations are emitted only when used, so other agendas keep their bytes.
        if (value.RunnerScoreAreaPoints is { } runnerPoints) builder.Append("|runnerScoreAreaPoints=").Append(runnerPoints);
        if (value.StealClicks != 0) builder.Append("|stealClicks=").Append(value.StealClicks);
        if (value.InstallsFaceUp) builder.Append("|installsFaceUp=1");
    }

    private static void AppendRunEvent(StringBuilder builder, RunEventBehaviorSpec value)
    {
        builder.Append(value.Cost).Append('|').Append(value.HostedCredits);
        if (value.RezCostModifiers is { Count: > 0 } modifiers)
        {
            builder.Append("|rezCostModifiers=");
            AppendSequence(builder, modifiers, AppendRunRezCostModifier);
        }

        // Keep the legacy byte projection for run events that do not use the
        // Slice 4.5 declarations. New segments are emitted only when the
        // corresponding optional contract is present.
        if (value.AllowedTargetServers is { Count: > 0 } servers)
        {
            builder.Append("|allowedTargetServers=");
            foreach (ServerKind server in servers)
            {
                builder.Append((int)server).Append(',');
            }
        }

        if (value.SuccessDraw is { } successDraw)
        {
            builder.Append("|successDraw=draw=")
                .Append(successDraw.DrawCount)
                .Append(",reason=");
            AppendString(builder, successDraw.ReasonKey);
        }

        if (value.BreachAccessModifier is { } breachAccessModifier)
        {
            builder.Append("|breachAccessModifier=delta=")
                .Append(breachAccessModifier.Delta)
                .Append(",reason=");
            AppendString(builder, breachAccessModifier.ReasonKey);
        }

        if (value.AdditionalClicks != 0)
        {
            builder.Append("|additionalClicks=").Append(value.AdditionalClicks);
        }

        if (value.RedirectApproachTo is { } redirect)
        {
            builder.Append("|redirectApproachTo=").Append((int)redirect);
        }

        if (value.PreventFirstEndRun)
        {
            builder.Append("|preventFirstEndRun=1");
        }

        if (value.LocksCreditPool)
        {
            builder.Append("|locksCreditPool=1");
        }

        if (value.CostLoweredPerIceOn is { } perIce)
        {
            builder.Append("|costLoweredPerIceOn=").Append((int)perIce);
        }

        if (value.RemovedFromGameAfterRun)
        {
            builder.Append("|removedFromGameAfterRun=1");
        }

        if (value.TargetMustBeProtected)
        {
            builder.Append("|targetMustBeProtected=1");
        }
    }

    private static void AppendRunRezCostModifier(
        StringBuilder builder,
        BehaviorRunRezCostModifier value)
    {
        builder.Append("delta=").Append(value.Delta).Append(",reason=");
        AppendString(builder, value.ReasonKey);
    }

    private static void AppendCardType(StringBuilder builder, BehaviorCardType value) =>
        builder.Append((int)value);

    private static void AppendGeneric(StringBuilder builder, CardBehaviorDefinition value)
    {
        builder.Append("controller=").Append((int)value.Controller);
        builder.Append("|extension=");
        AppendString(builder, value.ExtensionHandlerId);
        AppendSequence(builder, value.Costs, AppendCost);
        AppendSequence(builder, value.Conditions, AppendCondition);
        if (value.CanBeAdvanced)
        {
            builder.Append(",canBeAdvanced=1");
        }
        if (value.RunnerCannotTrash)
        {
            builder.Append(",runnerCannotTrash=1");
        }
        if (value.RecurringCredits != 0)
        {
            builder.Append(",recurringCredits=").Append(value.RecurringCredits);
        }
        if (value.RootAssetTrashCostIncrease != 0)
        {
            builder.Append(",rootAssetTrashCostIncrease=").Append(value.RootAssetTrashCostIncrease);
        }
        if (value.HostedCardsPlayAsGrip)
        {
            builder.Append(",hostedCardsPlayAsGrip=1");
        }
        if (value.RezForfeitDiscount != 0)
        {
            builder.Append(",rezForfeitDiscount=").Append(value.RezForfeitDiscount);
        }
        if (value.HostIcebreakerStrength != 0)
        {
            builder.Append(",hostIcebreakerStrength=").Append(value.HostIcebreakerStrength);
        }
        if (value.RezRequirements is { } rezRequirements)
        {
            builder.Append(",rezRequirements=");
            AppendSequence(builder, rezRequirements, AppendCondition);
        }
        if (value.StealCredits != 0)
        {
            builder.Append(",stealCredits=").Append(value.StealCredits);
        }
        if (value.ReturnsHostedBadPublicity)
        {
            builder.Append(",returnsHostedBadPublicity=1");
        }
        if (value.InstallServerKinds is { } installKinds)
        {
            builder.Append(",installServerKinds=").Append(string.Join("+", installKinds.Select(static kind => (int)kind)));
        }
        if (value.FirstDoubleOperationClickDiscount != 0)
        {
            builder.Append(",firstDoubleClickDiscount=").Append(value.FirstDoubleOperationClickDiscount);
        }
        if (value.ScoreCostAsAgenda is { } scoreCost)
        {
            builder.Append(",scoreCostAsAgenda=").Append(scoreCost.Points)
                .Append(':').Append(scoreCost.CannotForfeit ? 1 : 0)
                .Append(':').Append(scoreCost.OnlyAgendasInstalledThisTurn ? 1 : 0);
        }
        if (value.HostedCreditUses is { } uses)
        {
            builder.Append(",hostedCreditUses=");
            foreach (BehaviorHostedCreditUse use in uses)
            {
                builder.Append((int)use.Kind).Append(':').Append(string.Join('+', use.Subtypes)).Append(';');
            }
        }
        if (value.Hosting is { } hosting)
        {
            builder.Append("|hosting=").Append(hosting.BreakHostSubroutine ? 1 : 0)
                .Append(':').Append(hosting.DerezAtVirusCounters);
            if (hosting.HostGainsEveryIceType)
            {
                builder.Append(":gains-every-ice-type");
            }
        }
        AppendSequence(builder, value.Targets, AppendTarget);
        AppendSequence(builder, value.Instructions, AppendInstruction);
        AppendSequence(builder, value.StaticModifiers, AppendStaticModifier);
        AppendSequence(builder, value.Triggers, AppendTrigger);
        // Keep the byte projection of existing M2 definitions unchanged;
        // only definitions that actually declare the new field receive its
        // paid-ability fingerprint segment.
        if (value.PaidAbilities.Count > 0)
        {
            AppendSequence(builder, value.PaidAbilities, AppendPaidAbility);
        }

        // Preserve historical M2 fingerprint bytes for definitions that do
        // not use conditional cost modifiers. The new segment is present only
        // for definitions that actually declare one.
        if (value.CostModifiers is { Count: > 0 })
        {
            AppendSequence(builder, value.CostModifiers, AppendCostModifier);
        }

        if (value.ValueModifiers is { Count: > 0 })
        {
            AppendSequence(builder, value.ValueModifiers, AppendValueModifier);
        }

        if (value.FirstProgramInstallCostModifiers is { Count: > 0 })
        {
            builder.Append("|firstProgramInstallCostModifiers=");
            AppendSequence(builder, value.FirstProgramInstallCostModifiers, AppendFirstProgramInstallCostModifier);
        }

        if (value.FirstHqBreachAccessModifiers is { Count: > 0 })
        {
            builder.Append("|firstHqBreachAccessModifiers=");
            AppendSequence(builder, value.FirstHqBreachAccessModifiers, AppendFirstHqBreachAccessModifier);
        }

        if (value.FirstBasicDrawReplacements is { Count: > 0 })
        {
            builder.Append("|firstBasicDrawReplacements=");
            AppendSequence(builder, value.FirstBasicDrawReplacements, AppendFirstBasicDrawReplacement);
        }
    }

    private static void AppendCostModifier(StringBuilder builder, BehaviorCostModifier value)
    {
        builder.Append("kind=").Append((int)value.Kind)
            .Append(",delta=").Append(value.Delta)
            .Append(",condition=");
        AppendCondition(builder, value.Condition);
        builder.Append(",reason=");
        AppendString(builder, value.ReasonKey);
        if (value.PerInstalledFilter is { } perInstalled)
        {
            builder.Append(",per-installed=");
            AppendFilter(builder, perInstalled);
        }
    }

    private static void AppendFirstProgramInstallCostModifier(
        StringBuilder builder,
        BehaviorFirstProgramInstallCostModifier value)
    {
        builder.Append("delta=").Append(value.Delta).Append(",reason=");
        AppendString(builder, value.ReasonKey);
    }

    private static void AppendFirstHqBreachAccessModifier(
        StringBuilder builder,
        BehaviorFirstHqBreachAccessModifier value)
    {
        builder.Append("delta=").Append(value.Delta).Append(",reason=");
        AppendString(builder, value.ReasonKey);
    }

    private static void AppendFirstBasicDrawReplacement(
        StringBuilder builder,
        BehaviorFirstBasicDrawReplacement value)
    {
        builder.Append("drawCount=").Append(value.DrawCount).Append(",reason=");
        AppendString(builder, value.ReasonKey);
    }

    private static void AppendValueModifier(StringBuilder builder, BehaviorValueModifier value)
    {
        builder.Append("value=").Append((int)value.ValueKind)
            .Append(",source=").Append((int)value.Source)
            .Append(",operation=").Append((int)value.Operation)
            .Append(",delta=").Append(value.Delta)
            .Append(",reason=");
        AppendString(builder, value.ReasonKey);
        builder.Append(",condition=");
        AppendCondition(builder, value.Condition);
        builder.Append(",filter=");
        AppendFilter(builder, value.InstalledFilter);
        builder.Append(",variable=");
        AppendString(builder, value.VariableKey);
        builder.Append(",counter=");
        AppendString(builder, value.CounterName);
        builder.Append(",threshold=");
        AppendNullableInt(builder, value.Threshold);
        if (value.Zone is { } zone)
        {
            builder.Append(",zone=").Append((int)zone);
        }
    }

    private static void AppendDynamicPump(
        StringBuilder builder,
        BehaviorDynamicPumpDeclaration value)
    {
        builder.Append("value=").Append((int)value.ValueKind)
            .Append(",source=").Append((int)value.Source)
            .Append(",operation=").Append((int)value.Operation)
            .Append(",unitDelta=").Append(value.UnitDelta)
            .Append(",variable=");
        AppendString(builder, value.VariableKey);
        builder.Append(",reason=");
        AppendString(builder, value.ReasonKey);
        builder.Append(",filter=");
        AppendFilter(builder, value.InstalledFilter);
    }

    private static void AppendPaidAbility(StringBuilder builder, BehaviorPaidAbility value)
    {
        builder.Append("key=");
        AppendString(builder, value.Key);
        builder.Append(",timing=").Append((int)value.Timing)
            .Append(",activation=").Append((int)value.Activation)
            .Append(",usageLimit=").Append((int)value.UsageLimit);
        AppendSequence(builder, value.Costs, AppendCost);
        AppendSequence(builder, value.Conditions, AppendCondition);
        AppendSequence(builder, value.Targets, AppendTarget);
        AppendSequence(builder, value.Instructions, AppendInstruction);
        if (value.UsableFromRunnerScoreArea)
        {
            builder.Append(",fromRunnerScoreArea=1");
        }
        if (value.UsableFromArchives)
        {
            builder.Append(",fromArchives=1");
        }
    }

    private static void AppendSequence<T>(
        StringBuilder builder,
        IReadOnlyList<T> values,
        Action<StringBuilder, T> append)
    {
        builder.Append("|count=").Append(values.Count).Append('[');
        foreach (T value in values)
        {
            append(builder, value);
            builder.Append(';');
        }

        builder.Append(']');
    }

    private static void AppendCost(StringBuilder builder, BehaviorCost value)
    {
        builder.Append("kind=").Append((int)value.Kind)
            .Append(",amount=").Append(value.Amount)
            .Append(",counter=");
        AppendString(builder, value.CounterName);
        // Preserve historical M2 fingerprint bytes for existing cost kinds;
        // the additional key is encoded only when the new cost field is in
        // use, so adding the schema does not rewrite pinned catalogs.
        if (value.AdditionalKey is not null)
        {
            builder.Append(",additional=");
            AppendString(builder, value.AdditionalKey);
        }

        if (value.AdditionalTarget is not null)
        {
            builder.Append(",additionalTarget=");
            AppendTarget(builder, value.AdditionalTarget);
        }

        if (value.AdditionalKind is { } additionalKind)
        {
            builder.Append(",additionalKind=").Append((int)additionalKind);
        }

        if (value.CreditSourceSubtype is not null)
        {
            builder.Append(",creditSource=");
            AppendString(builder, value.CreditSourceSubtype);
        }
    }

    private static void AppendCondition(StringBuilder builder, BehaviorCondition? value)
    {
        if (value is null)
        {
            builder.Append("null");
            return;
        }

        builder.Append("kind=").Append((int)value.Kind)
            .Append(",fact=");
        AppendString(builder, value.Fact);
        builder.Append(",value=");
        AppendNullableInt(builder, value.Value);
        // Preserve the historical fingerprint bytes for controller-relative
        // conditions. The subject field is encoded only for an explicit
        // Tagged/NotTagged subject so adding this optional contract field does
        // not invalidate existing behavior definition pins.
        if (value.SubjectSide is { } subjectSide)
        {
            builder.Append(",subject=").Append((int)subjectSide);
        }
    }

    private static void AppendTarget(StringBuilder builder, BehaviorTarget? value)
    {
        if (value is null)
        {
            builder.Append("null");
            return;
        }

        builder.Append("kind=").Append((int)value.Kind)
            .Append(",selector=");
        AppendString(builder, value.Selector);
        builder.Append(",zone=");
        AppendNullableEnum(builder, value.Zone);
        builder.Append(",filter=");
        AppendFilter(builder, value.Filter);
        builder.Append(",reveal=").Append(value.Reveal ? '1' : '0');
    }

    private static void AppendInstruction(StringBuilder builder, BehaviorInstruction value)
    {
        builder.Append("kind=").Append((int)value.Kind)
            .Append(",amount=").Append(value.Amount)
            .Append(",argument=");
        AppendString(builder, value.Argument);
        builder.Append(",target=");
        AppendTarget(builder, value.Target);
        builder.Append(",source=");
        AppendNullableEnum(builder, value.SourceZone);
        builder.Append(",destination=");
        AppendNullableEnum(builder, value.DestinationZone);
        builder.Append(",filter=");
        AppendFilter(builder, value.Filter);
        builder.Append(",condition=");
        AppendCondition(builder, value.Condition);
        builder.Append(",choice=");
        AppendChoice(builder, value.Choice);
        builder.Append(",branch=");
        AppendBranch(builder, value.Branch);
        builder.Append(",optional=").Append(value.Optional ? '1' : '0');
        // Preserve historical fingerprint bytes for instructions that do not
        // use the hosted-credit operand. Hosted-credit load/place declarations
        // carry the normalized restriction, including the default
        // CardEffectOnly value.
        if (value.SpendRestriction is { } spendRestriction)
        {
            builder.Append(",spendRestriction=").Append((int)spendRestriction);
        }

        // Keep the historical byte projection for instructions that do not
        // declare a run operand. Only the new non-null typed operand receives
        // this additional fingerprint segment.
        if (value.RunOperand is { } runOperand)
        {
            builder.Append(",runOperand=serverKind=").Append((int)runOperand.ServerKind)
                .Append(",requireNotRunThisTurn=").Append(runOperand.RequireNotRunThisTurn ? '1' : '0')
                .Append(",sourceCounter=");
            AppendString(builder, runOperand.SourceCounterName);
            builder.Append(",delta=");
            AppendNullableInt(builder, runOperand.BreachAccessDeltaPerCounter);
            builder.Append(",reason=");
            AppendString(builder, runOperand.BreachAccessReasonKey);
        }

        // Dynamic amounts are structural operands. Keep the legacy bytes for
        // static instructions and add this segment only when the new
        // vocabulary is actually used.
        if (value.AmountOperand is { } amountOperand)
        {
            builder.Append(",amountOperand=counter=");
            AppendString(builder, amountOperand.SourceCounterName);
            builder.Append(",multiplier=").Append(amountOperand.Multiplier);
            if (amountOperand.Kind is { } amountKind)
            {
                builder.Append(",amountKind=").Append((int)amountKind).Append(",recorded=");
                AppendString(builder, amountOperand.RecordedKey);
                builder.Append(",offset=").Append(amountOperand.Offset);
            }
        }

        if (value.RecordAs is { } recordAs)
        {
            builder.Append(",recordAs=");
            AppendString(builder, recordAs);
        }

        if (value.CostRule is { } costRule)
        {
            builder.Append(",costRule=").Append((int)costRule);
        }

        if (value.DamageKind is { } damageKind)
        {
            builder.Append(",damageKind=").Append((int)damageKind);
        }
        if (value.DamageAmountOperand is { } damageAmount)
        {
            builder.Append(",damageAmount=").Append((int)damageAmount.Kind)
                .Append(",base=").Append(damageAmount.BaseAmount)
                .Append(",trashSourceAsCost=").Append(damageAmount.TrashSourceAsCost ? '1' : '0');
        }

        // Preserve historical fingerprints for instructions that retain the
        // legacy controller recipient. Encode only an explicit selector.
        if (value.Recipient is { } recipient)
        {
            builder.Append(",recipient=").Append((int)recipient);
        }
    }

    private static void AppendStaticModifier(StringBuilder builder, BehaviorStaticModifier value)
    {
        builder.Append("kind=");
        AppendNullableEnum(builder, value.Kind);
        builder.Append(",key=");
        AppendString(builder, value.ValueKey);
        builder.Append(",delta=").Append(value.Delta)
            .Append(",reason=");
        AppendString(builder, value.ReasonKey);
    }

    private static void AppendTrigger(StringBuilder builder, BehaviorTrigger value)
    {
        builder.Append("key=");
        AppendString(builder, value.Key);
        builder.Append(",occurrence=").Append((int)value.Occurrence)
            .Append(",mode=").Append((int)value.Mode)
            .Append(",scope=").Append((int)value.Scope)
            .Append(",subject=");
        AppendFilter(builder, value.SubjectCardFilter);
        if (value.RunOutcomeRequirement is { } outcomeRequirement)
        {
            builder.Append(",runOutcome=").Append((int)outcomeRequirement);
        }

        if (value.ServerKindRequirement is { } serverKindRequirement)
        {
            builder.Append(",serverKind=").Append((int)serverKindRequirement);
        }

        // Preserve historical fingerprints for trigger declarations that do
        // not use the optional source-topology predicate.
        if (value.RequireSourceOnAttackedServer)
        {
            builder.Append(",requireSourceOnAttackedServer=1");
        }

        if (value.RequireSourceInstalled)
        {
            builder.Append(",requireSourceInstalled=1");
        }
        if (value.OccurrenceControllerSide is { } occurrenceSide) builder.Append(",occurrenceSide=").Append((int)occurrenceSide);
        if (value.OncePerTurn) builder.Append(",oncePerTurn=1");
        if (value.Condition is { } condition) builder.Append(",condition=").Append((int)condition);
        if (value.Requirement is { } requirement)
        {
            builder.Append(",requirement=");
            AppendCondition(builder, requirement);
        }
        if (value.Face is { } face) builder.Append(",face=").Append((int)face);
        if (value.Persistent) builder.Append(",persistent=1");
        if (value.RequireAgendaStolenDuringRun) builder.Append(",requireAgendaStolenDuringRun=1");
        if (value.ActiveWhileUnrezzed)
        {
            builder.Append(",activeWhileUnrezzed=1");
        }
        if (value.ActiveWhileAccessed)
        {
            builder.Append(",activeWhileAccessed=1");
        }
        builder.Append(",instructions=");
        AppendSequence(builder, value.Instructions, AppendInstruction);
    }

    private static void AppendChoice(StringBuilder builder, BehaviorChoiceSpec? value)
    {
        if (value is null)
        {
            builder.Append("null");
            return;
        }

        builder.Append("key=");
        AppendString(builder, value.Key);
        builder.Append(",chooser=").Append((int)value.Chooser)
            .Append(",mode=").Append((int)value.Mode)
            .Append(",cardinality=");
        AppendCardinality(builder, value.Cardinality);
        builder.Append(",options=");
        AppendSequence(builder, value.Options, AppendChoiceOption);
        builder.Append(",target=");
        AppendTarget(builder, value.Target);
    }

    private static void AppendChoiceOption(StringBuilder builder, BehaviorChoiceOption value)
    {
        builder.Append("key=");
        AppendString(builder, value.Key);
        builder.Append(",instructions=");
        AppendSequence(builder, value.Instructions, AppendInstruction);
        builder.Append(",target=");
        AppendTarget(builder, value.Target);
        // Keep legacy option fingerprints byte-compatible when the optional
        // structured-alternative fields are absent.
        if (value.Costs is { Count: > 0 })
        {
            builder.Append(",costs=");
            AppendSequence(builder, value.Costs, AppendCost);
        }

        if (value.Semantic is { } semantic)
        {
            builder.Append(",semantic=").Append((int)semantic);
        }

        if (value.Condition is { } condition)
        {
            builder.Append(",condition=");
            AppendCondition(builder, condition);
        }
    }

    private static void AppendBranch(StringBuilder builder, BehaviorBranchSpec? value)
    {
        if (value is null)
        {
            builder.Append("null");
            return;
        }

        builder.Append("key=");
        AppendString(builder, value.Key);
        builder.Append(",condition=");
        AppendCondition(builder, value.Condition);
        builder.Append(",true=");
        AppendSequence(builder, value.WhenTrue, AppendInstruction);
        builder.Append(",false=");
        AppendSequence(builder, value.WhenFalse, AppendInstruction);
    }

    private static void AppendCardinality(StringBuilder builder, BehaviorChoiceCardinality? value)
    {
        if (value is null)
        {
            builder.Append("null");
            return;
        }

        builder.Append("kind=").Append((int)value.Kind).Append(",count=");
        AppendNullableInt(builder, value.Count);
        if (value.ClampsToCandidates)
        {
            builder.Append(",clamps=1");
        }
    }

    private static void AppendFilter(StringBuilder builder, BehaviorCardFilter? value)
    {
        if (value is null)
        {
            builder.Append("null");
            return;
        }

        builder.Append("type=").Append((int)value.CardType)
            .Append(",definition=");
        AppendCardDefinitionId(builder, value.DefinitionId);
        builder.Append(",owner=");
        AppendNullableEnum(builder, value.OwnerSide);
        builder.Append(",subtype=");
        AppendString(builder, value.Subtype);
        if (value.Rezzed is { } rezzed) builder.Append(",rezzed=").Append(rezzed ? 1 : 0);
        if (value.AgendaPoints is { } points) builder.Append(",agendaPoints=").Append(points);
    }

    private static void AppendCardDefinitionId(StringBuilder builder, CardDefinitionId? value) =>
        AppendString(builder, value?.Value);

    private static void AppendNullableInt(StringBuilder builder, int? value)
    {
        if (value is { } number)
        {
            builder.Append(number);
        }
        else
        {
            builder.Append("null");
        }
    }

    private static void AppendNullableEnum<TEnum>(StringBuilder builder, TEnum? value)
        where TEnum : struct, Enum
    {
        if (value is { } enumValue)
        {
            builder.Append((int)(object)enumValue);
        }
        else
        {
            builder.Append("null");
        }
    }

    private static void AppendString(StringBuilder builder, string? value)
    {
        if (value is null)
        {
            builder.Append("null");
            return;
        }

        builder.Append(value.Length).Append(':').Append(value);
    }
}

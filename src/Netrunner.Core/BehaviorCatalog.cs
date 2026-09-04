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
        BehaviorAdditionalCostKind? additionalKind = null)
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
            && kind is not (BehaviorConditionKind.Tagged or BehaviorConditionKind.NotTagged))
        {
            throw new ArgumentException(
                "An explicit subject side is valid only for Tagged and NotTagged conditions.",
                nameof(subjectSide));
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
                or BehaviorConditionKind.FoundCard)
            && (fact is not null || value is not null))
        {
            throw new ArgumentException(
                "This condition kind does not accept a fact or numeric value.",
                fact is not null ? nameof(fact) : nameof(value));
        }

        Fact = fact;
        Kind = kind;
        SubjectSide = subjectSide;
        Value = value;
    }

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

            // Installed selectors are intentionally narrow. The target
            // itself supplies the installed/location predicate; the only
            // additional selector permitted in this slice is the owning
            // side. Definition, subtype, and card-class filters remain
            // hidden-zone vocabulary and must not silently widen this
            // selector's trust boundary.
            if (filter is not null
                && (filter.CardType != BehaviorCardType.Any
                    || filter.DefinitionId is not null
                    || filter.Subtype is not null))
            {
                throw new ArgumentException(
                    "Installed-card targets may carry only an owner-side filter.",
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
        BehaviorAmountOperand? amountOperand = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (kind != BehaviorInstructionKind.ModifyEncounteredIceStrength)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(amount);
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

    private void ValidateOperands()
    {
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

        if (Kind == BehaviorInstructionKind.ModifyEncounteredIceStrength)
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

        if (Kind != BehaviorInstructionKind.GainCredits && AmountOperand is not null)
        {
            throw new ArgumentException(
                "A dynamic amount operand is valid only for GainCredits instructions.",
                nameof(AmountOperand));
        }

        if (Kind == BehaviorInstructionKind.GainCredits
            && AmountOperand is not null
            && Amount != 0)
        {
            throw new ArgumentException(
                "A GainCredits instruction must use either a static amount or a dynamic amount operand, not both.",
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
                or BehaviorInstructionKind.AdvanceCard)
            && (Kind != BehaviorInstructionKind.GainCredits || AmountOperand is null)
            && Amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Amount),
                "This executable numeric instruction requires a positive amount.");
        }

        if (Kind == BehaviorInstructionKind.AddCounter)
        {
            if (Amount <= 0)
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
            or BehaviorInstructionKind.AdvanceCard)
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
                    RequireZone(
                        shuffleSource,
                        nameof(SourceZone),
                        BehaviorZone.Deck,
                        BehaviorZone.Headquarters,
                        BehaviorZone.Archives,
                        BehaviorZone.Stack);
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
                        BehaviorZone.Heap);
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
                        BehaviorZone.Stack);
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
            && (SourceZone is not null || DestinationZone is not null))
        {
            throw new ArgumentException(
                "Source and destination zones are not valid for this instruction kind.",
                nameof(SourceZone));
        }

        if (Kind is not (BehaviorInstructionKind.Reveal
            or BehaviorInstructionKind.Search
            or BehaviorInstructionKind.ShuffleIntoDeck
            or BehaviorInstructionKind.AddToHand
            or BehaviorInstructionKind.AddCounter
            or BehaviorInstructionKind.TrashCard
            or BehaviorInstructionKind.InstallCard
            or BehaviorInstructionKind.AdvanceCard
            or BehaviorInstructionKind.MoveCard
            or BehaviorInstructionKind.Choose)
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

    // Short aliases keep the contract pleasant for callers while the longer
    // names remain unambiguous in serialized/fingerprint output.
    Shuffle = ShuffleIntoDeck,
    Trash = TrashCard,
    Install = InstallCard,
    Advance = AdvanceCard,
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
        ServerKind? serverKindRequirement = null)
    {
        if (!StableIdentifier.TryValidate(key))
        {
            throw new ArgumentException(
                "A behavior trigger key must be a stable identifier.",
                nameof(key));
        }

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
                || occurrence is not (OccurrenceKind.RunSuccessful or OccurrenceKind.RunEnded)))
        {
            throw new ArgumentException(
                "A server-kind requirement is valid only for RunSuccessful or RunEnded triggers and must name a defined server kind.",
                nameof(serverKindRequirement));
        }

        ArgumentNullException.ThrowIfNull(instructions);
        if (instructions.Count == 0)
        {
            throw new ArgumentException(
                "A behavior trigger must declare at least one instruction.",
                nameof(instructions));
        }

        Key = key;
        Occurrence = occurrence;
        Mode = mode;
        Scope = scope;
        SubjectCardFilter = subjectCardFilter;
        Instructions = Freeze(instructions);
        BehaviorRunOperandValidation.RequireNoStartRun(Instructions, nameof(instructions));
        RunOutcomeRequirement = runOutcomeRequirement;
        ServerKindRequirement = serverKindRequirement;
    }

    public string Key { get; }

    public OccurrenceKind Occurrence { get; }

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

    public IReadOnlyList<BehaviorInstruction> Instructions { get; }

    public bool Equals(BehaviorTrigger? other) =>
        other is not null
        && string.Equals(Key, other.Key, StringComparison.Ordinal)
        && Occurrence == other.Occurrence
        && Mode == other.Mode
        && Scope == other.Scope
        && BehaviorContractStructure.AreEqual(SubjectCardFilter, other.SubjectCardFilter)
        && RunOutcomeRequirement == other.RunOutcomeRequirement
        && ServerKindRequirement == other.ServerKindRequirement
        && BehaviorContractStructure.SequenceEquals(
            Instructions,
            other.Instructions,
            BehaviorContractStructure.AreEqual);

    public override bool Equals(object? obj) => Equals(obj as BehaviorTrigger);

    public override int GetHashCode() =>
        HashCode.Combine(
            Key,
            Occurrence,
            Mode,
            Scope,
            SubjectCardFilter,
            RunOutcomeRequirement,
            ServerKindRequirement,
            Instructions.Count);

    private static bool IsCardBearing(OccurrenceKind occurrence) => occurrence is
        OccurrenceKind.CardInstalled
        or OccurrenceKind.CardRezzed
        or OccurrenceKind.RunSuccessful
        or OccurrenceKind.RunEnded
        or OccurrenceKind.AgendaScoredOrStolen
        or OccurrenceKind.CardAccessed
        or OccurrenceKind.HostedCreditPoolEmptied;

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
        IReadOnlyList<BehaviorFirstBasicDrawReplacement>? firstBasicDrawReplacements = null)
    {
        DefinitionId = definitionId;
        if (!Enum.IsDefined(controller))
        {
            throw new ArgumentOutOfRangeException(nameof(controller));
        }

        Controller = controller;
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

    internal BehaviorCatalog(
        string version,
        IReadOnlyDictionary<CardDefinitionId, IceBehaviorSpec> ice,
        IReadOnlyDictionary<CardDefinitionId, IcebreakerBehaviorSpec> icebreakers,
        IReadOnlyDictionary<CardDefinitionId, AssetBehaviorSpec> assets,
        IReadOnlyDictionary<CardDefinitionId, AgendaBehaviorSpec> agendas,
        IReadOnlyDictionary<CardDefinitionId, RunEventBehaviorSpec> runEvents,
        IReadOnlyDictionary<CardDefinitionId, CardBehaviorDefinition> generic,
        IReadOnlyDictionary<CardDefinitionId, BehaviorCardType>? cardTypes = null)
    {
        Version = ValidateVersion(version);
        BehaviorRegistryVersion = Version;
        _ice = Freeze(ice);
        _icebreakers = Freeze(icebreakers);
        _assets = Freeze(assets);
        _agendas = Freeze(agendas);
        _runEvents = Freeze(runEvents);
        _generic = Freeze(generic);
        _cardTypes = FreezeCardTypes(cardTypes ?? new Dictionary<CardDefinitionId, BehaviorCardType>());
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
            _cardTypes);
    }

    public string Version { get; }

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

    /// <summary>Returns the stable fingerprint of one generic definition.</summary>
    public string GetDefinitionFingerprint(CardDefinitionId definitionId)
    {
        if (!_generic.TryGetValue(definitionId, out CardBehaviorDefinition? definition))
        {
            throw new KeyNotFoundException($"Generic behavior definition '{definitionId}' is not registered.");
        }

        return BehaviorCatalogFingerprint.ComputeDefinition(definition);
    }

    public bool TryGetCardType(CardDefinitionId definitionId, out BehaviorCardType cardType) =>
        _cardTypes.TryGetValue(definitionId, out cardType);

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

    public BehaviorCatalog Build() => new(
        Version,
        _ice,
        _icebreakers,
        _assets,
        _agendas,
        _runEvents,
        _generic,
        _cardTypes);

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
                    static (left, right) => left.Equals(right)),
            (CardBehaviorDefinition a, CardBehaviorDefinition b) => a.DefinitionId == b.DefinitionId
                && a.Controller == b.Controller
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
        IReadOnlyDictionary<CardDefinitionId, BehaviorCardType>? cardTypes = null)
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

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

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

    private static void AppendAgenda(StringBuilder builder, AgendaBehaviorSpec value) => builder.Append(value.AgendaPoints);

    private static void AppendRunEvent(StringBuilder builder, RunEventBehaviorSpec value)
    {
        builder.Append(value.Cost).Append('|').Append(value.HostedCredits);
        if (value.RezCostModifiers is { Count: > 0 } modifiers)
        {
            builder.Append("|rezCostModifiers=");
            AppendSequence(builder, modifiers, AppendRunRezCostModifier);
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

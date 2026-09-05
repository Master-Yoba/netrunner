using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

/// <summary>Who receives a declarative choice prompt.</summary>
public enum BehaviorChooser
{
    ActingPlayer,
    Controller,
    Opponent,
    Corp,
    Runner,
}

/// <summary>
/// How a choice is presented. Cardinality is carried separately so a mode
/// choice and a target choice have the same bounded selection contract.
/// </summary>
public enum BehaviorChoiceMode
{
    Mandatory,
    Optional,
    Mode,
    TargetSelection,
    OrderSelection,
}

/// <summary>Cardinality kind for a bounded declarative choice.</summary>
public enum BehaviorChoiceCardinalityKind
{
    Exactly,
    UpTo,
    AnyNumber,
}

/// <summary>Immutable exactly/up-to/any-number choice cardinality.</summary>
public sealed class BehaviorChoiceCardinality : IEquatable<BehaviorChoiceCardinality>
{
    public BehaviorChoiceCardinality(BehaviorChoiceCardinalityKind kind, int? count = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (kind is BehaviorChoiceCardinalityKind.Exactly or BehaviorChoiceCardinalityKind.UpTo)
        {
            if (count is null || count < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(count),
                    "Exactly and up-to cardinalities require a positive count.");
            }
        }
        else if (count is not null)
        {
            throw new ArgumentException(
                "Any-number cardinality must not carry a count.",
                nameof(count));
        }

        Kind = kind;
        Count = count;
    }

    public BehaviorChoiceCardinalityKind Kind { get; }

    public int? Count { get; }

    public static BehaviorChoiceCardinality Exactly(int count) =>
        new(BehaviorChoiceCardinalityKind.Exactly, count);

    public static BehaviorChoiceCardinality UpTo(int count) =>
        new(BehaviorChoiceCardinalityKind.UpTo, count);

    public static BehaviorChoiceCardinality AnyNumber { get; } =
        new(BehaviorChoiceCardinalityKind.AnyNumber);

    public bool Equals(BehaviorChoiceCardinality? other) =>
        other is not null
        && Kind == other.Kind
        && Count == other.Count;

    public override bool Equals(object? obj) => Equals(obj as BehaviorChoiceCardinality);

    public override int GetHashCode() => HashCode.Combine(Kind, Count);
}

/// <summary>
/// Typed card filter for a hidden-zone selection. Subtype is a stable data key,
/// never raw rules text or an executable predicate.
/// </summary>
public sealed class BehaviorCardFilter : IEquatable<BehaviorCardFilter>
{
    [JsonConstructor]
    public BehaviorCardFilter(
        BehaviorCardType cardType = BehaviorCardType.Any,
        CardDefinitionId? definitionId = null,
        PlayerSide? ownerSide = null,
        string? subtype = null,
        bool? rezzed = null)
    {
        if (!Enum.IsDefined(cardType))
        {
            throw new ArgumentOutOfRangeException(nameof(cardType));
        }

        if (definitionId is { } id && !StableIdentifier.TryValidate(id.Value))
        {
            throw new ArgumentException(
                "A card filter definition ID must be a stable identifier.",
                nameof(definitionId));
        }

        if (subtype is not null && !StableIdentifier.TryValidate(subtype))
        {
            throw new ArgumentException(
                "A card-filter subtype must be a stable identifier.",
                nameof(subtype));
        }

        if (ownerSide is { } side && !Enum.IsDefined(side))
        {
            throw new ArgumentOutOfRangeException(nameof(ownerSide));
        }

        Rezzed = rezzed;
        CardType = cardType;
        DefinitionId = definitionId;
        OwnerSide = ownerSide;
        Subtype = subtype;
    }

    public BehaviorCardType CardType { get; }

    public bool? Rezzed { get; }

    public CardDefinitionId? DefinitionId { get; }

    public PlayerSide? OwnerSide { get; }

    public string? Subtype { get; }

    public static BehaviorCardFilter Any { get; } = new();

    public bool Equals(BehaviorCardFilter? other) =>
        other is not null
        && Rezzed == other.Rezzed
        && CardType == other.CardType
        && DefinitionId == other.DefinitionId
        && OwnerSide == other.OwnerSide
        && string.Equals(Subtype, other.Subtype, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as BehaviorCardFilter);

    public override int GetHashCode() => HashCode.Combine(CardType, DefinitionId, OwnerSide, Subtype);
}

/// <summary>Core card categories usable by a declarative card filter.</summary>
public enum BehaviorCardType
{
    Any,
    Agenda,
    Asset,
    Event,
    Hardware,
    Ice,
    Icebreaker,
    Identity,
    Operation,
    Resource,
    Upgrade,
    /// <summary>
    /// A Runner program, including programs that are not icebreakers.  This
    /// remains distinct from <see cref="Icebreaker"/> so trigger filters can
    /// express a printed "virus program" condition without relying on a
    /// title-specific catalog branch.
    /// </summary>
    Program,
    ProgramOrHardware,
    NonAgenda,
}

/// <summary>
/// A data-only dynamic amount used by an executable behavior instruction.
/// The initial vocabulary intentionally has one closed form: read a named
/// counter from the behavior source card and multiply it by a positive
/// constant.  The source and multiplier are structural catalog data, not a
/// persisted computed value, so a resumed frame re-derives the amount from
/// the same authoritative state.
/// </summary>
public sealed class BehaviorAmountOperand : IEquatable<BehaviorAmountOperand>
{
    [JsonConstructor]
    public BehaviorAmountOperand(string sourceCounterName, int multiplier)
    {
        if (!StableIdentifier.TryValidate(sourceCounterName))
        {
            throw new ArgumentException(
                "A dynamic amount source counter name must be a stable identifier.",
                nameof(sourceCounterName));
        }

        if (multiplier <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(multiplier),
                "A dynamic amount multiplier must be positive.");
        }

        SourceCounterName = sourceCounterName;
        Multiplier = multiplier;
    }

    [JsonInclude]
    [JsonRequired]
    public string SourceCounterName { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Multiplier { get; private set; }

    public bool Equals(BehaviorAmountOperand? other) =>
        other is not null
        && string.Equals(SourceCounterName, other.SourceCounterName, StringComparison.Ordinal)
        && Multiplier == other.Multiplier;

    public override bool Equals(object? obj) => Equals(obj as BehaviorAmountOperand);

    public override int GetHashCode() => HashCode.Combine(SourceCounterName, Multiplier);
}

/// <summary>
/// Zones that may be named by a behavior contract. In particular, Deck,
/// Headquarters, and Archives make hidden-zone search/reveal explicit.
/// </summary>
public enum BehaviorZone
{
    Deck,
    Headquarters,
    Archives,
    Grip,
    Stack,
    Heap,
    Rig,
    PlayArea,
    ScoreArea,
    ServerIce,
    ServerRoot,
    RemovedFromGame,
}

/// <summary>
/// Explicit presentation meaning for a declarative choice option. The value
/// is metadata for clients; reducers never infer a fallback from card text or
/// an option key.
/// </summary>
public enum BehaviorChoiceOptionSemantic
{
    Normal,
    Fallback,
}

/// <summary>One option in a mode, target, or ordered choice.</summary>
public sealed class BehaviorChoiceOption : IEquatable<BehaviorChoiceOption>
{
    [JsonConstructor]
    public BehaviorChoiceOption(
        string key,
        IReadOnlyList<BehaviorInstruction>? instructions = null,
        BehaviorTarget? target = null,
        IReadOnlyList<BehaviorCost>? costs = null,
        BehaviorChoiceOptionSemantic? semantic = null)
    {
        if (!StableIdentifier.TryValidate(key))
        {
            throw new ArgumentException(
                "A behavior choice option key must be a stable identifier.",
                nameof(key));
        }

        Key = key;
        Instructions = Freeze(instructions);
        Target = target;
        Costs = FreezeCosts(costs);
        if (semantic is { } value && !Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(semantic));
        }

        Semantic = semantic;
    }

    public string Key { get; }

    public IReadOnlyList<BehaviorInstruction> Instructions { get; }

    public BehaviorTarget? Target { get; }

    /// <summary>
    /// Typed costs paid by the materialized choice owner immediately before
    /// this option's instructions are expanded. An omitted value preserves
    /// the legacy no-cost option shape.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BehaviorCost>? Costs { get; }

    /// <summary>
    /// Optional client-facing semantic marker. It has no executable meaning;
    /// fallback behavior is represented explicitly instead of inferred from
    /// labels or instruction text.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BehaviorChoiceOptionSemantic? Semantic { get; }

    public bool Equals(BehaviorChoiceOption? other) =>
        other is not null
        && string.Equals(Key, other.Key, StringComparison.Ordinal)
        && BehaviorContractStructure.SequenceEquals(Instructions, other.Instructions, BehaviorContractStructure.AreEqual)
        && BehaviorContractStructure.AreEqual(Target, other.Target)
        && SequenceEquals(Costs, other.Costs)
        && Semantic == other.Semantic;

    public override bool Equals(object? obj) => Equals(obj as BehaviorChoiceOption);

    public override int GetHashCode() =>
        HashCode.Combine(Key, Instructions.Count, Target, Costs?.Count ?? 0, Semantic);

    private static bool SequenceEquals(
        IReadOnlyList<BehaviorCost>? left,
        IReadOnlyList<BehaviorCost>? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.SequenceEqual(right);
    }

    private static ReadOnlyCollection<BehaviorCost>? FreezeCosts(
        IReadOnlyList<BehaviorCost>? values)
    {
        if (values is null || values.Count == 0)
        {
            return null;
        }

        BehaviorCost[] copy = values.ToArray();
        if (copy.Any(static value => value is null))
        {
            throw new ArgumentException(
                "Behavior choice option cost collections must not contain null entries.",
                nameof(values));
        }

        foreach (BehaviorCost cost in copy)
        {
            if (cost.Kind is not (BehaviorCostKind.Credits
                or BehaviorCostKind.Clicks
                or BehaviorCostKind.Additional))
            {
                throw new ArgumentException(
                    $"Choice options support only Credits, Clicks, and structured Additional costs; '{cost.Kind}' is not supported.",
                    nameof(values));
            }

            if (cost.Kind == BehaviorCostKind.Additional
                && (cost.AdditionalKind != BehaviorAdditionalCostKind.TrashCards
                    || cost.AdditionalTarget is not
                    {
                        Kind: BehaviorTargetKind.ZoneCards,
                        Zone: not null,
                        Filter.OwnerSide: not null,
                        Reveal: false,
                    }))
            {
                throw new ArgumentException(
                    "Choice Additional costs must be typed private TrashCards operations over an explicitly owned, unrevealed ZoneCards target.",
                    nameof(values));
            }
        }

        return new ReadOnlyCollection<BehaviorCost>(copy);
    }

    private static ReadOnlyCollection<T> Freeze<T>(IEnumerable<T>? values)
        where T : class
    {
        T[] copy = (values ?? []).ToArray();
        if (copy.Any(static value => value is null))
        {
            throw new ArgumentException(
                "Behavior choice option collections must not contain null entries.",
                nameof(values));
        }

        return new ReadOnlyCollection<T>(copy);
    }
}

/// <summary>
/// A reusable explicit choice. A target descriptor may supply dynamic
/// candidates (for example cards in hidden HQ/Archives) without exposing a
/// runtime predicate or a mutable card list.
/// </summary>
public sealed class BehaviorChoiceSpec : IEquatable<BehaviorChoiceSpec>
{
    [JsonConstructor]
    public BehaviorChoiceSpec(
        string key,
        BehaviorChooser chooser,
        BehaviorChoiceMode mode,
        BehaviorChoiceCardinality cardinality,
        IReadOnlyList<BehaviorChoiceOption>? options = null,
        BehaviorTarget? target = null)
    {
        if (!StableIdentifier.TryValidate(key))
        {
            throw new ArgumentException(
                "A behavior choice key must be a stable identifier.",
                nameof(key));
        }

        if (!Enum.IsDefined(chooser))
        {
            throw new ArgumentOutOfRangeException(nameof(chooser));
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        ArgumentNullException.ThrowIfNull(cardinality);
        Key = key;
        Chooser = chooser;
        Mode = mode;
        Cardinality = cardinality;
        Options = Freeze(options);
        Target = target;

        var optionKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (BehaviorChoiceOption option in Options)
        {
            if (!optionKeys.Add(option.Key))
            {
                throw new ArgumentException(
                    $"Behavior choice '{key}' contains duplicate option '{option.Key}'.",
                    nameof(options));
            }
        }

        if (Mode == BehaviorChoiceMode.Mode && Options.Count < 2)
        {
            throw new ArgumentException(
                "A mode choice requires at least two options.",
                nameof(options));
        }

        if (Mode == BehaviorChoiceMode.Mode
            && (cardinality.Kind != BehaviorChoiceCardinalityKind.Exactly
                || cardinality.Count != 1))
        {
            throw new ArgumentException(
                "A mode choice must select exactly one option.",
                nameof(cardinality));
        }

        if (Mode == BehaviorChoiceMode.Optional
            && (cardinality.Kind != BehaviorChoiceCardinalityKind.UpTo
                || cardinality.Count != 1))
        {
            throw new ArgumentException(
                "An optional choice must select up to one option.",
                nameof(cardinality));
        }

        if (Mode == BehaviorChoiceMode.TargetSelection && target is null && Options.Count == 0)
        {
            throw new ArgumentException(
                "A target selection requires a target descriptor or options.",
                nameof(target));
        }

        if (cardinality.Count is { } count && Options.Count > 0 && count > Options.Count)
        {
            throw new ArgumentException(
                $"Choice cardinality {count} exceeds the {Options.Count} declared options.",
                nameof(cardinality));
        }
    }

    public string Key { get; }

    public BehaviorChooser Chooser { get; }

    public BehaviorChoiceMode Mode { get; }

    public BehaviorChoiceCardinality Cardinality { get; }

    public IReadOnlyList<BehaviorChoiceOption> Options { get; }

    public BehaviorTarget? Target { get; }

    public bool Equals(BehaviorChoiceSpec? other) =>
        other is not null
        && string.Equals(Key, other.Key, StringComparison.Ordinal)
        && Chooser == other.Chooser
        && Mode == other.Mode
        && Cardinality.Equals(other.Cardinality)
        && BehaviorContractStructure.SequenceEquals(Options, other.Options, BehaviorContractStructure.AreEqual)
        && BehaviorContractStructure.AreEqual(Target, other.Target);

    public override bool Equals(object? obj) => Equals(obj as BehaviorChoiceSpec);

    public override int GetHashCode() =>
        HashCode.Combine(Key, Chooser, Mode, Cardinality, Options.Count, Target);

    private static ReadOnlyCollection<T> Freeze<T>(IEnumerable<T>? values)
        where T : class
    {
        T[] copy = (values ?? []).ToArray();
        if (copy.Any(static value => value is null))
        {
            throw new ArgumentException(
                "Behavior choice collections must not contain null entries.",
                nameof(values));
        }

        return new ReadOnlyCollection<T>(copy);
    }
}

/// <summary>
/// A typed conditional branch. Both arms are immutable ordered instruction
/// lists; an omitted false arm is an explicit no-op.
/// </summary>
public sealed class BehaviorBranchSpec : IEquatable<BehaviorBranchSpec>
{
    [JsonConstructor]
    public BehaviorBranchSpec(
        BehaviorCondition condition,
        IReadOnlyList<BehaviorInstruction> whenTrue,
        IReadOnlyList<BehaviorInstruction>? whenFalse = null,
        string? key = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        if (key is not null && !StableIdentifier.TryValidate(key))
        {
            throw new ArgumentException(
                "A behavior branch key must be a stable identifier.",
                nameof(key));
        }

        ArgumentNullException.ThrowIfNull(whenTrue);
        Condition = condition;
        WhenTrue = Freeze(whenTrue, nameof(whenTrue));
        WhenFalse = Freeze(whenFalse ?? [], nameof(whenFalse));
        Key = key;
        if (WhenTrue.Count == 0)
        {
            throw new ArgumentException(
                "A behavior branch must have at least one true-arm instruction.",
                nameof(whenTrue));
        }
    }

    public string? Key { get; }

    public BehaviorCondition Condition { get; }

    public IReadOnlyList<BehaviorInstruction> WhenTrue { get; }

    public IReadOnlyList<BehaviorInstruction> WhenFalse { get; }

    public bool Equals(BehaviorBranchSpec? other) =>
        other is not null
        && string.Equals(Key, other.Key, StringComparison.Ordinal)
        && Condition.Equals(other.Condition)
        && BehaviorContractStructure.SequenceEquals(WhenTrue, other.WhenTrue, BehaviorContractStructure.AreEqual)
        && BehaviorContractStructure.SequenceEquals(WhenFalse, other.WhenFalse, BehaviorContractStructure.AreEqual);

    public override bool Equals(object? obj) => Equals(obj as BehaviorBranchSpec);

    public override int GetHashCode() =>
        HashCode.Combine(Key, Condition, WhenTrue.Count, WhenFalse.Count);

    private static ReadOnlyCollection<T> Freeze<T>(IEnumerable<T> values, string parameterName)
        where T : class
    {
        T[] copy = values.ToArray();
        if (copy.Any(static value => value is null))
        {
            throw new ArgumentException(
                "Behavior branch collections must not contain null entries.",
                parameterName);
        }

        return new ReadOnlyCollection<T>(copy);
    }
}

/// <summary>Typed static modifiers supported by the declarative foundation.</summary>
public enum BehaviorStaticModifierKind
{
    MemoryUnits,
    MaximumHandSize,
}

internal static class BehaviorContractStructure
{
    public static bool SequenceEquals<T>(
        IReadOnlyList<T> left,
        IReadOnlyList<T> right,
        Func<T, T, bool> comparer)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (!comparer(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    public static bool AreEqual(BehaviorChoiceCardinality? left, BehaviorChoiceCardinality? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool AreEqual(BehaviorCardFilter? left, BehaviorCardFilter? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool AreEqual(
        BehaviorDynamicPumpDeclaration? left,
        BehaviorDynamicPumpDeclaration? right) =>
        left is null
            ? right is null
            : right is not null
                && left.ValueKind == right.ValueKind
                && left.Source == right.Source
                && left.Operation == right.Operation
                && left.UnitDelta == right.UnitDelta
                && string.Equals(left.VariableKey, right.VariableKey, StringComparison.Ordinal)
                && string.Equals(left.ReasonKey, right.ReasonKey, StringComparison.Ordinal)
                && AreEqual(left.InstalledFilter, right.InstalledFilter);

    public static bool AreEqual(BehaviorTarget? left, BehaviorTarget? right) =>
        left is null
            ? right is null
            : right is not null
                && left.Kind == right.Kind
                && string.Equals(left.Selector, right.Selector, StringComparison.Ordinal)
                && left.Zone == right.Zone
                && AreEqual(left.Filter, right.Filter)
                && left.Reveal == right.Reveal;

    public static bool AreEqual(BehaviorInstruction? left, BehaviorInstruction? right) =>
        left is null
            ? right is null
            : right is not null
                && left.Kind == right.Kind
                && left.Amount == right.Amount
                && string.Equals(left.Argument, right.Argument, StringComparison.Ordinal)
                && AreEqual(left.Target, right.Target)
                && left.SourceZone == right.SourceZone
                && left.DestinationZone == right.DestinationZone
                && AreEqual(left.Filter, right.Filter)
                && Equals(left.Condition, right.Condition)
                && AreEqual(left.Choice, right.Choice)
                && AreEqual(left.Branch, right.Branch)
                && left.Optional == right.Optional
                && left.SpendRestriction == right.SpendRestriction
                && Equals(left.RunOperand, right.RunOperand)
                && Equals(left.AmountOperand, right.AmountOperand)
                && left.Recipient == right.Recipient
                && left.DamageKind == right.DamageKind
                && Equals(left.DamageAmountOperand, right.DamageAmountOperand);

    public static bool AreEqual(BehaviorChoiceOption? left, BehaviorChoiceOption? right) =>
        left is null ? right is null : right is not null && left.Equals(right);

    public static bool AreEqual(BehaviorChoiceSpec? left, BehaviorChoiceSpec? right) =>
        left is null ? right is null : right is not null && left.Equals(right);

    public static bool AreEqual(BehaviorBranchSpec? left, BehaviorBranchSpec? right) =>
        left is null ? right is null : right is not null && left.Equals(right);

    public static bool AreEqual(BehaviorStaticModifier? left, BehaviorStaticModifier? right) =>
        left is null
            ? right is null
            : right is not null
                && string.Equals(left.ValueKey, right.ValueKey, StringComparison.Ordinal)
                && left.Delta == right.Delta
                && string.Equals(left.ReasonKey, right.ReasonKey, StringComparison.Ordinal)
                && left.Kind == right.Kind;

    public static bool AreEqual(BehaviorValueModifier? left, BehaviorValueModifier? right) =>
        left is null
            ? right is null
            : right is not null
                && left.ValueKind == right.ValueKind
                && left.Source == right.Source
                && left.Operation == right.Operation
                && left.Delta == right.Delta
                && string.Equals(left.ReasonKey, right.ReasonKey, StringComparison.Ordinal)
                && Equals(left.Condition, right.Condition)
                && AreEqual(left.InstalledFilter, right.InstalledFilter)
                && string.Equals(left.VariableKey, right.VariableKey, StringComparison.Ordinal)
                && string.Equals(left.CounterName, right.CounterName, StringComparison.Ordinal)
                && left.Threshold == right.Threshold;
}

/// <summary>Authoritative facts used to determine a damage amount.</summary>
public enum BehaviorDamageAmountKind
{
    SourceAdvancementCounters,
    ScoredAgendaPrintedPointsThisTurn,
}

/// <summary>Typed damage calculation, evaluated once before costs and interrupts.</summary>
public sealed record BehaviorDamageAmountOperand
{
    [JsonConstructor]
    public BehaviorDamageAmountOperand(
        BehaviorDamageAmountKind kind,
        int baseAmount = 0,
        bool trashSourceAsCost = false)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(baseAmount);
        if (trashSourceAsCost && kind != BehaviorDamageAmountKind.SourceAdvancementCounters)
        {
            throw new ArgumentException("A self-trash damage cost requires an advancement snapshot.", nameof(trashSourceAsCost));
        }
        Kind = kind;
        BaseAmount = baseAmount;
        TrashSourceAsCost = trashSourceAsCost;
    }

    public BehaviorDamageAmountKind Kind { get; }
    public int BaseAmount { get; }
    public bool TrashSourceAsCost { get; }
}

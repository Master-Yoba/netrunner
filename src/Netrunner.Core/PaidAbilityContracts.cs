using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

/// <summary>
/// Timing windows in which a behavior-owned ability may be declared. The
/// restricted windows are intentional: an ability declared for the encounter
/// or access window cannot be silently treated as a generic paid ability.
/// </summary>
public enum BehaviorAbilityTiming
{
    ActionPhase,
    AnyPaidWindow,
    InitiationPaidWindow,
    ApproachPaidWindow,
    EncounterPaidWindow,
    MovementPaidWindow,
    AfterMovePaidWindow,
    BreachAccessPaidWindow,
    RunEndPaidWindow,
}

/// <summary>Whether an ability is an action or a paid ability.</summary>
public enum BehaviorAbilityActivation
{
    ClickAction,
    PaidAbility,
}

/// <summary>
/// Lifecycle cursor for a serialized ability activation. Each value is a
/// distinct checkpoint so a resumed action cannot be mistaken for a different
/// phase of payment or execution.
/// </summary>
public enum AbilityLifecycleStage
{
    PayCosts,
    CostCheckpoint,
    Execute,
    AwaitingChild,
    Complete,
}

/// <summary>The usage limit captured by an ability declaration and snapshot.</summary>
public enum BehaviorAbilityUsageLimit
{
    Unlimited,
    OncePerTurn,
}

/// <summary>
/// Immutable usage facts captured when an ability is issued. The snapshot is
/// deliberately small: later reducers must not infer usage from mutable card
/// or turn state while resuming a serialized action/frame.
/// </summary>
public sealed class AbilityUsageSnapshot : IEquatable<AbilityUsageSnapshot>
{
    [JsonConstructor]
    public AbilityUsageSnapshot(BehaviorAbilityUsageLimit limit, int usesThisTurn)
    {
        if (!Enum.IsDefined(limit))
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        if (usesThisTurn < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(usesThisTurn),
                "Ability usage cannot be negative.");
        }

        if (limit == BehaviorAbilityUsageLimit.OncePerTurn && usesThisTurn > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(usesThisTurn),
                "A once-per-turn ability can have at most one use this turn.");
        }

        Limit = limit;
        UsesThisTurn = usesThisTurn;
    }

    [JsonInclude]
    [JsonRequired]
    public BehaviorAbilityUsageLimit Limit { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int UsesThisTurn { get; private set; }

    public bool Equals(AbilityUsageSnapshot? other) =>
        other is not null
        && Limit == other.Limit
        && UsesThisTurn == other.UsesThisTurn;

    public override bool Equals(object? obj) => Equals(obj as AbilityUsageSnapshot);

    public override int GetHashCode() => HashCode.Combine(Limit, UsesThisTurn);
}

/// <summary>
/// Immutable, data-only declaration for one behavior-owned paid ability.
/// Nothing in this type materializes an action, pays a cost, or mutates game
/// state.
/// </summary>
public sealed class BehaviorPaidAbility : IEquatable<BehaviorPaidAbility>
{
    [JsonConstructor]
    public BehaviorPaidAbility(
        string key,
        BehaviorAbilityTiming timing,
        BehaviorAbilityActivation activation,
        IReadOnlyList<BehaviorCost> costs,
        IReadOnlyList<BehaviorCondition> conditions,
        IReadOnlyList<BehaviorTarget> targets,
        IReadOnlyList<BehaviorInstruction> instructions,
        BehaviorAbilityUsageLimit usageLimit)
    {
        if (!StableIdentifier.TryValidate(key))
        {
            throw new ArgumentException(
                "A paid ability key must be a stable identifier.",
                nameof(key));
        }

        if (!Enum.IsDefined(timing))
        {
            throw new ArgumentOutOfRangeException(nameof(timing));
        }

        if (!Enum.IsDefined(activation))
        {
            throw new ArgumentOutOfRangeException(nameof(activation));
        }

        if (!Enum.IsDefined(usageLimit))
        {
            throw new ArgumentOutOfRangeException(nameof(usageLimit));
        }

        Key = key;
        Timing = timing;
        Activation = activation;
        UsageLimit = usageLimit;
        Costs = Freeze(costs, nameof(costs));
        Conditions = Freeze(conditions, nameof(conditions));
        Targets = Freeze(targets, nameof(targets));
        Instructions = Freeze(instructions, nameof(instructions));
        ValidateCombinations();
    }

    [JsonInclude]
    [JsonRequired]
    public string Key { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public BehaviorAbilityTiming Timing { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public BehaviorAbilityActivation Activation { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public BehaviorAbilityUsageLimit UsageLimit { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<BehaviorCost> Costs { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<BehaviorCondition> Conditions { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<BehaviorTarget> Targets { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<BehaviorInstruction> Instructions { get; private set; }

    public bool Equals(BehaviorPaidAbility? other) =>
        other is not null
        && string.Equals(Key, other.Key, StringComparison.Ordinal)
        && Timing == other.Timing
        && Activation == other.Activation
        && UsageLimit == other.UsageLimit
        && SequenceEquals(Costs, other.Costs)
        && SequenceEquals(Conditions, other.Conditions)
        && SequenceEquals(Targets, other.Targets)
        && SequenceEquals(Instructions, other.Instructions);

    public override bool Equals(object? obj) => Equals(obj as BehaviorPaidAbility);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Key, StringComparer.Ordinal);
        hash.Add(Timing);
        hash.Add(Activation);
        hash.Add(UsageLimit);
        AddSequence(ref hash, Costs);
        AddSequence(ref hash, Conditions);
        AddSequence(ref hash, Targets);
        AddSequence(ref hash, Instructions);
        return hash.ToHashCode();
    }

    private void ValidateCombinations()
    {
        bool hasClickCost = Costs.Any(static cost => cost.Kind == BehaviorCostKind.Clicks);
        bool hasLoseClickCost = Costs.Any(static cost => cost.Kind == BehaviorCostKind.LoseClick);

        if (Activation == BehaviorAbilityActivation.ClickAction)
        {
            if (Timing != BehaviorAbilityTiming.ActionPhase)
            {
                throw new ArgumentException(
                    "Click actions are legal only during the action phase.",
                    nameof(Timing));
            }

            if (Costs.Count(static cost => cost.Kind == BehaviorCostKind.Clicks) != 1
                || Costs.Single(static cost => cost.Kind == BehaviorCostKind.Clicks).Amount != 1
                || hasLoseClickCost)
            {
                throw new ArgumentException(
                    "A click action requires exactly one Click cost of one click and may not use LoseClick.",
                    nameof(Costs));
            }
        }
        else if (hasClickCost)
        {
            throw new ArgumentException(
                "Paid abilities may not carry an action Click cost; use LoseClick for a paid click cost.",
                nameof(Costs));
        }

        bool hasAccessTarget = Targets.Any(static target =>
            target.Kind == BehaviorTargetKind.AccessedCard);
        if (hasAccessTarget
            && Timing is not (BehaviorAbilityTiming.BreachAccessPaidWindow
                or BehaviorAbilityTiming.AnyPaidWindow))
        {
            throw new ArgumentException(
                "An accessed-card ability must use the breach-access or any paid window.",
                nameof(Timing));
        }

        if (Timing == BehaviorAbilityTiming.ActionPhase && hasAccessTarget)
        {
            throw new ArgumentException(
                "An accessed-card ability is not valid during the action phase.",
                nameof(Timing));
        }
    }

    private static ReadOnlyCollection<T> Freeze<T>(IReadOnlyList<T> values, string parameterName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(values);
        T[] copy = values.ToArray();
        if (copy.Any(static value => value is null))
        {
            throw new ArgumentException(
                "Paid ability declaration collections must not contain null entries.",
                parameterName);
        }

        return new ReadOnlyCollection<T>(copy);
    }

    private static bool SequenceEquals<T>(IReadOnlyList<T> left, IReadOnlyList<T> right)
        where T : notnull
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (!Equals(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static void AddSequence<T>(ref HashCode hash, IReadOnlyList<T> values)
        where T : notnull
    {
        hash.Add(values.Count);
        foreach (T value in values)
        {
            hash.Add(value);
        }
    }
}

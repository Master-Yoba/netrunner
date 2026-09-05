using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

/// <summary>
/// Values that can be calculated by the immutable behavior vocabulary.  The
/// vocabulary is intentionally small in this checkpoint; adding a value is a
/// contract change rather than an implicit string convention.
/// </summary>
public enum BehaviorValueKind
{
    Strength,
    Damage,
    PrintedCost,
}

/// <summary>Typed operation used by a value calculation declaration.</summary>
public enum BehaviorValueOperation
{
    Add,
}

/// <summary>
/// How a behavior declaration obtains its contribution to a value.  A
/// declaration is authored on the source definition; the physical source
/// card is attached only when a quote is projected.
/// </summary>
public enum BehaviorValueSourceKind
{
    Static,
    PerInstalledCard,
    Variable,
    CounterThreshold,
    EncounterScoped,
}

/// <summary>
/// A dynamic breaker-pump declaration. The declaration has no printed
/// constant: activation counts the matching active installed cards and
/// snapshots that quantity multiplied by <see cref="UnitDelta"/> into a
/// run-scoped value adjustment. It is deliberately a separate declaration
/// from <see cref="BehaviorValueModifier"/> so it cannot become an always-on
/// passive strength modifier.
/// </summary>
public sealed record BehaviorDynamicPumpDeclaration
{
    [JsonConstructor]
    public BehaviorDynamicPumpDeclaration(
        BehaviorCardFilter installedFilter,
        int unitDelta,
        string variableKey,
        string reasonKey)
    {
        ArgumentNullException.ThrowIfNull(installedFilter);
        if (installedFilter.CardType != BehaviorCardType.Icebreaker
            || installedFilter.OwnerSide != PlayerSide.Runner
            || installedFilter.Subtype is not null)
        {
            throw new ArgumentException(
                "Dynamic breaker pumps require a Runner-owned icebreaker filter without subtype metadata.",
                nameof(installedFilter));
        }

        if (unitDelta <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitDelta),
                "A dynamic pump unit delta must be positive.");
        }

        if (!StableIdentifier.TryValidate(variableKey))
        {
            throw new ArgumentException(
                "A dynamic pump variable key must be a stable identifier.",
                nameof(variableKey));
        }

        if (!StableIdentifier.TryValidate(reasonKey))
        {
            throw new ArgumentException(
                "A dynamic pump reason key must be a stable identifier.",
                nameof(reasonKey));
        }

        InstalledFilter = installedFilter;
        UnitDelta = unitDelta;
        VariableKey = variableKey;
        ReasonKey = reasonKey;
    }

    /// <summary>The value this declaration can affect.</summary>
    [JsonIgnore]
    public BehaviorValueKind ValueKind { get; } = BehaviorValueKind.Strength;

    /// <summary>The realized dynamic contribution is additive.</summary>
    [JsonIgnore]
    public BehaviorValueSourceKind Source { get; } = BehaviorValueSourceKind.Variable;

    /// <summary>The realized dynamic contribution is additive.</summary>
    [JsonIgnore]
    public BehaviorValueOperation Operation { get; } = BehaviorValueOperation.Add;

    public BehaviorCardFilter InstalledFilter { get; }

    /// <summary>Amount added for each matching installed icebreaker.</summary>
    public int UnitDelta { get; }

    /// <summary>Stable key identifying the dynamic quantity.</summary>
    public string VariableKey { get; }

    /// <summary>Stable localization key for the realized calculation term.</summary>
    public string ReasonKey { get; }
}

/// <summary>
/// An immutable, data-only value modifier declaration.  Only the structural
/// combinations implemented by <see cref="EncounterStrength"/> are accepted;
/// unsupported combinations fail during construction rather than silently
/// becoming live rules.
/// </summary>
public sealed record BehaviorValueModifier
{
    [JsonConstructor]
    public BehaviorValueModifier(
        BehaviorValueKind valueKind,
        BehaviorValueSourceKind source,
        BehaviorValueOperation operation,
        int delta,
        string reasonKey,
        BehaviorCondition? condition = null,
        BehaviorCardFilter? installedFilter = null,
        string? variableKey = null,
        string? counterName = null,
        int? threshold = null)
    {
        if (!Enum.IsDefined(valueKind))
        {
            throw new ArgumentOutOfRangeException(nameof(valueKind));
        }

        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        if (!Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        if (operation != BehaviorValueOperation.Add)
        {
            throw new ArgumentException(
                "Only additive value modifiers are executable in this checkpoint.",
                nameof(operation));
        }

        if (delta == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delta),
                "A value modifier delta must be nonzero.");
        }

        if (!StableIdentifier.TryValidate(reasonKey))
        {
            throw new ArgumentException(
                "A value modifier reason key must be a stable identifier.",
                nameof(reasonKey));
        }

        if (installedFilter is not null && source != BehaviorValueSourceKind.PerInstalledCard)
        {
            throw new ArgumentException(
                "An installed-card filter is valid only for per-installed-card modifiers.",
                nameof(installedFilter));
        }

        if (variableKey is not null
            && (source != BehaviorValueSourceKind.Variable
                || !StableIdentifier.TryValidate(variableKey)))
        {
            throw new ArgumentException(
                "A variable key is valid only for variable modifiers and must be a stable identifier.",
                nameof(variableKey));
        }

        if (counterName is not null
            && (source != BehaviorValueSourceKind.CounterThreshold
                || !StableIdentifier.TryValidate(counterName)))
        {
            throw new ArgumentException(
                "A counter name is valid only for counter-threshold modifiers and must be a stable identifier.",
                nameof(counterName));
        }

        if (threshold is not null
            && (source != BehaviorValueSourceKind.CounterThreshold || threshold < 1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(threshold),
                "Counter-threshold modifiers require a positive threshold.");
        }

        if (source == BehaviorValueSourceKind.PerInstalledCard && installedFilter is null)
        {
            throw new ArgumentNullException(
                nameof(installedFilter),
                "Per-installed-card modifiers require a typed installed-card filter.");
        }

        if (source == BehaviorValueSourceKind.Variable && variableKey is null)
        {
            throw new ArgumentNullException(
                nameof(variableKey),
                "Variable modifiers require a stable variable key.");
        }

        if (source == BehaviorValueSourceKind.CounterThreshold
            && (counterName is null || threshold is null))
        {
            throw new ArgumentException(
                "Counter-threshold modifiers require both a counter name and threshold.",
                nameof(counterName));
        }

        if (source != BehaviorValueSourceKind.CounterThreshold
            && (counterName is not null || threshold is not null))
        {
            throw new ArgumentException(
                "Counter name and threshold are valid only for counter-threshold modifiers.",
                nameof(counterName));
        }

        if (source is BehaviorValueSourceKind.Static
            or BehaviorValueSourceKind.PerInstalledCard)
        {
            // These declarations are already scoped by their source kind.
            // Conditional behavior needs a dedicated typed source kind rather
            // than an arbitrary condition that projection might ignore.
            if (condition is not null)
            {
                throw new ArgumentException(
                    "Static and per-installed-card modifiers do not accept an untyped condition.",
                    nameof(condition));
            }
        }
        else if (source is BehaviorValueSourceKind.Variable
            or BehaviorValueSourceKind.CounterThreshold)
        {
            if (condition is not null)
            {
                throw new ArgumentException(
                    "Variable and counter-threshold modifiers carry their own typed activation context.",
                    nameof(condition));
            }
        }
        else if (source == BehaviorValueSourceKind.EncounterScoped)
        {
            throw new ArgumentException(
                "Encounter-scoped contributions must be realized run adjustments with an explicit target.",
                nameof(source));
        }

        // The current executable value is strength. Keep the check explicit so
        // deserialized/forged future enum values do not fall through to a
        // default arithmetic path.
        if (valueKind != BehaviorValueKind.Strength)
        {
            throw new ArgumentOutOfRangeException(nameof(valueKind));
        }

        ValueKind = valueKind;
        Source = source;
        Operation = operation;
        Delta = delta;
        ReasonKey = reasonKey;
        Condition = condition;
        InstalledFilter = installedFilter;
        VariableKey = variableKey;
        CounterName = counterName;
        Threshold = threshold;
    }

    public BehaviorValueModifier(
        BehaviorValueKind valueKind,
        BehaviorValueSourceKind source,
        int delta,
        string reasonKey,
        BehaviorCondition? condition = null,
        BehaviorCardFilter? installedFilter = null,
        string? variableKey = null,
        string? counterName = null,
        int? threshold = null)
        : this(
            valueKind,
            source,
            BehaviorValueOperation.Add,
            delta,
            reasonKey,
            condition,
            installedFilter,
            variableKey,
            counterName,
            threshold)
    {
    }

    public BehaviorValueModifier(
        BehaviorValueSourceKind source,
        int delta,
        string reasonKey,
        BehaviorCondition? condition = null,
        BehaviorCardFilter? installedFilter = null,
        string? variableKey = null,
        string? counterName = null,
        int? threshold = null)
        : this(
            BehaviorValueKind.Strength,
            source,
            BehaviorValueOperation.Add,
            delta,
            reasonKey,
            condition,
            installedFilter,
            variableKey,
            counterName,
            threshold)
    {
    }

    public BehaviorValueKind ValueKind { get; }

    public BehaviorValueSourceKind Source { get; }

    public BehaviorValueOperation Operation { get; }

    public int Delta { get; }

    public string ReasonKey { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BehaviorCondition? Condition { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BehaviorCardFilter? InstalledFilter { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VariableKey { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CounterName { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Threshold { get; }

    public static BehaviorValueModifier Static(
        int delta,
        string reasonKey) =>
        new(BehaviorValueSourceKind.Static, delta, reasonKey);

    public static BehaviorValueModifier PerInstalled(
        BehaviorCardFilter filter,
        int delta,
        string reasonKey) =>
        new(
            BehaviorValueSourceKind.PerInstalledCard,
            delta,
            reasonKey,
            installedFilter: filter);

    public static BehaviorValueModifier Variable(
        string variableKey,
        int delta,
        string reasonKey) =>
        new(
            BehaviorValueSourceKind.Variable,
            delta,
            reasonKey,
            variableKey: variableKey);

    public static BehaviorValueModifier CounterThreshold(
        string counterName,
        int threshold,
        int delta,
        string reasonKey) =>
        new(
            BehaviorValueSourceKind.CounterThreshold,
            delta,
            reasonKey,
            counterName: counterName,
            threshold: threshold);
}

/// <summary>
/// One detached explanation for a calculated value. It carries no display
/// string; clients localize <see cref="ReasonKey"/> and choose their own
/// formatting. Provenance is optional because an observation may redact a
/// source that is not visible to its viewer.
/// </summary>
public sealed record ValueCalculationTerm
{
    [JsonConstructor]
    public ValueCalculationTerm(
        BehaviorValueSourceKind source,
        BehaviorValueOperation operation,
        int delta,
        string reasonKey,
        CardDefinitionId? sourceDefinitionId = null,
        CardInstanceId? sourceCardId = null,
        int? quantity = null,
        int? unitDelta = null,
        string? variableKey = null)
    {
        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        if (!Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        if (operation != BehaviorValueOperation.Add)
        {
            throw new ArgumentException(
                "Only additive calculation terms are executable in this checkpoint.",
                nameof(operation));
        }

        if (delta == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(delta), "A calculation term delta must be nonzero.");
        }

        if (!StableIdentifier.TryValidate(reasonKey))
        {
            throw new ArgumentException(
                "A calculation term reason key must be a stable identifier.",
                nameof(reasonKey));
        }

        if (sourceDefinitionId is { } definitionId
            && !StableIdentifier.TryValidate(definitionId.Value))
        {
            throw new ArgumentException(
                "A calculation term source definition must be a stable identifier.",
                nameof(sourceDefinitionId));
        }

        if (sourceCardId is { } cardId
            && !StableIdentifier.TryValidate(cardId.Value))
        {
            throw new ArgumentException(
                "A calculation term source card must be a stable identifier.",
                nameof(sourceCardId));
        }

        if (quantity is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "A calculation term quantity must be positive when supplied.");
        }

        if (unitDelta is 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitDelta),
                "A calculation term unit delta must be nonzero when supplied.");
        }

        if (variableKey is not null && !StableIdentifier.TryValidate(variableKey))
        {
            throw new ArgumentException(
                "A calculation term variable key must be a stable identifier.",
                nameof(variableKey));
        }

        switch (source)
        {
            case BehaviorValueSourceKind.Static:
            case BehaviorValueSourceKind.CounterThreshold:
            case BehaviorValueSourceKind.EncounterScoped:
                if (quantity is not null || unitDelta is not null || variableKey is not null)
                {
                    throw new ArgumentException(
                        $"{source} terms do not carry quantity, unit delta, or variable metadata.",
                        nameof(quantity));
                }

                break;
            case BehaviorValueSourceKind.PerInstalledCard:
                if (quantity is null || unitDelta is null)
                {
                    throw new ArgumentException(
                        "Per-installed-card terms require a realized quantity and unit delta.",
                        nameof(quantity));
                }

                if (variableKey is not null)
                {
                    throw new ArgumentException(
                        "Per-installed-card terms do not carry variable metadata.",
                        nameof(variableKey));
                }

                ValidateRealizedProduct(delta, quantity.Value, unitDelta.Value);

                break;
            case BehaviorValueSourceKind.Variable:
                if (variableKey is null)
                {
                    throw new ArgumentNullException(
                        nameof(variableKey),
                        "Variable terms require their stable variable key.");
                }

                if (quantity is null || unitDelta is null)
                {
                    throw new ArgumentException(
                        "Variable terms require a realized quantity and unit delta.",
                        nameof(quantity));
                }

                ValidateRealizedProduct(delta, quantity.Value, unitDelta.Value);

                break;
            default:
                // Enum values from a custom/forward catalog must not acquire
                // meaning through a permissive default branch.
                throw new ArgumentOutOfRangeException(nameof(source));
        }

        Source = source;
        Operation = operation;
        Delta = delta;
        ReasonKey = reasonKey;
        SourceDefinitionId = sourceDefinitionId;
        SourceCardId = sourceCardId;
        Quantity = quantity;
        UnitDelta = unitDelta;
        VariableKey = variableKey;
    }

    private static void ValidateRealizedProduct(int delta, int quantity, int unitDelta)
    {
        try
        {
            if (delta != checked(quantity * unitDelta))
            {
                throw new ArgumentException(
                    "A realized calculation term delta must equal quantity multiplied by unit delta.",
                    nameof(delta));
            }
        }
        catch (OverflowException exception)
        {
            throw new ArgumentException(
                "A realized calculation term quantity and unit delta overflow.",
                nameof(delta),
                exception);
        }
    }

    public ValueCalculationTerm(
        BehaviorValueSourceKind source,
        int delta,
        string reasonKey,
        CardDefinitionId? sourceDefinitionId = null,
        CardInstanceId? sourceCardId = null,
        int? quantity = null,
        int? unitDelta = null,
        string? variableKey = null)
        : this(
            source,
            BehaviorValueOperation.Add,
            delta,
            reasonKey,
            sourceDefinitionId,
            sourceCardId,
            quantity,
            unitDelta,
            variableKey)
    {
    }

    public BehaviorValueSourceKind Source { get; }

    public BehaviorValueOperation Operation { get; }

    public int Delta { get; }

    public string ReasonKey { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CardDefinitionId? SourceDefinitionId { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CardInstanceId? SourceCardId { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Quantity { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? UnitDelta { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VariableKey { get; }

}

/// <summary>
/// Immutable checked calculation of one typed value. Terms are canonicalized
/// by their typed source/provenance key, so equivalent declarations always
/// produce deterministic order independent of collection insertion order.
/// </summary>
public sealed class ValueQuote : IEquatable<ValueQuote>
{
    [JsonConstructor]
    public ValueQuote(
        BehaviorValueKind valueKind,
        int baseValue,
        int effectiveValue,
        IReadOnlyList<ValueCalculationTerm>? terms = null)
    {
        if (!Enum.IsDefined(valueKind))
        {
            throw new ArgumentOutOfRangeException(nameof(valueKind));
        }

        if (valueKind != BehaviorValueKind.Strength)
        {
            throw new ArgumentOutOfRangeException(nameof(valueKind));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(baseValue);
        ValueCalculationTerm[] copy = (terms ?? [])
            .Select((term, index) =>
            {
                ArgumentNullException.ThrowIfNull(term);
                return (term, index);
            })
            .OrderBy(static pair => pair.term.Source)
            .ThenBy(static pair => pair.term.SourceDefinitionId?.Value, StringComparer.Ordinal)
            .ThenBy(static pair => pair.term.SourceCardId?.Value, StringComparer.Ordinal)
            .ThenBy(static pair => pair.term.ReasonKey, StringComparer.Ordinal)
            .ThenBy(static pair => pair.term.Operation)
            .ThenBy(static pair => pair.term.Delta)
            .ThenBy(static pair => pair.term.Quantity)
            .ThenBy(static pair => pair.term.UnitDelta)
            .ThenBy(static pair => pair.term.VariableKey, StringComparer.Ordinal)
            .ThenBy(static pair => pair.index)
            .Select(static pair => pair.term)
            .ToArray();

        int expected = CalculateEffective(baseValue, copy);
        if (effectiveValue != expected)
        {
            throw new ArgumentException(
                $"The effective value {effectiveValue} does not match the checked quote calculation {expected}.",
                nameof(effectiveValue));
        }

        ValueKind = valueKind;
        BaseValue = baseValue;
        EffectiveValue = effectiveValue;
        Terms = new ReadOnlyCollection<ValueCalculationTerm>(copy);
    }

    [JsonInclude]
    [JsonRequired]
    public BehaviorValueKind ValueKind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int BaseValue { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int EffectiveValue { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<ValueCalculationTerm> Terms { get; private set; }

    public bool Equals(ValueQuote? other) =>
        other is not null
        && ValueKind == other.ValueKind
        && BaseValue == other.BaseValue
        && EffectiveValue == other.EffectiveValue
        && Terms.SequenceEqual(other.Terms);

    public override bool Equals(object? obj) => Equals(obj as ValueQuote);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ValueKind);
        hash.Add(BaseValue);
        hash.Add(EffectiveValue);
        foreach (ValueCalculationTerm term in Terms)
        {
            hash.Add(term);
        }

        return hash.ToHashCode();
    }

    private static int CalculateEffective(int baseValue, IReadOnlyList<ValueCalculationTerm> terms)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(baseValue);
        long total = baseValue;
        foreach (ValueCalculationTerm term in terms)
        {
            ArgumentNullException.ThrowIfNull(term);
            total = checked(total + term.Delta);
        }

        if (total > int.MaxValue || total < int.MinValue)
        {
            throw new OverflowException("Value quote terms exceed the supported integer range.");
        }

        // Preserve the historical EncounterStrength arithmetic: modifiers may
        // reduce an effective strength below zero and the comparison layer is
        // responsible for deciding what that means.
        return (int)total;
    }
}

/// <summary>
/// The one authoritative strength projection used by encounter legality,
/// invariant reconstruction, observations, and Match. Legacy integer helpers
/// below intentionally delegate to the same quote calculation.
/// </summary>
public static class EncounterStrength
{
    /// <summary>Behavior identifier of the icebreaker pump lingering effect.</summary>
    public const string IcebreakerPumpBehavior = "icebreaker-pump";

    private const string LegacyRemoteReason = "ice-strength-protecting-remote";
    private const string PumpReason = "icebreaker-pump";

    public static ValueQuote IceQuote(IceBehaviorSpec spec, ServerKind serverKind)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (!Enum.IsDefined(serverKind))
        {
            throw new ArgumentOutOfRangeException(nameof(serverKind));
        }

        var terms = new List<ValueCalculationTerm>();
        AddLegacyIceTerms(terms, spec, serverKind, sourceCardId: null);
        AddDeclaredTerms(
            terms,
            spec.ValueModifiers,
            targetDefinitionId: spec.DefinitionId,
            sourceCardId: null,
            cards: null,
            zones: null,
            rulesState: null,
            behaviorCatalog: null,
            run: null);
        return new ValueQuote(BehaviorValueKind.Strength, spec.Strength, CalculateEffective(spec.Strength, terms), terms);
    }

    public static ValueQuote BreakerQuote(
        IcebreakerBehaviorSpec spec,
        IReadOnlyList<RunLingeringEffect> activeEffects,
        CardInstanceId breakerId)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(activeEffects);
        var terms = new List<ValueCalculationTerm>();
        AddPumpTerms(terms, spec, activeEffects, breakerId);
        AddDeclaredTerms(
            terms,
            spec.ValueModifiers,
            spec.DefinitionId,
            sourceCardId: null,
            cards: null,
            zones: null,
            rulesState: null,
            behaviorCatalog: null,
            run: null);
        return new ValueQuote(
            BehaviorValueKind.Strength,
            spec.BaseStrength,
            CalculateEffective(spec.BaseStrength, terms),
            terms);
    }

    /// <summary>Projects an encountered ICE from authoritative state.</summary>
    public static ValueQuote? ProjectIceQuote(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        CardInstanceId iceId,
        ServerKind serverKind)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        if (state.RunState is not { } run
            || state.Servers.FirstOrDefault(server => server.Id == run.AttackedServer)?.Kind != serverKind)
        {
            return null;
        }

        CardInstanceState? ice = state.Cards.FirstOrDefault(card => card.Id == iceId);
        return ice is null
            || !IsActiveInstalled(iceId, state.Cards, state.Zones)
            || !IsCurrentEncounterIce(state.RunState, iceId)
            ? null
            : ProjectIceQuote(
                behaviorCatalog,
                state.Cards,
                state.Zones,
                state.RulesState,
                state.RunState,
                ice,
                serverKind);
    }

    /// <summary>Projects an encountered ICE using the run's attacked server.</summary>
    public static ValueQuote? ProjectIceQuote(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        CardInstanceId iceId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        RunState? run = state.RunState;
        if (run is null)
        {
            return null;
        }

        ServerState? server = state.Servers.FirstOrDefault(candidate => candidate.Id == run.AttackedServer);
        return server is null
            ? null
            : ProjectIceQuote(state, behaviorCatalog, iceId, server.Kind);
    }

    /// <summary>Projects an installed Runner breaker from authoritative state.</summary>
    public static ValueQuote? ProjectBreakerQuote(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        CardInstanceId breakerId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        CardInstanceState? breaker = state.Cards.FirstOrDefault(card => card.Id == breakerId);
        return breaker is null
            || !IsActiveInstalled(breakerId, state.Cards, state.Zones)
            || !IsEncounterActive(state.RunState)
            ? null
            : ProjectBreakerQuote(
                behaviorCatalog,
                state.Cards,
                state.Zones,
                state.RulesState,
                state.RunState,
                breaker);
    }

    /// <summary>
    /// Internal state-shape overload used by a reducer while it is building a
    /// prospective state. It uses exactly the same projector as the public
    /// GameState overload and never consults a mutable registry.
    /// </summary>
    internal static ValueQuote? ProjectIceQuote(
        IBehaviorCatalog behaviorCatalog,
        IReadOnlyList<CardInstanceState> cards,
        IReadOnlyList<ZoneState> zones,
        RulesState? rulesState,
        RunState? run,
        CardInstanceState ice,
        ServerKind serverKind)
    {
        if (!IsActiveInstalled(ice.Id, cards, zones)
            || !IsCurrentEncounterIce(run, ice.Id))
        {
            return null;
        }

        if (!behaviorCatalog.TryGetIce(ice.DefinitionId, out IceBehaviorSpec? spec))
        {
            return null;
        }

        var terms = new List<ValueCalculationTerm>();
        AddLegacyIceTerms(terms, spec, serverKind, ice.Id);
        AddRunValueAdjustmentTerms(terms, cards, run, ice.Id);
        AddDeclaredTerms(
            terms,
            spec.ValueModifiers,
            ice.DefinitionId,
            ice.Id,
            cards,
            zones,
            rulesState,
            behaviorCatalog,
            run);
        if (behaviorCatalog.TryGet(ice.DefinitionId, out CardBehaviorDefinition? definition))
        {
            AddDeclaredTerms(
                terms,
                definition.ValueModifiers,
                ice.DefinitionId,
                ice.Id,
                cards,
                zones,
                rulesState,
                behaviorCatalog,
                run);
        }

        return new ValueQuote(BehaviorValueKind.Strength, spec.Strength, CalculateEffective(spec.Strength, terms), terms);
    }

    internal static ValueQuote? ProjectBreakerQuote(
        IBehaviorCatalog behaviorCatalog,
        IReadOnlyList<CardInstanceState> cards,
        IReadOnlyList<ZoneState> zones,
        RulesState? rulesState,
        RunState? run,
        CardInstanceState breaker)
    {
        if (!IsActiveInstalled(breaker.Id, cards, zones)
            || !IsEncounterActive(run))
        {
            return null;
        }

        if (!behaviorCatalog.TryGetIcebreaker(breaker.DefinitionId, out IcebreakerBehaviorSpec? spec))
        {
            return null;
        }

        var terms = new List<ValueCalculationTerm>();
        AddPumpTerms(terms, spec, run?.LingeringEffects ?? [], breaker.Id);
        AddRunValueAdjustmentTerms(terms, cards, run, breaker.Id);
        AddDeclaredTerms(
            terms,
            spec.ValueModifiers,
            breaker.DefinitionId,
            breaker.Id,
            cards,
            zones,
            rulesState,
            behaviorCatalog,
            run);
        if (behaviorCatalog.TryGet(breaker.DefinitionId, out CardBehaviorDefinition? definition))
        {
            AddDeclaredTerms(
                terms,
                definition.ValueModifiers,
                breaker.DefinitionId,
                breaker.Id,
                cards,
                zones,
                rulesState,
                behaviorCatalog,
                run);
        }

        return new ValueQuote(
            BehaviorValueKind.Strength,
            spec.BaseStrength,
            CalculateEffective(spec.BaseStrength, terms),
            terms);
    }

    // M2 integer adapters. They remain public because a few compatibility
    // fixtures use them, but all arithmetic now passes through ValueQuote.
    public static int Ice(IceBehaviorSpec spec, ServerKind serverKind) =>
        IceQuote(spec, serverKind).EffectiveValue;

    public static int Breaker(
        IcebreakerBehaviorSpec spec,
        IReadOnlyList<RunLingeringEffect> activeEffects,
        CardInstanceId breakerId) =>
        BreakerQuote(spec, activeEffects, breakerId).EffectiveValue;

    private static void AddLegacyIceTerms(
        List<ValueCalculationTerm> terms,
        IceBehaviorSpec spec,
        ServerKind serverKind,
        CardInstanceId? sourceCardId)
    {
        foreach (IceStrengthModifierSpec modifier in spec.StrengthModifiers)
        {
            if (modifier.Condition == IceStrengthModifierCondition.ProtectingRemoteServer
                && serverKind == ServerKind.Remote)
            {
                terms.Add(new ValueCalculationTerm(
                    BehaviorValueSourceKind.Static,
                    BehaviorValueOperation.Add,
                    modifier.Delta,
                    LegacyRemoteReason,
                    spec.DefinitionId,
                    sourceCardId));
            }
        }
    }

    private static void AddPumpTerms(
        List<ValueCalculationTerm> terms,
        IcebreakerBehaviorSpec spec,
        IReadOnlyList<RunLingeringEffect> activeEffects,
        CardInstanceId breakerId)
    {
        int count = activeEffects.Count(effect =>
            effect.BehaviorId == IcebreakerPumpBehavior
            && effect.SourceCardId == breakerId
            && effect.Expiry == RunLingeringEffectExpiry.Encounter
            && effect.ValueAdjustment is null);
        if (count == 0)
        {
            return;
        }

        int delta = checked(spec.PumpStrength * count);
        terms.Add(new ValueCalculationTerm(
            BehaviorValueSourceKind.Variable,
            BehaviorValueOperation.Add,
            delta,
            PumpReason,
            spec.DefinitionId,
            breakerId,
            count,
            spec.PumpStrength,
            IcebreakerPumpBehavior));
    }

    private static void AddRunValueAdjustmentTerms(
        List<ValueCalculationTerm> terms,
        IReadOnlyList<CardInstanceState> cards,
        RunState? run,
        CardInstanceId targetCardId)
    {
        if (!IsEncounterActive(run) || run is null)
        {
            return;
        }

        foreach (RunLingeringEffect effect in run.LingeringEffects)
        {
            RunValueAdjustment? adjustment = effect.ValueAdjustment;
            if (effect.Expiry != RunLingeringEffectExpiry.Encounter
                || adjustment is null
                || adjustment.TargetCardId != targetCardId
                || effect.SourceCardId != adjustment.SourceCardId
                || adjustment.ValueKind != BehaviorValueKind.Strength
                || adjustment.Operation != BehaviorValueOperation.Add
                || !IsCurrentAdjustmentTarget(adjustment, run, targetCardId))
            {
                continue;
            }

            CardDefinitionId? sourceDefinitionId = cards
                .FirstOrDefault(card => card.Id == adjustment.SourceCardId)
                ?.DefinitionId;
            terms.Add(new ValueCalculationTerm(
                adjustment.Source,
                adjustment.Operation,
                adjustment.Delta,
                adjustment.ReasonKey,
                sourceDefinitionId,
                adjustment.SourceCardId,
                quantity: adjustment.Quantity,
                unitDelta: adjustment.UnitDelta,
                variableKey: adjustment.VariableKey));
        }
    }

    private static bool IsCurrentAdjustmentTarget(
        RunValueAdjustment adjustment,
        RunState? run,
        CardInstanceId targetCardId) => adjustment.Source switch
        {
            // An encounter-scoped source such as Leech targets the ICE that
            // initialized this encounter. The source itself may move after
            // activation; target identity does not change.
            BehaviorValueSourceKind.EncounterScoped =>
                IsEncounterActive(run)
                && run!.Encounter!.IceId == targetCardId,
            // A dynamic pump targets the breaker whose activation created the
            // snapshot. The authoritative public/internal breaker projector
            // additionally requires that target to remain installed.
            BehaviorValueSourceKind.Variable =>
                IsEncounterActive(run)
                && adjustment.SourceCardId == targetCardId,
            _ => false,
        };

    private static void AddDeclaredTerms(
        List<ValueCalculationTerm> terms,
        IReadOnlyList<BehaviorValueModifier>? modifiers,
        CardDefinitionId targetDefinitionId,
        CardInstanceId? sourceCardId,
        IReadOnlyList<CardInstanceState>? cards,
        IReadOnlyList<ZoneState>? zones,
        RulesState? rulesState,
        IBehaviorCatalog? behaviorCatalog,
        RunState? run)
    {
        if (modifiers is null || modifiers.Count == 0)
        {
            return;
        }

        foreach (BehaviorValueModifier modifier in modifiers)
        {
            if (modifier.ValueKind != BehaviorValueKind.Strength
                || modifier.Operation != BehaviorValueOperation.Add)
            {
                // This is defensive for catalogs supplied by an alternate
                // implementation of IBehaviorCatalog. A malformed contract
                // cannot become implicit arithmetic.
                continue;
            }

            switch (modifier.Source)
            {
                case BehaviorValueSourceKind.Static:
                    if (modifier.Condition is not null)
                    {
                        continue;
                    }

                    if (sourceCardId is { } staticSource
                        && cards is not null
                        && zones is not null
                        && !IsActiveInstalled(staticSource, cards, zones))
                    {
                        continue;
                    }

                    terms.Add(Term(
                        modifier,
                        BehaviorValueSourceKind.Static,
                        modifier.Delta,
                        targetDefinitionId,
                        sourceCardId));
                    break;
                case BehaviorValueSourceKind.PerInstalledCard:
                    if (cards is null
                        || zones is null
                        || behaviorCatalog is null
                        || sourceCardId is not { } perSource
                        || !IsActiveInstalled(perSource, cards, zones))
                    {
                        continue;
                    }

                    CardInstanceState? source = cards.FirstOrDefault(card => card.Id == perSource);
                    if (source is null)
                    {
                        continue;
                    }

                    int count = cards.Count(card =>
                        card.OwnerSide == source.OwnerSide
                        && IsActiveInstalled(card.Id, cards, zones)
                        && MatchesFilter(card, modifier.InstalledFilter!, behaviorCatalog));
                    if (count == 0)
                    {
                        continue;
                    }

                    int delta = checked(modifier.Delta * count);
                    terms.Add(Term(
                        modifier,
                        BehaviorValueSourceKind.PerInstalledCard,
                        delta,
                        targetDefinitionId,
                        perSource,
                        quantity: count,
                        unitDelta: modifier.Delta));
                    break;
                case BehaviorValueSourceKind.Variable:
                    if (cards is null
                        || zones is null
                        || sourceCardId is not { } variableSource
                        || !IsActiveInstalled(variableSource, cards, zones)
                        || run is null
                        || modifier.VariableKey is null)
                    {
                        continue;
                    }

                    int variableCount = run.LingeringEffects.Count(effect =>
                        effect.BehaviorId == modifier.VariableKey
                        && effect.SourceCardId == variableSource
                        && effect.Expiry == RunLingeringEffectExpiry.Encounter);
                    if (variableCount == 0)
                    {
                        continue;
                    }

                    terms.Add(Term(
                        modifier,
                        BehaviorValueSourceKind.Variable,
                        checked(modifier.Delta * variableCount),
                        targetDefinitionId,
                        variableSource,
                        quantity: variableCount,
                        unitDelta: modifier.Delta,
                        variableKey: modifier.VariableKey));
                    break;
                case BehaviorValueSourceKind.CounterThreshold:
                    if (cards is null
                        || zones is null
                        || sourceCardId is not { } counterSource
                        || !IsActiveInstalled(counterSource, cards, zones)
                        || modifier.CounterName is null
                        || modifier.Threshold is not { } threshold
                        || CounterValue(counterSource, modifier.CounterName, cards, rulesState) < threshold)
                    {
                        continue;
                    }

                    terms.Add(Term(
                        modifier,
                        BehaviorValueSourceKind.CounterThreshold,
                        modifier.Delta,
                        targetDefinitionId,
                        counterSource));
                    break;
                case BehaviorValueSourceKind.EncounterScoped:
                    if (cards is null
                        || zones is null
                        || sourceCardId is not { } encounterSource
                        || !IsActiveInstalled(encounterSource, cards, zones)
                        || !IsEncounterActive(run))
                    {
                        continue;
                    }

                    terms.Add(Term(
                        modifier,
                        BehaviorValueSourceKind.EncounterScoped,
                        modifier.Delta,
                        targetDefinitionId,
                        encounterSource));
                    break;
                default:
                    // Exhaustive fail-closed behavior for alternate/future
                    // enum values loaded through a custom catalog.
                    continue;
            }
        }
    }

    private static ValueCalculationTerm Term(
        BehaviorValueModifier modifier,
        BehaviorValueSourceKind source,
        int delta,
        CardDefinitionId sourceDefinitionId,
        CardInstanceId? sourceCardId,
        int? quantity = null,
        int? unitDelta = null,
        string? variableKey = null) =>
        new(
            source,
            BehaviorValueOperation.Add,
            delta,
            modifier.ReasonKey,
            sourceDefinitionId,
            sourceCardId,
            quantity,
            unitDelta,
            variableKey);

    private static bool IsEncounterActive(RunState? run) =>
        run is
        {
            Step: RunTimingStep.Encounter or RunTimingStep.EncounterPaidWindow,
            Encounter: { Status: RunEncounterStatus.Initialized },
        }
        && run.Encounter is not null;

    private static bool IsCurrentEncounterIce(RunState? run, CardInstanceId iceId) =>
        IsEncounterActive(run)
        && run!.Encounter!.IceId == iceId;

    /// <summary>
    /// Resolves an activation-time dynamic pump from the same authoritative
    /// installed-card projection used by per-installed value declarations.
    /// </summary>
    internal static bool TryResolveDynamicPump(
        IcebreakerBehaviorSpec spec,
        CardInstanceId breakerId,
        IReadOnlyList<CardInstanceState> cards,
        IReadOnlyList<ZoneState> zones,
        IBehaviorCatalog behaviorCatalog,
        out int quantity,
        out int delta)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(zones);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        quantity = 0;
        delta = 0;
        BehaviorDynamicPumpDeclaration? declaration = spec.DynamicPump;
        if (declaration is null
            || !IsActiveInstalled(breakerId, cards, zones))
        {
            return false;
        }

        CardInstanceState? source = cards.FirstOrDefault(card => card.Id == breakerId);
        if (source is null || source.OwnerSide != PlayerSide.Runner)
        {
            return false;
        }

        quantity = cards.Count(card =>
            IsActiveInstalled(card.Id, cards, zones)
            && MatchesFilter(card, declaration.InstalledFilter, behaviorCatalog));
        if (quantity <= 0)
        {
            quantity = 0;
            return false;
        }

        try
        {
            delta = checked(quantity * declaration.UnitDelta);
            return true;
        }
        catch (OverflowException)
        {
            quantity = 0;
            delta = 0;
            return false;
        }
    }

    private static bool IsActiveInstalled(
        CardInstanceId cardId,
        IReadOnlyList<CardInstanceState> cards,
        IReadOnlyList<ZoneState> zones)
    {
        CardInstanceState? card = cards.FirstOrDefault(candidate => candidate.Id == cardId);
        ZoneState? zone = card is null ? null : zones.FirstOrDefault(candidate => candidate.Id == card.ZoneId);
        if (card is null || zone is null || !zone.CardIds.Contains(cardId))
        {
            return false;
        }

        return zone.Kind switch
        {
            ZoneKind.RunnerRig => card.OwnerSide == PlayerSide.Runner && card.FaceUp,
            ZoneKind.ServerRoot or ZoneKind.ServerIce => card.OwnerSide == PlayerSide.Corp && card.Rezzed,
            _ => false,
        };
    }

    private static int CounterValue(
        CardInstanceId cardId,
        string counterName,
        IReadOnlyList<CardInstanceState> cards,
        RulesState? rulesState)
    {
        CardInstanceState? card = cards.FirstOrDefault(candidate => candidate.Id == cardId);
        if (card is null)
        {
            return 0;
        }

        if (string.Equals(counterName, "advancement", StringComparison.Ordinal))
        {
            return card.AdvancementCounters;
        }

        return rulesState?.Counters
            .Where(counter => counter.CardId == cardId
                && string.Equals(counter.CounterName, counterName, StringComparison.Ordinal))
            .Select(static counter => counter.Count)
            .FirstOrDefault() ?? 0;
    }

    private static bool MatchesFilter(
        CardInstanceState card,
        BehaviorCardFilter filter,
        IBehaviorCatalog behaviorCatalog)
    {
        if (filter.DefinitionId is { } definitionId && definitionId != card.DefinitionId)
        {
            return false;
        }

        if (filter.OwnerSide is { } ownerSide && ownerSide != card.OwnerSide)
        {
            return false;
        }

        if (filter.Subtype is not null)
        {
            // Subtype metadata belongs to the optional runtime catalog and is
            // intentionally not inferred from titles or rules text here.
            return false;
        }

        return filter.CardType switch
        {
            BehaviorCardType.Any => true,
            BehaviorCardType.Ice => behaviorCatalog.TryGetIce(card.DefinitionId, out _),
            BehaviorCardType.Icebreaker => behaviorCatalog.TryGetIcebreaker(card.DefinitionId, out _),
            BehaviorCardType.Asset => behaviorCatalog.TryGetAsset(card.DefinitionId, out _),
            BehaviorCardType.Agenda => behaviorCatalog.TryGetAgenda(card.DefinitionId, out _),
            BehaviorCardType.Event => behaviorCatalog.TryGetRunEvent(card.DefinitionId, out _),
            _ => behaviorCatalog.TryGetCardType(card.DefinitionId, out BehaviorCardType type)
                && type == filter.CardType,
        };
    }

    private static int CalculateEffective(int baseValue, IReadOnlyList<ValueCalculationTerm> terms)
    {
        long total = baseValue;
        foreach (ValueCalculationTerm term in terms)
        {
            total = checked(total + term.Delta);
        }

        if (total > int.MaxValue || total < int.MinValue)
        {
            throw new OverflowException("Value quote terms exceed the supported integer range.");
        }

        return (int)total;
    }
}

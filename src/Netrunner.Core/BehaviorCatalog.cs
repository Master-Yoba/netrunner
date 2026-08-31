using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

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
}

/// <summary>
/// A typed cost used by a composable compiled card behavior.  The first slice
/// defines the contract; later card batches add concrete instruction users.
/// </summary>
public sealed record BehaviorCost
{
    public BehaviorCost(BehaviorCostKind kind, int amount = 1, string? counterName = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "A behavior cost amount must not be negative.");
        }

        if (kind == BehaviorCostKind.Counter
            && string.IsNullOrWhiteSpace(counterName))
        {
            throw new ArgumentException("Counter costs require a counter name.", nameof(counterName));
        }

        Kind = kind;
        Amount = amount;
        CounterName = counterName;
    }

    public BehaviorCostKind Kind { get; }

    public int Amount { get; }

    public string? CounterName { get; }
}

public enum BehaviorCostKind
{
    Credits,
    Clicks,
    Tags,
    Counter,
    TrashSelf,
    LoseClick,
}

/// <summary>A serializable condition term in a compiled behavior contract.</summary>
public sealed record BehaviorCondition
{
    public BehaviorCondition(BehaviorConditionKind kind, string? fact = null, int? value = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (kind is BehaviorConditionKind.FactEquals or BehaviorConditionKind.CounterAtLeast
            && string.IsNullOrWhiteSpace(fact))
        {
            throw new ArgumentException("This condition kind requires a fact or counter name.", nameof(fact));
        }

        Fact = fact;
        Kind = kind;
        Value = value;
    }

    public BehaviorConditionKind Kind { get; }

    public string? Fact { get; }

    public int? Value { get; }
}

public enum BehaviorConditionKind
{
    FactEquals,
    CounterAtLeast,
    Installed,
    Encountering,
    SuccessfulRun,
    SourceController,
}

/// <summary>A typed target selector; IDs are resolved by Core at runtime.</summary>
public sealed record BehaviorTarget
{
    public BehaviorTarget(BehaviorTargetKind kind, string? selector = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (kind == BehaviorTargetKind.NamedCard && string.IsNullOrWhiteSpace(selector))
        {
            throw new ArgumentException("Named-card targets require a selector.", nameof(selector));
        }

        Kind = kind;
        Selector = selector;
    }

    public BehaviorTargetKind Kind { get; }

    public string? Selector { get; }
}

public enum BehaviorTargetKind
{
    Self,
    Controller,
    Opponent,
    InstalledCard,
    NamedCard,
    Server,
    AccessedCard,
    ChosenCard,
}

/// <summary>One inert, typed instruction in a compiled behavior program.</summary>
public sealed record BehaviorInstruction
{
    public BehaviorInstruction(BehaviorInstructionKind kind, int amount = 0, string? argument = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        Kind = kind;
        Amount = amount;
        Argument = argument;
    }

    public BehaviorInstructionKind Kind { get; }

    public int Amount { get; }

    public string? Argument { get; }
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
}

/// <summary>A static value modifier with a typed condition and reason key.</summary>
public sealed record BehaviorStaticModifier
{
    public BehaviorStaticModifier(string valueKey, int delta, string? reasonKey = null)
    {
        if (string.IsNullOrWhiteSpace(valueKey))
        {
            throw new ArgumentException("A modifier value key is required.", nameof(valueKey));
        }

        ValueKey = valueKey;
        Delta = delta;
        ReasonKey = reasonKey;
    }

    public string ValueKey { get; }

    public int Delta { get; }

    public string? ReasonKey { get; }
}

/// <summary>A trigger binding identified by occurrence and timing window.</summary>
public sealed record BehaviorTrigger
{
    public BehaviorTrigger(string occurrenceKey, TriggerMode mode = TriggerMode.Optional)
    {
        if (string.IsNullOrWhiteSpace(occurrenceKey))
        {
            throw new ArgumentException("A trigger occurrence key is required.", nameof(occurrenceKey));
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        OccurrenceKey = occurrenceKey;
        Mode = mode;
    }

    public string OccurrenceKey { get; }

    public TriggerMode Mode { get; }
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
    public CardBehaviorDefinition(
        CardDefinitionId definitionId,
        PlayerSide controller,
        IEnumerable<BehaviorCost>? costs = null,
        IEnumerable<BehaviorCondition>? conditions = null,
        IEnumerable<BehaviorTarget>? targets = null,
        IEnumerable<BehaviorInstruction>? instructions = null,
        IEnumerable<BehaviorStaticModifier>? staticModifiers = null,
        IEnumerable<BehaviorTrigger>? triggers = null,
        string? extensionHandlerId = null)
    {
        DefinitionId = definitionId;
        if (!Enum.IsDefined(controller))
        {
            throw new ArgumentOutOfRangeException(nameof(controller));
        }

        Controller = controller;
        Costs = Freeze(costs);
        Conditions = Freeze(conditions);
        Targets = Freeze(targets);
        Instructions = Freeze(instructions);
        StaticModifiers = Freeze(staticModifiers);
        Triggers = Freeze(triggers);
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
}

/// <summary>
/// Immutable behavior catalog.  Use <see cref="BehaviorCatalogBuilder"/> to
/// compose one; once built, all collections and lookups are detached and safe
/// to share between parallel engine sessions.
/// </summary>
public sealed class BehaviorCatalog : IBehaviorCatalog
{
    private readonly IReadOnlyDictionary<CardDefinitionId, IceBehaviorSpec> _ice;
    private readonly IReadOnlyDictionary<CardDefinitionId, IcebreakerBehaviorSpec> _icebreakers;
    private readonly IReadOnlyDictionary<CardDefinitionId, AssetBehaviorSpec> _assets;
    private readonly IReadOnlyDictionary<CardDefinitionId, AgendaBehaviorSpec> _agendas;
    private readonly IReadOnlyDictionary<CardDefinitionId, RunEventBehaviorSpec> _runEvents;
    private readonly IReadOnlyDictionary<CardDefinitionId, CardBehaviorDefinition> _generic;

    internal BehaviorCatalog(
        string version,
        IReadOnlyDictionary<CardDefinitionId, IceBehaviorSpec> ice,
        IReadOnlyDictionary<CardDefinitionId, IcebreakerBehaviorSpec> icebreakers,
        IReadOnlyDictionary<CardDefinitionId, AssetBehaviorSpec> assets,
        IReadOnlyDictionary<CardDefinitionId, AgendaBehaviorSpec> agendas,
        IReadOnlyDictionary<CardDefinitionId, RunEventBehaviorSpec> runEvents,
        IReadOnlyDictionary<CardDefinitionId, CardBehaviorDefinition> generic)
    {
        Version = ValidateVersion(version);
        BehaviorRegistryVersion = Version;
        _ice = Freeze(ice);
        _icebreakers = Freeze(icebreakers);
        _assets = Freeze(assets);
        _agendas = Freeze(agendas);
        _runEvents = Freeze(runEvents);
        _generic = Freeze(generic);
        ValidateTypedFamilyConflicts(_ice, _icebreakers, _assets, _agendas, _runEvents);
        Fingerprint = BehaviorCatalogFingerprint.Compute(
            Version,
            _ice,
            _icebreakers,
            _assets,
            _agendas,
            _runEvents,
            _generic);
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

    public BehaviorCatalog Build() => new(
        Version,
        _ice,
        _icebreakers,
        _assets,
        _agendas,
        _runEvents,
        _generic);

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
                && SequenceEquals(a.StrengthModifiers, b.StrengthModifiers),
            (IcebreakerBehaviorSpec a, IcebreakerBehaviorSpec b) => a.DefinitionId == b.DefinitionId
                && a.BaseStrength == b.BaseStrength
                && a.InterfaceType == b.InterfaceType
                && a.BreakCost == b.BreakCost
                && a.BreakLimit == b.BreakLimit
                && a.PumpCost == b.PumpCost
                && a.PumpStrength == b.PumpStrength,
            (AssetBehaviorSpec a, AssetBehaviorSpec b) => a.DefinitionId == b.DefinitionId && a.TrashCost == b.TrashCost,
            (AgendaBehaviorSpec a, AgendaBehaviorSpec b) => a.DefinitionId == b.DefinitionId && a.AgendaPoints == b.AgendaPoints,
            (RunEventBehaviorSpec a, RunEventBehaviorSpec b) => a.DefinitionId == b.DefinitionId
                && a.Cost == b.Cost
                && a.HostedCredits == b.HostedCredits,
            (CardBehaviorDefinition a, CardBehaviorDefinition b) => a.DefinitionId == b.DefinitionId
                && a.Controller == b.Controller
                && SequenceEquals(a.Costs, b.Costs)
                && SequenceEquals(a.Conditions, b.Conditions)
                && SequenceEquals(a.Targets, b.Targets)
                && SequenceEquals(a.Instructions, b.Instructions)
                && SequenceEquals(a.StaticModifiers, b.StaticModifiers)
                && SequenceEquals(a.Triggers, b.Triggers)
                && a.ExtensionHandlerId == b.ExtensionHandlerId,
            _ => false,
        };

    public static bool SequenceEquals<T>(IReadOnlyList<T> left, IReadOnlyList<T> right)
        where T : notnull => left.SequenceEqual(right);
}

internal static class BehaviorCatalogFingerprint
{
    public static string Compute(
        string version,
        IReadOnlyDictionary<CardDefinitionId, IceBehaviorSpec> ice,
        IReadOnlyDictionary<CardDefinitionId, IcebreakerBehaviorSpec> icebreakers,
        IReadOnlyDictionary<CardDefinitionId, AssetBehaviorSpec> assets,
        IReadOnlyDictionary<CardDefinitionId, AgendaBehaviorSpec> agendas,
        IReadOnlyDictionary<CardDefinitionId, RunEventBehaviorSpec> runEvents,
        IReadOnlyDictionary<CardDefinitionId, CardBehaviorDefinition> generic)
    {
        var builder = new StringBuilder();
        builder.Append("version\n").Append(version).Append('\n');
        AppendFamily(builder, "ice", ice, AppendIce);
        AppendFamily(builder, "icebreaker", icebreakers, AppendIcebreaker);
        AppendFamily(builder, "asset", assets, AppendAsset);
        AppendFamily(builder, "agenda", agendas, AppendAgenda);
        AppendFamily(builder, "run-event", runEvents, AppendRunEvent);
        AppendFamily(builder, "generic", generic, AppendGeneric);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static void AppendFamily<T>(
        StringBuilder builder,
        string family,
        IReadOnlyDictionary<CardDefinitionId, T> values,
        Action<StringBuilder, T> append)
        where T : class
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
    }

    private static void AppendIcebreaker(StringBuilder builder, IcebreakerBehaviorSpec value) => builder
        .Append(value.BaseStrength).Append('|').Append(value.InterfaceType).Append('|')
        .Append(value.BreakCost).Append('|').Append(value.BreakLimit).Append('|')
        .Append(value.PumpCost).Append('|').Append(value.PumpStrength);

    private static void AppendAsset(StringBuilder builder, AssetBehaviorSpec value) => builder.Append(value.TrashCost);

    private static void AppendAgenda(StringBuilder builder, AgendaBehaviorSpec value) => builder.Append(value.AgendaPoints);

    private static void AppendRunEvent(StringBuilder builder, RunEventBehaviorSpec value) => builder
        .Append(value.Cost).Append('|').Append(value.HostedCredits);

    private static void AppendGeneric(StringBuilder builder, CardBehaviorDefinition value)
    {
        builder.Append(value.Controller).Append('|').Append(value.ExtensionHandlerId ?? string.Empty);
        AppendValues(builder, value.Costs);
        AppendValues(builder, value.Conditions);
        AppendValues(builder, value.Targets);
        AppendValues(builder, value.Instructions);
        AppendValues(builder, value.StaticModifiers);
        AppendValues(builder, value.Triggers);
    }

    private static void AppendValues<T>(StringBuilder builder, IReadOnlyList<T> values)
        where T : notnull
    {
        foreach (T value in values)
        {
            builder.Append('|').Append(value);
        }
    }
}

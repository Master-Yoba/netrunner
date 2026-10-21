using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

/// <summary>
/// The compiled card class used by the real-turn card pipeline.  This is
/// deliberately independent from <see cref="IBehaviorCatalog"/>: a card may
/// have printed play/install metadata without having an implemented ability.
/// </summary>
public enum RuntimeCardClass
{
    Identity,
    Agenda,
    Asset,
    Upgrade,
    Ice,
    Operation,
    Event,
    Program,
    Hardware,
    Resource,
}

/// <summary>
/// Immutable, typed printed metadata for one card definition.  The engine
/// never derives these values from a title or rules text.  Null costs mean the
/// field is not applicable to this class (for example, an agenda has no
/// printed rez cost).
/// </summary>
public sealed record CardRuntimeSpec
{
    [JsonConstructor]
    public CardRuntimeSpec(
        CardDefinitionId definitionId,
        PlayerSide side,
        RuntimeCardClass cardClass,
        int? playCost = null,
        int? installCost = null,
        int? rezCost = null,
        int memoryUnits = 0,
        int? trashCost = null,
        int agendaPoints = 0,
        int advancementRequirement = 0,
        IReadOnlyList<string>? subtypes = null,
        bool unique = false,
        bool console = false,
        string? uniquenessKey = null,
        string? consoleKey = null)
    {
        if (!Enum.IsDefined(side))
        {
            throw new ArgumentOutOfRangeException(nameof(side));
        }

        if (!Enum.IsDefined(cardClass))
        {
            throw new ArgumentOutOfRangeException(nameof(cardClass));
        }

        ValidateNonNegative(playCost, nameof(playCost));
        ValidateNonNegative(installCost, nameof(installCost));
        ValidateNonNegative(rezCost, nameof(rezCost));
        ValidateNonNegative(trashCost, nameof(trashCost));
        ValidateNonNegative(memoryUnits, nameof(memoryUnits));
        ValidateNonNegative(agendaPoints, nameof(agendaPoints));
        ValidateNonNegative(advancementRequirement, nameof(advancementRequirement));

        if (subtypes is not null && subtypes.Any(value => !StableIdentifier.TryValidate(value)))
        {
            throw new ArgumentException("Card subtypes must be stable non-empty identifiers.", nameof(subtypes));
        }

        if (uniquenessKey is not null && !StableIdentifier.TryValidate(uniquenessKey))
        {
            throw new ArgumentException("A uniqueness key must be stable.", nameof(uniquenessKey));
        }

        if (consoleKey is not null && !StableIdentifier.TryValidate(consoleKey))
        {
            throw new ArgumentException("A console key must be stable.", nameof(consoleKey));
        }

        ValidateClassShape(
            side,
            cardClass,
            playCost,
            installCost,
            rezCost,
            memoryUnits,
            trashCost,
            agendaPoints,
            advancementRequirement,
            subtypes,
            unique,
            console,
            uniquenessKey,
            consoleKey);

        DefinitionId = definitionId;
        Side = side;
        CardClass = cardClass;
        PlayCost = playCost;
        InstallCost = installCost;
        RezCost = rezCost;
        MemoryUnits = memoryUnits;
        TrashCost = trashCost;
        AgendaPoints = agendaPoints;
        AdvancementRequirement = advancementRequirement;
        Subtypes = new ReadOnlyCollection<string>((subtypes ?? [])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray());
        Unique = unique;
        Console = console;
        UniquenessKey = uniquenessKey;
        ConsoleKey = consoleKey;
    }

    [JsonInclude]
    [JsonRequired]
    public CardDefinitionId DefinitionId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide Side { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public RuntimeCardClass CardClass { get; private set; }

    /// <summary>Alias used by projections that call the field simply Class.</summary>
    [JsonIgnore]
    public RuntimeCardClass Class => CardClass;

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PlayCost { get; private set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? InstallCost { get; private set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RezCost { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int MemoryUnits { get; private set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TrashCost { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int AgendaPoints { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int AdvancementRequirement { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<string> Subtypes { get; private set; }

    [JsonInclude]
    [JsonRequired]
    [JsonPropertyName("unique")]
    public bool Unique { get; private set; }

    [JsonIgnore]
    public bool IsUnique => Unique;

    [JsonInclude]
    [JsonRequired]
    [JsonPropertyName("console")]
    public bool Console { get; private set; }

    [JsonIgnore]
    public bool IsConsole => Console;

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UniquenessKey { get; private set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConsoleKey { get; private set; }

    private static void ValidateNonNegative(int value, string name) =>
        ValidateNonNegative((int?)value, name);

    private static void ValidateNonNegative(int? value, string name)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(name, "Printed values must not be negative.");
        }
    }

    private static void ValidateClassShape(
        PlayerSide side,
        RuntimeCardClass cardClass,
        int? playCost,
        int? installCost,
        int? rezCost,
        int memoryUnits,
        int? trashCost,
        int agendaPoints,
        int advancementRequirement,
        IReadOnlyList<string>? subtypes,
        bool unique,
        bool console,
        string? uniquenessKey,
        string? consoleKey)
    {
        bool canPlay = cardClass is RuntimeCardClass.Operation or RuntimeCardClass.Event;
        bool canRez = cardClass is RuntimeCardClass.Asset or RuntimeCardClass.Upgrade or RuntimeCardClass.Ice;

        bool validSide = cardClass switch
        {
            RuntimeCardClass.Identity => true,
            RuntimeCardClass.Agenda
                or RuntimeCardClass.Asset
                or RuntimeCardClass.Upgrade
                or RuntimeCardClass.Ice
                or RuntimeCardClass.Operation => side == PlayerSide.Corp,
            RuntimeCardClass.Event
                or RuntimeCardClass.Program
                or RuntimeCardClass.Hardware
                or RuntimeCardClass.Resource => side == PlayerSide.Runner,
            _ => false,
        };
        if (!validSide)
        {
            throw new ArgumentException($"Runtime card class '{cardClass}' is not valid for side '{side}'.", nameof(side));
        }

        if ((playCost is not null) != canPlay)
        {
            throw new ArgumentException($"Runtime card class '{cardClass}' must {(canPlay ? "have" : "not have")} a play cost.", nameof(playCost));
        }

        bool requiresPrintedInstallCost = cardClass is RuntimeCardClass.Program
            or RuntimeCardClass.Hardware
            or RuntimeCardClass.Resource;
        if ((installCost is not null) != requiresPrintedInstallCost)
        {
            throw new ArgumentException($"Runtime card class '{cardClass}' must {(requiresPrintedInstallCost ? "have" : "not have")} a printed install cost.", nameof(installCost));
        }

        if ((rezCost is not null) != canRez)
        {
            throw new ArgumentException($"Runtime card class '{cardClass}' must {(canRez ? "have" : "not have")} a rez cost.", nameof(rezCost));
        }

        if (cardClass != RuntimeCardClass.Program && memoryUnits != 0)
        {
            throw new ArgumentException("Only programs may contribute memory units.", nameof(memoryUnits));
        }

        if (cardClass != RuntimeCardClass.Agenda
            && (agendaPoints != 0 || advancementRequirement != 0))
        {
            throw new ArgumentException("Agenda points and advancement requirements are only valid for agendas.");
        }

        if (cardClass == RuntimeCardClass.Agenda
            && (agendaPoints <= 0 || advancementRequirement <= 0))
        {
            throw new ArgumentException("An agenda requires positive agenda points and advancement requirement.", nameof(agendaPoints));
        }

        // CR 2.6.1: assets and upgrades have trash costs, as do some ice and
        // operations.
        bool mayHavePrintedTrashCost = side == PlayerSide.Corp
            && cardClass is RuntimeCardClass.Asset
                or RuntimeCardClass.Upgrade
                or RuntimeCardClass.Operation
                or RuntimeCardClass.Ice;
        if (!mayHavePrintedTrashCost && trashCost is not null)
        {
            throw new ArgumentException(
                "Printed trash cost is only applicable to Corp assets, upgrades, ice and operations.",
                nameof(trashCost));
        }

        if (unique && uniquenessKey is null)
        {
            throw new ArgumentException("A unique card requires an explicit uniqueness key.", nameof(uniquenessKey));
        }

        if (console)
        {
            if (cardClass != RuntimeCardClass.Hardware)
            {
                throw new ArgumentException("Only hardware may be a console.", nameof(console));
            }

            if (consoleKey is null)
            {
                throw new ArgumentException("A console requires an explicit console key.", nameof(consoleKey));
            }

            if (subtypes is null || !subtypes.Any(value => string.Equals(value, "console", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("A console must declare the 'console' subtype.", nameof(subtypes));
            }
        }

        if (consoleKey is not null && !console)
        {
            throw new ArgumentException("A console key is only valid when the card is a console.", nameof(consoleKey));
        }
    }
}

/// <summary>Immutable composition dependency for real card actions.</summary>
public interface IRuntimeCardCatalog
{
    string Version { get; }

    string Fingerprint { get; }

    IReadOnlyList<CardDefinitionId> DefinitionIds { get; }

    bool TryGet(CardDefinitionId definitionId, out CardRuntimeSpec spec);

    CardRuntimeSpec GetSpec(CardDefinitionId definitionId);
}

/// <summary>
/// Deterministic runtime metadata catalog.  Classification is an adjunct and
/// is intentionally absent from BehaviorCatalog.AllDefinitionIds.
/// </summary>
public sealed class RuntimeCardCatalog : IRuntimeCardCatalog
{
    private readonly ReadOnlyDictionary<CardDefinitionId, CardRuntimeSpec> _specs;

    [JsonConstructor]
    public RuntimeCardCatalog(string version, IReadOnlyList<CardRuntimeSpec>? specs = null)
    {
        Version = StableIdentifier.Validate(version, nameof(version));
        var map = new Dictionary<CardDefinitionId, CardRuntimeSpec>();
        foreach (CardRuntimeSpec spec in specs ?? [])
        {
            ArgumentNullException.ThrowIfNull(spec);
            if (!map.TryAdd(spec.DefinitionId, spec))
            {
                throw new ArgumentException($"Runtime metadata for '{spec.DefinitionId}' is duplicated.", nameof(specs));
            }
        }

        _specs = new ReadOnlyDictionary<CardDefinitionId, CardRuntimeSpec>(map);
        Specs = new ReadOnlyCollection<CardRuntimeSpec>(map.Values
            .OrderBy(value => value.DefinitionId.Value, StringComparer.Ordinal)
            .ToArray());
        DefinitionIds = new ReadOnlyCollection<CardDefinitionId>(map.Keys
            .OrderBy(value => value.Value, StringComparer.Ordinal)
            .ToArray());
        Fingerprint = ComputeFingerprint(Version, DefinitionIds.Select(id => map[id]).ToArray());
    }

    public RuntimeCardCatalog(string version, params CardRuntimeSpec[] specs)
        : this(version, (IReadOnlyList<CardRuntimeSpec>)specs)
    {
    }

    public string Version { get; }

    public string Fingerprint { get; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<CardRuntimeSpec> Specs { get; private set; }

    [JsonIgnore]
    public IReadOnlyList<CardDefinitionId> DefinitionIds { get; }

    public bool TryGet(CardDefinitionId definitionId, out CardRuntimeSpec spec) =>
        _specs.TryGetValue(definitionId, out spec!);

    public CardRuntimeSpec GetSpec(CardDefinitionId definitionId) =>
        TryGet(definitionId, out CardRuntimeSpec spec)
            ? spec
            : throw new KeyNotFoundException($"Runtime metadata for '{definitionId}' is not registered.");

    public static RuntimeCardCatalog Empty(string version = "runtime-empty") =>
        new(version, []);

    private static string ComputeFingerprint(string version, IReadOnlyList<CardRuntimeSpec> specs)
    {
        // Build a primitive canonical representation rather than hashing the
        // catalog object so derived lookup properties never affect the pin.
        var entries = specs.Select(spec => new
        {
            definitionId = spec.DefinitionId.Value,
            side = spec.Side.ToString(),
            cardClass = spec.CardClass.ToString(),
            playCost = spec.PlayCost,
            installCost = spec.InstallCost,
            rezCost = spec.RezCost,
            memoryUnits = spec.MemoryUnits,
            trashCost = spec.TrashCost,
            agendaPoints = spec.AgendaPoints,
            advancementRequirement = spec.AdvancementRequirement,
            subtypes = spec.Subtypes,
            unique = spec.IsUnique,
            console = spec.IsConsole,
            uniquenessKey = spec.UniquenessKey,
            consoleKey = spec.ConsoleKey,
        }).ToArray();
        byte[] bytes = CanonicalJson.SerializeUtf8(new { version, specs = entries });
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}

/// <summary>Convenient deterministic builder for composition roots.</summary>
public sealed class RuntimeCardCatalogBuilder
{
    private readonly string _version;
    private readonly List<CardRuntimeSpec> _specs = [];

    public RuntimeCardCatalogBuilder(string version)
    {
        _version = StableIdentifier.Validate(version, nameof(version));
    }

    public RuntimeCardCatalogBuilder Add(CardRuntimeSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        _specs.Add(spec);
        return this;
    }

    public RuntimeCardCatalog Build() => new(_version, _specs);
}

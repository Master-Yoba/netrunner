using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

public static partial class ResolutionBehaviorIds
{
    public const string Ability = "rules.paid-ability-4.3";
}

/// <summary>
/// Serializable, data-only cursor for one behavior-owned ability. The frame
/// retains the exact issued snapshots; execution, payment, and state changes
/// are deliberately outside this schema checkpoint.
/// </summary>
public sealed record AbilityFrame : ResolutionFrame
{
    [JsonConstructor]
    public AbilityFrame(
        int version,
        string behaviorId,
        CardDefinitionId definitionId,
        CardInstanceId sourceCardId,
        string abilityKey,
        PlayerId controller,
        BehaviorAbilityTiming timing,
        BehaviorAbilityActivation activation,
        IReadOnlyList<BehaviorCost> costs,
        IReadOnlyList<CardInstanceId> targetCardIds,
        AbilityUsageSnapshot usage,
        string catalogFingerprint,
        string definitionFingerprint,
        ZoneId sourceZoneId,
        ZoneKind sourceZoneKind,
        AbilityLifecycleStage stage,
        string? parentFrameIdentity = null)
        : base(version, behaviorId)
    {
        ArgumentNullException.ThrowIfNull(behaviorId);
        if (!StableIdentifier.TryValidate(definitionId.Value))
        {
            throw new ArgumentException(
                "An ability frame requires a stable definition ID.",
                nameof(definitionId));
        }

        if (!StableIdentifier.TryValidate(sourceCardId.Value))
        {
            throw new ArgumentException(
                "An ability frame requires a stable source card ID.",
                nameof(sourceCardId));
        }

        if (!StableIdentifier.TryValidate(controller.Value))
        {
            throw new ArgumentException(
                "An ability frame requires a stable controller ID.",
                nameof(controller));
        }

        if (!StableIdentifier.TryValidate(sourceZoneId.Value))
        {
            throw new ArgumentException(
                "An ability frame requires a stable source zone ID.",
                nameof(sourceZoneId));
        }

        if (!Enum.IsDefined(sourceZoneKind))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceZoneKind));
        }

        if (!Enum.IsDefined(stage))
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }

        if (!StableIdentifier.TryValidate(abilityKey))
        {
            throw new ArgumentException(
                "An ability frame requires a stable ability key.",
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
                "An ability frame requires a lowercase SHA-256 catalog fingerprint.",
                nameof(catalogFingerprint));
        }

        if (!DeterministicHash.IsSha256Hex(definitionFingerprint))
        {
            throw new ArgumentException(
                "An ability frame requires a lowercase SHA-256 definition fingerprint.",
                nameof(definitionFingerprint));
        }

        ArgumentNullException.ThrowIfNull(costs);
        ArgumentNullException.ThrowIfNull(targetCardIds);
        ArgumentNullException.ThrowIfNull(usage);
        if (parentFrameIdentity is not null && !StableIdentifier.TryValidate(parentFrameIdentity))
        {
            throw new ArgumentException(
                "An ability frame parent identity must be stable.",
                nameof(parentFrameIdentity));
        }

        DefinitionId = definitionId;
        SourceCardId = sourceCardId;
        AbilityKey = abilityKey;
        Controller = controller;
        SourceZoneId = sourceZoneId;
        SourceZoneKind = sourceZoneKind;
        Timing = timing;
        Activation = activation;
        Stage = stage;
        Costs = Freeze(costs, nameof(costs));
        TargetCardIds = FreezeIds(targetCardIds);
        Usage = new AbilityUsageSnapshot(usage.Limit, usage.UsesThisTurn);
        CatalogFingerprint = catalogFingerprint;
        DefinitionFingerprint = definitionFingerprint;
        ParentFrameIdentity = parentFrameIdentity;
    }

    [JsonInclude]
    [JsonRequired]
    public CardDefinitionId DefinitionId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId SourceCardId { get; private set; }

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
    public PlayerId Controller { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public BehaviorAbilityTiming Timing { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public BehaviorAbilityActivation Activation { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public AbilityLifecycleStage Stage { get; private set; }

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

    [JsonInclude]
    [JsonRequired]
    public string? ParentFrameIdentity { get; private set; }

    private static ReadOnlyCollection<BehaviorCost> Freeze(
        IReadOnlyList<BehaviorCost> values,
        string parameterName)
    {
        BehaviorCost[] copy = values.ToArray();
        if (copy.Any(static value => value is null))
        {
            throw new ArgumentException(
                "Ability frame costs must not contain null entries.",
                parameterName);
        }

        return new ReadOnlyCollection<BehaviorCost>(copy);
    }

    private static ReadOnlyCollection<CardInstanceId> FreezeIds(
        IReadOnlyList<CardInstanceId> values) =>
        new(values.ToArray());
}

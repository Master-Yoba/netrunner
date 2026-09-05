using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

/// <summary>The original installed object whose persistent ability survived access trashing.</summary>
public sealed record PersistentRunAbility(
    CardInstanceId SourceCardId,
    CardDefinitionId DefinitionId,
    ZoneId SourceZoneId,
    string TriggerKey);

/// <summary>Run-local facts carried into the run-end reaction checkpoint (CR 9.12.5).</summary>
public sealed record RunPersistentContext
{
    [JsonConstructor]
    public RunPersistentContext(
        RunId runId,
        ServerId serverId,
        IReadOnlyList<CardInstanceId> stolenAgendas,
        IReadOnlyList<PersistentRunAbility> abilities)
    {
        ArgumentNullException.ThrowIfNull(stolenAgendas);
        ArgumentNullException.ThrowIfNull(abilities);
        if (!StableIdentifier.TryValidate(runId.Value) || !StableIdentifier.TryValidate(serverId.Value)
            || stolenAgendas.Any(id => !StableIdentifier.TryValidate(id.Value))
            || stolenAgendas.Distinct().Count() != stolenAgendas.Count
            || abilities.Any(value => value is null
                || !StableIdentifier.TryValidate(value.SourceCardId.Value)
                || !StableIdentifier.TryValidate(value.DefinitionId.Value)
                || !StableIdentifier.TryValidate(value.SourceZoneId.Value)
                || !StableIdentifier.TryValidate(value.TriggerKey))
            || abilities.DistinctBy(value => (value.SourceCardId, value.TriggerKey)).Count() != abilities.Count)
            throw new ArgumentException("Persistent run context requires distinct, valid source identities.");
        RunId = runId;
        ServerId = serverId;
        StolenAgendas = new ReadOnlyCollection<CardInstanceId>(stolenAgendas.ToArray());
        Abilities = new ReadOnlyCollection<PersistentRunAbility>(abilities.ToArray());
    }

    public RunId RunId { get; }
    public ServerId ServerId { get; }
    public IReadOnlyList<CardInstanceId> StolenAgendas { get; }
    public IReadOnlyList<PersistentRunAbility> Abilities { get; }

    internal RunPersistentContext Copy() => new(RunId, ServerId, StolenAgendas, Abilities);

    internal static bool Matches(RunPersistentContext? left, RunPersistentContext? right) =>
        left is null ? right is null : right is not null
            && left.RunId == right.RunId && left.ServerId == right.ServerId
            && left.StolenAgendas.SequenceEqual(right.StolenAgendas)
            && left.Abilities.SequenceEqual(right.Abilities);
}

using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

/// <summary>The original installed object whose persistent ability survived access trashing.</summary>
public sealed record PersistentRunAbility(
    CardInstanceId SourceCardId,
    CardDefinitionId DefinitionId,
    ZoneId SourceZoneId,
    string TriggerKey);

/// <summary>
/// VP-RETURN-AT-RUN-END: a card a source's ability remembered for its run's end, by a stable key (the program Beta Build
/// installed).
/// </summary>
public sealed record RunRememberedCard(CardInstanceId SourceCardId, string Key, CardInstanceId CardId);

/// <summary>Run-local facts carried into the run-end reaction checkpoint (CR 9.12.5).</summary>
public sealed record RunPersistentContext
{
    [JsonConstructor]
    public RunPersistentContext(
        RunId runId,
        ServerId serverId,
        IReadOnlyList<CardInstanceId> stolenAgendas,
        IReadOnlyList<PersistentRunAbility> abilities,
        int accessOccurrenceCount = 0,
        IReadOnlyList<RunRememberedCard>? rememberedCards = null)
    {
        if (rememberedCards is not null && (rememberedCards.Count == 0
            || rememberedCards.Any(value => value is null
                || !StableIdentifier.TryValidate(value.SourceCardId.Value)
                || !StableIdentifier.TryValidate(value.Key)
                || !StableIdentifier.TryValidate(value.CardId.Value))
            || rememberedCards.DistinctBy(value => (value.SourceCardId, value.Key)).Count() != rememberedCards.Count))
            throw new ArgumentException("Remembered cards are omitted while none, and each source remembers a key once.");
        RememberedCards = rememberedCards is null ? null : new ReadOnlyCollection<RunRememberedCard>(rememberedCards.ToArray());
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
        ArgumentOutOfRangeException.ThrowIfNegative(accessOccurrenceCount);
        AccessOccurrenceCount = accessOccurrenceCount;
        RunId = runId;
        ServerId = serverId;
        StolenAgendas = new ReadOnlyCollection<CardInstanceId>(stolenAgendas.ToArray());
        Abilities = new ReadOnlyCollection<PersistentRunAbility>(abilities.ToArray());
    }

    [JsonInclude]
    [JsonRequired]
    public int AccessOccurrenceCount { get; private set; }

    public RunId RunId { get; }
    public ServerId ServerId { get; }
    public IReadOnlyList<CardInstanceId> StolenAgendas { get; }
    public IReadOnlyList<PersistentRunAbility> Abilities { get; }

    /// <summary>VP-RETURN-AT-RUN-END: cards abilities remembered for the run's end; omitted while none.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<RunRememberedCard>? RememberedCards { get; }

    internal RunPersistentContext Copy() => new(RunId, ServerId, StolenAgendas, Abilities, AccessOccurrenceCount, RememberedCards);

    internal RunPersistentContext WithRemembered(RunRememberedCard remembered) => new(RunId, ServerId, StolenAgendas, Abilities,
        AccessOccurrenceCount,
        (RememberedCards ?? []).Where(value => value.SourceCardId != remembered.SourceCardId || value.Key != remembered.Key)
            .Append(remembered).ToArray());

    internal static bool Matches(RunPersistentContext? left, RunPersistentContext? right) =>
        left is null ? right is null : right is not null
            && left.RunId == right.RunId && left.ServerId == right.ServerId
            && left.AccessOccurrenceCount == right.AccessOccurrenceCount
            && left.StolenAgendas.SequenceEqual(right.StolenAgendas)
            && left.Abilities.SequenceEqual(right.Abilities)
            && (left.RememberedCards ?? []).SequenceEqual(right.RememberedCards ?? []);
}

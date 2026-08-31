using System.Collections.ObjectModel;
using Netrunner.Cards;

namespace Netrunner.Match;

/// <summary>
/// Presentation metadata for a known card. Rules text is retained in the
/// normalized markup form here; clients use <see cref="CardMarkupFormatter"/>
/// at the final text boundary.
/// </summary>
public sealed record CardPresentationMetadata(
    string DefinitionId,
    string Title,
    string Type,
    string Faction,
    IReadOnlyList<string> Subtypes,
    int? Cost,
    int? Strength,
    int? MemoryCost,
    int? TrashCost,
    int? AgendaPoints,
    int? AdvancementRequirement,
    string RulesText)
{
    public static CardPresentationMetadata From(NormalizedCardDefinition card) => new(
        card.Code,
        card.Title,
        DisplayType(card.Type),
        card.Faction,
        new ReadOnlyCollection<string>(card.Subtypes.ToArray()),
        card.Cost,
        card.Strength,
        card.MemoryCost,
        card.TrashCost,
        card.AgendaPoints,
        card.AdvancementRequirement,
        card.Text);

    public static CardPresentationMetadata Unknown(string? definitionId = null) => new(
        definitionId ?? "",
        definitionId is null ? "Unknown card" : $"Unknown card ({definitionId})",
        "Card",
        "",
        Array.Empty<string>(),
        null,
        null,
        null,
        null,
        null,
        null,
        "Card details are hidden.");

    private static string DisplayType(string type) => type switch
    {
        "ice" => "ICE",
        "identity" => "Identity",
        "agenda" => "Agenda",
        "asset" => "Asset",
        "event" => "Event",
        "hardware" => "Hardware",
        "operation" => "Operation",
        "program" => "Program",
        "resource" => "Resource",
        "upgrade" => "Upgrade",
        _ => type,
    };
}

/// <summary>
/// Read-only metadata lookup injected at the Match composition boundary.
/// It has no access to authoritative state and therefore cannot be used to
/// infer a hidden card's identity.
/// </summary>
public interface ICardMetadataResolver
{
    IReadOnlyList<CardPresentationMetadata> Cards { get; }

    bool TryResolve(string definitionId, out CardPresentationMetadata metadata);
}

/// <summary>Resolver backed by one immutable card-data snapshot.</summary>
public sealed class CardCatalogMetadataResolver : ICardMetadataResolver
{
    private readonly ReadOnlyDictionary<string, CardPresentationMetadata> _byCode;

    public CardCatalogMetadataResolver(CardDataSet data)
        : this(data?.Cards ?? throw new ArgumentNullException(nameof(data)))
    {
    }

    public CardCatalogMetadataResolver(IEnumerable<NormalizedCardDefinition> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        CardPresentationMetadata[] values = cards
            .Select(CardPresentationMetadata.From)
            .ToArray();
        if (values.Length == 0)
        {
            throw new ArgumentException("Card metadata resolver requires at least one card.", nameof(cards));
        }

        var byCode = new Dictionary<string, CardPresentationMetadata>(StringComparer.Ordinal);
        foreach (CardPresentationMetadata value in values)
        {
            if (string.IsNullOrWhiteSpace(value.DefinitionId))
            {
                throw new ArgumentException("Card metadata definition IDs must be nonblank.", nameof(cards));
            }

            if (!byCode.TryAdd(value.DefinitionId, value))
            {
                throw new ArgumentException(
                    $"Card metadata contains duplicate definition ID '{value.DefinitionId}'.",
                    nameof(cards));
            }
        }

        _byCode = new ReadOnlyDictionary<string, CardPresentationMetadata>(byCode);
        Cards = new ReadOnlyCollection<CardPresentationMetadata>(
            values.OrderBy(value => value.Title, StringComparer.Ordinal)
                .ThenBy(value => value.DefinitionId, StringComparer.Ordinal)
                .ToArray());
    }

    public IReadOnlyList<CardPresentationMetadata> Cards { get; }

    public bool TryResolve(string definitionId, out CardPresentationMetadata metadata)
    {
        if (definitionId is not null && _byCode.TryGetValue(definitionId, out metadata!))
        {
            return true;
        }

        metadata = CardPresentationMetadata.Unknown(definitionId);
        return false;
    }
}

/// <summary>Empty resolver used by tooling when no content is provisioned.</summary>
public sealed class EmptyCardMetadataResolver : ICardMetadataResolver
{
    public static EmptyCardMetadataResolver Instance { get; } = new();

    private EmptyCardMetadataResolver()
    {
    }

    public IReadOnlyList<CardPresentationMetadata> Cards { get; } =
        Array.Empty<CardPresentationMetadata>();

    public bool TryResolve(string definitionId, out CardPresentationMetadata metadata)
    {
        metadata = CardPresentationMetadata.Unknown(definitionId);
        return false;
    }
}

/// <summary>Composition helper for the current generated Cards resource.</summary>
public static class CardMetadataResolvers
{
    private static readonly Lazy<ICardMetadataResolver> DefaultResolver = new(
        CreateDefault,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static ICardMetadataResolver Default => DefaultResolver.Value;

    public static ICardMetadataResolver CreateDefault()
    {
        return CardDataResource.TryLoadEmbedded(
            out CardDataSet? data,
            out _)
            && data is not null
            ? new CardCatalogMetadataResolver(data)
            : EmptyCardMetadataResolver.Instance;
    }
}

using System.Text.Json.Serialization;

namespace Netrunner.Core;

/// <summary>Kind of agenda movement recorded in one side's turn facts.</summary>
public enum AgendaTurnChangeKind
{
    Score,
    Steal,
}

/// <summary>
/// One typed agenda movement observed during a turn. The card remains an
/// instance identifier so facts cannot be coupled to card titles or printed
/// definitions.
/// </summary>
public sealed record AgendaTurnChange
{
    [JsonConstructor]
    public AgendaTurnChange(
        CardInstanceId cardId,
        PlayerSide beneficiarySide,
        int points,
        AgendaTurnChangeKind kind)
    {
        if (!Enum.IsDefined(beneficiarySide))
        {
            throw new ArgumentOutOfRangeException(nameof(beneficiarySide));
        }

        if (points <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(points), "Agenda points must be positive.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        CardId = cardId;
        BeneficiarySide = beneficiarySide;
        Points = points;
        Kind = kind;
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide BeneficiarySide { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Points { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public AgendaTurnChangeKind Kind { get; private set; }
}

/// <summary>One signed tag change applied to a player during a turn.</summary>
public sealed record TagTurnChange
{
    [JsonConstructor]
    public TagTurnChange(PlayerId affectedPlayerId, int delta)
    {
        if (delta == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(delta), "A tag change must be nonzero.");
        }

        AffectedPlayerId = affectedPlayerId;
        Delta = delta;
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId AffectedPlayerId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Delta { get; private set; }
}

/// <summary>
/// Counted use of a first-X effect. The source card is part of the key so
/// distinct physical copies do not share a usage ledger.
/// </summary>
public sealed record FirstXUseKey
{
    [JsonConstructor]
    public FirstXUseKey(CardInstanceId sourceCardId, string key, int count)
    {
        if (!StableIdentifier.TryValidate(sourceCardId.Value))
        {
            throw new ArgumentException("A first-X use key requires a stable source card ID.", nameof(sourceCardId));
        }

        if (!StableIdentifier.TryValidate(key))
        {
            throw new ArgumentException("A first-X use key requires a stable key.", nameof(key));
        }

        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "A first-X use count must be positive.");
        }

        SourceCardId = sourceCardId;
        Key = key;
        Count = count;
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId SourceCardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string Key { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Count { get; private set; }

}

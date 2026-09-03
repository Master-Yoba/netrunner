using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

/// <summary>
/// Selects the rules profile used when a deck request is validated.  The
/// default profile is the user-facing, complete deck-construction contract.
/// <see cref="DeveloperReduced"/> exists solely for deterministic early
/// simulation fixtures while the Gateway card pool is being implemented.
/// </summary>
public enum DeckValidationProfile
{
    UserFacing = 0,
    /// <summary>Alias for callers that describe the normal profile as standard.</summary>
    Standard = UserFacing,
    /// <summary>Alias for callers that describe the normal profile as public.</summary>
    Public = UserFacing,
    DeveloperReduced = 1,
}

/// <summary>
/// One code/quantity entry in a deck request.  Card codes are intentionally
/// opaque to Core; Cards owns their metadata and meaning.
/// </summary>
public sealed record DeckCardEntry
{
    [JsonConstructor]
    public DeckCardEntry(string code, int quantity)
    {
        ArgumentNullException.ThrowIfNull(code);
        Code = code;
        Quantity = quantity;
    }

    [JsonInclude]
    public string Code { get; }

    [JsonInclude]
    public int Quantity { get; }

    /// <summary>Readable alias for composition code that calls entries cards.</summary>
    [JsonIgnore]
    public string CardCode => Code;
}

/// <summary>
/// Versioned, metadata-free deck input.  It is a request contract only: it
/// does not create cards, shuffle a deck, or mutate a game state.
/// </summary>
public sealed record DeckRequest
{
    public const int CurrentSchemaVersion = 1;

    [JsonConstructor]
    public DeckRequest(
        int schemaVersion,
        PlayerSide side,
        string identityCode,
        IReadOnlyList<DeckCardEntry> cards,
        DeckValidationProfile validationProfile = DeckValidationProfile.UserFacing,
        string? presetId = null,
        string? cardDataVersion = null)
    {
        ArgumentNullException.ThrowIfNull(identityCode);
        ArgumentNullException.ThrowIfNull(cards);
        SchemaVersion = schemaVersion;
        Side = side;
        IdentityCode = identityCode;
        Cards = new ReadOnlyCollection<DeckCardEntry>(cards.ToArray());
        ValidationProfile = validationProfile;
        PresetId = presetId;
        CardDataVersion = cardDataVersion;
    }

    public int SchemaVersion { get; }

    [JsonInclude]
    public PlayerSide Side { get; }

    [JsonInclude]
    public string IdentityCode { get; }

    [JsonInclude]
    public IReadOnlyList<DeckCardEntry> Cards { get; }

    [JsonInclude]
    public DeckValidationProfile ValidationProfile { get; }

    /// <summary>
    /// Optional official preset identity.  It is metadata for composition and
    /// does not turn a custom deck into a preset.
    /// </summary>
    [JsonInclude]
    public string? PresetId { get; }

    /// <summary>
    /// Optional authenticated card-data version pin.  A user-facing request
    /// composed from a card snapshot should carry its manifest commit here.
    /// </summary>
    [JsonInclude]
    public string? CardDataVersion { get; }
}

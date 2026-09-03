using System.Collections.ObjectModel;
using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>One deterministic deck-validation diagnostic with an exact input path.</summary>
public sealed record DeckValidationDiagnostic(string Code, string Path, string Message)
{
    public override string ToString() => $"{Path}: {Message}";
}

/// <summary>Immutable result of validating one versioned deck request.</summary>
public sealed class DeckValidationResult
{
    internal DeckValidationResult(
        IReadOnlyList<DeckValidationDiagnostic> diagnostics,
        int cardCount,
        int influenceUsed,
        int agendaPoints,
        (int Minimum, int Maximum)? agendaPointRange,
        bool exactTutorialPreset,
        string? cardDataVersion)
    {
        Diagnostics = new ReadOnlyCollection<DeckValidationDiagnostic>(diagnostics.ToArray());
        CardCount = cardCount;
        InfluenceUsed = influenceUsed;
        AgendaPoints = agendaPoints;
        AgendaPointRange = agendaPointRange;
        ExactTutorialPreset = exactTutorialPreset;
        CardDataVersion = cardDataVersion;
    }

    public IReadOnlyList<DeckValidationDiagnostic> Diagnostics { get; }

    public bool IsValid => Diagnostics.Count == 0;

    public int CardCount { get; }

    public int InfluenceUsed { get; }

    public int AgendaPoints { get; }

    public (int Minimum, int Maximum)? AgendaPointRange { get; }

    public bool ExactTutorialPreset { get; }

    public string? CardDataVersion { get; }

    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new DeckValidationException(Diagnostics);
        }
    }
}

/// <summary>Raised when a deck request fails one or more legality checks.</summary>
public sealed class DeckValidationException : InvalidOperationException
{
    public DeckValidationException(IReadOnlyList<DeckValidationDiagnostic> diagnostics)
        : base("The deck request is invalid:\n" + string.Join("\n", diagnostics.Select(static value => value.ToString())))
    {
        Diagnostics = new ReadOnlyCollection<DeckValidationDiagnostic>(diagnostics.ToArray());
    }

    public IReadOnlyList<DeckValidationDiagnostic> Diagnostics { get; }
}

/// <summary>
/// Metadata-driven System Gateway deck legality. This layer owns card meaning;
/// Core supplies only the versioned, opaque deck request contract.
/// </summary>
public static class DeckValidator
{
    public const int CurrentSchemaVersion = DeckRequest.CurrentSchemaVersion;

    /// <summary>
    /// Validates against an immutable normalized metadata catalog. Tutorial
    /// identities are checked against the embedded official preset data when
    /// encountered; callers may supply a catalog to make that dependency
    /// explicit in tests or content-enabled composition.
    /// </summary>
    public static DeckValidationResult Validate(
        DeckRequest request,
        CardMetadataCatalog metadata,
        OfficialPresetCatalog? officialPresets = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);

        var diagnostics = new List<DeckValidationDiagnostic>();
        int cardCount = 0;
        int influenceUsed = 0;
        int agendaPoints = 0;
        (int Minimum, int Maximum)? agendaPointRange = null;
        bool exactTutorialPreset = false;

        ValidateRequestHeader(request, diagnostics);

        NormalizedCardDefinition? identity = null;
        bool validSide = Enum.IsDefined(request.Side);
        if (validSide)
        {
            if (string.IsNullOrWhiteSpace(request.IdentityCode))
            {
                Add(diagnostics, "identity-required", "identityCode", "an identity code is required.");
            }
            else if (!metadata.TryGetValue(request.IdentityCode, out identity))
            {
                Add(
                    diagnostics,
                    "identity-unknown",
                    "identityCode",
                    $"identity code '{request.IdentityCode}' is not present in the metadata catalog.");
            }
            else
            {
                ValidateIdentity(identity, request.Side, diagnostics);
            }
        }

        var entriesByCode = new Dictionary<string, (int Quantity, int FirstIndex)>(StringComparer.Ordinal);
        var definitionsByCode = new Dictionary<string, NormalizedCardDefinition>(StringComparer.Ordinal);
        var entriesByName = new Dictionary<string, (int Quantity, int FirstIndex, string FirstCode)>(StringComparer.Ordinal);
        for (int index = 0; index < request.Cards.Count; index++)
        {
            DeckCardEntry? entry = request.Cards[index];
            string path = $"cards[{index}]";
            if (entry is null)
            {
                Add(diagnostics, "card-entry-null", path, "card entries must not be null.");
                continue;
            }

            string codePath = $"{path}.code";
            string quantityPath = $"{path}.quantity";
            if (string.IsNullOrWhiteSpace(entry.Code))
            {
                Add(diagnostics, "card-code-required", codePath, "a card code is required.");
                continue;
            }

            if (!metadata.TryGetValue(entry.Code, out NormalizedCardDefinition? definition))
            {
                Add(
                    diagnostics,
                    "card-unknown",
                    codePath,
                    $"card code '{entry.Code}' is not present in the metadata catalog.");
                continue;
            }

            if (!string.Equals(definition.Side, SideCode(request.Side), StringComparison.Ordinal))
            {
                Add(
                    diagnostics,
                    "card-side",
                    codePath,
                    $"card '{entry.Code}' belongs to side '{definition.Side}', expected '{SideCode(request.Side)}'.");
            }

            if (string.Equals(definition.Type, "identity", StringComparison.Ordinal))
            {
                Add(
                    diagnostics,
                    "identity-in-deck",
                    codePath,
                    "identity cards cannot appear in a deck.");
            }

            if (entry.Quantity <= 0)
            {
                Add(
                    diagnostics,
                    "quantity-positive",
                    quantityPath,
                    $"quantity must be positive, found {entry.Quantity}.");
            }

            if (entriesByCode.TryGetValue(entry.Code, out (int Quantity, int FirstIndex) existing))
            {
                Add(
                    diagnostics,
                    "duplicate-entry",
                    codePath,
                    $"card code '{entry.Code}' appears in multiple deck entries (first at cards[{existing.FirstIndex}].code).");
                entriesByCode[entry.Code] = (SafeAdd(existing.Quantity, entry.Quantity), existing.FirstIndex);
            }
            else
            {
                entriesByCode.Add(entry.Code, (entry.Quantity, index));
                definitionsByCode.Add(entry.Code, definition);
            }

            if (entriesByName.TryGetValue(definition.Title, out (int Quantity, int FirstIndex, string FirstCode) nameExisting))
            {
                entriesByName[definition.Title] =
                    (SafeAdd(nameExisting.Quantity, entry.Quantity), nameExisting.FirstIndex, nameExisting.FirstCode);
            }
            else
            {
                entriesByName.Add(definition.Title, (entry.Quantity, index, entry.Code));
            }

            cardCount = SafeAdd(cardCount, entry.Quantity);
            if (string.Equals(definition.Type, "agenda", StringComparison.Ordinal))
            {
                agendaPoints = SafeAdd(agendaPoints, SafeMultiply(entry.Quantity, definition.AgendaPoints ?? 0));
            }
        }

        foreach ((string name, (int Quantity, int FirstIndex, string FirstCode) aggregate) in entriesByName)
        {
            if (!definitionsByCode.TryGetValue(aggregate.FirstCode, out NormalizedCardDefinition? definition))
            {
                continue;
            }

            if (aggregate.Quantity > 3)
            {
                Add(
                    diagnostics,
                    "copy-limit",
                    $"cards[{aggregate.FirstIndex}].quantity",
                    $"card name '{name}' has {aggregate.Quantity} copies; no more than 3 copies by name are allowed.");
            }

        }

        // Printed limits apply to each card code, whereas the three-copy rule
        // above applies to the printed card name. Keep these checks separate
        // so a future reprint sharing a name cannot evade the name limit.
        foreach ((string code, (int Quantity, int FirstIndex) aggregate) in entriesByCode)
        {
            if (!definitionsByCode.TryGetValue(code, out NormalizedCardDefinition? definition))
            {
                continue;
            }

            if (aggregate.Quantity > definition.DeckLimit)
            {
                Add(
                    diagnostics,
                    "deck-limit",
                    $"cards[{aggregate.FirstIndex}].quantity",
                    $"card '{code}' has {aggregate.Quantity} copies; the printed deck limit is {definition.DeckLimit}.");
            }

            if (identity is not null
                && string.Equals(definition.Side, SideCode(request.Side), StringComparison.Ordinal)
                && !string.Equals(definition.Faction, identity.Faction, StringComparison.Ordinal))
            {
                if (definition.InfluenceCost is null
                    && !string.Equals(definition.Type, "agenda", StringComparison.Ordinal))
                {
                    Add(
                        diagnostics,
                        "influence-cost-missing",
                        $"cards[{aggregate.FirstIndex}].code",
                        $"out-of-faction card '{code}' has no influence cost.");
                }
                else
                {
                    influenceUsed = SafeAdd(
                        influenceUsed,
                        SafeMultiply(aggregate.Quantity, definition.InfluenceCost ?? 0));
                }
            }
        }

        OfficialPresetCatalog? presetCatalog = null;
        if (identity is not null
            && IsTutorialIdentity(identity))
        {
            presetCatalog = officialPresets ?? OfficialPresetCatalog.Embedded();
            exactTutorialPreset = IsExactTutorialPreset(request, presetCatalog, diagnostics);
        }
        else if (!string.IsNullOrWhiteSpace(request.PresetId))
        {
            presetCatalog = officialPresets ?? OfficialPresetCatalog.Embedded();
            ValidatePresetReference(request, presetCatalog, diagnostics);
        }

        if (identity is not null)
        {
            bool reduced = request.ValidationProfile == DeckValidationProfile.DeveloperReduced;
            if (!reduced && !exactTutorialPreset)
            {
                ValidateMinimumDeckSize(identity, cardCount, diagnostics);
                if (request.Side == PlayerSide.Corp && cardCount >= 40)
                {
                    agendaPointRange = CorpAgendaPointRange(cardCount);
                    if (agendaPoints < agendaPointRange.Value.Minimum
                        || agendaPoints > agendaPointRange.Value.Maximum)
                    {
                        Add(
                            diagnostics,
                            "agenda-range",
                            "cards",
                            $"Corp agenda points total {agendaPoints}; a {cardCount}-card deck requires "
                            + $"{agendaPointRange.Value.Minimum}-{agendaPointRange.Value.Maximum} agenda points.");
                    }
                }
            }

            if (!reduced
                && !exactTutorialPreset
                && request.Side == PlayerSide.Corp
                && cardCount < 40)
            {
                // The normal minimum-size diagnostic is the authoritative
                // failure for undersized Corp decks; no agenda range exists
                // below the standard 40-card threshold.
                agendaPointRange = null;
            }

            if (identity.InfluenceLimit is { } influenceLimit
                && influenceUsed > influenceLimit)
            {
                Add(
                    diagnostics,
                    "influence-limit",
                    "cards",
                    $"out-of-faction influence total {influenceUsed} exceeds identity limit {influenceLimit}.");
            }
        }

        ValidateCardDataVersion(request, diagnostics, expectedVersion: null);
        return new DeckValidationResult(
            diagnostics,
            cardCount,
            influenceUsed,
            agendaPoints,
            agendaPointRange,
            exactTutorialPreset,
            request.CardDataVersion);
    }

    /// <summary>Validates a request against the generated authenticated snapshot.</summary>
    public static DeckValidationResult Validate(
        DeckRequest request,
        CardDataSet data,
        OfficialPresetCatalog? officialPresets = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        CardMetadataCatalog metadata = new(data.Cards);
        DeckValidationResult result = Validate(request, metadata, officialPresets);
        var diagnostics = result.Diagnostics.ToList();
        ValidateCardDataVersion(request, diagnostics, data.Manifest.CommitSha);
        return new DeckValidationResult(
            diagnostics,
            result.CardCount,
            result.InfluenceUsed,
            result.AgendaPoints,
            result.AgendaPointRange,
            result.ExactTutorialPreset,
            request.CardDataVersion ?? data.Manifest.CommitSha);
    }

    public static DeckValidationResult ValidateEmbedded(
        DeckRequest request,
        OfficialPresetCatalog? officialPresets = null) =>
        Validate(request, CardDataResource.LoadEmbedded(), officialPresets);

    /// <summary>
    /// Validates metadata legality and requires every card in the request to
    /// have a compiled behavior in the supplied immutable catalog. This
    /// explicit overload is the seam used by setup/simulation composition;
    /// <see cref="DeckValidationProfile.DeveloperReduced"/> never bypasses
    /// this implemented-behavior check.
    /// </summary>
    public static DeckValidationResult ValidateWithBehaviorCatalog(
        DeckRequest request,
        CardMetadataCatalog metadata,
        IBehaviorCatalog behaviorCatalog,
        OfficialPresetCatalog? officialPresets = null)
    {
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        DeckValidationResult result = Validate(request, metadata, officialPresets);
        var diagnostics = result.Diagnostics.ToList();
        var reported = new HashSet<string>(StringComparer.Ordinal);
        var implementedDefinitionIds = new HashSet<CardDefinitionId>(behaviorCatalog.AllDefinitionIds);
        if (!string.IsNullOrWhiteSpace(request.IdentityCode)
            && metadata.TryGetValue(request.IdentityCode, out NormalizedCardDefinition? identity)
            && string.Equals(identity.Type, "identity", StringComparison.Ordinal)
            && !implementedDefinitionIds.Contains(new CardDefinitionId(identity.Code)))
        {
            diagnostics.Add(new DeckValidationDiagnostic(
                "implemented-behavior",
                "identityCode",
                $"identity '{identity.Code}' has no compiled behavior in the supplied catalog."));
        }

        for (int index = 0; index < request.Cards.Count; index++)
        {
            DeckCardEntry? entry = request.Cards[index];
            if (entry is null
                || string.IsNullOrWhiteSpace(entry.Code)
                || !metadata.TryGetValue(entry.Code, out _)
                || !reported.Add(entry.Code))
            {
                continue;
            }

            if (!implementedDefinitionIds.Contains(new CardDefinitionId(entry.Code)))
            {
                diagnostics.Add(new DeckValidationDiagnostic(
                    "implemented-behavior",
                    $"cards[{index}].code",
                    $"card '{entry.Code}' has no compiled behavior in the supplied catalog."));
            }
        }

        return new DeckValidationResult(
            diagnostics,
            result.CardCount,
            result.InfluenceUsed,
            result.AgendaPoints,
            result.AgendaPointRange,
            result.ExactTutorialPreset,
            result.CardDataVersion);
    }

    public static DeckValidationResult ValidateWithBehaviorCatalog(
        DeckRequest request,
        CardDataSet data,
        IBehaviorCatalog behaviorCatalog,
        OfficialPresetCatalog? officialPresets = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        CardMetadataCatalog metadata = new(data.Cards);
        DeckValidationResult result = ValidateWithBehaviorCatalog(request, metadata, behaviorCatalog, officialPresets);
        var diagnostics = result.Diagnostics.ToList();
        ValidateCardDataVersion(request, diagnostics, data.Manifest.CommitSha);
        return new DeckValidationResult(
            diagnostics,
            result.CardCount,
            result.InfluenceUsed,
            result.AgendaPoints,
            result.AgendaPointRange,
            result.ExactTutorialPreset,
            request.CardDataVersion ?? data.Manifest.CommitSha);
    }

    public static void ValidateOrThrow(
        DeckRequest request,
        CardMetadataCatalog metadata,
        OfficialPresetCatalog? officialPresets = null) =>
        Validate(request, metadata, officialPresets).ThrowIfInvalid();

    public static void ValidateOrThrow(
        DeckRequest request,
        CardDataSet data,
        OfficialPresetCatalog? officialPresets = null) =>
        Validate(request, data, officialPresets).ThrowIfInvalid();

    private static void ValidateRequestHeader(
        DeckRequest request,
        List<DeckValidationDiagnostic> diagnostics)
    {
        if (request.SchemaVersion != CurrentSchemaVersion)
        {
            Add(
                diagnostics,
                "schema-version",
                "schemaVersion",
                $"expected {CurrentSchemaVersion}, found {request.SchemaVersion}.");
        }

        if (!Enum.IsDefined(request.Side))
        {
            Add(
                diagnostics,
                "side",
                "side",
                $"'{request.Side}' is not a valid player side.");
        }

        if (!Enum.IsDefined(request.ValidationProfile))
        {
            Add(
                diagnostics,
                "validation-profile",
                "validationProfile",
                $"'{request.ValidationProfile}' is not a supported validation profile.");
        }

        if (request.PresetId is not null && string.IsNullOrWhiteSpace(request.PresetId))
        {
            Add(diagnostics, "preset-id", "presetId", "must be nonblank when supplied.");
        }
    }

    private static void ValidateIdentity(
        NormalizedCardDefinition identity,
        PlayerSide requestedSide,
        List<DeckValidationDiagnostic> diagnostics)
    {
        string path = "identityCode";
        if (!string.Equals(identity.Side, SideCode(requestedSide), StringComparison.Ordinal))
        {
            Add(
                diagnostics,
                "identity-side",
                path,
                $"identity '{identity.Code}' belongs to side '{identity.Side}', expected '{SideCode(requestedSide)}'.");
        }

        if (!string.Equals(identity.Type, "identity", StringComparison.Ordinal))
        {
            Add(
                diagnostics,
                "identity-type",
                path,
                $"card '{identity.Code}' has type '{identity.Type}', expected 'identity'.");
        }

        if (identity.MinimumDeckSize is null)
        {
            Add(diagnostics, "identity-minimum-missing", path, "identity minimum deck size is missing from metadata.");
        }

        if (identity.InfluenceLimit is null && !IsTutorialIdentity(identity))
        {
            Add(diagnostics, "identity-influence-missing", path, "identity influence limit is missing from metadata.");
        }
    }

    private static void ValidateMinimumDeckSize(
        NormalizedCardDefinition identity,
        int cardCount,
        List<DeckValidationDiagnostic> diagnostics)
    {
        if (identity.MinimumDeckSize is { } minimum && cardCount < minimum)
        {
            Add(
                diagnostics,
                "minimum-deck-size",
                "cards",
                $"deck contains {cardCount} cards; identity requires at least {minimum}.");
        }
    }

    private static bool IsTutorialIdentity(NormalizedCardDefinition identity) =>
        string.Equals(identity.Code, "30076", StringComparison.Ordinal)
        || string.Equals(identity.Code, "30077", StringComparison.Ordinal);

    private static bool IsExactTutorialPreset(
        DeckRequest request,
        OfficialPresetCatalog presets,
        List<DeckValidationDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(request.PresetId))
        {
            if (!presets.TryGet(request.PresetId, out OfficialPreset selected))
            {
                Add(
                    diagnostics,
                    "preset-unknown",
                    "presetId",
                    $"official preset '{request.PresetId}' was not found.");
                return false;
            }

            if (selected.Side != request.Side || !string.Equals(selected.IdentityCode, request.IdentityCode, StringComparison.Ordinal))
            {
                Add(
                    diagnostics,
                    "preset-identity",
                    "presetId",
                    $"preset '{request.PresetId}' is for {selected.Side}/{selected.IdentityCode}, "
                    + $"not {request.Side}/{request.IdentityCode}.");
                return false;
            }

            if (!DeckCardsEqual(request.Cards, selected.Cards))
            {
                Add(
                    diagnostics,
                    "tutorial-preset",
                    "cards",
                    $"tutorial identity '{request.IdentityCode}' is accepted only with the exact official preset '{request.PresetId}'.");
                return false;
            }

            return true;
        }

        OfficialPreset[] matches = presets.Presets
            .Where(preset => preset.Side == request.Side
                && string.Equals(preset.IdentityCode, request.IdentityCode, StringComparison.Ordinal)
                && DeckCardsEqual(request.Cards, preset.Cards))
            .ToArray();
        if (matches.Length > 0)
        {
            return true;
        }

        Add(
            diagnostics,
            "tutorial-preset",
            "cards",
            $"tutorial identity '{request.IdentityCode}' is accepted only with the exact official starter or expanded preset.");
        return false;
    }

    private static void ValidatePresetReference(
        DeckRequest request,
        OfficialPresetCatalog presets,
        List<DeckValidationDiagnostic> diagnostics)
    {
        if (!presets.TryGet(request.PresetId!, out OfficialPreset selected))
        {
            Add(
                diagnostics,
                "preset-unknown",
                "presetId",
                $"official preset '{request.PresetId}' was not found.");
            return;
        }

        if (selected.Side != request.Side || !string.Equals(selected.IdentityCode, request.IdentityCode, StringComparison.Ordinal))
        {
            Add(
                diagnostics,
                "preset-identity",
                "presetId",
                $"preset '{request.PresetId}' is for {selected.Side}/{selected.IdentityCode}, "
                + $"not {request.Side}/{request.IdentityCode}.");
        }

        if (!DeckCardsEqual(request.Cards, selected.Cards))
        {
            Add(
                diagnostics,
                "preset-cards",
                "cards",
                $"deck does not exactly match official preset '{request.PresetId}'.");
        }
    }

    private static bool DeckCardsEqual(
        IReadOnlyList<DeckCardEntry> actual,
        IReadOnlyList<DeckCardEntry> expected)
    {
        var actualByCode = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (DeckCardEntry? card in actual)
        {
            if (card is null || string.IsNullOrWhiteSpace(card.Code))
            {
                return false;
            }

            if (actualByCode.ContainsKey(card.Code))
            {
                return false;
            }

            actualByCode.Add(card.Code, card.Quantity);
        }

        if (actualByCode.Count != expected.Count)
        {
            return false;
        }

        foreach (DeckCardEntry card in expected)
        {
            if (!actualByCode.TryGetValue(card.Code, out int quantity) || quantity != card.Quantity)
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateCardDataVersion(
        DeckRequest request,
        List<DeckValidationDiagnostic> diagnostics,
        string? expectedVersion)
    {
        if (request.CardDataVersion is not null && string.IsNullOrWhiteSpace(request.CardDataVersion))
        {
            Add(diagnostics, "card-data-version", "cardDataVersion", "must be nonblank when supplied.");
        }

        if (expectedVersion is not null
            && request.CardDataVersion is not null
            && !string.Equals(request.CardDataVersion, expectedVersion, StringComparison.Ordinal))
        {
            Add(
                diagnostics,
                "card-data-version",
                "cardDataVersion",
                $"'{request.CardDataVersion}' does not match authenticated metadata version '{expectedVersion}'.");
        }
    }

    private static (int Minimum, int Maximum) CorpAgendaPointRange(int cardCount)
    {
        if (cardCount is >= 40 and <= 44)
        {
            return (18, 19);
        }

        if (cardCount is >= 45 and <= 49)
        {
            return (20, 21);
        }

        if (cardCount is >= 50 and <= 54)
        {
            return (22, 23);
        }

        int additionalSets = Math.Max(0, (cardCount - 50) / 5);
        return (22 + additionalSets * 2, 23 + additionalSets * 2);
    }

    private static string SideCode(PlayerSide side) => side == PlayerSide.Corp ? "corp" : "runner";

    private static void Add(
        List<DeckValidationDiagnostic> diagnostics,
        string code,
        string path,
        string message) =>
        diagnostics.Add(new DeckValidationDiagnostic(code, path, message));

    private static int SafeAdd(int left, int right)
    {
        try
        {
            return checked(left + right);
        }
        catch (OverflowException)
        {
            return int.MaxValue;
        }
    }

    private static int SafeMultiply(int left, int right)
    {
        try
        {
            return checked(left * right);
        }
        catch (OverflowException)
        {
            return int.MaxValue;
        }
    }
}

/// <summary>Compatibility name for code that spells the owner explicitly.</summary>
public static class CardDeckValidator
{
    public static DeckValidationResult Validate(
        DeckRequest request,
        CardMetadataCatalog metadata,
        OfficialPresetCatalog? officialPresets = null) =>
        DeckValidator.Validate(request, metadata, officialPresets);

    public static DeckValidationResult Validate(
        DeckRequest request,
        CardDataSet data,
        OfficialPresetCatalog? officialPresets = null) =>
        DeckValidator.Validate(request, data, officialPresets);

    public static DeckValidationResult ValidateWithBehaviorCatalog(
        DeckRequest request,
        CardMetadataCatalog metadata,
        IBehaviorCatalog behaviorCatalog,
        OfficialPresetCatalog? officialPresets = null) =>
        DeckValidator.ValidateWithBehaviorCatalog(request, metadata, behaviorCatalog, officialPresets);

    public static DeckValidationResult ValidateWithBehaviorCatalog(
        DeckRequest request,
        CardDataSet data,
        IBehaviorCatalog behaviorCatalog,
        OfficialPresetCatalog? officialPresets = null) =>
        DeckValidator.ValidateWithBehaviorCatalog(request, data, behaviorCatalog, officialPresets);
}

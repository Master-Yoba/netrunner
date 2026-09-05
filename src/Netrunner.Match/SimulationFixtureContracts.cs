using System.Collections.ObjectModel;
using Netrunner.Cards;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>One deterministic diagnostic for a SIM-401 fixture document.</summary>
public sealed record SimulationFixtureDiagnostic(string Code, string Path, string Message)
{
    public override string ToString() => $"{Path}: {Message}";
}

/// <summary>Raised when a simulation fixture fails its contract or legality gate.</summary>
public sealed class SimulationFixtureValidationException : InvalidOperationException
{
    public SimulationFixtureValidationException(IReadOnlyList<SimulationFixtureDiagnostic> diagnostics)
        : base("The simulation fixture is invalid:\n" + string.Join("\n", diagnostics.Select(static value => value.ToString())))
    {
        Diagnostics = new ReadOnlyCollection<SimulationFixtureDiagnostic>(diagnostics.ToArray());
    }

    public IReadOnlyList<SimulationFixtureDiagnostic> Diagnostics { get; }
}

/// <summary>Immutable result from structural or execution-bound fixture validation.</summary>
public sealed class SimulationFixtureValidationResult
{
    internal SimulationFixtureValidationResult(IEnumerable<SimulationFixtureDiagnostic> diagnostics)
    {
        Diagnostics = new ReadOnlyCollection<SimulationFixtureDiagnostic>(diagnostics.ToArray());
    }

    public IReadOnlyList<SimulationFixtureDiagnostic> Diagnostics { get; }

    public bool IsValid => Diagnostics.Count == 0;

    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new SimulationFixtureValidationException(Diagnostics);
        }
    }
}

/// <summary>
/// Optional deterministic policy ordering. Action keys are opaque stable IDs;
/// this contract does not execute or interpret a policy.
/// </summary>
public sealed record SimulationPolicyOverrides
{
    public SimulationPolicyOverrides(
        IEnumerable<string>? corpActionOrder = null,
        IEnumerable<string>? runnerActionOrder = null)
    {
        CorpActionOrder = Freeze(corpActionOrder);
        RunnerActionOrder = Freeze(runnerActionOrder);
    }

    public IReadOnlyList<string> CorpActionOrder { get; }

    public IReadOnlyList<string> RunnerActionOrder { get; }

    private static ReadOnlyCollection<string> Freeze(IEnumerable<string>? values)
    {
        string[] copy = (values ?? []).ToArray();
        if (copy.Any(static value => value is null))
        {
            throw new ArgumentException("Simulation policy action keys must not contain null entries.", nameof(values));
        }

        return new ReadOnlyCollection<string>(copy);
    }
}

/// <summary>Expected terminal state declared by a deterministic simulation fixture.</summary>
public sealed record SimulationOutcomeExpectation
{
    public SimulationOutcomeExpectation(string kind, PlayerSide? winner = null, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(kind);
        Kind = kind;
        Winner = winner;
        Reason = reason;
    }

    /// <summary>Currently <c>ongoing</c> or <c>won</c>.</summary>
    public string Kind { get; }

    public PlayerSide? Winner { get; }

    /// <summary>One of agendaPoints, deckOut, or flatline for a won outcome.</summary>
    public string? Reason { get; }
}

/// <summary>
/// Versioned developer-curated SIM-401 input. This is a composition document,
/// not a setup transition: it contains no GameState, card instances, shuffle
/// output, or prebuilt pending decision. MATCH-401A/AGT-401 will consume it
/// through the normal setup and action path once those dependencies land.
/// </summary>
public sealed class SimulationFixtureDocument
{
    public const int CurrentSchemaVersion = 1;

    public const int MaximumActionCap = 10_000;

    private static readonly Lazy<IBehaviorCatalog> DefaultBehaviorCatalog = new(
        GatewayBehaviorCatalog.Create,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public SimulationFixtureDocument(
        int schemaVersion,
        string fixtureId,
        string rulesVersion,
        string cardDataVersion,
        string behaviorRegistryVersion,
        string behaviorCatalogFingerprint,
        ulong seed,
        int actionCap,
        DeckRequest corpDeck,
        DeckRequest runnerDeck,
        SimulationOutcomeExpectation expectedOutcome,
        SimulationPolicyOverrides? policy = null,
        IReadOnlyList<string>? citations = null,
        string? crossCheckNote = null)
    {
        ArgumentNullException.ThrowIfNull(fixtureId);
        ArgumentNullException.ThrowIfNull(rulesVersion);
        ArgumentNullException.ThrowIfNull(cardDataVersion);
        ArgumentNullException.ThrowIfNull(behaviorRegistryVersion);
        ArgumentNullException.ThrowIfNull(behaviorCatalogFingerprint);
        ArgumentNullException.ThrowIfNull(corpDeck);
        ArgumentNullException.ThrowIfNull(runnerDeck);
        ArgumentNullException.ThrowIfNull(expectedOutcome);
        SchemaVersion = schemaVersion;
        FixtureId = fixtureId;
        RulesVersion = rulesVersion;
        CardDataVersion = cardDataVersion;
        BehaviorRegistryVersion = behaviorRegistryVersion;
        BehaviorCatalogFingerprint = behaviorCatalogFingerprint;
        Seed = seed;
        ActionCap = actionCap;
        CorpDeck = corpDeck;
        RunnerDeck = runnerDeck;
        ExpectedOutcome = expectedOutcome;
        Policy = policy;
        Citations = new ReadOnlyCollection<string>((citations ?? []).ToArray());
        CrossCheckNote = crossCheckNote;
    }

    public int SchemaVersion { get; }

    public string FixtureId { get; }

    public string RulesVersion { get; }

    /// <summary>Authenticated metadata commit/hash shared by both decks.</summary>
    public string CardDataVersion { get; }

    public string BehaviorRegistryVersion { get; }

    public string BehaviorCatalogFingerprint { get; }

    public ulong Seed { get; }

    public int ActionCap { get; }

    public DeckRequest CorpDeck { get; }

    public DeckRequest RunnerDeck { get; }

    public SimulationPolicyOverrides? Policy { get; }

    public SimulationOutcomeExpectation ExpectedOutcome { get; }

    public IReadOnlyList<string> Citations { get; }

    public string? CrossCheckNote { get; }

    /// <summary>
    /// Structural SIM-401 validation. This checks pins, sides, reduced-profile
    /// ownership, exact official preset references, and deterministic fields.
    /// It does not claim that card codes have executable behavior until an
    /// authenticated metadata/behavior catalog is supplied to the execution
    /// overload below.
    /// </summary>
    public IReadOnlyList<SimulationFixtureDiagnostic> CollectProblems(
        OfficialPresetCatalog? officialPresets = null,
        IBehaviorCatalog? behaviorCatalog = null)
    {
        OfficialPresetCatalog presets = officialPresets ?? OfficialPresetCatalog.Embedded();
        IBehaviorCatalog behaviors = behaviorCatalog ?? DefaultBehaviorCatalog.Value;
        var diagnostics = new List<SimulationFixtureDiagnostic>();
        ValidateHeader(diagnostics, behaviors);
        ValidateDeckContract(CorpDeck, PlayerSide.Corp, "corpDeck", presets, diagnostics);
        ValidateDeckContract(RunnerDeck, PlayerSide.Runner, "runnerDeck", presets, diagnostics);
        if (CorpDeck.PresetId is not null
            && RunnerDeck.PresetId is not null
            && string.Equals(CorpDeck.PresetId, RunnerDeck.PresetId, StringComparison.Ordinal))
        {
            Add(
                diagnostics,
                "preset-duplicate",
                "runnerDeck.presetId",
                $"official preset '{RunnerDeck.PresetId}' is already selected by corpDeck.");
        }

        ValidatePolicy(diagnostics);
        ValidateOutcome(diagnostics);
        ValidateProvenance(diagnostics);
        return new ReadOnlyCollection<SimulationFixtureDiagnostic>(diagnostics);
    }

    /// <summary>
    /// Full simulation-input validation. The supplied metadata and immutable
    /// behavior catalog are mandatory so reduced decks cannot bypass known-code
    /// or implemented-behavior checks. This still only validates inputs; it
    /// never starts setup or mutates a game state.
    /// </summary>
    public IReadOnlyList<SimulationFixtureDiagnostic> CollectProblems(
        CardDataSet data,
        IBehaviorCatalog behaviorCatalog,
        OfficialPresetCatalog? officialPresets = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        OfficialPresetCatalog presets = officialPresets ?? OfficialPresetCatalog.Embedded();
        var diagnostics = CollectProblems(presets, behaviorCatalog).ToList();
        if (!string.Equals(CardDataVersion, data.Manifest.CommitSha, StringComparison.Ordinal))
        {
            Add(
                diagnostics,
                "metadata-pin",
                "cardDataVersion",
                $"'{CardDataVersion}' does not match authenticated metadata version '{data.Manifest.CommitSha}'.");
        }

        AddDeckLegalityProblems(CorpDeck, "corpDeck", data, behaviorCatalog, presets, diagnostics);
        AddDeckLegalityProblems(RunnerDeck, "runnerDeck", data, behaviorCatalog, presets, diagnostics);
        return new ReadOnlyCollection<SimulationFixtureDiagnostic>(diagnostics);
    }

    public SimulationFixtureValidationResult ValidateForSimulation(
        CardDataSet data,
        IBehaviorCatalog behaviorCatalog,
        OfficialPresetCatalog? officialPresets = null)
    {
        IReadOnlyList<SimulationFixtureDiagnostic> diagnostics =
            CollectProblems(data, behaviorCatalog, officialPresets);
        var result = new SimulationFixtureValidationResult(diagnostics);
        result.ThrowIfInvalid();
        return result;
    }

    public void ValidateOrThrow(
        OfficialPresetCatalog? officialPresets = null,
        IBehaviorCatalog? behaviorCatalog = null)
    {
        SimulationFixtureValidationResult result =
            new(CollectProblems(officialPresets, behaviorCatalog));
        result.ThrowIfInvalid();
    }

    private void ValidateHeader(List<SimulationFixtureDiagnostic> diagnostics, IBehaviorCatalog behaviorCatalog)
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            Add(diagnostics, "schema-version", "schemaVersion", $"expected {CurrentSchemaVersion}, found {SchemaVersion}.");
        }

        if (!IsStableIdentifier(FixtureId))
        {
            Add(diagnostics, "fixture-id", "fixtureId", "fixtureId must be a nonblank stable identifier.");
        }

        if (!string.Equals(RulesVersion, EngineCompatibility.RulesVersion, StringComparison.Ordinal))
        {
            Add(
                diagnostics,
                "rules-version",
                "rulesVersion",
                $"'{RulesVersion}' is not supported; expected '{EngineCompatibility.RulesVersion}'.");
        }

        if (!IsLowerHex(CardDataVersion, 40))
        {
            Add(diagnostics, "metadata-pin", "cardDataVersion", "cardDataVersion must be a lowercase 40-hex metadata commit pin.");
        }

        if (!IsStableIdentifier(BehaviorRegistryVersion))
        {
            Add(diagnostics, "behavior-pin", "behaviorRegistryVersion", "behaviorRegistryVersion must be a nonblank stable identifier.");
        }
        else if (!string.Equals(BehaviorRegistryVersion, behaviorCatalog.Version, StringComparison.Ordinal))
        {
            Add(
                diagnostics,
                "behavior-pin",
                "behaviorRegistryVersion",
                $"'{BehaviorRegistryVersion}' does not match supplied catalog version '{behaviorCatalog.Version}'.");
        }

        if (!IsLowerHex(BehaviorCatalogFingerprint, 64))
        {
            Add(
                diagnostics,
                "behavior-fingerprint",
                "behaviorCatalogFingerprint",
                "behaviorCatalogFingerprint must be a lowercase SHA-256 hex value.");
        }
        else if (!string.Equals(BehaviorCatalogFingerprint, behaviorCatalog.Fingerprint, StringComparison.Ordinal))
        {
            Add(
                diagnostics,
                "behavior-fingerprint",
                "behaviorCatalogFingerprint",
                $"'{BehaviorCatalogFingerprint}' does not match supplied catalog fingerprint '{behaviorCatalog.Fingerprint}'.");
        }

        if (ActionCap <= 0 || ActionCap > MaximumActionCap)
        {
            Add(
                diagnostics,
                "action-cap",
                "actionCap",
                $"actionCap must be between 1 and {MaximumActionCap}, found {ActionCap}.");
        }

        if (CorpDeck.CardDataVersion is not null
            && !string.Equals(CorpDeck.CardDataVersion, CardDataVersion, StringComparison.Ordinal))
        {
            Add(diagnostics, "metadata-pin", "corpDeck.cardDataVersion", "deck metadata pin must match cardDataVersion.");
        }

        if (RunnerDeck.CardDataVersion is not null
            && !string.Equals(RunnerDeck.CardDataVersion, CardDataVersion, StringComparison.Ordinal))
        {
            Add(diagnostics, "metadata-pin", "runnerDeck.cardDataVersion", "deck metadata pin must match cardDataVersion.");
        }
    }

    private static void ValidateDeckContract(
        DeckRequest deck,
        PlayerSide expectedSide,
        string path,
        OfficialPresetCatalog presets,
        List<SimulationFixtureDiagnostic> diagnostics)
    {
        if (deck.SchemaVersion != DeckRequest.CurrentSchemaVersion)
        {
            Add(diagnostics, "deck-schema-version", $"{path}.schemaVersion", $"expected {DeckRequest.CurrentSchemaVersion}, found {deck.SchemaVersion}.");
        }

        if (!Enum.IsDefined(deck.Side))
        {
            Add(diagnostics, "deck-side", $"{path}.side", $"'{deck.Side}' is not a valid player side.");
        }
        else if (deck.Side != expectedSide)
        {
            Add(diagnostics, "deck-side", $"{path}.side", $"expected '{expectedSide}', found '{deck.Side}'.");
        }

        if (!IsStableIdentifier(deck.IdentityCode))
        {
            Add(diagnostics, "deck-identity", $"{path}.identityCode", "identityCode must be a nonblank stable identifier.");
        }

        if (deck.ValidationProfile != DeckValidationProfile.DeveloperReduced)
        {
            Add(
                diagnostics,
                "deck-profile",
                $"{path}.validationProfile",
                "SIM-401 fixtures must use the explicit DeveloperReduced deck-validation profile.");
        }

        var seenCodes = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < deck.Cards.Count; index++)
        {
            DeckCardEntry? card = deck.Cards[index];
            string cardPath = $"{path}.cards[{index}]";
            if (card is null)
            {
                Add(diagnostics, "deck-card-null", cardPath, "deck card entries must not be null.");
                continue;
            }

            if (!IsStableIdentifier(card.Code))
            {
                Add(diagnostics, "deck-card-code", $"{cardPath}.code", "card code must be a nonblank stable identifier.");
            }
            else if (!seenCodes.Add(card.Code))
            {
                Add(diagnostics, "deck-card-duplicate", $"{cardPath}.code", $"card code '{card.Code}' appears more than once.");
            }

            if (card.Quantity <= 0)
            {
                Add(diagnostics, "deck-card-quantity", $"{cardPath}.quantity", $"quantity must be positive, found {card.Quantity}.");
            }
        }

        if (deck.PresetId is not null)
        {
            if (!IsStableIdentifier(deck.PresetId))
            {
                Add(diagnostics, "preset-id", $"{path}.presetId", "presetId must be a nonblank stable identifier when supplied.");
            }
            else if (!presets.TryGet(deck.PresetId, out OfficialPreset preset))
            {
                Add(diagnostics, "preset-unknown", $"{path}.presetId", $"official preset '{deck.PresetId}' was not found.");
            }
            else
            {
                if (preset.Side != expectedSide)
                {
                    Add(diagnostics, "preset-side", $"{path}.presetId", $"preset '{deck.PresetId}' belongs to side '{preset.Side}', expected '{expectedSide}'.");
                }

                if (!string.Equals(deck.IdentityCode, preset.IdentityCode, StringComparison.Ordinal))
                {
                    Add(diagnostics, "preset-identity", $"{path}.identityCode", $"identity '{deck.IdentityCode}' does not match preset identity '{preset.IdentityCode}'.");
                }

                if (!DeckCardsEqual(deck.Cards, preset.Cards))
                {
                    Add(diagnostics, "preset-cards", $"{path}.cards", $"cards do not exactly match official preset '{deck.PresetId}'.");
                }
            }
        }
    }

    private static void AddDeckLegalityProblems(
        DeckRequest deck,
        string path,
        CardDataSet data,
        IBehaviorCatalog behaviorCatalog,
        OfficialPresetCatalog presets,
        List<SimulationFixtureDiagnostic> diagnostics)
    {
        DeckValidationResult result = DeckValidator.ValidateWithBehaviorCatalog(
            deck,
            data,
            behaviorCatalog,
            presets);
        foreach (DeckValidationDiagnostic diagnostic in result.Diagnostics)
        {
            Add(diagnostics, $"deck-{diagnostic.Code}", $"{path}.{diagnostic.Path}", diagnostic.Message);
        }
    }

    private void ValidatePolicy(List<SimulationFixtureDiagnostic> diagnostics)
    {
        if (Policy is null)
        {
            return;
        }

        ValidatePolicyOrder(Policy.CorpActionOrder, "policy.corpActionOrder", diagnostics);
        ValidatePolicyOrder(Policy.RunnerActionOrder, "policy.runnerActionOrder", diagnostics);
    }

    private static void ValidatePolicyOrder(
        IReadOnlyList<string> actions,
        string path,
        List<SimulationFixtureDiagnostic> diagnostics)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < actions.Count; index++)
        {
            string action = actions[index];
            string actionPath = $"{path}[{index}]";
            if (!IsStableIdentifier(action))
            {
                Add(diagnostics, "policy-action", actionPath, "action keys must be nonblank stable identifiers.");
            }
            else if (!seen.Add(action))
            {
                Add(diagnostics, "policy-duplicate", actionPath, $"action key '{action}' appears more than once.");
            }
        }
    }

    private void ValidateOutcome(List<SimulationFixtureDiagnostic> diagnostics)
    {
        if (!string.Equals(ExpectedOutcome.Kind, "ongoing", StringComparison.Ordinal)
            && !string.Equals(ExpectedOutcome.Kind, "won", StringComparison.Ordinal))
        {
            Add(diagnostics, "outcome-kind", "expectedOutcome.kind", "expectedOutcome.kind must be 'ongoing' or 'won'.");
            return;
        }

        if (string.Equals(ExpectedOutcome.Kind, "ongoing", StringComparison.Ordinal))
        {
            if (ExpectedOutcome.Winner is not null)
            {
                Add(diagnostics, "outcome-winner", "expectedOutcome.winner", "an ongoing outcome must not specify a winner.");
            }

            if (ExpectedOutcome.Reason is not null)
            {
                Add(diagnostics, "outcome-reason", "expectedOutcome.reason", "an ongoing outcome must not specify a reason.");
            }

            return;
        }

        if (ExpectedOutcome.Winner is not { } winner || !Enum.IsDefined(winner))
        {
            Add(diagnostics, "outcome-winner", "expectedOutcome.winner", "a won outcome requires a valid winner.");
        }

        if (ExpectedOutcome.Reason is not ("agendaPoints" or "deckOut" or "flatline"))
        {
            Add(diagnostics, "outcome-reason", "expectedOutcome.reason", "a won outcome requires agendaPoints, deckOut, or flatline.");
        }
    }

    private void ValidateProvenance(List<SimulationFixtureDiagnostic> diagnostics)
    {
        if (Citations.Count == 0)
        {
            Add(diagnostics, "provenance-citations", "citations", "at least one rules/provenance citation is required.");
        }
        else
        {
            for (int index = 0; index < Citations.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(Citations[index]))
                {
                    Add(diagnostics, "provenance-citation", $"citations[{index}]", "citation must be nonblank.");
                }
            }
        }

        if (string.IsNullOrWhiteSpace(CrossCheckNote))
        {
            Add(diagnostics, "provenance-cross-check", "crossCheckNote", "a cross-check note is required.");
        }
    }

    private static bool DeckCardsEqual(
        IReadOnlyList<DeckCardEntry> actual,
        IReadOnlyList<DeckCardEntry> expected)
    {
        if (actual.Count != expected.Count)
        {
            return false;
        }

        for (int index = 0; index < actual.Count; index++)
        {
            DeckCardEntry? left = actual[index];
            DeckCardEntry? right = expected[index];
            if (left is null
                || right is null
                || !string.Equals(left.Code, right.Code, StringComparison.Ordinal)
                || left.Quantity != right.Quantity)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsStableIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            return false;
        }

        return value.All(static character => !char.IsWhiteSpace(character) && !char.IsControl(character));
    }

    private static bool IsLowerHex(string? value, int length)
    {
        if (value is null || value.Length != length)
        {
            return false;
        }

        return value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static void Add(List<SimulationFixtureDiagnostic> diagnostics, string code, string path, string message) =>
        diagnostics.Add(new SimulationFixtureDiagnostic(code, path, message));
}

/// <summary>
/// Immutable collection of SIM-401 documents. It catches duplicate fixture
/// IDs before any future runner tries to select a document.
/// </summary>
public sealed class SimulationFixtureCatalog
{
    public SimulationFixtureCatalog(IEnumerable<SimulationFixtureDocument> fixtures)
    {
        ArgumentNullException.ThrowIfNull(fixtures);
        Fixtures = new ReadOnlyCollection<SimulationFixtureDocument>(fixtures.ToArray());
    }

    public IReadOnlyList<SimulationFixtureDocument> Fixtures { get; }

    public SimulationFixtureDocument Get(string fixtureId) =>
        Fixtures.Single(fixture => string.Equals(fixture.FixtureId, fixtureId, StringComparison.Ordinal));

    public IReadOnlyList<SimulationFixtureDiagnostic> CollectProblems(
        OfficialPresetCatalog? officialPresets = null,
        IBehaviorCatalog? behaviorCatalog = null)
    {
        var diagnostics = new List<SimulationFixtureDiagnostic>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < Fixtures.Count; index++)
        {
            SimulationFixtureDocument fixture = Fixtures[index];
            string path = $"fixtures[{index}]";
            if (seen.TryGetValue(fixture.FixtureId, out int firstIndex))
            {
                diagnostics.Add(new SimulationFixtureDiagnostic(
                    "fixture-duplicate",
                    $"{path}.fixtureId",
                    $"fixture ID '{fixture.FixtureId}' already occurs at fixtures[{firstIndex}].fixtureId."));
            }
            else
            {
                seen.Add(fixture.FixtureId, index);
            }

            foreach (SimulationFixtureDiagnostic diagnostic in fixture.CollectProblems(officialPresets, behaviorCatalog))
            {
                diagnostics.Add(diagnostic with { Path = $"{path}.{diagnostic.Path}" });
            }
        }

        return new ReadOnlyCollection<SimulationFixtureDiagnostic>(diagnostics);
    }
}

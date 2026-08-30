using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

public enum ScenarioResolutionKind
{
    NestedNumber,
}

/// <summary>One explicit physical card placed by a compact scenario.</summary>
public sealed record ScenarioCardSpec
{
    [JsonConstructor]
    public ScenarioCardSpec(
        CardInstanceId id,
        CardDefinitionId definitionId,
        ZoneKind zone,
        PlayerSide ownerSide,
        bool faceUp = false,
        bool rezzed = false,
        int advancementCounters = 0)
    {
        Id = id;
        DefinitionId = definitionId;
        Zone = zone;
        OwnerSide = ownerSide;
        FaceUp = faceUp;
        Rezzed = rezzed;
        AdvancementCounters = advancementCounters;
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId Id { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardDefinitionId DefinitionId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneKind Zone { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide OwnerSide { get; private set; }

    public bool FaceUp { get; }

    public bool Rezzed { get; }

    public int AdvancementCounters { get; }
}

/// <summary>Typed nested-resolution setup supported by the scenario fixture.</summary>
public sealed record ScenarioResolutionSpec
{
    [JsonConstructor]
    public ScenarioResolutionSpec(
        ScenarioResolutionKind kind,
        PlayerSide ownerSide,
        PlayerSide targetSide,
        int minimum,
        int maximum,
        int creditsPerChoice,
        NestedNumberStage stage = NestedNumberStage.OuterPrompt,
        int? chosenOuterValue = null)
    {
        Kind = kind;
        OwnerSide = ownerSide;
        TargetSide = targetSide;
        Minimum = minimum;
        Maximum = maximum;
        CreditsPerChoice = creditsPerChoice;
        Stage = stage;
        ChosenOuterValue = chosenOuterValue;
    }

    [JsonInclude]
    [JsonRequired]
    public ScenarioResolutionKind Kind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide OwnerSide { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide TargetSide { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Minimum { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Maximum { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int CreditsPerChoice { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public NestedNumberStage Stage { get; private set; }

    public int? ChosenOuterValue { get; }
}

/// <summary>
/// Compact deterministic scenario document. Omitted cards and resolution mean
/// the normal empty/default game setup.
/// </summary>
public sealed class ScenarioArtifact
{
    [JsonConstructor]
    public ScenarioArtifact(
        int schemaVersion,
        string artifactKind,
        string rulesVersion,
        string cardDataVersion,
        ulong seed,
        IReadOnlyList<ScenarioCardSpec>? cards = null,
        ScenarioResolutionSpec? resolution = null,
        string? expectedStateHash = null)
    {
        ArgumentNullException.ThrowIfNull(artifactKind);
        ArgumentNullException.ThrowIfNull(rulesVersion);
        ArgumentNullException.ThrowIfNull(cardDataVersion);
        SchemaVersion = schemaVersion;
        ArtifactKind = artifactKind;
        RulesVersion = rulesVersion;
        CardDataVersion = cardDataVersion;
        Seed = seed;
        Cards = new ReadOnlyCollection<ScenarioCardSpec>((cards ?? []).Select(CloneCard).ToArray());
        Resolution = resolution;
        ExpectedStateHash = expectedStateHash;
    }

    public ScenarioArtifact(
        ulong seed = 1,
        IReadOnlyList<ScenarioCardSpec>? cards = null,
        ScenarioResolutionSpec? resolution = null,
        string? expectedStateHash = null,
        string? rulesVersion = null,
        string? cardDataVersion = null)
        : this(
            EngineCompatibility.ArtifactSchemaVersion,
            EngineArtifactKinds.Scenario,
            rulesVersion ?? EngineCompatibility.RulesVersion,
            cardDataVersion ?? EngineCompatibility.CardDataVersion,
            seed,
            cards,
            resolution,
            expectedStateHash)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public int SchemaVersion { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string ArtifactKind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string RulesVersion { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string CardDataVersion { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ulong Seed { get; private set; }

    public IReadOnlyList<ScenarioCardSpec> Cards { get; }

    public ScenarioResolutionSpec? Resolution { get; }

    public string? ExpectedStateHash { get; }

    public IReadOnlyList<StateInvariantViolation> CollectViolations()
    {
        var violations = new List<StateInvariantViolation>();
        if (SchemaVersion != EngineCompatibility.ArtifactSchemaVersion)
        {
            Add(
                violations,
                "artifact.unsupported-schema",
                "schemaVersion",
                $"Scenario schema version {SchemaVersion} is not supported; expected {EngineCompatibility.ArtifactSchemaVersion}.");
        }

        if (!string.Equals(ArtifactKind, EngineArtifactKinds.Scenario, StringComparison.Ordinal))
        {
            Add(violations, "artifact.kind-mismatch", "artifactKind", "The artifact kind must be 'scenario'.");
        }

        if (!string.Equals(RulesVersion, EngineCompatibility.RulesVersion, StringComparison.Ordinal))
        {
            Add(
                violations,
                "artifact.unsupported-rules",
                "rulesVersion",
                $"Rules version '{RulesVersion}' is not supported; expected '{EngineCompatibility.RulesVersion}'.");
        }

        if (!string.Equals(CardDataVersion, EngineCompatibility.CardDataVersion, StringComparison.Ordinal))
        {
            Add(
                violations,
                "artifact.unsupported-card-data",
                "cardDataVersion",
                $"Card-data version '{CardDataVersion}' is not supported; expected '{EngineCompatibility.CardDataVersion}'.");
        }

        var cardIds = new HashSet<CardInstanceId>();
        for (int index = 0; index < Cards.Count; index++)
        {
            ScenarioCardSpec? card = Cards[index];
            string path = $"cards[{index}]";
            if (card is null)
            {
                Add(violations, "scenario.null-card", path, "Scenario cards must not contain null entries.");
                continue;
            }

            if (!StableIdentifier.TryValidate(card.Id.Value))
            {
                Add(violations, "id.invalid", $"{path}.id", "Scenario card ID is empty or invalid.");
            }
            else if (!cardIds.Add(card.Id))
            {
                Add(violations, "scenario.duplicate-card-id", $"{path}.id", $"Card instance ID '{card.Id}' occurs more than once.");
            }

            if (!StableIdentifier.TryValidate(card.DefinitionId.Value))
            {
                Add(violations, "id.invalid", $"{path}.definitionId", "Scenario card definition ID is empty or invalid.");
            }
            else if (!ScenarioBuilder.IsKnownDefinition(card.DefinitionId))
            {
                Add(violations, "scenario.unknown-definition", $"{path}.definitionId", $"Card definition '{card.DefinitionId}' is not registered in the fixture card catalog.");
            }

            bool supportedCardZone = TryGetScenarioZoneOwner(card.Zone, out PlayerSide expectedOwner);
            if (!supportedCardZone)
            {
                Add(
                    violations,
                    "scenario.invalid-zone",
                    $"{path}.zone",
                    Enum.IsDefined(card.Zone)
                        ? "Scenario card placement must use one of the foundational Corp or Runner zones; server/shared topology is not a compact scenario input."
                    : $"'{card.Zone}' is not a supported zone kind.");
            }
            else if (card.Zone == ZoneKind.RunnerPlayArea)
            {
                Add(
                    violations,
                    "scenario.invalid-zone",
                    $"{path}.zone",
                    "RunnerPlayArea is an active-run zone and cannot be populated by a pre-run compact scenario.");
            }

            if (!Enum.IsDefined(card.OwnerSide))
            {
                Add(violations, "scenario.invalid-owner", $"{path}.ownerSide", $"'{card.OwnerSide}' is not a valid player side.");
            }
            else if (supportedCardZone)
            {
                if (card.OwnerSide != expectedOwner)
                {
                    Add(
                        violations,
                        "scenario.owner-zone-mismatch",
                        $"{path}.ownerSide",
                        $"Zone '{card.Zone}' requires owner side '{expectedOwner}', but the scenario specifies '{card.OwnerSide}'.");
                }
            }

            if (card.AdvancementCounters < 0)
            {
                Add(violations, "scenario.negative-counters", $"{path}.advancementCounters", "Advancement counters must not be negative.");
            }
        }

        ScenarioResolutionSpec? resolution = Resolution;
        if (resolution is not null)
        {
            if (!Enum.IsDefined(resolution.Kind))
            {
                Add(violations, "scenario.invalid-resolution-kind", "resolution.kind", $"'{resolution.Kind}' is not supported.");
            }

            if (!Enum.IsDefined(resolution.OwnerSide))
            {
                Add(violations, "scenario.invalid-owner", "resolution.ownerSide", $"'{resolution.OwnerSide}' is not a valid player side.");
            }

            if (!Enum.IsDefined(resolution.TargetSide))
            {
                Add(violations, "scenario.invalid-owner", "resolution.targetSide", $"'{resolution.TargetSide}' is not a valid player side.");
            }

            if (resolution.Minimum > resolution.Maximum)
            {
                Add(violations, "scenario.invalid-range", "resolution", "Resolution minimum must not exceed maximum.");
            }

            if ((long)resolution.Maximum - resolution.Minimum > 64)
            {
                Add(violations, "scenario.range-too-large", "resolution", "Scenario number ranges may contain at most 65 values.");
            }

            if (!Enum.IsDefined(resolution.Stage))
            {
                Add(violations, "scenario.invalid-stage", "resolution.stage", $"'{resolution.Stage}' is not supported.");
            }
            else if (resolution.Stage == NestedNumberStage.OuterPrompt && resolution.ChosenOuterValue.HasValue)
            {
                Add(violations, "scenario.unexpected-outer-choice", "resolution.chosenOuterValue", "An outer prompt must not contain an outer choice.");
            }
            else if (resolution.Stage == NestedNumberStage.InnerPrompt
                && (!resolution.ChosenOuterValue.HasValue
                    || resolution.ChosenOuterValue.Value < resolution.Minimum
                    || resolution.ChosenOuterValue.Value > resolution.Maximum))
            {
                Add(violations, "scenario.invalid-outer-choice", "resolution.chosenOuterValue", "An inner prompt requires an outer choice within the configured range.");
            }
        }

        if (ExpectedStateHash is not null && !DeterministicHash.IsSha256Hex(ExpectedStateHash))
        {
            Add(violations, "artifact.invalid-state-hash", "expectedStateHash", "Expected state hash must be lowercase SHA-256 hex.");
        }

        return violations.AsReadOnly();

        static void Add(List<StateInvariantViolation> target, string code, string path, string message) =>
            target.Add(new StateInvariantViolation(code, path, message));
    }

    public void Validate()
    {
        IReadOnlyList<StateInvariantViolation> violations = CollectViolations();
        if (violations.Count > 0)
        {
            throw new StateInvariantException(violations);
        }
    }

    private static ScenarioCardSpec CloneCard(ScenarioCardSpec? card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new ScenarioCardSpec(
            card.Id,
            card.DefinitionId,
            card.Zone,
            card.OwnerSide,
            card.FaceUp,
            card.Rezzed,
            card.AdvancementCounters);
    }

    private static bool TryGetScenarioZoneOwner(ZoneKind zone, out PlayerSide owner)
    {
        switch (zone)
        {
            case ZoneKind.CorpDeck:
            case ZoneKind.CorpHand:
            case ZoneKind.CorpDiscard:
            case ZoneKind.CorpScore:
                owner = PlayerSide.Corp;
                return true;
            case ZoneKind.RunnerStack:
            case ZoneKind.RunnerGrip:
            case ZoneKind.RunnerPlayArea:
            case ZoneKind.RunnerHeap:
            case ZoneKind.RunnerRig:
            case ZoneKind.RunnerScore:
                owner = PlayerSide.Runner;
                return true;
            default:
                owner = default;
                return false;
        }
    }
}

public static class ScenarioBuilder
{
    private static readonly HashSet<CardDefinitionId> KnownDefinitions =
        new HashSet<CardDefinitionId>
        {
            new CardDefinitionId("fixture:first"),
            new CardDefinitionId("fixture:second"),
        };

    public static bool IsKnownDefinition(CardDefinitionId definitionId) => KnownDefinitions.Contains(definitionId);

    public static GameState Build(ScenarioArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        artifact.Validate();

        GameState baseState = GameState.CreateNew(artifact.Seed, artifact.RulesVersion, artifact.CardDataVersion);
        if (artifact.Cards.Count > 0)
        {
            baseState = AddCards(baseState, artifact.Cards);
        }

        if (artifact.Resolution is not null)
        {
            baseState = AddResolution(baseState, artifact.Resolution);
        }

        StateInvariantValidator.Validate(baseState);
        if (artifact.ExpectedStateHash is not null)
        {
            string actualHash = DeterministicHash.ForState(baseState);
            if (!string.Equals(actualHash, artifact.ExpectedStateHash, StringComparison.Ordinal))
            {
                throw new StateInvariantException(
                    [new StateInvariantViolation(
                        "scenario.state-hash-mismatch",
                        "expectedStateHash",
                        $"Expected scenario state hash '{artifact.ExpectedStateHash}', but built '{actualHash}'.")]);
            }
        }

        return baseState;
    }

    private static GameState AddCards(GameState baseState, IReadOnlyList<ScenarioCardSpec> specifications)
    {
        var cards = new List<CardInstanceState>(specifications.Count);
        var cardIds = new HashSet<CardInstanceId>();
        var zoneCards = baseState.Zones.ToDictionary(zone => zone.Id, zone => zone.CardIds.ToList());
        var zonesByKind = baseState.Zones
            .Where(zone => zone.Kind is not (ZoneKind.ServerIce or ZoneKind.ServerRoot))
            .GroupBy(zone => zone.Kind)
            .ToDictionary(group => group.Key, group => group.Single());

        foreach (ScenarioCardSpec specification in specifications)
        {
            if (!cardIds.Add(specification.Id))
            {
                throw new StateInvariantException(
                    [new StateInvariantViolation(
                        "scenario.duplicate-card-id",
                        "cards",
                        $"Card instance ID '{specification.Id}' occurs more than once.")]);
            }

            if (!zonesByKind.TryGetValue(specification.Zone, out ZoneState? zone))
            {
                throw new StateInvariantException(
                    [new StateInvariantViolation(
                        "scenario.zone-not-found",
                        "cards.zone",
                        $"Scenario card zone '{specification.Zone}' is not a default installable zone.")]);
            }

            cards.Add(new CardInstanceState(
                specification.Id,
                specification.DefinitionId,
                zone.Id,
                specification.OwnerSide,
                specification.FaceUp,
                specification.Rezzed,
                specification.AdvancementCounters));
            zoneCards[zone.Id].Add(specification.Id);
        }

        var allocator = new StableIdAllocator(baseState.Allocator);
        foreach (ScenarioCardSpec specification in specifications)
        {
            if (TryParseCardSequence(specification.Id.Value, out ulong sequence)
                && sequence >= allocator.State.NextSequence)
            {
                if (sequence == ulong.MaxValue)
                {
                    throw new StateInvariantException(
                        [new StateInvariantViolation(
                            "allocator.exhausted",
                            "cards",
                            "A scenario card ID uses the maximum allocatable sequence.")]);
                }

                allocator = new StableIdAllocator(sequence + 1);
            }
        }

        ZoneState[] zones = baseState.Zones
            .Select(zone => zoneCards.TryGetValue(zone.Id, out List<CardInstanceId>? ids)
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, ids)
                : zone)
            .ToArray();
        return new GameState(
            baseState.GameId,
            baseState.RulesVersion,
            baseState.CardDataVersion,
            baseState.Players,
            cards,
            zones,
            baseState.Servers,
            baseState.Turn,
            baseState.Outcome,
            baseState.RandomState,
            allocator.State,
            actionSequence: baseState.ActionSequence,
            decisionRevision: baseState.DecisionRevision,
            eventSequence: baseState.EventSequence,
            resolutionStack: baseState.ResolutionStack,
            pendingDecision: baseState.PendingDecision,
            initialSeed: baseState.InitialSeed);
    }

    private static GameState AddResolution(GameState state, ScenarioResolutionSpec specification)
    {
        if (specification.Kind != ScenarioResolutionKind.NestedNumber)
        {
            throw new StateInvariantException(
                [new StateInvariantViolation(
                    "scenario.invalid-resolution-kind",
                    "resolution.kind",
                    $"Resolution kind '{specification.Kind}' is not registered.")]);
        }

        PlayerId owner = state.Players.Single(player => player.Side == specification.OwnerSide).Id;
        PlayerId target = state.Players.Single(player => player.Side == specification.TargetSide).Id;
        var frame = new ChooseNumberThenAdjustCreditsFrame(
            owner,
            target,
            specification.Minimum,
            specification.Maximum,
            specification.CreditsPerChoice);
        GameState prompt = GameEngine.BeginResolution(state, frame);
        if (specification.Stage == NestedNumberStage.OuterPrompt)
        {
            return prompt;
        }

        PendingDecision decision = prompt.PendingDecision
            ?? throw new StateInvariantException(
                [new StateInvariantViolation(
                    "scenario.resolution-shape",
                    "resolution",
                    "The nested fixture did not produce its outer decision.")]);
        LegalAction action = decision.LegalActions.Single(candidate =>
            candidate.Parameters is NumberChoiceParameters numberChoice
            && numberChoice.Value == specification.ChosenOuterValue);
        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id));
        if (!result.Accepted)
        {
            throw new StateInvariantException(
                [new StateInvariantViolation(
                    "scenario.resolution-shape",
                    "resolution",
                    $"The configured outer choice could not be applied: {result.Rejection?.Message}")]);
        }

        return result.State;
    }

    private static bool TryParseCardSequence(string? value, out ulong sequence)
    {
        sequence = 0;
        const string prefix = "card:";
        if (value is null || !value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> digits = value.AsSpan(prefix.Length);
        if (digits.IsEmpty)
        {
            return false;
        }

        foreach (char digit in digits)
        {
            if (digit is < '0' or > '9')
            {
                return false;
            }
        }

        return ulong.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out sequence);
    }
}

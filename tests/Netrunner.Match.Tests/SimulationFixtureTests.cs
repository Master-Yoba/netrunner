using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class SimulationFixtureTests
{
    private const string BasicFixtureId = "sim-slice44-batch3-basic";
    private const string ExpandedFixtureId = "sim-slice44-batch3-expanded";
    private const string GatewayBehaviorRegistryVersion = "gateway-m4-4.5-c1";
    private const string GatewayBehaviorCatalogFingerprint = "c170516786fef0130e37153f246faf3a2d74249311ec6e49751727a9429f1a25";

    private static readonly HashSet<string> AllowedCorpCodes = new(StringComparer.Ordinal)
    {
        "30037",
        "30040",
        "30041",
        "30048",
        "30053",
        "30056",
        "30064",
        "30067",
        "30071",
        "30072",
        "30074",
        "30075",
    };

    private static readonly HashSet<string> AllowedRunnerCodes = new(StringComparer.Ordinal)
    {
        "30002",
        "30005",
        "30006",
        "30011",
        "30020",
        "30021",
        "30029",
        "30030",
        "30031",
        "30003",
        "30007",
        "30009",
        "30014",
        "30015",
        "30016",
        "30018",
        "30023",
        "30024",
        "30027",
        "30033",
        "30008",
        "30012",
        "30013",
        "30022",
        "30025",
        "30026",
        "30034",
    };

    [Fact]
    public void CheckedInFixturesAcceptReducedSlice44DecksAndRoundTripStably()
    {
        SimulationFixtureCatalog catalog = LoadFixtures();
        BehaviorCatalog fullCatalog = GatewayBehaviorCatalog.Create();

        Assert.Equal(2, catalog.Fixtures.Count);
        Assert.Equal(
            new[] { BasicFixtureId, ExpandedFixtureId },
            catalog.Fixtures.Select(static fixture => fixture.FixtureId));

        Assert.Equal(
            AllowedCorpCodes.Concat(AllowedRunnerCodes).OrderBy(static code => code),
            catalog.Fixtures
                .SelectMany(static fixture => fixture.CorpDeck.Cards.Concat(fixture.RunnerDeck.Cards))
                .Select(static card => card.Code)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static code => code));
        Assert.Equal(44, fullCatalog.AllDefinitionIds.Count);
        Assert.All(
            AllowedCorpCodes.Concat(AllowedRunnerCodes),
            code => Assert.Contains(new CardDefinitionId(code), fullCatalog.AllDefinitionIds));

        foreach (SimulationFixtureDocument fixture in catalog.Fixtures)
        {
            Assert.Empty(fixture.CollectProblems());
            fixture.ValidateOrThrow(behaviorCatalog: fullCatalog);
            Assert.Equal("26.03", fixture.RulesVersion);
            Assert.Equal("236140cf84491c23995b028bc791281837520b97", fixture.CardDataVersion);
            Assert.Equal(GatewayBehaviorRegistryVersion, fixture.BehaviorRegistryVersion);
            Assert.Equal(GatewayBehaviorCatalogFingerprint, fixture.BehaviorCatalogFingerprint);
            Assert.Equal(GatewayBehaviorRegistryVersion, fullCatalog.Version);
            Assert.Equal(GatewayBehaviorCatalogFingerprint, fullCatalog.Fingerprint);
            Assert.Equal(DeckValidationProfile.DeveloperReduced, fixture.CorpDeck.ValidationProfile);
            Assert.Equal(DeckValidationProfile.DeveloperReduced, fixture.RunnerDeck.ValidationProfile);
            Assert.Null(fixture.CorpDeck.PresetId);
            Assert.Null(fixture.RunnerDeck.PresetId);
            Assert.Equal(PlayerSide.Corp, fixture.CorpDeck.Side);
            Assert.Equal(PlayerSide.Runner, fixture.RunnerDeck.Side);
            Assert.Equal("30077", fixture.CorpDeck.IdentityCode);
            Assert.Equal("30076", fixture.RunnerDeck.IdentityCode);
            Assert.All(fixture.CorpDeck.Cards, card => Assert.Contains(card.Code, AllowedCorpCodes));
            Assert.All(fixture.RunnerDeck.Cards, card => Assert.Contains(card.Code, AllowedRunnerCodes));
            Assert.All(
                fixture.CorpDeck.Cards.Concat(fixture.RunnerDeck.Cards),
                card => Assert.Equal(1, card.Quantity));

            string encoded = SimulationFixtureJson.Serialize(fixture);
            SimulationFixtureDocument parsed = SimulationFixtureJson.Parse(encoded);
            Assert.Equal(encoded, SimulationFixtureJson.Serialize(parsed));
            Assert.Equal(fixture.Seed, parsed.Seed);
            Assert.Equal(fixture.ActionCap, parsed.ActionCap);
        }
    }

    [Fact]
    public void StructuralValidationRejectsEachPinnedDeckAndPolicyFailureWithExactPaths()
    {
        SimulationFixtureDocument fixture = LoadFixtures().Get(BasicFixtureId);

        AssertDiagnostic(
            Rebuild(fixture, schemaVersion: SimulationFixtureDocument.CurrentSchemaVersion + 1),
            "schema-version",
            "schemaVersion");
        AssertDiagnostic(
            Rebuild(fixture, rulesVersion: "26.02"),
            "rules-version",
            "rulesVersion");
        AssertDiagnostic(
            Rebuild(fixture, cardDataVersion: new string('0', 40)),
            "metadata-pin",
            "cardDataVersion");
        AssertDiagnostic(
            Rebuild(fixture, behaviorRegistryVersion: "unregistered-sim-catalog"),
            "behavior-pin",
            "behaviorRegistryVersion");
        AssertDiagnostic(
            Rebuild(fixture, behaviorCatalogFingerprint: new string('0', 64)),
            "behavior-fingerprint",
            "behaviorCatalogFingerprint");
        AssertDiagnostic(
            Rebuild(fixture, actionCap: 0),
            "action-cap",
            "actionCap");
        AssertDiagnostic(
            Rebuild(fixture, actionCap: SimulationFixtureDocument.MaximumActionCap + 1),
            "action-cap",
            "actionCap");

        AssertDiagnostic(
            Rebuild(
                fixture,
                corpDeck: RebuildDeck(fixture.CorpDeck, schemaVersion: DeckRequest.CurrentSchemaVersion + 1)),
            "deck-schema-version",
            "corpDeck.schemaVersion");
        AssertDiagnostic(
            Rebuild(
                fixture,
                runnerDeck: RebuildDeck(fixture.RunnerDeck, side: (PlayerSide)99)),
            "deck-side",
            "runnerDeck.side");
        AssertDiagnostic(
            Rebuild(
                fixture,
                corpDeck: RebuildDeck(fixture.CorpDeck, validationProfile: DeckValidationProfile.UserFacing)),
            "deck-profile",
            "corpDeck.validationProfile");

        DeckCardEntry firstCorpCard = fixture.CorpDeck.Cards[0];
        AssertDiagnostic(
            Rebuild(
                fixture,
                corpDeck: RebuildDeck(
                    fixture.CorpDeck,
                    cards: fixture.CorpDeck.Cards.Append(new DeckCardEntry(firstCorpCard.Code, 1)).ToArray())),
            "deck-card-duplicate",
            "corpDeck.cards[12].code");
        AssertDiagnostic(
            Rebuild(
                fixture,
                corpDeck: RebuildDeck(fixture.CorpDeck, presetId: "missing-official-preset")),
            "preset-unknown",
            "corpDeck.presetId");
        AssertDiagnostic(
            Rebuild(
                fixture,
                corpDeck: RebuildDeck(fixture.CorpDeck, presetId: "system-gateway-basic-runner")),
            "preset-side",
            "corpDeck.presetId");
        AssertDiagnostic(
            Rebuild(
                fixture,
                corpDeck: RebuildDeck(
                    fixture.CorpDeck,
                    presetId: "system-gateway-basic-corp",
                    cards: fixture.CorpDeck.Cards
                        .Select((card, index) => index == 0
                            ? new DeckCardEntry(card.Code, card.Quantity - 1)
                            : card)
                        .ToArray())),
            "preset-cards",
            "corpDeck.cards");
        AssertDiagnostic(
            Rebuild(
                fixture,
                policy: new SimulationPolicyOverrides(["draw", "draw"], ["gain-credit"])),
            "policy-duplicate",
            "policy.corpActionOrder[1]");
    }

    [Fact]
    public void StructuralValidationRejectsInvalidOutcomeShape()
    {
        SimulationFixtureDocument fixture = LoadFixtures().Get(BasicFixtureId);

        SimulationFixtureDocument invalidKind = Rebuild(
            fixture,
            expectedOutcome: new SimulationOutcomeExpectation("finished"));
        AssertDiagnostic(invalidKind, "outcome-kind", "expectedOutcome.kind");

        SimulationFixtureDocument ongoingWithWinner = Rebuild(
            fixture,
            expectedOutcome: new SimulationOutcomeExpectation("ongoing", PlayerSide.Corp));
        AssertDiagnostic(ongoingWithWinner, "outcome-winner", "expectedOutcome.winner");

        SimulationFixtureDocument wonWithoutFields = Rebuild(
            fixture,
            expectedOutcome: new SimulationOutcomeExpectation("won"));
        AssertDiagnostic(wonWithoutFields, "outcome-winner", "expectedOutcome.winner");
        AssertDiagnostic(wonWithoutFields, "outcome-reason", "expectedOutcome.reason");
    }

    [Fact]
    public void FixtureCatalogRejectsDuplicateIdsBeforeSelection()
    {
        SimulationFixtureDocument fixture = LoadFixtures().Get(BasicFixtureId);
        SimulationFixtureCatalog duplicateCatalog = new([fixture, fixture]);

        AssertDiagnostic(
            duplicateCatalog.CollectProblems(),
            "fixture-duplicate",
            "fixtures[1].fixtureId");
    }

    [Fact]
    public void ParserRejectsUnknownDuplicateAndInvalidSideWithExactPaths()
    {
        SimulationFixtureDocument fixture = LoadFixtures().Get(BasicFixtureId);
        string json = SimulationFixtureJson.Serialize(fixture);

        string unknown = json.Replace(
            "  \"fixtureId\": \"sim-slice44-batch3-basic\",",
            "  \"fixtureId\": \"sim-slice44-batch3-basic\",\n  \"unexpected\": true,",
            StringComparison.Ordinal);
        AssertParsePath(unknown, "simulation fixture.unexpected");

        string duplicate = json.Replace(
            "  \"fixtureId\": \"sim-slice44-batch3-basic\",",
            "  \"fixtureId\": \"sim-slice44-batch3-basic\",\n  \"fixtureId\": \"duplicate\",",
            StringComparison.Ordinal);
        AssertParsePath(duplicate, "simulation fixture.fixtureId");

        string missing = json.Replace(
            "  \"actionCap\": 128,\n",
            string.Empty,
            StringComparison.Ordinal);
        AssertParsePath(missing, "simulation fixture.actionCap");

        string invalidSide = json.Replace(
            "    \"side\": \"runner\",",
            "    \"side\": \"invalid\",",
            StringComparison.Ordinal);
        AssertParsePath(invalidSide, "runnerDeck.side");
    }

    [Fact]
    public void ExecutionBoundValidationRequiresMetadataAndBehaviorCatalog()
    {
        SimulationFixtureDocument fixture = LoadFixtures().Get(BasicFixtureId);
        CardDataSet data = CardDataResource.LoadEmbedded();

        Assert.Throws<ArgumentNullException>(() => fixture.ValidateForSimulation(data, null!));

        BehaviorCatalog emptyBehavior = new BehaviorCatalogBuilder("sim-empty").Build();
        IReadOnlyList<SimulationFixtureDiagnostic> problems = fixture.CollectProblems(data, emptyBehavior);
        Assert.Contains(
            problems,
            diagnostic => diagnostic.Code == "behavior-pin"
                && diagnostic.Path == "behaviorRegistryVersion");
        // The default clean build intentionally embeds only synthetic card
        // data; this assertion proves the execution-bound overload fails
        // closed on that metadata pin rather than starting setup anyway.
        Assert.Contains(
            problems,
            diagnostic => diagnostic.Code == "metadata-pin"
                && diagnostic.Path == "cardDataVersion");
    }

    private static SimulationFixtureCatalog LoadFixtures()
    {
        string directory = Path.Combine(FindRepositoryRoot(), "data", "m4", "sim");
        return SimulationFixtureProvider.LoadDirectory(directory);
    }

    private static void AssertDiagnostic(
        SimulationFixtureDocument fixture,
        string code,
        string path) =>
        AssertDiagnostic(fixture.CollectProblems(), code, path);

    private static void AssertDiagnostic(
        IEnumerable<SimulationFixtureDiagnostic> diagnostics,
        string code,
        string path)
    {
        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Code == code && diagnostic.Path == path);
    }

    private static void AssertParsePath(string json, string path)
    {
        SimulationFixtureValidationException exception = Assert.Throws<SimulationFixtureValidationException>(
            () => SimulationFixtureJson.Parse(json));
        Assert.Contains(exception.Diagnostics, diagnostic => diagnostic.Path == path);
    }

    private static SimulationFixtureDocument Rebuild(
        SimulationFixtureDocument fixture,
        int? schemaVersion = null,
        string? rulesVersion = null,
        string? cardDataVersion = null,
        string? behaviorRegistryVersion = null,
        string? behaviorCatalogFingerprint = null,
        int? actionCap = null,
        DeckRequest? corpDeck = null,
        DeckRequest? runnerDeck = null,
        SimulationPolicyOverrides? policy = null,
        SimulationOutcomeExpectation? expectedOutcome = null) =>
        new(
            schemaVersion ?? fixture.SchemaVersion,
            fixture.FixtureId,
            rulesVersion ?? fixture.RulesVersion,
            cardDataVersion ?? fixture.CardDataVersion,
            behaviorRegistryVersion ?? fixture.BehaviorRegistryVersion,
            behaviorCatalogFingerprint ?? fixture.BehaviorCatalogFingerprint,
            fixture.Seed,
            actionCap ?? fixture.ActionCap,
            corpDeck ?? fixture.CorpDeck,
            runnerDeck ?? fixture.RunnerDeck,
            expectedOutcome ?? fixture.ExpectedOutcome,
            policy ?? fixture.Policy,
            fixture.Citations,
            fixture.CrossCheckNote);

    private static DeckRequest RebuildDeck(
        DeckRequest deck,
        int? schemaVersion = null,
        PlayerSide? side = null,
        string? identityCode = null,
        IReadOnlyList<DeckCardEntry>? cards = null,
        DeckValidationProfile? validationProfile = null,
        string? presetId = null,
        string? cardDataVersion = null) =>
        new(
            schemaVersion ?? deck.SchemaVersion,
            side ?? deck.Side,
            identityCode ?? deck.IdentityCode,
            cards ?? deck.Cards,
            validationProfile ?? deck.ValidationProfile,
            presetId ?? deck.PresetId,
            cardDataVersion ?? deck.CardDataVersion);

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Netrunner.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate the Netrunner repository root.");
    }
}

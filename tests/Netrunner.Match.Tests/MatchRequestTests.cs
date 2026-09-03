using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class MatchRequestTests
{
    [Fact]
    public void VersionedRequestRoundTripsWithoutStartingSetup()
    {
        MatchRequest request = new(
            MatchRequest.CurrentSchemaVersion,
            EngineCompatibility.RulesVersion,
            "fixture",
            42,
            Deck(PlayerSide.Corp, "corp-id"),
            Deck(PlayerSide.Runner, "runner-id"),
            "fixture-group");

        MatchRequest parsed = MatchRequestJson.Parse(MatchRequestJson.Serialize(request));

        Assert.Equal(request.SchemaVersion, parsed.SchemaVersion);
        Assert.Equal(request.RulesVersion, parsed.RulesVersion);
        Assert.Equal(request.CardDataVersion, parsed.CardDataVersion);
        Assert.Equal(request.Seed, parsed.Seed);
        Assert.Equal(request.PresetGroup, parsed.PresetGroup);
        Assert.Equal(request.CorpDeck.Side, parsed.CorpDeck.Side);
        Assert.Equal(request.CorpDeck.IdentityCode, parsed.CorpDeck.IdentityCode);
        Assert.Equal(request.CorpDeck.Cards, parsed.CorpDeck.Cards);
        Assert.Equal(PlayerSide.Runner, parsed.RunnerDeck.Side);
    }

    [Fact]
    public void ParserReportsUnknownAndMissingFieldsWithExactPaths()
    {
        string json = MatchRequestJson.Serialize(new MatchRequest(
            MatchRequest.CurrentSchemaVersion,
            EngineCompatibility.RulesVersion,
            "fixture",
            1,
            Deck(PlayerSide.Corp, "corp-id"),
            Deck(PlayerSide.Runner, "runner-id")));

        string unknown = json.Replace(
            "\"schemaVersion\": 1,",
            "\"schemaVersion\": 1,\n  \"unexpected\": true,",
            StringComparison.Ordinal);
        MatchRequestValidationException unknownException = Assert.Throws<MatchRequestValidationException>(
            () => MatchRequestJson.Parse(unknown));
        Assert.Contains("match request.unexpected", unknownException.Message, StringComparison.Ordinal);

        int runnerProperty = json.LastIndexOf(",\n  \"runnerDeck\"", StringComparison.Ordinal);
        Assert.True(runnerProperty > 0);
        string missing = json[..runnerProperty] + "\n}\n";
        MatchRequestValidationException missingException = Assert.Throws<MatchRequestValidationException>(
            () => MatchRequestJson.Parse(missing));
        Assert.Contains("match request.runnerDeck", missingException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UserFacingCompositionRejectsDeveloperReducedProfile()
    {
        CardDataSet data = CardDataResource.LoadEmbedded();
        DeckRequest reduced = new(
            DeckRequest.CurrentSchemaVersion,
            PlayerSide.Runner,
            "runner-id",
            [new DeckCardEntry("30029", 1)],
            DeckValidationProfile.DeveloperReduced);

        MatchRequestValidationException exception = Assert.Throws<MatchRequestValidationException>(() =>
            MatchRequestComposer.ComposeUserFacing(
                reduced,
                new DeckRequest(
                    DeckRequest.CurrentSchemaVersion,
                    PlayerSide.Corp,
                    "corp-id",
                    [new DeckCardEntry("30067", 1)],
                    DeckValidationProfile.DeveloperReduced),
                data));

        Assert.Contains("runnerDeck.validationProfile", exception.Message, StringComparison.Ordinal);
        Assert.Contains("DeveloperReduced", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UserFacingCompositionRequiresBehaviorCatalog()
    {
        MatchRequestValidationException exception = Assert.Throws<MatchRequestValidationException>(() =>
            MatchRequestComposer.ComposeUserFacing(
                Deck(PlayerSide.Corp, "corp-id"),
                Deck(PlayerSide.Runner, "runner-id"),
                CardDataResource.LoadEmbedded()));

        Assert.Contains("behaviorCatalog", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OfficialPresetsAreValidatedButNotYetUserPlayable()
    {
        MatchRequestValidationException exception = Assert.Throws<MatchRequestValidationException>(() =>
            MatchRequestComposer.ComposeOfficial(
                "system-gateway-basic-corp",
                "system-gateway-basic-runner",
                CardDataResource.LoadEmbedded()));

        Assert.Contains("not user-playable", exception.Message, StringComparison.Ordinal);
        Assert.Contains("presets", exception.Message, StringComparison.Ordinal);
        Assert.Contains("behaviorCatalog", exception.Message, StringComparison.Ordinal);
    }

    private static DeckRequest Deck(PlayerSide side, string identityCode) =>
        new(
            DeckRequest.CurrentSchemaVersion,
            side,
            identityCode,
            [new DeckCardEntry("fixture-card", 1)],
            DeckValidationProfile.UserFacing,
            cardDataVersion: "fixture");
}

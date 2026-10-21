using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class FirstHqBreachAccessPresentationTests
{
    [Fact]
    public void MatchMapsRawFirstHqBreachModifierTerms()
    {
        M2ScenarioDocument document = M2ScenarioProvider.LoadRepresentative();
        Observation baseline = ObservationBuilder.Build(
            M2ScenarioSetup.Build(document),
            PlayerSide.Runner);
        var publicState = new Dictionary<string, object?>(baseline.PublicState)
        {
            ["run"] = new Dictionary<string, object?>
            {
                ["id"] = "run:first-hq",
                ["attackedServer"] = "server:remote",
                ["initiatingSide"] = PlayerSide.Runner,
                ["phase"] = RunCrPhase.Success,
                ["step"] = RunTimingStep.BreachCandidateSelection,
                ["currentIceIndex"] = -1,
                ["currentIce"] = null,
                ["successful"] = true,
                ["unsuccessful"] = false,
                ["endReason"] = null,
                ["priority"] = null,
                ["encounter"] = null,
                ["breach"] = new Dictionary<string, object?>
                {
                    ["candidates"] = Array.Empty<object?>(),
                    ["randomAccessLimit"] = 2,
                    ["hiddenCandidateSource"] = BreachHiddenCandidateSource.CorpHand,
                    ["chosen"] = Array.Empty<object?>(),
                    ["accessed"] = Array.Empty<object?>(),
                    ["accessState"] = RunAccessState.InProgress,
                },
                ["runCredits"] = 0,
                ["initiatingSource"] = null,
                ["breachAccessModifiers"] = new object?[]
                {
                    new Dictionary<string, object?>
                    {
                        ["sourceCardId"] = "card:first-hq-source",
                        ["attackedServerId"] = "server:headquarters",
                        ["delta"] = 1,
                        ["reasonKey"] = "first-hq-test",
                        ["sourceKind"] = RunBreachAccessModifierSourceKind.FirstHeadquartersBreach,
                    },
                },
            },
        };

        MatchViewModel view = ObservationViewMapper.Build(
            baseline with { PublicState = publicState },
            CardMetadataResolvers.Default,
            GatewayBehaviorCatalog.Create());

        RunFocusViewModel run = Assert.IsType<RunFocusViewModel>(view.Run);
        RunBreachAccessModifierViewModel modifier = Assert.Single(run.BreachAccessModifiers);
        Assert.Equal("card:first-hq-source", modifier.SourceCardId);
        Assert.Equal("server:headquarters", modifier.AttackedServerId);
        Assert.Equal(1, modifier.Delta);
        Assert.Equal("first-hq-test", modifier.ReasonKey);
        Assert.Equal(RunBreachAccessModifierSourceKind.FirstHeadquartersBreach, modifier.SourceKind);
    }
}

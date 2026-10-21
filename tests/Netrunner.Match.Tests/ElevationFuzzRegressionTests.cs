using Netrunner.Cards;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>EL.8: games of the 10,000-game Gateway + Elevation fuzz that exposed a defect, replayed by seed (official data only).</summary>
public sealed class ElevationFuzzRegressionTests
{
    [Theory]
    // A card-effect install (Topan, Magdalene, Synapse, Poétrï) offered a trojan that installs only on ICE, with no host;
    // Poétrï installing from HQ after a steal left the breach more random accesses than cards; and a breaker trashed
    // mid-run (Rising Tide under GAMEDRAGON™ Pro) kept its run-long pumps.
    [InlineData("elevation-auco-clone-vats~elevation-topan-street-gang", 11636590359662923524UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    [InlineData("elevation-auco-clone-vats~elevation-topan-street-gang", 1229562783193833075UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    [InlineData("elevation-auco-clone-vats~elevation-topan-street-gang", 3378684722239971413UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    [InlineData("elevation-bangun-open-house~elevation-topan-street-gang", 4919813262478015024UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    [InlineData("elevation-bangun-open-house~elevation-topan-street-gang", 10980545085324381808UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    [InlineData("elevation-leo-bioroid-wall~elevation-ryo-phoenix-fire", 11044153108927217858UL, FuzzAgentKind.Heuristic, FuzzAgentKind.RandomLegal)]
    [InlineData("elevation-leo-bioroid-wall~elevation-topan-street-gang", 571583689266804722UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    [InlineData("elevation-leo-bioroid-wall~elevation-topan-street-gang", 13914290485382154607UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    [InlineData("elevation-poetri-trendsetters~elevation-barry-tri-maf", 3863020783146909950UL, FuzzAgentKind.Heuristic, FuzzAgentKind.RandomLegal)]
    [InlineData("elevation-poetri-trendsetters~elevation-magdalene-cryptarchitect", 8890690608360693331UL, FuzzAgentKind.Heuristic, FuzzAgentKind.RandomLegal)]
    [InlineData("elevation-poetri-trendsetters~elevation-ryo-phoenix-fire", 6169175198764098149UL, FuzzAgentKind.Heuristic, FuzzAgentKind.RandomLegal)]
    [InlineData("elevation-poetri-trendsetters~elevation-topan-street-gang", 12093226971442364802UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    [InlineData("elevation-poetri-trendsetters~elevation-topan-street-gang", 17143733166459115242UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.RandomLegal)]
    [InlineData("elevation-poetri-trendsetters~elevation-topan-street-gang", 1251392891413117106UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    [InlineData("elevation-synapse-fast-news~elevation-dewi-shadow-play", 9844574047002420382UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    [InlineData("elevation-untaian-silk-road~elevation-dewi-shadow-play", 2655089528289181646UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.RandomLegal)]
    [InlineData("elevation-untaian-silk-road~elevation-topan-street-gang", 3595185979301660066UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    [InlineData("elevation-untaian-silk-road~elevation-topan-street-gang", 16403581370382204714UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    [InlineData("elevation-zwicky-shell-companies~elevation-dewi-shadow-play", 4005004959134538068UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.RandomLegal)]
    [InlineData("elevation-zwicky-shell-companies~elevation-magdalene-cryptarchitect", 4962536658878489839UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    [InlineData("elevation-zwicky-shell-companies~elevation-topan-street-gang", 8983391476979939617UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.RandomLegal)]
    [InlineData("elevation-zwicky-shell-companies~elevation-topan-street-gang", 16270335162777857310UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.RandomLegal)]
    [InlineData("elevation-zwicky-shell-companies~elevation-topan-street-gang", 987762550797513125UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    [InlineData("elevation-zwicky-shell-companies~elevation-topan-street-gang", 3384548798248417848UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    [InlineData("elevation-zwicky-shell-companies~elevation-topan-street-gang", 17333548461353161807UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.RandomLegal)]
    public async Task TheGameThatExposedTheDefectFinishes(string contentId, ulong seed, FuzzAgentKind corp, FuzzAgentKind runner)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        FuzzGameResult result = await RulesFuzzRunner.RunAsync(new FuzzGameCase("regression", contentId, seed, corp, runner));
        Assert.False(result.IsDefect, $"{contentId} seed {seed}: {result.Status} {result.Message}");
    }
}

using Netrunner.Cards;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>Elevation ladder games that once failed, replayed by seed (official data only).</summary>
public sealed class ElevationLadderRegressionTests
{
    [Theory]
    // EL.7 ladder: the contract agent rejected N-Pot's credit-cost break as malformed.
    [InlineData("standard-vs-contract-elevation-sim-synapse-corp~elevation-sim-dewi-runner-001", "standard", "contract")]
    [InlineData("easy-vs-contract-elevation-sim-leo-corp~elevation-sim-dewi-runner-001", "easy", "contract")]
    // EL.7 ladder: a resolution frame and the pending decision disagreed.
    [InlineData("easy-vs-random-elevation-sim-leo-corp~elevation-sim-zahya-runner-001", "easy", "random")]
    public async Task FailedLadderGamesFinish(string id, string corp, string runner)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        string content = id[(id.IndexOf("elevation-sim-", StringComparison.Ordinal))..^4];
        var game = new LadderGame(id, content, 17050827162618642909, corp, runner);

        LadderGameResult result = await RatingLadder.PlayAsync(game);

        Assert.True(result.Error is null, result.Error);
    }

    [Theory]
    // EL.8 deck-pool ladder (gateway-elevation-v1): Proprionegation moving the Runner from the server itself, breach
    // modifiers left on the old attacked server, run events played from Bling's hosted cards, and a nested frame.
    [InlineData("elevation-auco-clone-vats~elevation-dewi-shadow-play", 1388093174763754486UL, "standard", "easy")]
    [InlineData("elevation-auco-clone-vats~elevation-magdalene-cryptarchitect", 17556307665739472111UL, "standard", "standard")]
    [InlineData("elevation-auco-clone-vats~elevation-muslihat-market-runs", 1083063190161792685UL, "standard", "easy")]
    [InlineData("elevation-auco-clone-vats~elevation-muslihat-market-runs", 7677668395478051093UL, "standard", "easy")]
    [InlineData("elevation-auco-clone-vats~elevation-topan-street-gang", 12388372526157219937UL, "easy", "standard")]
    [InlineData("elevation-nebula-headliners~elevation-topan-street-gang", 12817519384413900442UL, "standard", "easy")]
    [InlineData("elevation-poetri-trendsetters~elevation-ryo-phoenix-fire", 11145531781909109915UL, "standard", "easy")]
    [InlineData("elevation-poetri-trendsetters~elevation-topan-street-gang", 6792651836792244621UL, "easy", "easy")]
    [InlineData("elevation-synapse-fast-news~elevation-topan-street-gang", 9015723799811212650UL, "standard", "standard")]
    [InlineData("elevation-untaian-silk-road~elevation-barry-tri-maf", 5214384911678184470UL, "easy", "easy")]
    [InlineData("elevation-untaian-silk-road~elevation-magdalene-cryptarchitect", 9952235901236162495UL, "easy", "easy")]
    [InlineData("elevation-untaian-silk-road~elevation-magdalene-cryptarchitect", 9952235901236162495UL, "easy", "standard")]
    [InlineData("elevation-untaian-silk-road~elevation-muslihat-market-runs", 15510371961856622496UL, "easy", "easy")]
    [InlineData("elevation-untaian-silk-road~elevation-muslihat-market-runs", 15948314377339988856UL, "standard", "easy")]
    [InlineData("elevation-untaian-silk-road~elevation-muslihat-market-runs", 7973525229042040635UL, "standard", "easy")]
    [InlineData("elevation-untaian-silk-road~elevation-ryo-phoenix-fire", 10590750841635654863UL, "standard", "easy")]
    [InlineData("elevation-untaian-silk-road~elevation-topan-street-gang", 1207069443661085420UL, "standard", "standard")]
    public async Task FailedDeckPoolLadderGamesFinish(string content, ulong seed, string corp, string runner)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        LadderGameResult result = await RatingLadder.PlayAsync(new LadderGame("regression", content, seed, corp, runner));
        Assert.True(result.Error is null, $"{content} seed {seed}: {result.Error}");
    }
}

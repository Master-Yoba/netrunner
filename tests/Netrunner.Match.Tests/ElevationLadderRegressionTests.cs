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
}

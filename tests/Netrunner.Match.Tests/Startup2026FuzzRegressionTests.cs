using Netrunner.Cards;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>VP.8: games of the 10,000-game Startup 2026 fuzz that exposed a defect, replayed by seed (official data only).</summary>
public sealed class Startup2026FuzzRegressionTests
{
    [Theory]
    // Ryō's trigger trashed HQ's last card before Detente's random pick of an HQ card resolved; the pick bound nothing and
    // the host found no binding.
    [InlineData("startup-melies-u~startup-ryo-chain-reaction", 11106448721942459666UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    public async Task TheGameThatExposedTheDefectFinishes(string contentId, ulong seed, FuzzAgentKind corp, FuzzAgentKind runner)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        FuzzGameResult result = await RulesFuzzRunner.RunAsync(new FuzzGameCase("regression", contentId, seed, corp, runner));
        Assert.False(result.IsDefect, $"{contentId} seed {seed}: {result.Status} {result.Message}");
    }
}

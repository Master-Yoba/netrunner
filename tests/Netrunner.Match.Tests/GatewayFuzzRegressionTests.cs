using Netrunner.Cards;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// Rules defects the first fuzz over the Gateway sample decks found (Wave 4 C1),
/// kept as the exact fuzz games that exposed them. Each must now finish.
/// </summary>
public sealed class GatewayFuzzRegressionTests
{
    [Theory]
    // Public Trail's tag fires NBN: Reality Plus mid-operation; the validator rejected the nested checkpoint.
    [InlineData("gateway-hyper-velocity~gateway-party-hard", 11377507487361925694UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    // René "Loup" Arcemont's trash trigger was stranded beneath the next breach prompt.
    [InlineData("gateway-discretion-advised~gateway-party-hard", 14841925679280041817UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    // Pantograph installing a second Pantograph trashed its own resolving source.
    [InlineData("gateway-advanced-yomi~gateway-planning-ahead", 13468889593859949866UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.RandomLegal)]
    // The validator offered Pantograph a Trojan with no ICE to host it.
    [InlineData("gateway-advanced-yomi~gateway-planning-ahead", 14884431460595441018UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.RandomLegal)]
    // Longevity Serum and Spin Doctor let a selection grow past its "up to" limit.
    [InlineData("gateway-advanced-yomi~gateway-planning-ahead", 148751881476400987UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    [InlineData("gateway-advanced-yomi~gateway-planning-ahead", 538563701993816731UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    // Ansel 1.0 trashing a pumped Unity left its encounter boost behind.
    [InlineData("gateway-discretion-advised~gateway-planning-ahead", 16778342870047427233UL, FuzzAgentKind.Heuristic, FuzzAgentKind.RandomLegal)]
    public async Task TheGameThatExposedTheDefectFinishes(string contentId, ulong seed, FuzzAgentKind corp, FuzzAgentKind runner)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        FuzzGameResult result = await RulesFuzzRunner.RunAsync(new FuzzGameCase("regression", contentId, seed, corp, runner));
        Assert.False(result.IsDefect, $"{contentId} seed {seed}: {result.Status} {result.Message}");
    }
}

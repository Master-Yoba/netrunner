using Netrunner.Cards;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>EL.9: games of the 10,000-game NSG Core Sets fuzz that exposed a defect, replayed by seed (official data only).</summary>
public sealed class NsgCoreSetsFuzzRegressionTests
{
    [Theory]
    // The Zwicky Group's first credit gain through an agenda (Greenmail forfeited to rez Biawak) opened mid-run and
    // failed the run's stack topology; Plutus played Peer Review, whose install replaced Plutus; and Byte!, accessed
    // on top of R&D, was drawn by NBN: Reality Plus (triggered by Byte!'s own tag) before its net damage.
    [InlineData("nsg-core-quick-returns~nsg-core-flow-and-ebb", 3441742449212415182UL, FuzzAgentKind.Heuristic, FuzzAgentKind.RandomLegal)]
    [InlineData("nsg-core-quick-returns~nsg-core-sabbatical", 10050724854047656706UL, FuzzAgentKind.Heuristic, FuzzAgentKind.RandomLegal)]
    [InlineData("nsg-core-quick-returns~nsg-core-shootin-n-lootin", 17721227687297174739UL, FuzzAgentKind.Heuristic, FuzzAgentKind.RandomLegal)]
    [InlineData("nsg-core-quick-returns~nsg-core-prick-thyself", 9450625933511382347UL, FuzzAgentKind.Heuristic, FuzzAgentKind.RandomLegal)]
    [InlineData("nsg-core-quick-returns~nsg-core-professional-opportunities", 17175487923115488279UL, FuzzAgentKind.Heuristic, FuzzAgentKind.RandomLegal)]
    [InlineData("nsg-core-quick-returns~nsg-core-enthusiasm", 14475407379319754401UL, FuzzAgentKind.Heuristic, FuzzAgentKind.RandomLegal)]
    [InlineData("nsg-core-quick-returns~nsg-core-dashing-mad", 13853562064350498139UL, FuzzAgentKind.Heuristic, FuzzAgentKind.RandomLegal)]
    [InlineData("nsg-core-quick-returns~nsg-core-tickets-please", 15037807454944362291UL, FuzzAgentKind.Heuristic, FuzzAgentKind.RandomLegal)]
    [InlineData("nsg-core-quick-returns~nsg-core-bowel-movements", 8413407269838326215UL, FuzzAgentKind.Heuristic, FuzzAgentKind.RandomLegal)]
    [InlineData("nsg-core-fine-print~nsg-core-bowel-movements", 2042405473867437932UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.RandomLegal)]
    [InlineData("nsg-core-not-so-subtle~nsg-core-enthusiasm", 8647145671968970146UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.RandomLegal)]
    [InlineData("nsg-core-quick-returns~nsg-core-tickets-please", 11687915115610819955UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    [InlineData("nsg-core-fine-print~nsg-core-sabbatical", 6251565761047188973UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.RandomLegal)]
    [InlineData("nsg-core-fine-print~nsg-core-sabbatical", 4647442965316174684UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    [InlineData("nsg-core-fine-print~nsg-core-bowel-movements", 12530288529299492218UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    [InlineData("nsg-core-not-so-subtle~nsg-core-professional-opportunities", 8199987754339053749UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    [InlineData("nsg-core-fine-print~nsg-core-sabbatical", 6247761292894317160UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.RandomLegal)]
    [InlineData("nsg-core-fine-print~nsg-core-flow-and-ebb", 4567871192709096215UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    [InlineData("nsg-core-not-so-subtle~nsg-core-flow-and-ebb", 10153004222983936060UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    [InlineData("nsg-core-not-so-subtle~nsg-core-flow-and-ebb", 13250214131795763862UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.RandomLegal)]
    [InlineData("nsg-core-fine-print~nsg-core-enthusiasm", 17759537338756356491UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    [InlineData("nsg-core-quick-returns~nsg-core-enthusiasm", 13447816612695728525UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.RandomLegal)]
    [InlineData("nsg-core-quick-returns~nsg-core-tickets-please", 9296468197115880915UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    [InlineData("nsg-core-fine-print~nsg-core-flow-and-ebb", 3860866489297875286UL, FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic)]
    public async Task TheGameThatExposedTheDefectFinishes(string contentId, ulong seed, FuzzAgentKind corp, FuzzAgentKind runner)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        FuzzGameResult result = await RulesFuzzRunner.RunAsync(new FuzzGameCase("regression", contentId, seed, corp, runner));
        Assert.False(result.IsDefect, $"{contentId} seed {seed}: {result.Status} {result.Message}");
    }
}

using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;

using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// VP.3's misplay sweep (Vantage Point progress, VP.3): plays the heuristic bots were offered on the batch's ladder and
/// never took, each checked at a saved decision of that ladder. Official content only.
/// </summary>
public sealed class VantagePointBotTests
{
    private static async Task<(LegalAction Chosen, GameState State)> ChooseAsync(string fixture, long sequence, PlayerSide side) =>
        await BotLoop1Tests.ChooseWithStateAtAsync(fixture, side == PlayerSide.Corp ? CorpHeuristicPolicy.PolicyVersion : RunnerHeuristicPolicy.PolicyVersion,
            sequence, side);

    private static string? Card(GameState state, LegalAction action) => action.Parameters switch
    {
        RezCardParameters rez => state.GetCard(rez.CardId).DefinitionId.Value,
        PlayCardParameters play => state.GetCard(play.CardId).DefinitionId.Value,
        AbilityActionParameters ability => state.GetCard(ability.SourceCardId).DefinitionId.Value,
        _ => null,
    };

    [Theory]
    // Nihilo Agent's bad publicity feeds Editorial Division's search; Magistrate Revontulet taxes steals and scores.
    [InlineData("vp3-editorial-assets", 77, "36049")]
    [InlineData("vp3-editorial-assets", 181, "36048")]
    // Synchrocyclotron with a Double operation in HQ.
    [InlineData("vp3-synchrocyclotron", 192, "36027")]
    public async Task TheCorpRezzesAssetsThatWorkRezzed(string fixture, long sequence, string code)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseAsync(fixture, sequence, PlayerSide.Corp);
        Assert.True(chosen.Kind == ActionKind.RezCard && Card(state, chosen) == code, $"chose {chosen.Kind} {chosen.Description}.");
    }

    [Fact]
    public async Task TheCorpPlaysFloodTheMarketWithProtectedRemotesToCount()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseAsync("vp3-flood-the-market", 1161, PlayerSide.Corp);
        Assert.True(chosen.Kind == ActionKind.PlayCard && Card(state, chosen) == "36053", $"chose {chosen.Kind} {chosen.Description}.");
    }

    [Fact]
    public async Task TheRunnerLowersABarrierWithCorsairsStealthCredits()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseAsync("vp3-corsair", 879, PlayerSide.Runner);
        Assert.True(chosen.Kind == ActionKind.UseAbility && Card(state, chosen) == "36004", $"chose {chosen.Kind} {chosen.Description}.");
    }
}

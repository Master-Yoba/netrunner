using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;

using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// The misplay sweeps of VP.3 and VP.5 (Vantage Point progress): plays the heuristic bots were offered on a batch's ladder
/// and never took, each checked at a saved decision of VP.5's ladder (VP.3's games were recorded on the VP.3 catalog,
/// which the Startup pool no longer plays). Official content only.
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
        InstallCardParameters install => state.GetCard(install.CardId).DefinitionId.Value,
        _ => null,
    };

    [Theory]
    // Nihilo Agent's bad publicity feeds Editorial Division's search; Magistrate Revontulet taxes steals and scores.
    [InlineData("vp5-editorial-assets", 113, "36049")]
    [InlineData("vp5-editorial-assets", 63, "36048")]
    // Synchrocyclotron with a Double operation in HQ.
    [InlineData("vp5-synchrocyclotron", 329, "36027")]
    // VP.5: Flagship, a no-success upgrade, read as a scoring upgrade and never rezzed.
    [InlineData("vp5-flagship", 758, "36064")]
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
        (LegalAction chosen, GameState state) = await ChooseAsync("vp5-flood-the-market", 597, PlayerSide.Corp);
        Assert.True(chosen.Kind == ActionKind.PlayCard && Card(state, chosen) == "36053", $"chose {chosen.Kind} {chosen.Description}.");
    }

    [Fact]
    public async Task TheRunnerLowersABarrierWithCorsairsStealthCredits()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseAsync("vp5-corsair", 469, PlayerSide.Runner);
        Assert.True(chosen.Kind == ActionKind.UseAbility && Card(state, chosen) == "36004", $"chose {chosen.Kind} {chosen.Description}.");
    }

    // VP.5: run events whose value is past the run (Kompromat's bad publicity, Beta Build's program, Aircheck's run after the
    // run), Baker's install and its redirected run.
    [Theory]
    [InlineData("vp5-kompromat", 100, ActionKind.PlayCard, "36010")]
    [InlineData("vp5-beta-build", 29, ActionKind.PlayCard, "36019")]
    [InlineData("vp5-aircheck", 23, ActionKind.PlayCard, "36018")]
    [InlineData("vp5-baker-install", 193, ActionKind.InstallCard, "36015")]
    [InlineData("vp5-baker", 460, ActionKind.UseAbility, "36015")]
    public async Task TheRunnerPlaysVantagePointRunCards(string fixture, long sequence, ActionKind kind, string code)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseAsync(fixture, sequence, PlayerSide.Runner);
        Assert.True(chosen.Kind == kind && Card(state, chosen) == code, $"chose {chosen.Kind} {chosen.Description}.");
    }
}

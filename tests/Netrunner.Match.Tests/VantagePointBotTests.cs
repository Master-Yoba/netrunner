using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;

using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// The misplay sweeps of VP.3, VP.5 and VP.7 (Vantage Point progress): plays the heuristic bots were offered on a batch's
/// ladder and never took, each checked at a saved decision of VP.7's ladder (earlier ladders were recorded on catalogs the
/// Startup pool no longer plays). Official content only.
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
    [InlineData("vp7-editorial-assets", 109, "36049")]
    [InlineData("vp7-editorial-assets", 334, "36048")]
    // Synchrocyclotron with a Double operation in HQ.
    [InlineData("vp7-synchrocyclotron", 312, "36027")]
    // VP.5: Flagship, a no-success upgrade, read as a scoring upgrade and never rezzed.
    [InlineData("vp7-flagship", 434, "36064")]
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
        (LegalAction chosen, GameState state) = await ChooseAsync("vp7-flood-the-market", 1211, PlayerSide.Corp);
        Assert.True(chosen.Kind == ActionKind.PlayCard && Card(state, chosen) == "36053", $"chose {chosen.Kind} {chosen.Description}.");
    }

    // VP.5: run events whose value is past the run (Kompromat's bad publicity, Beta Build's program, Aircheck's run after the
    // run), Baker's install and its redirected run.
    [Theory]
    [InlineData("vp7-kompromat", 484, ActionKind.PlayCard, "36010")]
    [InlineData("vp7-beta-build", 29, ActionKind.PlayCard, "36019")]
    [InlineData("vp7-aircheck", 23, ActionKind.PlayCard, "36018")]
    [InlineData("vp7-baker-install", 32, ActionKind.InstallCard, "36015")]
    [InlineData("vp7-baker", 184, ActionKind.UseAbility, "36015")]
    public async Task TheRunnerPlaysVantagePointRunCards(string fixture, long sequence, ActionKind kind, string code)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseAsync(fixture, sequence, PlayerSide.Runner);
        Assert.True(chosen.Kind == kind && Card(state, chosen) == code, $"chose {chosen.Kind} {chosen.Description}.");
    }

    // VP.7: Cultivate's choice among R&D's top cards, and Nurse Hạnh's draws.
    [Fact]
    public async Task TheCorpPlaysCultivate()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseAsync("vp7-cultivate", 202, PlayerSide.Corp);
        Assert.True(chosen.Kind == ActionKind.PlayCard && Card(state, chosen) == "36043", $"chose {chosen.Kind} {chosen.Description}.");
    }

    [Fact]
    public async Task TheRunnerInstallsNurseHanh()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseAsync("vp7-nurse-hanh", 405, PlayerSide.Runner);
        Assert.True(chosen.Kind == ActionKind.InstallCard && Card(state, chosen) == "36007", $"chose {chosen.Kind} {chosen.Description}.");
    }
}

using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;

using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// The misplay sweeps of VP.3, VP.5, VP.7 and VP.8 (Vantage Point progress): plays the heuristic bots were offered on a
/// ladder and never took, each checked at a saved decision of VP.8's ladders, on the simulation decks and on Startup 2026's
/// (earlier ladders were recorded on catalogs the Startup pool no longer plays). Official content only.
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
    [InlineData("vp8-editorial-assets", 109, "36049")]
    [InlineData("vp8-editorial-assets", 334, "36048")]
    // Synchrocyclotron with a Double operation in HQ.
    [InlineData("vp8-synchrocyclotron", 312, "36027")]
    // VP.5: Flagship, a no-success upgrade, read as a scoring upgrade and never rezzed.
    [InlineData("vp8-flagship", 434, "36064")]
    // VP.8: Nihilo Agent's tag each turn in Reality Plus's tag deck, without Editorial Division's search.
    [InlineData("s26-nihilo-agent", 15, "36049")]
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
        (LegalAction chosen, GameState state) = await ChooseAsync("vp8-flood-the-market", 1211, PlayerSide.Corp);
        Assert.True(chosen.Kind == ActionKind.PlayCard && Card(state, chosen) == "36053", $"chose {chosen.Kind} {chosen.Description}.");
    }

    // VP.5: run events whose value is past the run (Kompromat's bad publicity, Beta Build's program, Aircheck's run after the
    // run), Baker's install and its redirected run.
    [Theory]
    [InlineData("vp8-kompromat", 442, ActionKind.PlayCard, "36010")]
    [InlineData("vp8-beta-build", 29, ActionKind.PlayCard, "36019")]
    [InlineData("vp8-aircheck", 23, ActionKind.PlayCard, "36018")]
    [InlineData("vp8-baker-install", 32, ActionKind.InstallCard, "36015")]
    [InlineData("vp8-baker", 184, ActionKind.UseAbility, "36015")]
    // VP.8: The Tungsten Tailor's lower ICE strength, on the simulation decks and in Startup 2026's MuslihaT.
    [InlineData("vp8-tungsten-tailor", 452, ActionKind.InstallCard, "36003")]
    [InlineData("s26-tungsten-tailor", 517, ActionKind.InstallCard, "36003")]
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
        (LegalAction chosen, GameState state) = await ChooseAsync("vp8-cultivate", 202, PlayerSide.Corp);
        Assert.True(chosen.Kind == ActionKind.PlayCard && Card(state, chosen) == "36043", $"chose {chosen.Kind} {chosen.Description}.");
    }

    [Fact]
    public async Task TheRunnerInstallsNurseHanh()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseAsync("vp8-nurse-hanh", 969, PlayerSide.Runner);
        Assert.True(chosen.Kind == ActionKind.InstallCard && Card(state, chosen) == "36007", $"chose {chosen.Kind} {chosen.Description}.");
    }

    // VP.8: the client presents Méliès U's secret copy to the Corp alone, and cards hosted face down on Read-Write Share as
    // backs to the Corp and as the Runner's own cards to the Runner (a VP.8 game of Méliès U against Hiram).
    [Fact]
    public void TheClientShowsTheSecretCopyAndFaceDownHostedCardsToTheirOwnersAlone()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (ReplayArtifact replay, FullMatchDefinition definition) = SavedDecision.ReadSaved(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "saved-games", "vp8-melies-read-write-share"));
        bool secret = false, hosted = false;
        foreach (Netrunner.Match.Regret.RegretPosition position in SavedDecision.Positions(replay, definition))
        {
            GameState state = position.State;
            MatchViewModel corp = MatchViewModelBuilder.Build(ObservationBuilder.Build(state, PlayerSide.Corp, definition.Behavior, definition.Runtime));
            MatchViewModel runner = MatchViewModelBuilder.Build(ObservationBuilder.Build(state, PlayerSide.Runner, definition.Behavior, definition.Runtime));
            if (state.RulesState?.SecretIdentity is { } identity)
            {
                Assert.Equal(identity.Side.ToString(), corp.SecretIdentitySide);
                Assert.Null(runner.SecretIdentitySide);
                Assert.Equal(identity.Flipped ? identity.Side.ToString() : null, runner.Corp.IdentitySide);
                secret = true;
            }

            if (runner.HostedFaceDown.Count > 0)
            {
                Assert.Equal(runner.HostedFaceDown.Select(static card => card.Id), corp.HostedFaceDown.Select(static card => card.Id));
                Assert.All(runner.HostedFaceDown, static card => Assert.True(card.IsKnown));
                Assert.All(corp.HostedFaceDown, static card => Assert.False(card.IsKnown));
                hosted = true;
            }
        }

        Assert.True(secret, "No position held Méliès U's secret copy.");
        Assert.True(hosted, "No position held a card hosted face down.");
    }
}

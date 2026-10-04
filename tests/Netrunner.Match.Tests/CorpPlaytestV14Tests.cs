using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// Playtest, 29 September (Runner, Stolen Goods, against Discretion Advised): Corp v13 rezzed Nico
/// Campaign in its own action phase, so it sat rezzed through the Runner's whole turn although its
/// money only comes when the Corp's turn begins. Corp v14 is asked at that position of the recorded game.
/// </summary>
public sealed class CorpPlaytestV14Tests
{
    private const string Fixture = "playtest-nico-rez";
    private const string Content = "gateway-discretion-advised~gateway-stolen-goods";
    private const ulong Seed = 4775946684322023204;
    private const long NicoRez = 53;
    private const string Nico = "30037";

    [Theory]
    [InlineData(CorpHeuristicPolicy.V13, true)]
    [InlineData(CorpHeuristicPolicy.V14, false)]
    public async Task NicoCampaignWaitsForTheEndOfTheRunnersTurn(string version, bool rezzes)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseAtAsync(version, NicoRez);
        bool rezzedNico = chosen.Parameters is RezCardParameters rez && state.GetCard(rez.CardId).DefinitionId.Value == Nico;
        Assert.True(rezzes == rezzedNico, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    private static async Task<(LegalAction Chosen, GameState State)> ChooseAtAsync(string version, long sequence)
    {
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(Content, Seed);
        ReplayArtifact replay = SavedDecision.ReadReplay(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", $"{Fixture}-replay.json"), definition);
        SavedChoice choice = await SavedDecision.ChooseAsync(replay, definition, sequence, PlayerSide.Corp, version);
        return (choice.Chosen, choice.State);
    }
}

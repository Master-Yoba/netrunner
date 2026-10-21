using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// Playtest, 30 September (Runner, Stolen Goods, against Discretion Advised): on its first turn Corp
/// v17 put Palisade on R&amp;D while Send a Message and Luminal Transubstantiation waited in an open HQ,
/// and on its second it put Seamless Launch's two counters on Palisade, which ignores them, over an
/// Urtica Cipher they would have armed. Corp v18 is asked at both positions of the recorded game.
/// </summary>
public sealed class CorpPlaytestV18Tests
{
    private const string Content = "gateway-discretion-advised~gateway-stolen-goods";
    private const ulong Seed = 16353449718733399204;
    private const string Headquarters = "server:4";
    private const string UrticaCipher = "30045";

    [Theory]
    [InlineData(CorpHeuristicPolicy.V17, false)]
    [InlineData(CorpHeuristicPolicy.V18, true)]
    public async Task IceGoesOnAnHqHoldingAgendas(string version, bool onHq)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, _) = await ChooseAtAsync(version, 8);
        bool hq = chosen.Parameters is InstallCardParameters { DestinationKind: InstallDestinationKind.ServerIce } install
            && install.ServerId?.Value == Headquarters;
        Assert.True(onHq == hq, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V18)]
    public async Task SeamlessLaunchArmsTheTrap(string version)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseAtAsync(version, 54);
        var choice = (BehaviorChoiceParameters)chosen.Parameters;
        Assert.Equal(UrticaCipher, state.GetCard(choice.CardIds.Single()).DefinitionId.Value);
    }

    private static async Task<(LegalAction Chosen, GameState State)> ChooseAtAsync(string version, long sequence)
    {
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(Content, Seed);
        ReplayArtifact replay = SavedDecision.ReadReplay(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "playtest-central-ice-replay.json"), definition);
        SavedChoice choice = await SavedDecision.ChooseAsync(replay, definition, sequence, PlayerSide.Corp, version);
        return (choice.Chosen, choice.State);
    }
}

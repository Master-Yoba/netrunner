using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// Bot plan H3 batch 13: Runner v36 values a draw engine over the turns left. Verbal Plasticity was worth three turns of
/// its extra card, so Topan and Magdalene installed it 0.4 times a game from three copies.
/// </summary>
public sealed class RunnerDrawEngineV36Tests
{
    /// <summary>A box ladder game, LEO Glacier against Topan, action 80 on turn 2: v35 passes Verbal Plasticity over, v36 installs it.</summary>
    [Theory]
    [InlineData(RunnerHeuristicPolicy.V35, false)]
    [InlineData(RunnerHeuristicPolicy.V36, true)]
    public async Task VerbalPlasticityIsInstalledEarly(string version, bool installs)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (ReplayArtifact replay, FullMatchDefinition definition) = SavedDecision.ReadSaved(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures",
            "saved-games", "runner-draw-engine-early"));
        SavedChoice choice = await SavedDecision.ChooseAsync(replay, definition, 80, PlayerSide.Runner, version);
        bool verbal = choice.Chosen.Parameters is InstallCardParameters install && choice.State.GetCard(install.CardId).DefinitionId.Value == "30034";
        Assert.True(installs == verbal, $"{version} chose {choice.Chosen.Kind} {choice.Chosen.Description}.");
    }
}

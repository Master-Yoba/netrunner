using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// Bot plan H3 batch 11: Runner v35 prices Madani's hosting at the clicks it saves. Magdalene hosted 7.6 times a game and
/// installed from Madani once, so most hosted breakers never reached the rig.
/// </summary>
public sealed class RunnerMadaniV35Tests
{
    /// <summary>
    /// A box ladder game, LEO Glacier against Magdalene, action 273: one program in the grip. Hosting it costs the click an
    /// install would, and Madani's free install brings out one a turn; v32 hosts it, v35 doesn't.
    /// </summary>
    [Theory]
    [InlineData(RunnerHeuristicPolicy.V32, true)]
    [InlineData(RunnerHeuristicPolicy.V35, false)]
    public async Task OneProgramIsNotHostedOnMadani(string version, bool hosts)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (ReplayArtifact replay, FullMatchDefinition definition) = SavedDecision.ReadSaved(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures",
            "saved-games", "runner-madani-single-host"));
        SavedChoice choice = await SavedDecision.ChooseAsync(replay, definition, 273, PlayerSide.Runner, version);
        Assert.True(hosts == choice.Chosen.Description.Contains("madani-host", StringComparison.Ordinal),
            $"{version} chose {choice.Chosen.Kind} {choice.Chosen.Description}.");
    }
}

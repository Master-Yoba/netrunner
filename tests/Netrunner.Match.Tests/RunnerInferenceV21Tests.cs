using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// A9b: Runner v21 infers from what it has seen. Each case is a ladder game where v20 and v21 decide differently for the
/// reason the user's design notes give (2 October).
/// </summary>
public sealed class RunnerInferenceV21Tests
{
    [Theory]
    [InlineData(RunnerHeuristicPolicy.V20, false)]
    [InlineData(RunnerHeuristicPolicy.V21, true)]
    public async Task ATagIsClearedOnceARetributionIsSeenInHq(string version, bool clears)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // The Runner accessed Retribution in HQ on turn 2, and the tracker still holds it there on turn 4 (it is). Tagged, v20
        // runs R&D; v21 clears the tag, since the Corp can pay for Retribution and trash a program or piece of hardware.
        (LegalAction chosen, _) = await ChooseAtAsync("a9-retribution-seen", version, 182);
        Assert.True(clears == (chosen.Kind == ActionKind.RemoveTag), $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V20, true)]
    [InlineData(RunnerHeuristicPolicy.V21, false)]
    public async Task AFaceDownCardSeenAsAnUrticaIsNotRunIntoAsIfUnknown(string version, bool runs)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // The tracker puts the remote's face-down root at 69% Urtica Cipher, from the Urtica it saw in HQ; it is the Urtica.
        (LegalAction chosen, GameState state) = await ChooseAtAsync("a9-urtica-seen", version, 613);
        bool remote = chosen.Parameters is RunServerParameters run && state.Servers.First(server => server.Id == run.ServerId).Kind == ServerKind.Remote;
        Assert.True(runs == remote, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V20, true)]
    [InlineData(RunnerHeuristicPolicy.V21, false)]
    public async Task FaceDownIceSeenAsAWhitespaceIsPlannedAsOne(string version, bool runs)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // HQ's face-down ICE is 69% Whitespace to the tracker (it is), and the Runner has no decoder: planned as a Whitespace
        // the Corp can rez, the run can't get in. v20 runs it as unknown ICE.
        (LegalAction chosen, GameState state) = await ChooseAtAsync("a9-whitespace-seen", version, 162);
        bool hq = chosen.Parameters is RunServerParameters run && state.Servers.First(server => server.Id == run.ServerId).Kind == ServerKind.Headquarters;
        Assert.True(runs == hq, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    private static async Task<(LegalAction Chosen, GameState State)> ChooseAtAsync(string fixture, string version, long sequence)
    {
        (ReplayArtifact replay, FullMatchDefinition definition) = SavedDecision.ReadSaved(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "saved-games", fixture));
        SavedChoice choice = await SavedDecision.ChooseAsync(replay, definition, sequence, PlayerSide.Runner, version);
        return (choice.Chosen, choice.State);
    }
}

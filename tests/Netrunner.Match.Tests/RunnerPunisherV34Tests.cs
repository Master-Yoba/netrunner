using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// Bot plan H3 batch 10: Runner v34 against the first Measured Response. 92% of the Runner's flatlines to it came from a
/// copy it hadn't seen, so v26's grip guard never woke.
/// </summary>
public sealed class RunnerPunisherV34Tests
{
    /// <summary>
    /// A box ladder game, Zwicky Supermodernism (Weyland, three Measured Response) against Magdalene. At threat 4, after a
    /// successful run, the Runner holds four cards with two clicks and 1 credit. v32 plays Creative Commission, which also
    /// loses the last click, ends the turn on three cards and dies to Measured Response; v37 (with v34's rules) reads Measured
    /// Response in the Weyland identity's faction and keeps a click to draw back to four.
    /// </summary>
    [Theory]
    [InlineData(RunnerHeuristicPolicy.V32, true)]
    [InlineData(RunnerHeuristicPolicy.V37, false)]
    public async Task TheGripStaysAboveMeasuredResponseInAWeylandGame(string version, bool plays)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (ReplayArtifact replay, FullMatchDefinition definition) = SavedDecision.ReadSaved(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures",
            "saved-games", "runner-faction-punisher-zwicky"));
        SavedChoice choice = await SavedDecision.ChooseAsync(replay, definition, 379, PlayerSide.Runner, version);
        Assert.True(plays == (choice.Chosen.Kind == ActionKind.PlayCard), $"{version} chose {choice.Chosen.Kind} {choice.Chosen.Description}.");
    }

    /// <summary>
    /// Runner v38 (MATCHUP_PT_UNTAIAN.md's Topan knob): a box ladder game, LEO Glacier against Topan, action 476, the last
    /// click with two cards in the grip. Topan's identity install takes one and its meat damage the other, ending the turn
    /// with no grip; v37 does it, v38 doesn't.
    /// </summary>
    [Theory]
    [InlineData(RunnerHeuristicPolicy.V37, true)]
    [InlineData(RunnerHeuristicPolicy.V38, false)]
    public async Task TopanKeepsACardAtTheTurnsEnd(string version, bool empties)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (ReplayArtifact replay, FullMatchDefinition definition) = SavedDecision.ReadSaved(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures",
            "saved-games", "runner-topan-empty-grip"));
        SavedChoice choice = await SavedDecision.ChooseAsync(replay, definition, 476, PlayerSide.Runner, version);
        Assert.True(empties == choice.Chosen.Parameters is AbilityActionParameters { AbilityKey: "topan-install" },
            $"{version} chose {choice.Chosen.Kind} {choice.Chosen.Description}.");
    }
}

using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// Playtest, 28 September (Runner against Quick and Dirty): Corp v9 spawned
/// empty ICE'd remotes, installed Malapert Data Vault in HQ (the second copy
/// replacing the first), never rezzed Archives' Funhouse while the Runner ran
/// Archives for money, and never advanced Pharos. Corp v11 is asked at those
/// same positions of the recorded game.
/// </summary>
public sealed class CorpPlaytestV11Tests
{
    [Fact]
    public async Task IceGoesWhereItProtectsSomething()
    {
        if (!Official) return;
        // Turn 4: v9 installed Palisade in a new remote with nothing to put behind it.
        (LegalAction chosen, _) = await ChooseAtAsync(163);
        Assert.False(chosen.Parameters is InstallCardParameters { CreateNewRemote: true }, Describe(chosen));
    }

    [Theory]
    [InlineData(215)]
    [InlineData(439)]
    public async Task MalapertDataVaultNeverGoesIntoHq(long sequence)
    {
        if (!Official) return;
        (LegalAction chosen, GameState state) = await ChooseAtAsync(sequence);
        if (chosen.Parameters is InstallCardParameters install
            && state.GetCard(install.CardId).DefinitionId.Value == "30066")
            Assert.True(install.CreateNewRemote || state.Servers.Single(server => server.Id == install.ServerId).Kind == ServerKind.Remote,
                Describe(chosen));
    }

    [Theory]
    [InlineData(288)]
    [InlineData(381)]
    public async Task FunhouseIsRezzedOnceTheRunnerFarmsArchives(long sequence)
    {
        if (!Official) return;
        (LegalAction chosen, _) = await ChooseAtAsync(sequence);
        Assert.Equal(ActionKind.RezCard, chosen.Kind);
    }

    [Fact]
    public async Task PharosIsAdvancedWhenTheTurnHasRoom()
    {
        if (!Official) return;
        // Turns 5 and 6: v9 gained credits and drew with Pharos on R&D unadvanced.
        var kinds = new List<string>();
        foreach (long sequence in new long[] { 218, 266, 269, 272 })
        {
            (LegalAction chosen, _) = await ChooseAtAsync(sequence);
            kinds.Add(Describe(chosen));
        }
        Assert.Contains(kinds, kind => kind.StartsWith("AdvanceCard", StringComparison.Ordinal));
    }

    private static bool Official => CardDataResource.EmbeddedMode == CardDataResource.OfficialMode;

    private static string Describe(LegalAction action) => $"{action.Kind} {action.Description}";

    /// <summary>Replays the recorded game to the Corp decision at <paramref name="sequence"/> and asks Corp v11.</summary>
    private static async Task<(LegalAction Chosen, GameState State)> ChooseAtAsync(long sequence)
    {
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition("gateway-quick-and-dirty~gateway-stolen-goods", 8751913655046395946);
        ReplayArtifact replay = SavedDecision.ReadReplay(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "playtest-weyland-remotes-replay.json"), definition);
        SavedChoice choice = await SavedDecision.ChooseAsync(replay, definition, sequence, PlayerSide.Corp, CorpHeuristicPolicy.V11);
        return (choice.Chosen, choice.State);
    }
}

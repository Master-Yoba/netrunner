using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// Bot loop 1's diagnosis (docs/history/BOT_IMPROVEMENT_PLAN.md): on the Core Battle Box, 41% of Standard Runner v22's games were
/// lost to its own choice, because a choice's damage cost nothing. Each fixture is a Battle Box ladder game at the fatal
/// decision.
/// </summary>
public sealed class BotLoop1Tests
{
    [Theory]
    [InlineData(RunnerHeuristicPolicy.V22, true)]
    [InlineData(RunnerHeuristicPolicy.V23, false)]
    public async Task TopanDoesNotInstallItsLastCardIntoAFlatline(string version, bool installs)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // One card in the grip: the install empties it, and Topan's meat damage then flatlines the Runner.
        LegalAction chosen = await ChooseAtAsync("loop1-topan-last-card", version, 140);
        bool topan = chosen.Parameters is AbilityActionParameters { AbilityKey: "topan-install" };
        Assert.True(installs == topan, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V22, "suffer-damage")]
    [InlineData(RunnerHeuristicPolicy.V23, "end-run")]
    public async Task SemakSamunsDamageIsNotTakenWithTooFewCards(string version, string option)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // Two cards in the grip against "end the run unless the Runner suffers 3 net damage".
        LegalAction chosen = await ChooseAtAsync("loop1-semak-samun-damage", version, 120);
        Assert.Equal(option, Assert.Single(Assert.IsType<BehaviorChoiceParameters>(chosen.Parameters).OptionKeys));
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V22, "take-damage")]
    [InlineData(RunnerHeuristicPolicy.V23, "pay-eight")]
    public async Task MeasuredResponseIsPaidRatherThanFlatlining(string version, string option)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // 18 credits and 3 cards in the grip against "do 4 meat damage unless the Runner pays 8".
        LegalAction chosen = await ChooseAtAsync("loop1-measured-response", version, 285);
        Assert.Equal(option, Assert.Single(Assert.IsType<BehaviorChoiceParameters>(chosen.Parameters).OptionKeys));
    }

    // Loop 1's games, rebound to the current catalog: the findings Codex's reviews made and the replays confirmed.

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V22, true)]
    [InlineData(RunnerHeuristicPolicy.V23, false)]
    public async Task TopanIsNotUsedWithOnlyEventsInTheGrip(string version, bool uses)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        LegalAction chosen = await ChooseAtAsync("loop1-005", version, 120);
        Assert.True(uses == chosen.Parameters is AbilityActionParameters { AbilityKey: "topan-install" }, $"{version} chose {chosen.Description}.");
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V22, true)]
    [InlineData(RunnerHeuristicPolicy.V23, false)]
    public async Task MadaniDoesNotHostWithNoProgramInTheGrip(string version, bool hosts)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        LegalAction chosen = await ChooseAtAsync("loop1-007", version, 128);
        Assert.True(hosts == chosen.Parameters is AbilityActionParameters { AbilityKey: "madani-host" }, $"{version} chose {chosen.Description}.");
    }

    [Fact]
    public async Task TheCorpSpringsByteWhenTheRunnerAccessesItFromRAndD()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // Its own trigger's source was hidden from the Corp in R&D, so it valued the trigger blind and declined a lethal Byte!.
        LegalAction chosen = await ChooseAtAsync("loop1-002", CorpHeuristicPolicy.PolicyVersion, 131, PlayerSide.Corp);
        Assert.Equal(ActionKind.UseTrigger, chosen.Kind);
    }

    [Fact]
    public async Task PoetrisScoringInstallFromRAndDIsTaken()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // The cards offered from R&D were hidden from the Corp, so it declined every free install as "unknown metadata".
        LegalAction chosen = await ChooseAtAsync("loop1-101", CorpHeuristicPolicy.PolicyVersion, 401, PlayerSide.Corp);
        Assert.True(chosen.Parameters is BehaviorInstallChoiceParameters { Choice: not null }, $"chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V26, false)]
    [InlineData(CorpHeuristicPolicy.V27, true)]
    public async Task PhatGioanBaotixitaIsRezzedToGatherCounters(string version, bool rezzes)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseWithStateAtAsync("loop1-002", version, 52, PlayerSide.Corp);
        bool phat = chosen.Parameters is RezCardParameters rez && state.GetCard(rez.CardId).DefinitionId.Value == "35051";
        Assert.True(rezzes == phat, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V26, false)]
    [InlineData(CorpHeuristicPolicy.V27, true)]
    public async Task HumanoidResourcesIsRezzedWithTheClicksToUseIt(string version, bool rezzes)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseWithStateAtAsync("loop1-003", version, 167, PlayerSide.Corp);
        bool humanoid = chosen.Parameters is RezCardParameters rez && state.GetCard(rez.CardId).DefinitionId.Value == "35039";
        Assert.True(rezzes == humanoid, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    // Loop 1's deferred findings (docs/history/BOT_LOOP_1.md), fixed in Runner v24 and Corp v28.

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V23, true)]
    [InlineData(RunnerHeuristicPolicy.V24, false)]
    public async Task MutualFavorIsNotPlayedWhenEveryIceTypeIsCovered(string version, bool plays)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseWithStateAtAsync("loop1-006", version, 154, PlayerSide.Runner);
        Assert.True(plays == (chosen.Parameters is PlayCardParameters play && state.GetCard(play.CardId).DefinitionId.Value == "30011"),
            $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V23, true)]
    [InlineData(RunnerHeuristicPolicy.V24, false)]
    public async Task ASecondMarjanahIsNotInstalled(string version, bool installs)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseWithStateAtAsync("loop1-006", version, 251, PlayerSide.Runner);
        Assert.True(installs == (chosen.Parameters is InstallCardParameters install && state.GetCard(install.CardId).DefinitionId.Value == "30016"),
            $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V23, false)]
    [InlineData(RunnerHeuristicPolicy.V24, true)]
    public async Task CleanGetawayIsPlayedOnARunTheRunnerSeesSucceed(string version, bool plays)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // Archives has no ICE: the basic run there was always preferred to Clean Getaway's 6 credits.
        (LegalAction chosen, GameState state) = await ChooseWithStateAtAsync("loop1-006", version, 532, PlayerSide.Runner);
        Assert.True(plays == (chosen.Parameters is PlayCardParameters play && state.GetCard(play.CardId).DefinitionId.Value == "35014"),
            $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V23, ActionKind.UseBioroidAbility)]
    [InlineData(RunnerHeuristicPolicy.V24, ActionKind.PumpBreaker)]
    public async Task NPotIsBrokenWholeAtThreatFive(string version, ActionKind kind)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // At threat 5 all three subroutines end the run. V23 saw only the first, broke it for 3 credits and passed the rest;
        // V24 plans all three, Sang Kancil's boost first.
        LegalAction chosen = await ChooseAtAsync("loop1-006", version, 606);
        Assert.True(kind == chosen.Kind, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V23, ActionKind.PassPriority)]
    [InlineData(RunnerHeuristicPolicy.V24, ActionKind.UseBioroidAbility)]
    public async Task NPotsCreditBreakIsUsedWithNoClicksLeft(string version, ActionKind kind)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // At threat 0 only the first subroutine ends the run; its break costs 3 credits, which V23 took for clicks it lacked.
        LegalAction chosen = await ChooseAtAsync("loop1-007", version, 47);
        Assert.Equal(kind, chosen.Kind);
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V23, ActionKind.NoAccessAction)]
    [InlineData(RunnerHeuristicPolicy.V24, ActionKind.TrashAccessedCard)]
    public async Task MeasuredResponseIsTrashedFromHqWhenItWouldFlatline(string version, ActionKind kind)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // Threat 4, two cards in the grip and 4 credits: next turn its 4 meat damage can't be paid off or absorbed.
        LegalAction chosen = await ChooseAtAsync("loop1-005", version, 127);
        Assert.Equal(kind, chosen.Kind);
    }

    [Fact]
    public async Task AnUnknownRemoteIsNotRunWithAGripAnAmbushWouldFlatline()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // Two cards in the grip; the remote held Byte! (3 net damage for 4 credits, and the Corp had 15).
        LegalAction before = await ChooseAtAsync("loop1-005", RunnerHeuristicPolicy.V23, 130);
        LegalAction after = await ChooseAtAsync("loop1-005", RunnerHeuristicPolicy.V24, 130);
        ServerId remote = Assert.IsType<RunServerParameters>(before.Parameters).ServerId;
        Assert.False(after.Parameters is RunServerParameters run && run.ServerId == remote
            || after.Parameters is PlayCardParameters play && play.TargetServerId == remote, $"v24 chose {after.Kind} {after.Description}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V27, true)]
    [InlineData(CorpHeuristicPolicy.V28, false)]
    public async Task MahkotaLangitGridDoesNotReplaceItsOwnUnrezzedCopy(string version, bool replaces)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseWithStateAtAsync("loop1-001", version, 327, PlayerSide.Corp);
        bool replacing = chosen.Parameters is InstallCardParameters install && install.RequiredReplacementCardIds
            .Any(id => state.GetCard(id).DefinitionId == state.GetCard(install.CardId).DefinitionId);
        Assert.True(replaces == replacing, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V27, true)]
    [InlineData(CorpHeuristicPolicy.V28, false)]
    public async Task PettyCashIsNotPlayedFromArchivesWithoutItsCost(string version, bool uses)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // 2 credits: the ability's play of a 3-credit operation fails and the click is lost.
        LegalAction chosen = await ChooseAtAsync("loop1-003", version, 329, PlayerSide.Corp);
        Assert.True(uses == chosen.Parameters is AbilityActionParameters { AbilityKey: "petty-cash-from-archives" },
            $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V27, "decline")]
    [InlineData(CorpHeuristicPolicy.V28, "pay")]
    public async Task PtUntaiansCounterIsPaidForOnAnUnrezzedTrap(string version, string option)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // 9 credits and an Urtica Cipher installed that turn.
        LegalAction chosen = await ChooseAtAsync("loop1-002", version, 57, PlayerSide.Corp);
        Assert.Equal(option, Assert.Single(Assert.IsType<BehaviorChoiceParameters>(chosen.Parameters).OptionKeys));
    }

    /// <summary>The version's choice for a side at the recorded decision, having watched the game to it.</summary>
    internal static async Task<LegalAction> ChooseAtAsync(string fixture, string version, long sequence, PlayerSide side = PlayerSide.Runner) =>
        (await ChooseWithStateAtAsync(fixture, version, sequence, side)).Chosen;

    internal static async Task<(LegalAction Chosen, GameState State)> ChooseWithStateAtAsync(string fixture, string version, long sequence,
        PlayerSide side)
    {
        (ReplayArtifact replay, FullMatchDefinition definition) = SavedDecision.ReadSaved(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "saved-games", fixture));
        SavedChoice choice = await SavedDecision.ChooseAsync(replay, definition, sequence, side, version);
        return (choice.Chosen, choice.State);
    }
}

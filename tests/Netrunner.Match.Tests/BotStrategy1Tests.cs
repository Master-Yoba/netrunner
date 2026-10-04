using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;
using static Netrunner.Match.Tests.BotLoop1Tests;

namespace Netrunner.Match.Tests;

/// <summary>
/// docs/BOT_STRATEGY_1.md: Runner v26 against its self-flatlines and Corp v30's sequencing and IP Enforcement, at decisions
/// from the Core Battle Box ladder where the new version and the previous one choose differently.
/// </summary>
public sealed class BotStrategy1Tests
{
    [Theory]
    [InlineData(RunnerHeuristicPolicy.V25, true)]
    [InlineData(RunnerHeuristicPolicy.V26, false)]
    public async Task ARunThatWouldArmMeasuredResponseWaitsForTheGripToSurviveIt(string version, bool runs)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // 2 points, one click left and two cards: a steal would make threat 4 with no click to draw up to Measured
        // Response's 4 meat damage.
        LegalAction chosen = await ChooseAtAsync("strategy1-measured-response", version, 130);
        Assert.True(runs == (chosen.Kind == ActionKind.StartRun), $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V25, true)]
    [InlineData(RunnerHeuristicPolicy.V26, false)]
    public async Task AtMatchPointAnEmptyGripDrawsBeforeRunningATrap(string version, bool runs)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // The Corp at 4 points, an advanced remote, no cards in the grip and four clicks.
        LegalAction chosen = await ChooseAtAsync("strategy1-match-point-draw", version, 329);
        Assert.True(runs == (chosen.Kind == ActionKind.StartRun), $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V29, false)]
    [InlineData(CorpHeuristicPolicy.V30, true)]
    public async Task PettyCashIsPlayedBeforeTheTurnsFirstActionFinishes(string version, bool plays)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseWithStateAtAsync("strategy1-petty-cash", version, 101, PlayerSide.Corp);
        bool petty = chosen.Parameters is PlayCardParameters play && state.GetCard(play.CardId).DefinitionId.Value == "35081";
        Assert.True(plays == petty, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V29, false)]
    [InlineData(CorpHeuristicPolicy.V30, true)]
    public async Task IpEnforcementTakesBackAStolenAgenda(string version, bool plays)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // A tagged Runner holds the Corp's agendas; v29 valued IP Enforcement at its tags alone.
        (LegalAction chosen, GameState state) = await ChooseWithStateAtAsync("strategy1-ip-enforcement", version, 197, PlayerSide.Corp);
        bool enforcement = chosen.Parameters is PlayCardParameters play && state.GetCard(play.CardId).DefinitionId.Value == "35066";
        Assert.True(plays == enforcement, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }
}

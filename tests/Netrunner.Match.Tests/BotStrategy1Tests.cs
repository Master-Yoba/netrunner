using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;
using static Netrunner.Match.Tests.BotLoop1Tests;

namespace Netrunner.Match.Tests;

/// <summary>
/// docs/history/BOT_STRATEGY_1.md: Runner v26 against its self-flatlines and Corp v30's sequencing and IP Enforcement, at decisions
/// from the Core Battle Box ladder where the new version and the previous one choose differently.
/// </summary>
public sealed class BotStrategy1Tests
{
    [Theory]
    [InlineData(RunnerHeuristicPolicy.V25, ActionKind.StartRun)]
    [InlineData(RunnerHeuristicPolicy.V26, ActionKind.DrawCard)]
    public async Task AfterASuccessfulRunTheGripIsDrawnUpAgainstMeasuredResponse(string version, ActionKind kind)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // Threat 6 against PT Untaian, a successful run made, two cards and 5 credits: Measured Response's 4 meat damage
        // would flatline the Runner next turn.
        LegalAction chosen = await ChooseAtAsync("strategy1-measured-response", version, 781);
        Assert.True(kind == chosen.Kind, $"{version} chose {chosen.Kind} {chosen.Description}.");
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
        // At 5 points against a tagged Runner holding the Corp's 2-point agenda: IP Enforcement takes it back and scores it
        // this turn. v29 valued the play at its tags alone.
        (LegalAction chosen, GameState state) = await ChooseWithStateAtAsync("strategy1-ip-enforcement", version, 603, PlayerSide.Corp);
        bool enforcement = chosen.Parameters is PlayCardParameters play && state.GetCard(play.CardId).DefinitionId.Value == "35066";
        Assert.True(plays == enforcement, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }
}

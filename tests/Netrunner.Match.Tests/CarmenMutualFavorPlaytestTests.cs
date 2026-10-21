using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// Playtest, 30 September (Stolen Goods against Discretion Advised): Mutual Favor, played after a
/// successful run, installed Carmen from the stack for 5 credits. Carmen's own discount applies
/// wherever it is installed from, so it costs 3.
/// </summary>
public sealed class CarmenMutualFavorPlaytestTests
{
    private const string Content = "gateway-discretion-advised~gateway-stolen-goods";
    private const ulong Seed = 8675284391991188168;
    private const long CarmenInstall = 176;
    private const string Carmen = "30015";

    [Fact]
    public void CarmenInstalledFromTheStackAfterASuccessfulRunCostsTwoLess()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(Content, Seed);
        ReplayArtifact replay = EngineJson.Deserialize<ReplayArtifact>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", "playtest-carmen-mutual-favor-replay.json")), definition.Behavior, definition.Runtime);
        GameState state = replay.InitialSnapshot.ToState();
        foreach (ReplayAction recorded in replay.Actions.TakeWhile(action => action.Sequence < CarmenInstall))
            state = GameEngine.Apply(state, recorded.Submission, definition.Behavior, definition.Runtime).State;

        Assert.True(state.Turn.History!.RunnerThisTurn!.SuccessfulRun);
        InstallCardParameters install = state.PendingDecision!.LegalActions.Select(action => action.Parameters)
            .OfType<InstallCardParameters>().Single(parameters => state.GetCard(parameters.CardId).DefinitionId.Value == Carmen
                && parameters.FinalizeSelection && parameters.SelectedReplacementCardIds.Count == 0);
        Assert.Equal(ZoneKind.RunnerStack, state.Zones.Single(zone => zone.CardIds.Contains(install.CardId)).Kind);
        Assert.Equal(3, install.CreditCost);

        int credits = state.Players.Single(player => player.Side == PlayerSide.Runner).Credits;
        LegalAction chosen = state.PendingDecision.LegalActions.First(action => ReferenceEquals(action.Parameters, install));
        state = GameEngine.Apply(state, new ActionSubmission(
            state.Players.Single(player => player.Side == PlayerSide.Runner).Id, state.PendingDecision.Id, state.PendingDecision.Revision, chosen.Id), definition.Behavior, definition.Runtime).State;
        Assert.Equal(credits - 3, state.Players.Single(player => player.Side == PlayerSide.Runner).Credits);
        Assert.Contains(install.CardId, state.GetZone(ZoneKind.RunnerRig).CardIds);
    }
}

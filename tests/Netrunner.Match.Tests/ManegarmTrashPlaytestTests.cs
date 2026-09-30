using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// Playtest, 30 September (Corp, Discretion Advised against the Stolen Goods bot): the Runner
/// accessed Manegarm Skunkworks in HQ with 3 credits, and only "No action" was offered. An upgrade
/// is trashed on access like an asset (CR 7.2.2).
/// </summary>
public sealed class ManegarmTrashPlaytestTests
{
    private const string Content = "gateway-discretion-advised~gateway-stolen-goods";
    private const ulong Seed = 3748720965229884459;
    private const long ManegarmAccess = 128;
    private const string Manegarm = "30042";

    [Fact]
    public void AnAccessedUpgradeCanBeTrashedForItsTrashCost()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(Content, Seed);
        ReplayArtifact replay = EngineJson.Deserialize<ReplayArtifact>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", "playtest-runner-bot-replay.json")), definition.Behavior, definition.Runtime);
        GameState state = replay.InitialSnapshot.ToState();
        foreach (ReplayAction recorded in replay.Actions.TakeWhile(action => action.Sequence < ManegarmAccess))
            state = GameEngine.Apply(state, recorded.Submission, definition.Behavior, definition.Runtime).State;

        PlayerState runner = state.Players.Single(player => player.Side == PlayerSide.Runner);
        Assert.Equal(3, runner.Credits);
        LegalAction trash = Assert.Single(state.PendingDecision!.LegalActions, action => action.Kind == ActionKind.TrashAccessedCard);
        var payment = (RunCardPaymentParameters)trash.Parameters;
        Assert.Equal(Manegarm, state.GetCard(payment.CardId).DefinitionId.Value);

        state = GameEngine.Apply(state, new ActionSubmission(runner.Id, state.PendingDecision.Id, state.PendingDecision.Revision, trash.Id),
            definition.Behavior, definition.Runtime).State;
        Assert.Equal(0, state.Players.Single(player => player.Side == PlayerSide.Runner).Credits);
        Assert.Contains(payment.CardId, state.GetZone(ZoneKind.CorpDiscard).CardIds);
    }
}

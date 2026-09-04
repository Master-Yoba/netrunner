using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// Focused CR 6.9.2b rez-cost tests: rezzing the approached ICE charges the
/// Corp its printed rez cost from the registered behavior spec, and the
/// RezIce action is materialized only when the Corp can afford it.
/// </summary>
public sealed class RunRezCostTests
{
    [Fact]
    public void RezChargesPrintedCost()
    {
        GameState prompt = BeginFixtureRun(FixtureIceBehaviorSpecs.RezCost, corpCredits: 5);
        GameState approach = RunTimingTests.PassTwice(prompt).State;
        Assert.Equal(RunTimingStep.ApproachPaidWindow, approach.RunState!.Step);
        Assert.Equal(PlayerSide.Runner, RunTimingTests.SideOf(approach, approach.PendingDecision!.Owner));

        GameState corpPrompt = RunTimingTests.ApplyKind(approach, ActionKind.PassPriority).State;
        Assert.Equal(RunTimingStep.ApproachPaidWindow, corpPrompt.RunState!.Step);
        Assert.Equal(PlayerSide.Corp, RunTimingTests.SideOf(corpPrompt, corpPrompt.PendingDecision!.Owner));
        LegalAction rez = Assert.Single(corpPrompt.PendingDecision.LegalActions, action => action.Kind == ActionKind.RezIce);

        PlayerId corpId = corpPrompt.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        int creditsBefore = corpPrompt.GetPlayer(corpId).Credits;
        ApplyResult result = RunTimingTests.ApplyAction(corpPrompt, rez);
        Assert.True(result.Accepted, result.Rejection?.Message);

        CardInstanceId iceId = corpPrompt.RunState!.CurrentIceId!.Value;
        // CR 26.03 8.1.1: rezzing the approached ICE turns it faceup.
        CardInstanceState rezzedIce = result.State.GetCard(iceId);
        Assert.True(rezzedIce.Rezzed);
        Assert.True(rezzedIce.FaceUp);
        Assert.Equal(creditsBefore - 2, result.State.GetPlayer(corpId).Credits);

        int creditsIndex = result.Events.ToList().FindIndex(value => value.Payload is CreditsAdjustedEventPayload payload
            && payload.Target == corpId
            && payload.Delta == -2);
        int rezzedIndex = result.Events.ToList().FindIndex(value => value.Kind == EngineEventKinds.IceRezzed);
        Assert.True(creditsIndex >= 0, "The rez must emit a CreditsAdjusted event with delta -2 targeting the Corp.");
        Assert.True(rezzedIndex >= 0, "The rez must emit an IceRezzed event.");
        Assert.True(creditsIndex < rezzedIndex, "The rez credit charge must be emitted before the IceRezzed event.");
    }

    [Fact]
    public void UnaffordableRezIsNotOffered()
    {
        GameState prompt = BeginFixtureRun(FixtureIceBehaviorSpecs.RezCost, corpCredits: 1);
        GameState approach = RunTimingTests.PassTwice(prompt).State;
        GameState corpPrompt = RunTimingTests.ApplyKind(approach, ActionKind.PassPriority).State;
        Assert.Equal(RunTimingStep.ApproachPaidWindow, corpPrompt.RunState!.Step);
        Assert.Equal(PlayerSide.Corp, RunTimingTests.SideOf(corpPrompt, corpPrompt.PendingDecision!.Owner));

        PendingDecision decision = Assert.IsType<PendingDecision>(corpPrompt.PendingDecision);
        Assert.DoesNotContain(decision.LegalActions, action => action.Kind == ActionKind.RezIce);
        Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.PassPriority);

        ApplyResult result = RunTimingTests.PassTwice(corpPrompt);
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(RunTimingStep.MovementPaidWindow, result.State.RunState!.Step);
        Assert.Null(result.State.RunState.Encounter);
        CardInstanceState ice = result.State.GetCard(corpPrompt.RunState.CurrentIceId!.Value);
        Assert.False(ice.Rezzed);
        Assert.False(ice.FaceUp);
    }

    [Fact]
    public void ExactlyAffordableRezIsOfferedAndLeavesZeroCredits()
    {
        GameState prompt = BeginFixtureRun(FixtureIceBehaviorSpecs.RezCost, corpCredits: 2);
        GameState approach = RunTimingTests.PassTwice(prompt).State;
        GameState corpPrompt = RunTimingTests.ApplyKind(approach, ActionKind.PassPriority).State;
        Assert.Equal(RunTimingStep.ApproachPaidWindow, corpPrompt.RunState!.Step);
        Assert.Equal(PlayerSide.Corp, RunTimingTests.SideOf(corpPrompt, corpPrompt.PendingDecision!.Owner));

        PendingDecision decision = Assert.IsType<PendingDecision>(corpPrompt.PendingDecision);
        LegalAction rez = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.RezIce);
        ApplyResult result = RunTimingTests.ApplyAction(corpPrompt, rez);
        Assert.True(result.Accepted, result.Rejection?.Message);

        PlayerId corpId = result.State.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        Assert.Equal(0, result.State.GetPlayer(corpId).Credits);
        CardInstanceState rezzedIce = result.State.GetCard(corpPrompt.RunState!.CurrentIceId!.Value);
        Assert.True(rezzedIce.Rezzed);
        Assert.True(rezzedIce.FaceUp);
    }

    private static GameState BeginFixtureRun(CardDefinitionId iceDefinition, int? corpCredits)
    {
        GameState state = RunTimingTests.CreateRunState(iceCount: 1, rezzed: false, iceDefinition: iceDefinition, corpCredits: corpCredits);
        return GameEngine.BeginRun(state, RunTimingTests.RemoteId(state), FixtureBehaviorCatalog.Catalog);
    }
}

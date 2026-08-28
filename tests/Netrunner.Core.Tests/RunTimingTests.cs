using System.Text.Json;
using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// Focused CR 26.03 section 6.9 timing matrix tests.  The representative ICE
/// has no registered ability in this slice; these tests prove timing, state
/// persistence, and the atomic decision boundary before card abilities land.
/// </summary>
public sealed class RunTimingTests
{
    [Fact]
    public void Cr691eInitiationPaidWindowAlternatesRunnerThenCorp()
    {
        GameState initial = CreateRunState(iceCount: 1, rezzed: false);
        GameState prompt = GameEngine.BeginRun(initial, RemoteId(initial));

        AssertRunPrompt(prompt, RunTimingStep.InitiationPaidWindow, PlayerSide.Runner, ActionKind.PassPriority);
        ApplyResult runnerPass = ApplyKind(prompt, ActionKind.PassPriority);
        Assert.True(runnerPass.Accepted, runnerPass.Rejection?.Message);
        AssertRunPrompt(runnerPass.State, RunTimingStep.InitiationPaidWindow, PlayerSide.Corp, ActionKind.PassPriority);

        ApplyResult corpPass = ApplyKind(runnerPass.State, ActionKind.PassPriority);
        Assert.True(corpPass.Accepted, corpPass.Rejection?.Message);
        AssertRunPrompt(corpPass.State, RunTimingStep.ApproachPaidWindow, PlayerSide.Runner, ActionKind.PassPriority);
        Assert.Equal(3, corpPass.State.DecisionRevision);
    }

    [Fact]
    public void Cr692bApproachAlwaysYieldsPaidWindowAndRezIsOptional()
    {
        GameState prompt = BeginRunPrompt(iceCount: 1, rezzed: false);
        GameState approach = PassTwice(prompt).State;
        PendingDecision decision = Assert.IsType<PendingDecision>(approach.PendingDecision);
        Assert.Equal(RunTimingStep.ApproachPaidWindow, approach.RunState!.Step);
        Assert.Equal(PlayerSide.Runner, SideOf(approach, decision.Owner));
        Assert.DoesNotContain(decision.LegalActions, action => action.Kind == ActionKind.RezIce);

        ApplyResult runnerPass = ApplyKind(approach, ActionKind.PassPriority);
        Assert.True(runnerPass.Accepted, runnerPass.Rejection?.Message);
        PendingDecision corpDecision = Assert.IsType<PendingDecision>(runnerPass.State.PendingDecision);
        Assert.Equal(PlayerSide.Corp, SideOf(runnerPass.State, corpDecision.Owner));
        LegalAction rez = Assert.Single(corpDecision.LegalActions, action => action.Kind == ActionKind.RezIce);
        Assert.IsType<RunIceParameters>(rez.Parameters);

        ApplyResult rezzed = ApplyAction(runnerPass.State, rez);
        Assert.True(rezzed.Accepted, rezzed.Rejection?.Message);
        Assert.Equal(RunTimingStep.ApproachPaidWindow, rezzed.State.RunState!.Step);
        Assert.Equal(PlayerSide.Corp, SideOf(rezzed.State, rezzed.State.PendingDecision!.Owner));
        Assert.DoesNotContain(rezzed.State.PendingDecision.LegalActions, action => action.Kind == ActionKind.RezIce);

        ApplyResult corpPass = ApplyKind(rezzed.State, ActionKind.PassPriority);
        Assert.True(corpPass.Accepted, corpPass.Rejection?.Message);
        Assert.Equal(PlayerSide.Runner, SideOf(corpPass.State, corpPass.State.PendingDecision!.Owner));
        ApplyResult finalPass = ApplyKind(corpPass.State, ActionKind.PassPriority);
        Assert.True(finalPass.Accepted, finalPass.Rejection?.Message);
        Assert.Equal(RunTimingStep.EncounterPaidWindow, finalPass.State.RunState!.Step);
        Assert.NotNull(finalPass.State.RunState.Encounter);
        Assert.Contains(rezzed.Events, value => value.Kind == EngineEventKinds.IceRezzed);
        Assert.Contains(finalPass.Events, value => value.Kind == EngineEventKinds.EncounterInitialized);
    }

    [Fact]
    public void Cr692bPassingApproachLeavesIceUnrezzedAndSkipsEncounter()
    {
        GameState approach = PassTwice(BeginRunPrompt(iceCount: 1, rezzed: false)).State;
        ApplyResult runnerPass = ApplyKind(approach, ActionKind.PassPriority);
        Assert.True(runnerPass.Accepted);
        ApplyResult corpPass = ApplyKind(runnerPass.State, ActionKind.PassPriority);

        Assert.True(corpPass.Accepted, corpPass.Rejection?.Message);
        Assert.Equal(RunTimingStep.MovementPaidWindow, corpPass.State.RunState!.Step);
        Assert.Null(corpPass.State.RunState.Encounter);
        CardInstanceState ice = corpPass.State.GetCard(corpPass.State.RunState.CurrentIceId!.Value);
        Assert.False(ice.Rezzed);
    }

    [Fact]
    public void Cr693bEncounterPaidWindowAlternatesBothSeats()
    {
        GameState approach = PassTwice(PassTwice(BeginRunPrompt(iceCount: 1, rezzed: true)).State).State;
        Assert.Equal(RunTimingStep.EncounterPaidWindow, approach.RunState!.Step);
        Assert.Equal(PlayerSide.Runner, SideOf(approach, approach.PendingDecision!.Owner));

        ApplyResult runnerPass = ApplyKind(approach, ActionKind.PassPriority);
        Assert.True(runnerPass.Accepted);
        Assert.Equal(PlayerSide.Corp, SideOf(runnerPass.State, runnerPass.State.PendingDecision!.Owner));
        ApplyResult corpPass = ApplyKind(runnerPass.State, ActionKind.PassPriority);
        Assert.True(corpPass.Accepted, corpPass.Rejection?.Message);
        Assert.Equal(RunTimingStep.MovementPaidWindow, corpPass.State.RunState!.Step);
    }

    [Fact]
    public void Cr694cJackOutIsRunnerOnlyAndCleansUpRun()
    {
        GameState movement = ReachJackOut(iceCount: 1, rezzed: false);
        PendingDecision decision = Assert.IsType<PendingDecision>(movement.PendingDecision);
        Assert.Equal(RunTimingStep.JackOutDecision, movement.RunState!.Step);
        Assert.Equal(PlayerSide.Runner, SideOf(movement, decision.Owner));
        Assert.Contains(decision.LegalActions, action => action.Kind == ActionKind.JackOut);
        Assert.Contains(decision.LegalActions, action => action.Kind == ActionKind.ContinueRun);

        PlayerId corp = movement.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        ApplyResult wrongOwner = ApplyAction(movement, decision.LegalActions.Single(action => action.Kind == ActionKind.JackOut), corp);
        Assert.Equal(ApplyRejectionCode.WrongOwner, wrongOwner.Rejection?.Code);
        Assert.Same(movement, wrongOwner.State);

        ApplyResult result = ApplyKind(movement, ActionKind.JackOut);
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Null(result.State.RunState);
        Assert.Empty(result.State.ResolutionStack);
        Assert.Null(result.State.PendingDecision);
        Assert.Contains(result.Events, value => value.Kind == EngineEventKinds.RunEnded);
        RunEndedEventPayload ended = Assert.IsType<RunEndedEventPayload>(result.Events[^1].Payload);
        Assert.Equal(RunEndReason.JackedOut, ended.Reason);
        Assert.False(ended.Successful);
    }

    [Fact]
    public void Cr694dContinueRunMovesInwardAutomaticallyThenOpensAfterMoveWindow()
    {
        GameState movement = ReachJackOut(iceCount: 2, rezzed: false);
        CardInstanceId outer = movement.RunState!.CurrentIceId!.Value;
        ApplyResult continueRun = ApplyKind(movement, ActionKind.ContinueRun);

        Assert.True(continueRun.Accepted, continueRun.Rejection?.Message);
        Assert.Equal(RunTimingStep.AfterMovePaidWindow, continueRun.State.RunState!.Step);
        Assert.Equal(0, continueRun.State.RunState.CurrentIceIndex);
        Assert.NotEqual(outer, continueRun.State.RunState.CurrentIceId);
        Assert.Equal(PlayerSide.Runner, SideOf(continueRun.State, continueRun.State.PendingDecision!.Owner));
        Assert.Contains(continueRun.Events, value => value.Kind == EngineEventKinds.RunMoved);
    }

    [Fact]
    public void Cr695aSuccessHasNoPaidWindowAndBreachBoundaryIsAutomatic()
    {
        GameState prompt = BeginRunPrompt(iceCount: 0, rezzed: false);
        ApplyResult result = PassTwice(prompt);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Null(result.State.RunState);
        Assert.Empty(result.State.ResolutionStack);
        Assert.Null(result.State.PendingDecision);
        Assert.Contains(result.Events, value => value.Kind == EngineEventKinds.RunSuccessful);
        Assert.Contains(result.Events, value => value.Kind == EngineEventKinds.BreachPlaceholderReached);
        Assert.Contains(result.Events, value => value.Kind == EngineEventKinds.RunEnded);
        Assert.DoesNotContain(result.Events, value => value.Kind == EngineEventKinds.RunPriorityPassed && value.Payload is RunPriorityPassedEventPayload payload && payload.Window.ToString().Contains("Success", StringComparison.Ordinal));
    }

    [Fact]
    public void Cr696dSaveLoadAtEveryYieldedDecisionProducesIdenticalContinuation()
    {
        GameState original = BeginRunPrompt(iceCount: 2, rezzed: false);
        int yielded = 0;
        while (original.PendingDecision is not null)
        {
            yielded++;
            string json = EngineJson.Serialize(original);
            GameState restored = EngineJson.DeserializeState(json);
            Assert.Equal(json, EngineJson.Serialize(restored));

            PendingDecision decision = original.PendingDecision;
            LegalAction action = decision.LegalActions[0];
            ActionSubmission submission = new(decision.Owner, decision.Id, decision.Revision, action.Id);
            ApplyResult left = GameEngine.Apply(original, submission);
            ApplyResult right = GameEngine.Apply(restored, submission);
            Assert.Equal(left.Status, right.Status);
            Assert.Equal(EngineJson.Serialize(left.State), EngineJson.Serialize(right.State));
            Assert.Equal(DeterministicHash.ForEvents(left.Events), DeterministicHash.ForEvents(right.Events));
            original = left.State;
            Assert.True(yielded < 32, "Run timing loop did not make progress.");
        }

        Assert.True(yielded > 0);
        StateInvariantValidator.Validate(original);
    }

    [Fact]
    public void LiveRunSnapshotIsDeeplyDetachedAndRoundTripsStrictly()
    {
        GameState prompt = BeginRunPrompt(iceCount: 1, rezzed: false);
        GameSnapshot snapshot = prompt.CreateSnapshot();
        string json = EngineJson.Serialize(snapshot);
        GameSnapshot restored = EngineJson.DeserializeSnapshot(json);

        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.NotNull(snapshot.RunState);
        Assert.NotSame(prompt.RunState, snapshot.RunState);
        Assert.NotSame(prompt.RunState!.Priority, snapshot.RunState!.Priority);
        Assert.Equal(EngineJson.Serialize(prompt), EngineJson.Serialize(snapshot.ToState()));
        Assert.Throws<NotSupportedException>(() => Assert.IsAssignableFrom<IList<RunLingeringEffect>>(snapshot.RunState.LingeringEffects).Add(
            new RunLingeringEffect(new RunEffectId("effect:test"), "fixture.noop", null, RunLingeringEffectExpiry.Run)));
    }

    [Fact]
    public void MalformedRunFrameOrRunStateFailsClosed()
    {
        GameState prompt = BeginRunPrompt(iceCount: 1, rezzed: false);
        string json = EngineJson.Serialize(prompt);
        string missingStep = json.Replace("\"step\":\"initiationPaidWindow\",", string.Empty, StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => EngineJson.DeserializeState(missingStep));

        string unknownFrame = json.Replace("\"$type\":\"run-timing\"", "\"$type\":\"future-run\"", StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => EngineJson.DeserializeState(unknownFrame));

        string wrongBehavior = json.Replace("\"behaviorId\":\"rules.run-timing-26.03\"", "\"behaviorId\":\"rules.other\"", StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => EngineJson.DeserializeState(wrongBehavior));
    }

    [Fact]
    public void RunActionRollbackKeepsExactStateForForgedRezTarget()
    {
        GameState approach = PassTwice(BeginRunPrompt(iceCount: 1, rezzed: false)).State;
        ApplyResult runnerPass = ApplyKind(approach, ActionKind.PassPriority);
        Assert.True(runnerPass.Accepted);
        GameState corpPrompt = runnerPass.State;
        PendingDecision decision = corpPrompt.PendingDecision!;
        LegalAction rez = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.RezIce);
        ActionSubmission forged = new(decision.Owner, decision.Id, decision.Revision, new ActionId("action:forged"));
        string before = EngineJson.Serialize(corpPrompt);
        ApplyResult rejected = GameEngine.Apply(corpPrompt, forged);

        Assert.Equal(ApplyRejectionCode.UnknownAction, rejected.Rejection?.Code);
        Assert.Same(corpPrompt, rejected.State);
        Assert.Equal(before, EngineJson.Serialize(rejected.State));

        ApplyResult accepted = ApplyAction(corpPrompt, rez);
        Assert.True(accepted.Accepted);
        Assert.True(accepted.State.GetCard(corpPrompt.RunState!.CurrentIceId!.Value).Rezzed);
    }

    private static GameState BeginRunPrompt(int iceCount, bool rezzed)
    {
        GameState state = CreateRunState(iceCount, rezzed);
        return GameEngine.BeginRun(state, RemoteId(state));
    }

    private static ApplyResult PassTwice(GameState state)
    {
        ApplyResult first = ApplyKind(state, ActionKind.PassPriority);
        Assert.True(first.Accepted, first.Rejection?.Message);
        return ApplyKind(first.State, ActionKind.PassPriority);
    }

    private static GameState ReachJackOut(int iceCount, bool rezzed)
    {
        GameState state = BeginRunPrompt(iceCount, rezzed);
        state = PassTwice(state).State;
        while (state.RunState is not null && state.PendingDecision is not null)
        {
            switch (state.RunState.Step)
            {
                case RunTimingStep.ApproachPaidWindow:
                case RunTimingStep.EncounterPaidWindow:
                case RunTimingStep.MovementPaidWindow:
                case RunTimingStep.AfterMovePaidWindow:
                    state = PassTwice(state).State;
                    break;
                case RunTimingStep.JackOutDecision:
                    return state;
                default:
                    throw new Xunit.Sdk.XunitException($"Unexpected yielded run step '{state.RunState.Step}'.");
            }
        }

        Assert.Equal(RunTimingStep.JackOutDecision, state.RunState!.Step);
        return state;
    }

    private static ApplyResult ApplyKind(GameState state, ActionKind kind, PlayerId? actor = null)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction action = Assert.Single(decision.LegalActions, candidate => candidate.Kind == kind);
        return ApplyAction(state, action, actor);
    }

    private static ApplyResult ApplyAction(GameState state, LegalAction action, PlayerId? actor = null)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        return GameEngine.Apply(
            state,
            new ActionSubmission(actor ?? decision.Owner, decision.Id, decision.Revision, action.Id));
    }

    private static void AssertRunPrompt(GameState state, RunTimingStep step, PlayerSide ownerSide, ActionKind actionKind)
    {
        Assert.NotNull(state.RunState);
        Assert.Equal(step, state.RunState.Step);
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        Assert.Equal(DecisionKind.RunTiming, decision.Kind);
        Assert.Equal(ownerSide, SideOf(state, decision.Owner));
        Assert.Contains(decision.LegalActions, action => action.Kind == actionKind);
    }

    private static PlayerSide SideOf(GameState state, PlayerId id) => state.GetPlayer(id).Side;

    private static ServerId RemoteId(GameState state) => state.Servers.Single(server => server.Kind == ServerKind.Remote).Id;

    private static GameState CreateRunState(int iceCount, bool rezzed)
    {
        GameState baseState = GameState.CreateNew(99);
        var allocator = new StableIdAllocator(baseState.Allocator);
        ServerId remoteId = allocator.NextServerId();
        ZoneId iceZoneId = allocator.NextZoneId();
        ZoneId rootZoneId = allocator.NextZoneId();
        var iceIds = new List<CardInstanceId>();
        var cards = new List<CardInstanceState>();
        for (int index = 0; index < iceCount; index++)
        {
            CardInstanceId cardId = allocator.NextCardInstanceId();
            iceIds.Add(cardId);
            cards.Add(new CardInstanceState(
                cardId,
                new CardDefinitionId($"fixture:ice-{index}"),
                iceZoneId,
                PlayerSide.Corp,
                faceUp: false,
                rezzed: rezzed));
        }

        var servers = baseState.Servers.Append(new ServerState(remoteId, ServerKind.Remote, iceZoneId, rootZoneId)).ToArray();
        var zones = baseState.Zones
            .Append(new ZoneState(iceZoneId, ZoneKind.ServerIce, PlayerSide.Corp, remoteId, iceIds))
            .Append(new ZoneState(rootZoneId, ZoneKind.ServerRoot, PlayerSide.Corp, remoteId))
            .ToArray();
        var players = baseState.Players.Select(player => new PlayerState(
            player.Id,
            player.Side,
            player.Credits,
            player.Side == PlayerSide.Runner ? 4 : 3,
            player.AgendaPoints)).ToArray();
        return new GameState(
            baseState.SchemaVersion,
            baseState.GameId,
            baseState.RulesVersion,
            baseState.CardDataVersion,
            players,
            cards,
            zones,
            servers,
            new TurnState(PlayerSide.Runner, TurnPhase.Runner, baseState.Turn.TurnNumber),
            new GameOutcome(),
            baseState.RandomState,
            allocator.State,
            baseState.DecisionRevision,
            baseState.EventSequence,
            [],
            null,
            baseState.ActionSequence,
            baseState.InitialSeed);
    }
}

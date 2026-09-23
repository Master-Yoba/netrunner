using System.Text.Json;
using System.Text.Json.Nodes;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// Prepared rules positions supplement complete-game portable-save tests. They
/// are scenario trace roots, never represented as ordinary player save files.
/// </summary>
public sealed class SessionPolicyMatrixTests
{
    [Theory]
    [InlineData(InterruptKind.Prevention, ActionKind.UsePrevention)]
    [InlineData(InterruptKind.Replacement, ActionKind.UseReplacement)]
    public void SyntheticInterruptCheckpointResumesWithoutRepeatingItsEffect(InterruptKind kind, ActionKind choice)
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer();
        GameState basis = GameState.CreateNew(552, cardDataVersion: definition.Request.CardDataVersion,
            behaviorRegistryVersion: definition.Behavior.Version, behaviorCatalogFingerprint: definition.Behavior.Fingerprint,
            runtimeCardCatalogVersion: definition.Runtime.Version, runtimeCardCatalogFingerprint: definition.Runtime.Fingerprint);
        PlayerId corp = basis.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        PlayerId runner = basis.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        RulesState defaults = RulesState.CreateDefault(basis.Players);
        var rules = new RulesState(defaults.Version, defaults.Players, defaults.Programs, defaults.Counters,
            defaults.LingeringEffects, defaults.Triggers,
            [new InterruptRegistration(new("interrupt:persistence"), runner, kind, false,
                new TriggerCondition(OccurrenceKind.Damage), "fixture.persistence", null, new CostBundle(),
                kind == InterruptKind.Prevention ? 2 : 0,
                kind == InterruptKind.Replacement ? new EffectBundle(new CreditAdjustmentEffect(runner, 2)) : new EffectBundle())]);
        JsonNode node = JsonNode.Parse(EngineJson.Serialize(basis))!;
        node["rulesState"] = JsonSerializer.SerializeToNode(rules, EngineJson.Options);
        GameState state = EngineJson.DeserializeState(node.ToJsonString(), definition.Behavior, definition.Runtime);
        state = GameEngine.BeginResolution(state, new EffectResolutionFrame(corp, new CostBundle(),
            new EffectBundle(new DamageEffect(runner, DamageKind.Net, 2))), definition.Behavior, definition.Runtime);
        var agent = new OfflineAgent(CorpPolicyProfiles.Create("standard", 552), new CorpHeuristicPolicy(),
            new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior));
        AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        string directory = Path.Combine(Path.GetTempPath(), "netrunner-interrupt-save-" + Guid.NewGuid().ToString("N"));
        try
        {
            using SessionTrace durable = SessionTrace.CreateNew(directory, state, PlayerSide.Runner, definition.Behavior,
                definition.Runtime, policy: SoloAgentState.Capture(agent));
            SessionRecoveryResult recovered = SessionTrace.Recover(directory, definition.Behavior, definition.Runtime);
            PendingDecision pending = state.PendingDecision!;
            LegalAction action = pending.LegalActions.Single(value => value.Kind == choice
                && (value.Parameters is not PreventionChoiceParameters prevention || prevention.Amount == 2));
            var submission = new ActionSubmission(pending.Owner, pending.Id, pending.Revision, action.Id);
            string operation = durable.AppendIntent(state, submission);
            ApplyResult original = GameEngine.Apply(state, submission, definition.Behavior, definition.Runtime);
            ApplyResult restored = GameEngine.Apply(recovered.State, submission, definition.Behavior, definition.Runtime);
            Assert.True(original.Accepted);
            Assert.Equal(original.AcceptedAction, restored.AcceptedAction);
            Assert.Equal(EngineJson.Serialize(original.Events), EngineJson.Serialize(restored.Events));
            AgentLifecycleHost.Accepted(agent, submission, original.State, definition.Behavior);
            durable.AppendAccepted(operation, state, original.AcceptedAction!, SoloAgentState.Capture(agent));
            GameState loaded = SessionTrace.Recover(directory, definition.Behavior, definition.Runtime).State;
            Assert.Equal(DeterministicHash.ForState(original.State), DeterministicHash.ForState(loaded));
            Assert.Equal(GameOutcomeKind.Ongoing, loaded.Outcome.Kind);
            Assert.Null(loaded.PendingDecision);
            Assert.Empty(loaded.ResolutionStack);
            Assert.Equal(state.GetPlayer(runner).Credits + (kind == InterruptKind.Replacement ? 2 : 0), loaded.GetPlayer(runner).Credits);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [OfficialGatewayTheory]
    [InlineData("corp-replacement-04")]
    [InlineData("corp-replacement-05")]
    [InlineData("corp-choices-01")]
    [InlineData("corp-choices-02")]
    [InlineData("corp-choices-04")]
    [InlineData("runner-rig-02")]
    [InlineData("runner-rig-03")]
    [InlineData("runner-rig-04")]
    [InlineData("runner-choices-01")]
    [InlineData("runner-choices-02")]
    [InlineData("runner-choices-03")]
    [InlineData("runner-choices-04")]
    [InlineData("runner-encounter-01")]
    [InlineData("runner-encounter-02")]
    [InlineData("runner-encounter-04")]
    [InlineData("runner-access-01")]
    [InlineData("runner-access-02")]
    [InlineData("runner-access-05")]
    [InlineData("runner-payments-01")]
    [InlineData("runner-payments-02")]
    [InlineData("runner-payments-03")]
    [InlineData("runner-payments-04")]
    public async Task EveryNestedBoundaryRetainsEngineEventsChoicesAndPolicy(string id)
    {
        TacticalFixture fixture = new GatewayTacticalCatalog(CardDataResource.LoadEmbedded()).Build(id);
        TacticalBoard board = fixture.Board;
        var trace = new TacticalTrace(fixture);
        PlayerSide human = fixture.Metadata.Side;
        PlayerSide side = human == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp;
        OfflineAgentConfiguration config = side == PlayerSide.Corp
            ? CorpPolicyProfiles.Create("standard", 551) : RunnerPolicyProfiles.Create("standard", 551);
        AgentFeatures features = ObservationReader.Read(ObservationBuilder.Build(trace.State, side, board.Behavior));
        // A prepared scenario has a new observation-history root at its actual
        // engine action cursor; no fictitious earlier bot decisions are inserted.
        OfflineAgentCheckpoint checkpoint = OfflineAgentCheckpoint.Initial(config) with
        {
            ObservationCursor = trace.State.ActionSequence,
            ObservationDigest = ObservationReader.Digest(features),
            Knowledge = AgentKnowledge.Empty.Observe(features, trace.State.ActionSequence),
        };
        var agent = new OfflineAgent(config, side == PlayerSide.Corp ? new CorpHeuristicPolicy() : new RunnerHeuristicPolicy(),
            new GatewayEvaluationCatalog(board.Runtime, board.Behavior), checkpoint);
        string directory = Path.Combine(Path.GetTempPath(), "netrunner-save-matrix-" + Guid.NewGuid().ToString("N"));
        try
        {
            using SessionTrace durable = SessionTrace.CreateNew(directory, trace.State, human, board.Behavior,
                board.Runtime, scenarioId: id, policy: SoloAgentState.Capture(agent));
            while (!fixture.Complete(trace) && trace.Steps.Count < fixture.ActionCap)
            {
                SessionRecoveryResult recovery = SessionTrace.Recover(directory, board.Behavior, board.Runtime);
                GameState restored = EngineJson.DeserializeSnapshot(
                    EngineJson.Serialize(recovery.State.CreateSnapshot(board.Behavior, board.Runtime)), board.Behavior, board.Runtime).ToState();
                IGameAgent resumed = recovery.Policy!.Restore(board.Behavior, board.Runtime);
                Assert.Equal(DeterministicHash.ForState(trace.State), DeterministicHash.ForState(restored));
                Assert.Equal(BenchmarkHash.Value(SoloAgentState.Capture(agent)), BenchmarkHash.Value(recovery.Policy));
                PendingDecision pending = trace.State.PendingDecision!;
                PlayerSide actor = trace.State.GetPlayer(pending.Owner).Side;
                LegalAction action;
                if (actor == human) action = fixture.Oracle(trace);
                else
                {
                    Observation observation = ObservationBuilder.Build(trace.State, side, board.Behavior);
                    ActionId originalChoice = await agent.ChooseActionAsync(observation);
                    ActionId restoredChoice = await resumed.ChooseActionAsync(ObservationBuilder.Build(restored, side, board.Behavior));
                    Assert.Equal(originalChoice, restoredChoice);
                    action = pending.LegalActions.Single(candidate => candidate.Id == originalChoice);
                }
                var submission = new ActionSubmission(pending.Owner, pending.Id, pending.Revision, action.Id);
                string operation = durable.AppendIntent(trace.State, submission);
                ApplyResult left = GameEngine.Apply(trace.State, submission, board.Behavior, board.Runtime);
                ApplyResult right = GameEngine.Apply(restored, submission, board.Behavior, board.Runtime);
                Assert.True(left.Accepted, id + ": " + left.Rejection?.Message);
                Assert.True(right.Accepted, id + ": " + right.Rejection?.Message);
                Assert.Equal(left.AcceptedAction, right.AcceptedAction);
                Assert.Equal(EngineJson.Serialize(left.Events), EngineJson.Serialize(right.Events));
                Assert.Equal(EngineJson.Serialize(left.State), EngineJson.Serialize(right.State));
                AgentLifecycleHost.Accepted(agent, submission, left.State, board.Behavior);
                AgentLifecycleHost.Accepted(resumed, submission, right.State, board.Behavior);
                SoloAgentState committed = SoloAgentState.Capture(agent);
                Assert.Equal(BenchmarkHash.Value(committed), BenchmarkHash.Value(SoloAgentState.Capture(resumed)));
                durable.AppendAccepted(operation, trace.State, left.AcceptedAction!, committed);
                trace.Steps.Add(new(actor, pending.Kind, action));
                trace.State = left.State;
            }
            Assert.True(fixture.Complete(trace), id + " did not complete its bounded continuation.");
            Assert.NotEmpty(trace.Steps);
            SessionRecoveryResult final = SessionTrace.Recover(directory, board.Behavior, board.Runtime);
            Assert.Equal(DeterministicHash.ForState(trace.State), DeterministicHash.ForState(final.State));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}

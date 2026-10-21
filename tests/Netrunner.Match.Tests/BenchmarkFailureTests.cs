using System.Text.Json;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class BenchmarkFailureTests : IDisposable
{
    private readonly string _output = Path.Combine(Path.GetTempPath(), "netrunner-benchmark-faults-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("observe", false)]
    [InlineData("observe", true)]
    [InlineData("commit", false)]
    [InlineData("commit", true)]
    [InlineData("discard", false)]
    [InlineData("discard", true)]
    public async Task LifecycleExceptionsAndHangsCannotAbortOrBlockReport(string stage, bool hang)
    {
        using var blocked = new ManualResetEventSlim(false);
        DeveloperMatchCards developer = DeveloperMatchCards.Create();
        var runner = new AiBenchmarkRunner(developer.Data, developer.Behavior, new LifecycleFactory(stage, hang, blocked));
        AiBenchmarkManifest manifest = BenchmarkSuites.CreateContract(runner, "developer");
        manifest = manifest with { Games = [manifest.Games[0]], Limits = new BenchmarkLimits(Actions: 1, WatchdogSeconds: 1) };
        try
        {
            BenchmarkGameResult game = Assert.Single((await runner.RunAsync(manifest, _output)).Games);
            Assert.Equal(hang ? BenchmarkTermination.WatchdogTimeout : BenchmarkTermination.AgentException, game.Result.Termination);
            Assert.Equal(stage == "observe" ? 0 : 1, game.Result.Actions);
            Assert.True(game.Result.ReplayVerified);
            Assert.NotNull(game.FailurePath);
        }
        finally { blocked.Set(); }
    }

    [Fact]
    public async Task ReusingAnAgentAcrossGamesFailsBeforeItReceivesAnotherObservation()
    {
        DeveloperMatchCards developer = DeveloperMatchCards.Create();
        var runner = new AiBenchmarkRunner(developer.Data, developer.Behavior, new SharedFactory(developer.Runtime));
        AiBenchmarkManifest manifest = BenchmarkSuites.CreateContract(runner, "developer");
        manifest = manifest with { Games = [manifest.Games[0], manifest.Games[1]] };
        AiBenchmarkReport report = await runner.RunAsync(manifest, _output);
        Assert.Equal(BenchmarkTermination.Terminal, report.Games[0].Result.Termination);
        Assert.Equal(BenchmarkTermination.AgentException, report.Games[1].Result.Termination);
        Assert.Equal(0, report.Games[1].Result.Actions);
        Assert.Contains("fresh agent", report.Games[1].Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoDecisionAndInvalidRootRemainDistinctFromTheirReplayStatus(bool invalid)
    {
        DeveloperMatchCards developer = DeveloperMatchCards.Create();
        var runner = new AiBenchmarkRunner(developer.Data, developer.Behavior);
        GameState state = GameState.CreateNew(1, behaviorRegistryVersion: developer.Behavior.Version,
            behaviorCatalogFingerprint: developer.Behavior.Fingerprint, runtimeCardCatalogVersion: developer.Runtime.Version,
            runtimeCardCatalogFingerprint: developer.Runtime.Fingerprint);
        if (invalid)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(EngineJson.Serialize(state))!;
            node["schemaVersion"] = 999;
            state = JsonSerializer.Deserialize<GameState>(node.ToJsonString(), EngineJson.Options)!;
        }
        BenchmarkGameSpec row = BenchmarkSuites.CreateContract(runner, "developer").Games[0];
        BenchmarkGameResult result = await runner.RunPositionAsync(row, state, new(), _output);
        Assert.Equal(invalid ? BenchmarkTermination.InvariantFailure : BenchmarkTermination.NoDecision, result.Result.ExecutionTermination);
        Assert.Equal(invalid ? BenchmarkTermination.ReplayMismatch : BenchmarkTermination.NoDecision, result.Result.Termination);
        Assert.Equal(!invalid, result.Result.ReplayVerified);
        Assert.Equal(0, result.Result.Actions);
        Assert.NotNull(result.FailurePath);
    }

    [Fact]
    public async Task RealCorePaidAbilityCycleIgnoresNewFramesActionsEventsAndDecisionIds()
    {
        CardDefinitionId definition = new("fixture:repeat-no-op");
        CardInstanceId source = new("card:repeat-no-op");
        BehaviorCatalog behavior = new BehaviorCatalogBuilder("fixture:benchmark-cycle-v1")
            .Add(new CardBehaviorDefinition(definition, PlayerSide.Runner, paidAbilities:
            [
                new BehaviorPaidAbility("repeat-no-op", BehaviorAbilityTiming.AnyPaidWindow, BehaviorAbilityActivation.PaidAbility,
                    [], [], [], [new BehaviorInstruction(BehaviorInstructionKind.NoOp)], BehaviorAbilityUsageLimit.Unlimited),
            ])).Build();
        RuntimeCardCatalog runtime = new RuntimeCardCatalogBuilder("fixture:benchmark-cycle-v1")
            .Add(new CardRuntimeSpec(definition, PlayerSide.Runner, RuntimeCardClass.Resource, installCost: 0)).Build();
        GameState basis = GameState.CreateNew(1, behaviorRegistryVersion: behavior.Version, behaviorCatalogFingerprint: behavior.Fingerprint,
            runtimeCardCatalogVersion: runtime.Version, runtimeCardCatalogFingerprint: runtime.Fingerprint);
        ZoneState rig = basis.GetZone(ZoneKind.RunnerRig);
        GameState state = new(basis.SchemaVersion, basis.GameId, basis.RulesVersion, basis.CardDataVersion,
            basis.Players, [new CardInstanceState(source, definition, rig.Id, PlayerSide.Runner, faceUp: true)],
            basis.Zones.Select(zone => zone.Id == rig.Id ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [source]) : zone).ToArray(),
            basis.Servers, new TurnState(PlayerSide.Runner), basis.Outcome, basis.RandomState, basis.Allocator,
            initialSeed: basis.InitialSeed, rulesState: basis.RulesState,
            behaviorRegistryVersion: behavior.Version, behaviorCatalogFingerprint: behavior.Fingerprint,
            runtimeCardCatalogVersion: runtime.Version, runtimeCardCatalogFingerprint: runtime.Fingerprint);
        state = GameEngine.BeginTurn(state, PlayerSide.Runner, behavior, runtime);
        StateInvariantValidator.Validate(state, behavior, runtime);
        var runner = new AiBenchmarkRunner(DeveloperMatchCards.Create().Data, behavior, runtime, new CyclingFactory());
        BenchmarkGameSpec row = BenchmarkSuites.CreateContract(new(DeveloperMatchCards.Create().Data, DeveloperMatchCards.Create().Behavior), "developer").Games[0];
        BenchmarkGameResult result = await runner.RunPositionAsync(row, state, new(RepetitionCount: 3, ProgressWindow: 16, Actions: 100), _output);
        Assert.Equal(BenchmarkTermination.RepeatedProgress, result.Result.Termination);
        Assert.InRange(result.Result.Actions, 2, 20);
        Assert.Equal(3, result.Result.Progress.Repetitions);
        Assert.True(result.Result.ReplayVerified, result.Error);
    }

    public void Dispose()
    {
        if (Directory.Exists(_output)) Directory.Delete(_output, recursive: true);
    }

    private sealed class LifecycleFactory(string stage, bool hang, ManualResetEventSlim blocked) : IBenchmarkAgentFactory
    {
        public void Validate(BenchmarkAgentSpec specification) => specification.Validate();
        public IGameAgent Create(BenchmarkAgentSpec specification, IRuntimeCardCatalog runtime, ICardEvaluationCatalog evaluation) =>
            new LifecycleAgent(specification.Side, runtime, stage, hang, blocked);
    }

    private sealed class LifecycleAgent(PlayerSide side, IRuntimeCardCatalog runtime, string stage, bool hang, ManualResetEventSlim blocked) : IAgentLifecycle
    {
        private readonly FullGameContractAgent _inner = new(side, runtime);
        public PlayerSide ControlledSide => side;
        public void Observe(Observation observation, long actionSequence) => Fail("observe");
        public void CommitDecision(DecisionId decisionId, long revision, ActionId actionId, long actionSequence) => Fail("commit");
        public void DiscardDecision() => Fail("discard");
        public ValueTask<ActionId> ChooseActionAsync(Observation observation, CancellationToken cancellationToken = default) =>
            _inner.ChooseActionAsync(observation, cancellationToken);
        private void Fail(string current)
        {
            if (current != stage) return;
            if (hang) blocked.Wait(CancellationToken.None);
            else throw new InvalidOperationException("Intentional lifecycle failure.");
        }
    }

    private sealed class SharedFactory(IRuntimeCardCatalog runtime) : IBenchmarkAgentFactory
    {
        private readonly IGameAgent _corp = new FullGameContractAgent(PlayerSide.Corp, runtime);
        private readonly IGameAgent _runner = new FullGameContractAgent(PlayerSide.Runner, runtime);
        public void Validate(BenchmarkAgentSpec specification) => specification.Validate();
        public IGameAgent Create(BenchmarkAgentSpec specification, IRuntimeCardCatalog runtime, ICardEvaluationCatalog evaluation) =>
            specification.Side == PlayerSide.Corp ? _corp : _runner;
    }

    private sealed class CyclingFactory : IBenchmarkAgentFactory
    {
        public void Validate(BenchmarkAgentSpec specification) => specification.Validate();
        public IGameAgent Create(BenchmarkAgentSpec specification, IRuntimeCardCatalog runtime, ICardEvaluationCatalog evaluation) => new CyclingAgent();
    }

    private sealed class CyclingAgent : IGameAgent
    {
        public ValueTask<ActionId> ChooseActionAsync(Observation observation, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(observation.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.UseAbility)?.Id
                ?? observation.LegalActions.First(action => action.Kind == ActionKind.PassPriority).Id);
    }
}

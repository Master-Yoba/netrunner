using System.Collections.Immutable;
using Netrunner.Core;
using Xunit;

namespace Netrunner.AI.Tests;

public sealed class PolicyVersionTests
{
    [Theory]
    [InlineData(PlayerSide.Runner, RunnerHeuristicPolicy.V1, "agent-features-v2")]
    [InlineData(PlayerSide.Runner, RunnerHeuristicPolicy.V2, "agent-features-v3")]
    [InlineData(PlayerSide.Corp, CorpHeuristicPolicy.V2, "agent-features-v2")]
    [InlineData(PlayerSide.Corp, CorpHeuristicPolicy.V3, "agent-features-v2")]
    public void EveryVersionConfiguresAndCreatesItsPolicy(PlayerSide side, string version, string featureVersion)
    {
        foreach (string profile in new[] { "standard", "easy" })
        {
            OfflineAgentConfiguration configuration = OfflinePolicies.Configure(side, profile, 7, version);

            Assert.Equal(version, configuration.PolicyVersion);
            Assert.Equal(featureVersion, configuration.FeatureVersion);
            Assert.Equal(version, OfflinePolicies.Create(configuration).Version);
        }
    }

    [Fact]
    public void LatestVersionsAreTheDefaultsAndUnknownVersionsFail()
    {
        Assert.Equal(RunnerHeuristicPolicy.V2, OfflinePolicies.Configure(PlayerSide.Runner, "standard", 1).PolicyVersion);
        Assert.Equal(CorpHeuristicPolicy.V3, OfflinePolicies.Configure(PlayerSide.Corp, "standard", 1).PolicyVersion);
        Assert.Throws<ArgumentException>(() => new RunnerHeuristicPolicy("runner-heuristic-v9"));
        Assert.Throws<ArgumentException>(() => OfflinePolicies.Configure(PlayerSide.Corp, "standard", 1, "corp-heuristic-v1"));
        // A v1 Runner profile can't claim the v3 features it doesn't read.
        OfflineAgentConfiguration v1 = RunnerPolicyProfiles.Create("standard", 1, RunnerHeuristicPolicy.V1);
        Assert.Throws<ArgumentException>(() => RunnerPolicyProfiles.Validate(v1 with { FeatureVersion = AgentFeatures.VersionWithHiddenAdvancements }));
    }

    [Fact]
    public void EasyIsDeliberatelyLessPrecise()
    {
        foreach (PlayerSide side in new[] { PlayerSide.Corp, PlayerSide.Runner })
        {
            OfflineAgentConfiguration easy = OfflinePolicies.Configure(side, "easy", 1);
            OfflineAgentConfiguration standard = OfflinePolicies.Configure(side, "standard", 1);
            Assert.True(easy.NearTieTolerance >= 100, $"{side} Easy tolerance {easy.NearTieTolerance}.");
            Assert.Equal(0, standard.NearTieTolerance);
        }
    }

    [Fact]
    public void HiddenAdvancementsAreReadOnlyByTheNewFeatureVersion()
    {
        Observation observation = RunnerDecision([Run("server:a")], HiddenAdvanced("server:a", 3));

        AgentFeatures v2 = ObservationReader.Read(observation);
        AgentFeatures v3 = ObservationReader.Read(observation, AgentFeatures.VersionWithHiddenAdvancements);

        Assert.Null(v2.Board.HiddenAdvancements);
        Assert.DoesNotContain("hiddenAdvancements", EngineJson.Serialize(v2), StringComparison.Ordinal);
        AgentHiddenAdvancement entry = Assert.Single(v3.Board.HiddenAdvancements!.Value);
        Assert.Equal(("server:a", "root", 0, 3), (entry.ServerId, entry.Zone, entry.Index, entry.AdvancementCounters));
        Assert.NotEqual(ObservationReader.Digest(v2), ObservationReader.Digest(v3));
    }

    [Fact]
    public async Task RunnerV2PrefersTheAdvancedFaceDownRemoteAndV1DoesNot()
    {
        Observation observation = RunnerDecision([Run("server:a"), Run("server:b")], HiddenAdvanced("server:a", 3));

        ImmutableArray<AgentCandidate> v2 = await Evaluate(observation, RunnerHeuristicPolicy.V2);
        ImmutableArray<AgentCandidate> v1 = await Evaluate(observation, RunnerHeuristicPolicy.V1);

        Assert.True(Score(v2, "action:run:server:a") > Score(v2, "action:run:server:b"));
        Assert.Equal(Score(v1, "action:run:server:a"), Score(v1, "action:run:server:b"));
    }

    private static async Task<ImmutableArray<AgentCandidate>> Evaluate(Observation observation, string version)
    {
        OfflineAgentConfiguration configuration = RunnerPolicyProfiles.Create("standard", 54, version);
        var agent = new OfflineAgent(configuration, OfflinePolicies.Create(configuration), new EmptyCatalog(), captureDiagnostics: true);
        agent.Observe(observation, 0);
        await agent.ChooseActionAsync(observation);
        return agent.LastEvaluation!.Candidates;
    }

    private static int Score(ImmutableArray<AgentCandidate> candidates, string id) =>
        candidates.Single(candidate => candidate.ActionId.Value == id).Utility.Score;

    private static LegalAction Run(string server) =>
        new(new($"action:run:{server}"), ActionKind.StartRun, "ignored", new RunServerParameters(new(server)));

    private static AgentHiddenAdvancement HiddenAdvanced(string server, int counters) => new(server, "root", 0, counters);

    /// <summary>Two unprotected remotes with one face-down root card each; the given entry adds public counters.</summary>
    private static Observation RunnerDecision(LegalAction[] actions, AgentHiddenAdvancement advancement)
    {
        GameState state = GameState.CreateNew(54);
        var decision = new ObservationDecision(new("decision:runner-policy"), 1,
            state.Players.Single(player => player.Side == PlayerSide.Runner).Id, PlayerSide.Runner, DecisionKind.TurnAction, "ignored");
        Observation observation = ObservationBuilder.Build(state, PlayerSide.Runner)
            with
        { DecisionId = decision.Id.Value, Decision = decision, LegalActions = actions };
        AgentBoard board = ObservationReader.Read(observation).Board with
        {
            Servers = [new("server:a", ServerKind.Remote, [], [null]), new("server:b", ServerKind.Remote, [], [null])],
            HiddenAdvancements = [advancement],
        };
        return observation with { PublicState = EngineJson.Deserialize<Dictionary<string, object?>>(EngineJson.Serialize(board)) };
    }

    private sealed class EmptyCatalog : ICardEvaluationCatalog
    {
        public string Version => "test-v1";
        public string Fingerprint => "test";
        public IReadOnlyCollection<CardDefinitionId> DefinitionIds => [];
        public bool TryGet(CardDefinitionId definitionId, out CardEvaluationDescriptor descriptor) { descriptor = null!; return false; }
    }
}

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
    [InlineData(PlayerSide.Corp, CorpHeuristicPolicy.V4, "agent-features-v2")]
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

    /// <summary>
    /// Bot plan S3: every version constructs, the latest as the live policy and every earlier one as the frozen legacy copy.
    /// </summary>
    [Theory]
    [InlineData(PlayerSide.Corp)]
    [InlineData(PlayerSide.Runner)]
    public void EveryVersionRoutesToTheLiveOrTheLegacyPolicy(PlayerSide side)
    {
        IReadOnlyList<string> versions = side == PlayerSide.Corp ? CorpHeuristicPolicy.Versions : RunnerHeuristicPolicy.Versions;
        string live = side == PlayerSide.Corp ? CorpHeuristicPolicy.PolicyVersion : RunnerHeuristicPolicy.PolicyVersion;
        foreach (string version in versions)
        {
            IOfflinePolicy policy = OfflinePolicies.Create(OfflinePolicies.Configure(side, "standard", 7, version));
            Assert.Equal(version, policy.Version);
            Type expected = version == live
                ? side == PlayerSide.Corp ? typeof(CorpHeuristicPolicy) : typeof(RunnerHeuristicPolicy)
                : side == PlayerSide.Corp ? typeof(LegacyCorpPolicy) : typeof(LegacyRunnerPolicy);
            Assert.IsType(expected, policy);
        }
        Assert.Throws<ArgumentException>(() => side == PlayerSide.Corp ? new CorpHeuristicPolicy(CorpHeuristicPolicy.V30) : new RunnerHeuristicPolicy(RunnerHeuristicPolicy.V26));
        Assert.Throws<ArgumentException>(() => side == PlayerSide.Corp ? new LegacyCorpPolicy(CorpHeuristicPolicy.V31) : new LegacyRunnerPolicy(RunnerHeuristicPolicy.V27));
    }

    /// <summary>
    /// Bot plan S3: the legacy files are the policy files as they were at 94a7404 (Corp v30, Runner v26), changed only by the
    /// class's name. A legacy file is never edited; a change that must touch one goes to the live policy instead.
    /// </summary>
    [Fact]
    public void TheLegacyFilesAreThePoliciesFrozenAtS3()
    {
        string legacy = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Netrunner.AI", "Legacy");
        string[] files = [.. Directory.EnumerateFiles(legacy, "*.cs").Select(Path.GetFileName).Order(StringComparer.Ordinal)!];
        Assert.Equal(FrozenHashes.Keys.Order(StringComparer.Ordinal), files);
        foreach (string file in files)
        {
            string original = File.ReadAllText(Path.Combine(legacy, file))
                .Replace("LegacyCorpPolicy", "CorpHeuristicPolicy", StringComparison.Ordinal)
                .Replace("LegacyRunnerPolicy", "RunnerHeuristicPolicy", StringComparison.Ordinal);
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(original))).ToLowerInvariant();
            Assert.True(hash == FrozenHashes[file], $"{file} differs from the policy it froze.");
        }
    }

    private static readonly Dictionary<string, string> FrozenHashes = new(StringComparer.Ordinal)
    {
        ["LegacyCorpPolicy.Choices.cs"] = "79e4f9931190899d3b47b864c9945d5da7bc04985575e620bf37e8371bad4a6e",
        ["LegacyCorpPolicy.cs"] = "dc7979204a9f7a1c0ae63e10dbb9ffabecdcb1459d2a30d5f4160f84dfee8f26",
        ["LegacyCorpPolicy.Development.cs"] = "d8c32b7364d5bf062f009f21a9ef980dc2c421af0c5d1ea8d4a2bcf1b3c9e631",
        ["LegacyCorpPolicy.Economy.cs"] = "8f1a4d7bcc706b5d0a9bafd33535bef9e83d9141fd74e3932dce96222df2ac76",
        ["LegacyRunnerPolicy.Choices.cs"] = "37730e985229a526cdf9d0021dd82e9ba0f51f3417b3cdc397fe02ae54361335",
        ["LegacyRunnerPolicy.cs"] = "278938ff83c24c5c8f0f220e488b3128f436ff437f2ea6e02c373d1169aae1b4",
        ["LegacyRunnerPolicy.Development.cs"] = "e252b57c7c01936fd272d3bbd9d0ab633ad9a21c1cbd30aff4d14ea0d7aab5eb",
        ["LegacyRunnerPolicy.Economy.cs"] = "45a8a986290c916118e53c855df6d073bc0d44a67ff265f8c15d7cd9abc038f3",
        ["LegacyRunnerPolicy.Runs.cs"] = "a402365e86917f7ac66416e5a15fdc06b10a728f26c7778127faa745e823f696",
    };

    [Fact]
    public void LatestVersionsAreTheDefaultsAndUnknownVersionsFail()
    {
        Assert.Equal(RunnerHeuristicPolicy.V27, OfflinePolicies.Configure(PlayerSide.Runner, "standard", 1).PolicyVersion);
        Assert.Equal(CorpHeuristicPolicy.V31, OfflinePolicies.Configure(PlayerSide.Corp, "standard", 1).PolicyVersion);
        Assert.Throws<ArgumentException>(() => new LegacyRunnerPolicy("runner-heuristic-v28"));
        Assert.Throws<ArgumentException>(() => OfflinePolicies.Configure(PlayerSide.Runner, "standard", 1, "runner-heuristic-v28"));
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

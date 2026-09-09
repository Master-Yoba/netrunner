using System.Text.Json;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;
using Xunit;

namespace Netrunner.Cli.Tests;

public sealed class SessionCommandTests
{
    [Fact]
    public async Task RecordedSessionCanBeVerifiedAndExportedWithoutAdvancingItsDecision()
    {
        string root = Path.Combine(Path.GetTempPath(), $"session-cli-{Guid.NewGuid():N}");
        string trace = Path.Combine(root, "trace");
        Directory.CreateDirectory(root);
        try
        {
            FullMatchDefinition definition = FullMatchDefinition.Developer(47042);
            var opponent = new FullGameContractAgent(new FullGameContractPolicy(PlayerSide.Runner), definition.Runtime);
            await using (var coordinator = new MatchCoordinator(definition, opponent,
                new MatchCoordinatorOptions { HumanSide = PlayerSide.Corp, SessionTraceDirectory = trace }))
            {
                await coordinator.StartAsync();
            }
            using var output = new StringWriter();
            using var error = new StringWriter();
            string replay = Path.Combine(root, "recovered-replay.json");
            string snapshot = Path.Combine(root, "recovered-state.json");
            Assert.Equal(0, CliCommands.Run(["session-recover", trace, "--at-action", "0",
                "--replay-out", replay, "--snapshot-out", snapshot, "--json"], output, error));
            Assert.Empty(error.ToString());
            using JsonDocument result = JsonDocument.Parse(output.ToString());
            Assert.Equal("ok", result.RootElement.GetProperty("status").GetString());
            GameSnapshot state = EngineJson.Deserialize<GameSnapshot>(File.ReadAllText(snapshot), definition.Behavior, definition.Runtime);
            Assert.Equal(0, state.ActionSequence);
            Assert.NotNull(state.PendingDecision);
            Assert.Equal(DeterministicHash.ForState(state.ToState()), result.RootElement.GetProperty("stateHash").GetString());
            output.GetStringBuilder().Clear();
            Assert.Equal(0, CliCommands.Run(["replay", replay, "--json"], output, error));
            Assert.Empty(error.ToString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RecoveryExportsCannotOverwriteTraceSourceFiles()
    {
        (string root, string trace) = await RecordInitialSession();
        try
        {
            string[] sourceFiles = Directory.GetFiles(trace, "*", SearchOption.AllDirectories);
            Assert.True(sourceFiles.Length >= 2, "A recorded session must contain more than one source artifact.");

            foreach ((string option, string sourceFile) in new[]
            {
                ("--replay-out", sourceFiles[0]),
                ("--snapshot-out", sourceFiles[1]),
            })
            {
                byte[] before = File.ReadAllBytes(sourceFile);
                using var output = new StringWriter();
                using var error = new StringWriter();
                Assert.Equal(1, CliCommands.Run(
                    ["session-recover", trace, "--at-action", "0", option, sourceFile, "--json"],
                    output,
                    error));
                Assert.Empty(error.ToString());
                using JsonDocument result = JsonDocument.Parse(output.ToString());
                Assert.Equal("failed", result.RootElement.GetProperty("status").GetString());
                string message = result.RootElement.GetProperty("message").GetString() ?? string.Empty;
                Assert.True(
                    message.Contains("overwrite", StringComparison.OrdinalIgnoreCase)
                        || message.Contains("source", StringComparison.OrdinalIgnoreCase)
                        || message.Contains("trace", StringComparison.OrdinalIgnoreCase),
                    message);
                Assert.Equal(before, File.ReadAllBytes(sourceFile));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MissingSessionDirectoryReturnsStructuredFailure()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"missing-session-{Guid.NewGuid():N}");
        Assert.False(Directory.Exists(missing));
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(1, CliCommands.Run(["session-recover", missing, "--json"], output, error));
        Assert.Empty(error.ToString());
        using JsonDocument result = JsonDocument.Parse(output.ToString());
        Assert.Equal("failed", result.RootElement.GetProperty("status").GetString());
        string message = result.RootElement.GetProperty("message").GetString() ?? string.Empty;
        Assert.Contains("session", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("directory", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecoveryRejectsActionCheckpointBeyondLastCommittedAction()
    {
        (string root, string trace) = await RecordInitialSession();
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(1, CliCommands.Run(["session-recover", trace, "--at-action", "1", "--json"], output, error));
            Assert.Empty(error.ToString());
            using JsonDocument result = JsonDocument.Parse(output.ToString());
            Assert.Equal("failed", result.RootElement.GetProperty("status").GetString());
            string message = result.RootElement.GetProperty("message").GetString() ?? string.Empty;
            Assert.Contains("action", message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("durable", message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OfficialRunEventSessionExportsSnapshotWithItsCatalogs()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        string root = Path.Combine(Path.GetTempPath(), $"session-run-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            CardDataSet data = CardDataResource.LoadEmbedded();
            BehaviorCatalog behavior = GatewayBehaviorCatalog.Create();
            RuntimeCardCatalog runtime = GatewayRuntimeCardCatalog.Create(data);
            MatchRequest request = MatchRequestComposer.ComposeOfficial(
                "system-gateway-basic-corp", "system-gateway-basic-runner", data, 47042, behaviorCatalog: behavior);
            GameState state = GameEngine.CreateMatch(request.Seed, request.CorpDeck, request.RunnerDeck,
                request.CardDataVersion, behavior, runtime).State;
            var agents = new Dictionary<PlayerSide, FullGameContractAgent>
            {
                [PlayerSide.Corp] = new(new FullGameContractPolicy(PlayerSide.Corp), runtime),
                [PlayerSide.Runner] = new(new FullGameContractPolicy(PlayerSide.Runner), runtime),
            };
            for (int step = 0; step < 100 && state.RunState?.ActiveRunEventId is null; step++)
            {
                PendingDecision pending = state.PendingDecision!;
                PlayerSide side = state.GetPlayer(pending.Owner).Side;
                Observation observation = ObservationBuilder.Build(state, side, behavior);
                ActionId action = await agents[side].ChooseActionAsync(observation, CancellationToken.None);
                ApplyResult applied = GameEngine.Apply(state,
                    new ActionSubmission(pending.Owner, pending.Id, pending.Revision, action), behavior, runtime);
                Assert.True(applied.Accepted, applied.Rejection?.Message);
                state = applied.State;
            }
            Assert.NotNull(state.RunState?.ActiveRunEventId);
            string trace = Path.Combine(root, "trace");
            using (SessionTrace writer = SessionTrace.CreateNew(trace, state, PlayerSide.Corp, behavior, runtime, request)) { }
            string snapshot = Path.Combine(root, "snapshot.json");
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(0, CliCommands.Run(["session-recover", trace, "--snapshot-out", snapshot], output, error));
            Assert.Empty(error.ToString());
            GameSnapshot exported = EngineJson.Deserialize<GameSnapshot>(File.ReadAllText(snapshot), behavior, runtime);
            Assert.Equal(DeterministicHash.ForState(state), DeterministicHash.ForState(exported.ToState()));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("--at-action", "-1")]
    [InlineData("--at-action", "abc")]
    [InlineData("--at-action", "1", "--at-action", "2")]
    [InlineData("--snapshot-out")]
    [InlineData("--unknown", "x")]
    public void InvalidRecoveryOptionsFailClearly(params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(1, CliCommands.Run(["session-recover", "missing-session", "--json", .. arguments], output, error));
        Assert.Empty(error.ToString());
        using JsonDocument result = JsonDocument.Parse(output.ToString());
        Assert.Equal("failed", result.RootElement.GetProperty("status").GetString());
        Assert.False(string.IsNullOrEmpty(result.RootElement.GetProperty("message").GetString()));
    }

    private static async Task<(string Root, string Trace)> RecordInitialSession()
    {
        string root = Path.Combine(Path.GetTempPath(), $"session-cli-regression-{Guid.NewGuid():N}");
        string trace = Path.Combine(root, "trace");
        Directory.CreateDirectory(root);
        try
        {
            FullMatchDefinition definition = FullMatchDefinition.Developer(47042);
            var opponent = new FullGameContractAgent(new FullGameContractPolicy(PlayerSide.Runner), definition.Runtime);
            await using var coordinator = new MatchCoordinator(definition, opponent,
                new MatchCoordinatorOptions { HumanSide = PlayerSide.Corp, SessionTraceDirectory = trace });
            await coordinator.StartAsync();
            return (root, trace);
        }
        catch
        {
            Directory.Delete(root, recursive: true);
            throw;
        }
    }
}

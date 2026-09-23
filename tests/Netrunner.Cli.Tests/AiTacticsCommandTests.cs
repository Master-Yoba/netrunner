using System.Text.Json;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;
using Xunit;

namespace Netrunner.Cli.Tests;

public sealed class AiTacticsCommandTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "netrunner-tactics-cli-" + Guid.NewGuid().ToString("N"));

    public AiTacticsCommandTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void InitEitherPinsOfficialInputsOrExplicitlyRejectsSyntheticContent()
    {
        string path = Path.Combine(_directory, "manifest.json");
        var result = Run("init", "--output", path);
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
        {
            Assert.Equal(2, result.Code);
            Assert.False(File.Exists(path));
            Assert.Contains("official", result.Json.GetProperty("message").GetString(), StringComparison.Ordinal);
            return;
        }
        Assert.Equal(0, result.Code);
        Assert.Equal(80, result.Json.GetProperty("positions").GetInt32());
        TacticalBenchmarkManifest manifest = EngineJson.Deserialize<TacticalBenchmarkManifest>(File.ReadAllText(path));
        Assert.Equal(80, manifest.Positions.Length);
        Assert.Equal(2, Run("init", "--output", path).Code);
        var proof = Run(path, "--output", Path.Combine(_directory, "proof"), "--verify-fixtures", "--workers", "3");
        Assert.Equal(0, proof.Code);
        Assert.Equal("fixture-validation", proof.Json.GetProperty("mode").GetString());
        Assert.True(proof.Json.GetProperty("passed").GetBoolean());
        var baseline = Run(path, "--output", Path.Combine(_directory, "baseline"));
        Assert.Equal(1, baseline.Code);
        Assert.Equal("policy-evaluation", baseline.Json.GetProperty("mode").GetString());
    }

    [Theory]
    [InlineData("standard")]
    [InlineData("easy")]
    public void CorpProfileInitRetainsFrozenCasesAndRecordsExactConfiguration(string profile)
    {
        string path = Path.Combine(_directory, "corp.json");
        var result = Run("init", "--corp-profile", profile, "--output", path);
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
        {
            Assert.Equal(2, result.Code);
            return;
        }
        Assert.Equal(0, result.Code);
        TacticalBenchmarkManifest baseline = new TacticalBenchmarkRunner(CardDataResource.LoadEmbedded()).CreateContractManifest();
        TacticalBenchmarkManifest manifest = EngineJson.Deserialize<TacticalBenchmarkManifest>(File.ReadAllText(path));
        Assert.Equal(40, manifest.Positions.Length);
        Assert.All(manifest.Positions, position =>
        {
            TacticalPositionSpec original = baseline.Positions.Single(value => value.Metadata.Id == position.Metadata.Id);
            Assert.Equal(original.InitialStateHash, position.InitialStateHash);
            Assert.Equal(original.HiddenTwinStateHash, position.HiddenTwinStateHash);
            Assert.Equal(original.Agent.PolicySeed, position.Agent.PolicySeed);
            Assert.Equal(PlayerSide.Corp, position.Agent.Side);
            Assert.Equal(profile, position.Agent.Configuration!.ProfileId);
            new BenchmarkAgentFactory().Validate(position.Agent);
        });
    }

    [Theory]
    [InlineData("standard")]
    [InlineData("easy")]
    public void RunnerProfileInitRetainsFrozenCasesAndRecordsExactConfiguration(string profile)
    {
        string path = Path.Combine(_directory, "runner.json");
        var result = Run("init", "--runner-profile", profile, "--output", path);
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
        {
            Assert.Equal(2, result.Code);
            return;
        }
        Assert.Equal(0, result.Code);
        TacticalBenchmarkManifest baseline = new TacticalBenchmarkRunner(CardDataResource.LoadEmbedded()).CreateContractManifest();
        TacticalBenchmarkManifest manifest = EngineJson.Deserialize<TacticalBenchmarkManifest>(File.ReadAllText(path));
        Assert.Equal(40, manifest.Positions.Length);
        Assert.All(manifest.Positions, position =>
        {
            TacticalPositionSpec original = baseline.Positions.Single(value => value.Metadata.Id == position.Metadata.Id);
            Assert.Equal(original.InitialStateHash, position.InitialStateHash);
            Assert.Equal(original.HiddenTwinStateHash, position.HiddenTwinStateHash);
            Assert.Equal(original.Agent.PolicySeed, position.Agent.PolicySeed);
            Assert.Equal(PlayerSide.Runner, position.Agent.Side);
            Assert.Equal(profile, position.Agent.Configuration!.ProfileId);
            new BenchmarkAgentFactory().Validate(position.Agent);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("missing --output unused --workers 0")]
    [InlineData("init --output unused --verify-fixtures")]
    [InlineData("init --corp-profile hard --output unused")]
    [InlineData("init --runner-profile hard --output unused")]
    [InlineData("init --corp-profile standard --runner-profile standard --output unused")]
    [InlineData("missing --output unused --json")]
    public void MalformedInputIsVersionedAndCreatesNoEvidence(string arguments)
    {
        var result = Run(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(2, result.Code);
        Assert.Equal("m5-tactical-error", result.Json.GetProperty("kind").GetString());
        Assert.Empty(Directory.EnumerateFileSystemEntries(_directory));
    }

    private static (int Code, JsonElement Json) Run(params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int code = CliCommands.Run(["ai-tactics", .. arguments, "--json"], output, error);
        Assert.Equal("", error.ToString());
        using JsonDocument json = JsonDocument.Parse(output.ToString());
        return (code, json.RootElement.Clone());
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}

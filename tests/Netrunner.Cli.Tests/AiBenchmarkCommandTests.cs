using System.Collections.Immutable;
using System.Text.Json;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;
using Xunit;

namespace Netrunner.Cli.Tests;

public sealed class AiBenchmarkCommandTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "netrunner-benchmark-cli-" + Guid.NewGuid().ToString("N"));

    public AiBenchmarkCommandTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void CorpProfileUsesTheSameGamesAndLeavesTheRunnerBaselinePinned()
    {
        string path = Path.Combine(_directory, "corp.json");
        var result = Run("init", "smoke", "--corp-profile", "standard", "--output", path);
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
        {
            Assert.Equal(2, result.Code);
            return;
        }
        Assert.Equal(0, result.Code);
        var runner = new AiBenchmarkRunner(CardDataResource.LoadEmbedded(), GatewayBehaviorCatalog.Create());
        AiBenchmarkManifest baseline = BenchmarkSuites.CreateContract(runner, "smoke");
        AiBenchmarkManifest manifest = EngineJson.Deserialize<AiBenchmarkManifest>(File.ReadAllText(path));
        Assert.Equal(baseline.Games.Length, manifest.Games.Length);
        Assert.All(manifest.Games, game =>
        {
            BenchmarkGameSpec original = baseline.Games.Single(value => value.Id == game.Id);
            Assert.Equal(original.GameSeed, game.GameSeed);
            Assert.Equal(original.CorpDeckHash, game.CorpDeckHash);
            Assert.Equal(original.Runner, game.Runner);
            Assert.Equal(original.Corp.PolicySeed, game.Corp.PolicySeed);
            Assert.Equal("standard", game.Corp.Configuration!.ProfileId);
            new BenchmarkAgentFactory().Validate(game.Corp);
        });
    }

    [Fact]
    public void InitCreatesOfflinePinnedManifestAndRefusesToOverwriteIt()
    {
        string path = Path.Combine(_directory, "manifest.json");
        (int code, JsonElement created) = Run("init", "developer", "--output", path);
        Assert.Equal(0, code);
        Assert.Equal(20, created.GetProperty("games").GetInt32());
        AiBenchmarkManifest manifest = EngineJson.Deserialize<AiBenchmarkManifest>(File.ReadAllText(path));
        manifest.Validate();
        Assert.Equal("developer", manifest.ContentMode);
        string contents = File.ReadAllText(path);
        Assert.Equal(2, Run("init", "developer", "--output", path).Code);
        Assert.Equal(contents, File.ReadAllText(path));
    }

    [Fact]
    public void ExecutionReportsCompletionAndCapsWithDistinctExitCodes()
    {
        DeveloperMatchCards developer = DeveloperMatchCards.Create();
        var runner = new AiBenchmarkRunner(developer.Data, developer.Behavior);
        AiBenchmarkManifest manifest = BenchmarkSuites.CreateContract(runner, "developer");
        manifest = manifest with { Games = manifest.Games.Take(1).ToImmutableArray() };
        string path = Path.Combine(_directory, "manifest.json");
        File.WriteAllText(path, EngineJson.Serialize(manifest));
        (int code, JsonElement report) = Run(path, "--output", Path.Combine(_directory, "complete"), "--workers", "2");
        Assert.Equal(0, code);
        Assert.Equal("m5-ai-benchmark-report", report.GetProperty("kind").GetString());
        Assert.Equal(1, report.GetProperty("aggregate").GetProperty("terminal").GetInt32());
        File.WriteAllText(path, EngineJson.Serialize(manifest with { Limits = new BenchmarkLimits(Actions: 1) }));
        (code, report) = Run(path, "--output", Path.Combine(_directory, "cap"));
        Assert.Equal(1, code);
        Assert.Equal("actionCap", report.GetProperty("games")[0].GetProperty("result").GetProperty("termination").GetString());
        Assert.True(report.GetProperty("games")[0].GetProperty("result").GetProperty("replayVerified").GetBoolean());
    }

    [Theory]
    [InlineData("")]
    [InlineData("init unknown --output unused")]
    [InlineData("init developer --corp-profile standard --output unused")]
    [InlineData("missing.json --output unused --workers 0")]
    [InlineData("missing.json --output unused --workers 65")]
    [InlineData("missing.json --output unused --workers 1 --workers 2")]
    [InlineData("missing.json --output unused --seed 42")]
    public void InvalidInvocationsReturnVersionedJsonWithoutCreatingEvidence(string arguments)
    {
        (int code, JsonElement result) = Run(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(2, code);
        Assert.Equal("m5-ai-benchmark-error", result.GetProperty("kind").GetString());
        Assert.Equal("invalid", result.GetProperty("status").GetString());
        Assert.Empty(Directory.EnumerateFileSystemEntries(_directory));
    }

    private static (int Code, JsonElement Result) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int code = CliCommands.Run(["ai-benchmark", .. args, "--json"], output, error);
        Assert.Equal("", error.ToString());
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        return (code, document.RootElement.Clone());
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}

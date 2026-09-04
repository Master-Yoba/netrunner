using System.Text.Json;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;
using Xunit;

namespace Netrunner.Cli.Tests;

public sealed class M2ProofCommandTests
{
    // Pinned from a green engine run (M2 milestone proof, slice 4; re-pinned
    // by M2 rework U5 and kept stable by U6 — the agenda mid-access window
    // (pass only) and the automatic 7.2.3 steal emit the same event stream
    // as the pre-U6 player-steal action, see M2ProofTests); the same
    // constants M2ProofTests pins for both policies. Slice 4.5 adds the
    // serialized approach occurrence (schema 9 -> 10), then targeted run-event
    // play and breach provenance (schema 10 -> 11). The explicit 6.9.4g
    // server-approach event also changes the event-stream pin.
    private const string ExpectedFinalStateHash =
        "7da1beb7aa61527ffa4e0cb9095397cc7809d481f34d1966826991ac5d411486";
    private const string ExpectedEventStreamHash =
        "33af2b049fbad324ed5ff0ee130740c3d65c03f2cb1196100afbdc73e9f0e67c";

    private static BehaviorCatalog BuildLegacyGatewayCatalog(BehaviorCatalog gateway)
    {
        return new BehaviorCatalogBuilder(gateway.Version)
            .AddIce(gateway.IceSpecs)
            .AddIcebreaker(gateway.IcebreakerSpecs)
            .AddAsset(gateway.AssetSpecs)
            .AddAgenda(gateway.AgendaSpecs)
            .AddRunEvent(gateway.RunEventSpecs)
            .Build();
    }

    [Fact]
    public void M2ProofCommandSucceedsAndReportsPinnedHashes()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "M2Scenarios", "representative-run.json");
        var output = new StringWriter();
        var error = new StringWriter();

        int code = CliCommands.Run(["m2-proof", path, "--json"], output, error);

        Assert.Equal(0, code);
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        JsonElement root = document.RootElement;
        Assert.Equal("m2:representative-run", root.GetProperty("scenarioId").GetString());
        Assert.True(root.GetProperty("replayVerified").GetBoolean());
        Assert.Empty(root.GetProperty("traceProblems").EnumerateArray().Select(element => element.GetString()));
        Assert.Equal(ExpectedFinalStateHash, root.GetProperty("finalStateHash").GetString());
        Assert.Equal(ExpectedEventStreamHash, root.GetProperty("eventStreamHash").GetString());
        Assert.True(root.GetProperty("checkpoints").GetInt32() > 0);
        Assert.True(root.GetProperty("replayActions").GetInt32() > 0);
        JsonElement policies = root.GetProperty("policies");
        Assert.Equal(ExpectedFinalStateHash, policies.GetProperty("scripted-human").GetProperty("finalStateHash").GetString());
        Assert.Equal(ExpectedEventStreamHash, policies.GetProperty("scripted-human").GetProperty("eventStreamHash").GetString());
        Assert.Equal(ExpectedFinalStateHash, policies.GetProperty("contract-agent").GetProperty("finalStateHash").GetString());
        Assert.Equal(ExpectedEventStreamHash, policies.GetProperty("contract-agent").GetProperty("eventStreamHash").GetString());
    }

    [Fact]
    public void M2ProofCommandReplaysAgainstTheScenarioSliceOfAnExpandedCatalog()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "M2Scenarios", "representative-run.json");
        BehaviorCatalog gateway = GatewayBehaviorCatalog.Create();
        Assert.Equal("gateway-m4-4.5-c1", gateway.Version);
        BehaviorCatalog legacy = BuildLegacyGatewayCatalog(gateway);
        BehaviorCatalog expanded = new BehaviorCatalogBuilder(legacy.Version, legacy)
            .Add(new CardBehaviorDefinition(
                GatewayCardCodes.NicoCampaign,
                PlayerSide.Corp,
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]))
            .Build();
        M2ScenarioDocument scenario = EngineJson.Deserialize<M2ScenarioDocument>(File.ReadAllText(path));
        BehaviorCatalog scenarioSlice = scenario.CreateBehaviorCatalog(expanded);
        Assert.Equal(EngineCompatibility.GatewayBehaviorRegistryVersion, scenarioSlice.Version);
        Assert.Equal(scenario.BehaviorCatalogFingerprint, scenarioSlice.Fingerprint);
        var output = new StringWriter();
        var error = new StringWriter();

        int code = CliCommands.RunWithBehaviorCatalog(["m2-proof", path, "--json"], output, error, expanded);

        Assert.Equal(0, code);
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        Assert.True(document.RootElement.GetProperty("replayVerified").GetBoolean());
        Assert.Equal(ExpectedFinalStateHash, document.RootElement.GetProperty("finalStateHash").GetString());
        Assert.Equal(ExpectedEventStreamHash, document.RootElement.GetProperty("eventStreamHash").GetString());
    }

    [Fact]
    public void M2ProofCommandFailsForMissingScenarioFile()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "M2Scenarios", "does-not-exist.json");
        var output = new StringWriter();
        var error = new StringWriter();

        int code = CliCommands.Run(["m2-proof", path, "--json"], output, error);

        Assert.NotEqual(0, code);
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        JsonElement root = document.RootElement;
        Assert.Equal("failed", root.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("message").GetString()));
    }
}

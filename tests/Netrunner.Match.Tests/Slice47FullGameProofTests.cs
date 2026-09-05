using System.Text.Json;

using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;
using Xunit.Abstractions;

namespace Netrunner.Match.Tests;

public sealed class Slice47FullGameProofTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("basic")]
    [InlineData("expanded")]
    public async Task CompleteCatalogAndProvisionedPresetsReachReplayedOutcomes(string kind)
    {
        BehaviorCatalog behavior = GatewayBehaviorCatalog.Create();
        Assert.Equal(77, behavior.AllDefinitionIds.Count);
        string repository = Assert.IsType<string>(FindRepository());
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(repository, "data", "m4", "scenarios", "scn-403-official-presets.json")));
        JsonElement scenario = fixture.RootElement;
        Assert.Equal("scn-403-official-presets", scenario.GetProperty("scenarioId").GetString());
        Assert.Equal(EngineCompatibility.StateSchemaVersion, scenario.GetProperty("engineStateSchemaVersion").GetInt32());
        Assert.Equal(behavior.Version, scenario.GetProperty("behaviorCatalogVersion").GetString());
        Assert.Equal(FullGameContractAgentVersion.AgentVersion, scenario.GetProperty("policyVersion").GetString());
        JsonElement expectation = Assert.Single(scenario.GetProperty("cases").EnumerateArray(),
            value => value.GetProperty("preset").GetString() == kind);
        int expectedActionCount = expectation.GetProperty("actionCount").GetInt32();
        PlayerSide expectedWinner = Enum.Parse<PlayerSide>(expectation.GetProperty("winner").GetString()!);
        GameWinReason expectedReason = Enum.Parse<GameWinReason>(expectation.GetProperty("reason").GetString()!);
        ulong seed = scenario.GetProperty("seed").GetUInt64();
        if (!File.Exists(Path.Combine(repository, CardDataLoader.DefaultRelativeDirectory, "cards.json")))
        {
            output.WriteLine("Official full-game proof requires the authenticated content lane; catalog completeness verified in synthetic CI.");
            return;
        }
        CardDataSet data = CardDataLoader.LoadSystemGateway(repository);
        MatchRequest request = MatchRequestComposer.ComposeOfficial($"system-gateway-{kind}-corp",
            $"system-gateway-{kind}-runner", data, seed: seed, behaviorCatalog: behavior);
        FullMatchDefinition definition = FullMatchDefinition.FromRequest(request, data, behavior);
        var trace = new List<string>();
        FullMatchResult result;
        try
        {
            result = await FullMatchRunner.RunAsync(definition, actionCap: scenario.GetProperty("actionCap").GetInt32(),
                corpAgent: new TracedAgent(new FullGameContractAgent(new FullGameContractPolicy(PlayerSide.Corp), definition.Runtime), trace),
                runnerAgent: new TracedAgent(new FullGameContractAgent(new FullGameContractPolicy(PlayerSide.Runner), definition.Runtime), trace));
        }
        catch
        {
            foreach (string entry in trace.TakeLast(8)) output.WriteLine(entry);
            throw;
        }
        output.WriteLine($"SLICE47_FULL_GAME preset={kind} seed={seed} actions={result.ActionCount} winner={result.Outcome.Winner} reason={result.Outcome.Reason} hash={result.FinalStateHash}");
        Assert.Equal(GameOutcomeKind.Won, result.Outcome.Kind);
        Assert.Equal(expectedWinner, result.Outcome.Winner);
        Assert.Equal(expectedReason, result.Outcome.Reason);
        Assert.Equal(expectedActionCount, result.ActionCount);
        Assert.IsType<SetupFrame>(Assert.Single(result.Replay.InitialSnapshot.ToState().ResolutionStack));
    }

    private sealed class TracedAgent(IGameAgent inner, List<string> trace) : IGameAgent
    {
        public async ValueTask<ActionId> ChooseActionAsync(Observation observation, CancellationToken cancellationToken = default)
        {
            ActionId id = await inner.ChooseActionAsync(observation, cancellationToken);
            trace.Add($"{trace.Count}: {EngineJson.Serialize(observation.LegalActions.Single(action => action.Id == id))}");
            return id;
        }
    }

    private static string? FindRepository()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Netrunner.sln"))) return directory.FullName;
        return null;
    }
}

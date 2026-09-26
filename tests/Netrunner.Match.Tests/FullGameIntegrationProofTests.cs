using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;
using Xunit.Abstractions;

namespace Netrunner.Match.Tests;

/// <summary>
/// Slice 4.8 proof for the official full-game path when the scripted human
/// takes one real mulligan. FullMatchRunner owns the per-yield rehydration and
/// final replay verification; this test supplies the human-side choice and a
/// bounded action trace for diagnostics.
/// </summary>
public sealed class FullGameIntegrationProofTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("basic", PlayerSide.Corp)]
    [InlineData("basic", PlayerSide.Runner)]
    [InlineData("expanded", PlayerSide.Corp)]
    [InlineData("expanded", PlayerSide.Runner)]
    public async Task OfficialPresetWithForcedHumanMulliganReplaysEveryYield(
        string kind,
        PlayerSide humanSide)
    {
        string repository = Assert.IsType<string>(FindRepository());
        string cardsPath = Path.Combine(repository, CardDataLoader.DefaultRelativeDirectory, "cards.json");
        if (!File.Exists(cardsPath))
        {
            output.WriteLine("Official full-game proof requires the authenticated content lane; skipped without local cards.json.");
            return;
        }

        BehaviorCatalog behavior = GatewayBehaviorCatalog.Create();
        CardDataSet data = CardDataLoader.LoadSystemGateway(repository);
        ulong seed = 47042;
        MatchRequest request = MatchRequestComposer.ComposeOfficial(
            $"system-gateway-{kind}-corp",
            $"system-gateway-{kind}-runner",
            data,
            seed,
            OfficialPresetCatalog.Embedded(),
            behaviorCatalog: behavior);
        FullMatchDefinition definition = FullMatchDefinition.FromRequest(request, data, behavior);

        var trace = new ActionTrace();
        IGameAgent corp = new TracedAgent(
            new FullGameContractAgent(new FullGameContractPolicy(PlayerSide.Corp), definition.Runtime),
            trace,
            humanSide == PlayerSide.Corp);
        IGameAgent runner = new TracedAgent(
            new FullGameContractAgent(new FullGameContractPolicy(PlayerSide.Runner), definition.Runtime),
            trace,
            humanSide == PlayerSide.Runner);

        try
        {
            FullMatchResult result = await FullMatchRunner.RunAsync(
                definition,
                actionCap: 4000,
                corpAgent: corp,
                runnerAgent: runner);

            Assert.Equal(GameOutcomeKind.Won, result.Outcome.Kind);
            Assert.True(result.ActionCount > 0);
            Assert.Equal(1, trace.MulliganCount);
            Assert.Equal(result.ActionCount, trace.Actions.Count);
            Assert.NotEmpty(result.Replay.Actions);
            output.WriteLine(
                $"SLICE48_FULL_GAME preset={kind} human={humanSide} seed={seed} actions={result.ActionCount} "
                + $"winner={result.Outcome.Winner} reason={result.Outcome.Reason} hash={result.FinalStateHash} replay=PASS mulligan=true");
        }
        catch
        {
            WriteFailure(kind, humanSide, seed, trace);
            throw;
        }
    }

    private void WriteFailure(
        string kind,
        PlayerSide humanSide,
        ulong seed,
        ActionTrace trace)
    {
        output.WriteLine(
            $"SLICE48_FULL_GAME FAIL preset={kind} human={humanSide} seed={seed} "
            + $"mulligans={trace.MulliganCount} traced_actions={trace.Actions.Count}");

        foreach (TraceEntry entry in trace.Actions.TakeLast(8))
        {
            output.WriteLine(
                $"accepted index={entry.Index} side={entry.Side} decision={entry.Submission.DecisionId.Value} "
                + $"revision={entry.Submission.Revision} action={entry.ActionJson}");
        }
    }

    private sealed class TracedAgent(IGameAgent inner, ActionTrace trace, bool forceMulligan) : IGameAgent
    {
        private bool _mulliganUsed;

        public async ValueTask<ActionId> ChooseActionAsync(
            Observation observation,
            CancellationToken cancellationToken = default)
        {
            ActionId actionId;
            if (forceMulligan
                && !_mulliganUsed
                && observation.Decision?.Kind == DecisionKind.Setup
                && observation.LegalActions.FirstOrDefault(action =>
                    action.Parameters is NumberChoiceParameters { Value: 1 }) is { } mulligan)
            {
                _mulliganUsed = true;
                trace.MulliganCount++;
                actionId = mulligan.Id;
            }
            else
            {
                actionId = await inner.ChooseActionAsync(observation, cancellationToken).ConfigureAwait(false);
            }

            LegalAction selected = observation.LegalActions.Single(action => action.Id == actionId);
            ObservationDecision decision = observation.Decision
                ?? throw new InvalidOperationException("The traced agent received no issued decision.");
            trace.Actions.Add(new TraceEntry(
                trace.Actions.Count,
                decision.OwnerSide,
                new ActionSubmission(decision.Owner, decision.Id, decision.Revision, actionId),
                EngineJson.Serialize(selected)));
            return actionId;
        }
    }

    private sealed class ActionTrace
    {
        public List<TraceEntry> Actions { get; } = [];

        public int MulliganCount { get; set; }
    }

    private sealed record TraceEntry(
        int Index,
        PlayerSide Side,
        ActionSubmission Submission,
        string ActionJson);

    private static string? FindRepository()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Netrunner.sln")))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}

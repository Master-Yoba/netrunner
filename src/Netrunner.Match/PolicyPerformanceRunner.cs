using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;

namespace Netrunner.Match;

public sealed record PolicyTimingSample(PlayerSide Side, DecisionKind Kind, int LegalActions, double Milliseconds);
public sealed record PolicyTimingGroup(PlayerSide Side, string Category, int Samples, int MinimumLegalActions,
    int P95LegalActions, int MaximumLegalActions, double MedianMilliseconds, double P95Milliseconds,
    double MaximumMilliseconds, double BudgetMilliseconds);
public sealed record PolicyPerformanceReport(int SchemaVersion, string Kind, string Hardware, string ManifestHash,
    string BuildRevision, string Runtime, string OperatingSystem, string Architecture,
    ImmutableSortedDictionary<string, string> AssemblyHashes, bool DiagnosticsEnabled,
    ImmutableArray<PolicyTimingGroup> Groups, ImmutableArray<PolicyTimingSample> Samples, bool Passed);

/// <summary>Serial direct policy timings, excluding scheduling, observation construction, rules, disk and rendering.</summary>
public static class PolicyPerformanceRunner
{
    public static async Task<PolicyPerformanceReport> RunAsync(CardDataSet data, IBehaviorCatalog behavior,
        AiBenchmarkManifest manifest, string outputDirectory, string hardware, string buildRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hardware);
        var samples = new ConcurrentQueue<PolicyTimingSample>();
        var factory = new TimedFactory(samples);
        var runner = new AiBenchmarkRunner(data, behavior, factory);
        AiBenchmarkReport games = await runner.RunAsync(manifest, outputDirectory, 1, buildRevision, cancellationToken: cancellationToken).ConfigureAwait(false);
        ImmutableArray<PolicyTimingSample> recorded = samples.ToImmutableArray();
        ImmutableArray<PolicyTimingGroup> groups = Summarize(recorded);
        bool passed = games.Aggregate.Terminal == games.Aggregate.Games && games.Aggregate.Failures == 0
            && games.Games.All(game => game.Result.ReplayVerified) && groups.Length == 4
            && groups.All(group => group.P95Milliseconds <= group.BudgetMilliseconds);
        var report = new PolicyPerformanceReport(1, "m5-policy-performance", hardware, games.ManifestHash,
            buildRevision, games.Runtime, games.OperatingSystem, games.Architecture, games.AssemblyHashes,
            false, groups, recorded, passed);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "performance.json"), EngineJson.Serialize(report), cancellationToken).ConfigureAwait(false);
        return report;
    }

    internal static ImmutableArray<PolicyTimingGroup> Summarize(IEnumerable<PolicyTimingSample> samples) => samples
        .GroupBy(sample => (sample.Side, Category: Ordinary(sample.Kind) ? "ordinary" : "tactical"))
        .OrderBy(group => group.Key.Side).ThenBy(group => group.Key.Category, StringComparer.Ordinal)
        .Select(group =>
        {
            double[] timings = group.Select(sample => sample.Milliseconds).Order().ToArray();
            int[] actions = group.Select(sample => sample.LegalActions).Order().ToArray();
            int p95 = (int)Math.Ceiling(timings.Length * 0.95) - 1;
            return new PolicyTimingGroup(group.Key.Side, group.Key.Category, timings.Length, actions[0], actions[p95], actions[^1],
                timings[(timings.Length - 1) / 2], timings[p95], timings[^1], group.Key.Category == "ordinary" ? 50 : 200);
        }).ToImmutableArray();

    // Everything involving a run, compulsory cleanup or nested choice is tactical.
    private static bool Ordinary(DecisionKind kind) => kind is DecisionKind.Setup or DecisionKind.TurnAction or DecisionKind.TurnTiming;

    private sealed class TimedFactory(ConcurrentQueue<PolicyTimingSample> samples) : IBenchmarkAgentFactory
    {
        public void Validate(BenchmarkAgentSpec specification)
        {
            new BenchmarkAgentFactory().Validate(specification);
            if (specification.Kind != "offline") throw new ArgumentException("Policy timing requires offline policies in both seats.");
        }

        public IGameAgent Create(BenchmarkAgentSpec specification, IRuntimeCardCatalog runtime, ICardEvaluationCatalog evaluation)
        {
            Validate(specification);
            var agent = new OfflineAgent(specification.Configuration!, OfflinePolicies.Create(specification.Configuration!),
                evaluation, captureDiagnostics: false);
            return new TimedAgent(agent, samples);
        }
    }

    private sealed class TimedAgent(OfflineAgent agent, ConcurrentQueue<PolicyTimingSample> samples) : IAgentLifecycle
    {
        public PlayerSide ControlledSide => agent.ControlledSide;
        public void Observe(Observation observation, long actionSequence) => agent.Observe(observation, actionSequence);
        public void CommitDecision(DecisionId decisionId, long revision, ActionId actionId, long actionSequence) =>
            agent.CommitDecision(decisionId, revision, actionId, actionSequence);
        public void DiscardDecision() => agent.DiscardDecision();

        public async ValueTask<ActionId> ChooseActionAsync(Observation observation, CancellationToken cancellationToken = default)
        {
            long start = Stopwatch.GetTimestamp();
            try { return await agent.ChooseActionAsync(observation, cancellationToken).ConfigureAwait(false); }
            finally
            {
                double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                samples.Enqueue(new(observation.Viewer, observation.Decision!.Kind, observation.LegalActions.Count, milliseconds));
            }
        }
    }
}

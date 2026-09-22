using System.Collections.Concurrent;
using System.Collections.Immutable;
using Netrunner.Cards;
using Netrunner.Core;

namespace Netrunner.Match;

public sealed record TacticalPositionSpec(TacticalCaseMetadata Metadata, string InitialStateHash, string? HiddenTwinStateHash,
    int ActionCap, BenchmarkAgentSpec Agent);

public sealed record TacticalBenchmarkManifest(int SchemaVersion, string Kind, string SuiteId, string BuilderVersion,
    BenchmarkPins Pins, ImmutableArray<TacticalPositionSpec> Positions)
{
    public const string ArtifactKind = "m5-tactical-benchmark";
    public void Validate()
    {
        if (SchemaVersion != 1 || Kind != ArtifactKind || !AiBenchmarkManifest.SafeId(SuiteId)
            || BuilderVersion != GatewayTacticalCatalog.BuilderVersion || Pins is null || Positions.IsDefaultOrEmpty
            || Positions.Length > 1000 || Positions.Any(position => position?.Metadata is null || position.Agent is null)
            || Positions.Select(position => position.Metadata.Id).Distinct(StringComparer.Ordinal).Count() != Positions.Length)
            throw new ArgumentException("Invalid tactical manifest/version/position IDs.");
        foreach (TacticalPositionSpec position in Positions)
        {
            if (!AiBenchmarkManifest.SafeId(position.Metadata.Id) || !AiBenchmarkManifest.IsHash(position.InitialStateHash)
                || position.HiddenTwinStateHash is { } twin && !AiBenchmarkManifest.IsHash(twin)
                || position.ActionCap is < 1 or > 1000 || position.Agent.Side != position.Metadata.Side)
                throw new ArgumentException($"Invalid tactical position '{position.Metadata.Id}'.");
            position.Agent.Validate();
        }
    }
}

public sealed record TacticalScore(PlayerSide Side, string Category, string Split, int Positions, int Correct,
    int Critical, int CriticalCorrect, int ReplayFailures, int ExecutionFailures)
{
    public decimal CorrectFraction => Positions == 0 ? 0 : (decimal)Correct / Positions;
    public bool MeetsStandardTarget => Positions > 0 && CorrectFraction >= 0.85m && Critical == CriticalCorrect
        && ReplayFailures == 0 && ExecutionFailures == 0;
}

public sealed record TacticalBenchmarkReport(int SchemaVersion, string Kind, string SuiteId, string Mode,
    string ManifestHash, BenchmarkPins Pins, string BuildRevision, int Workers, string DeterministicDigest,
    ImmutableSortedDictionary<string, string> AssemblyHashes, ImmutableArray<TacticalCaseResult> Positions,
    ImmutableArray<TacticalScore> Scores, bool Passed);

/// <summary>Prepared Core positions and assertions stay in Match; policies receive only their observations.</summary>
public sealed class TacticalBenchmarkRunner
{
    private readonly GatewayTacticalCatalog _catalog;
    private readonly IBenchmarkAgentFactory _agents;

    public TacticalBenchmarkRunner(CardDataSet data, IBenchmarkAgentFactory? agents = null)
    {
        _catalog = new(data);
        _agents = agents ?? new BenchmarkAgentFactory();
    }

    public TacticalBenchmarkManifest CreateContractManifest() => new(1, TacticalBenchmarkManifest.ArtifactKind,
        "gateway-tactics-contract-v1", GatewayTacticalCatalog.BuilderVersion, _catalog.Pins,
        GatewayTacticalCatalog.ImplementedIds.Select((id, index) =>
        {
            TacticalFixture fixture = _catalog.Build(id);
            return new TacticalPositionSpec(fixture.Metadata, DeterministicHash.ForState(fixture.Initial),
                fixture.HiddenTwin is { } twin ? DeterministicHash.ForState(twin) : null, fixture.ActionCap,
                BenchmarkAgentSpec.Contract(fixture.Metadata.Side, AgentLifecycleHost.DerivePolicySeed((ulong)(520000 + index), fixture.Metadata.Side)));
        }).ToImmutableArray());

    public async Task<TacticalBenchmarkReport> RunAsync(TacticalBenchmarkManifest manifest, string outputDirectory,
        int workers = 1, bool verifyFixtures = false, string buildRevision = "unknown", CancellationToken cancellationToken = default)
    {
        manifest.Validate();
        if (workers is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(workers));
        if (manifest.Pins != _catalog.Pins) throw new ArgumentException("Tactical content/catalog pins differ from this offline build.");
        var fixtures = new Dictionary<string, TacticalFixture>(StringComparer.Ordinal);
        foreach (TacticalPositionSpec position in manifest.Positions)
        {
            _agents.Validate(position.Agent);
            if (!GatewayTacticalCatalog.ImplementedIds.Contains(position.Metadata.Id)) throw new ArgumentException("Unknown tactical builder.");
            TacticalFixture fixture = _catalog.Build(position.Metadata.Id);
            if (position.Metadata != fixture.Metadata || position.InitialStateHash != DeterministicHash.ForState(fixture.Initial)
                || position.HiddenTwinStateHash != (fixture.HiddenTwin is { } twin ? DeterministicHash.ForState(twin) : null)
                || position.ActionCap != fixture.ActionCap)
                throw new ArgumentException($"Frozen tactical inputs differ for '{position.Metadata.Id}'.");
            fixtures.Add(position.Metadata.Id, fixture);
        }
        string output = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Choose a new tactical evidence directory; existing evidence is never overwritten.");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "PRIVATE-ARTIFACTS.txt"),
            "Private developer evidence: replays, snapshots and policy diagnostics can reveal hidden cards. No automatic upload.\n", CancellationToken.None).ConfigureAwait(false);
        await Write("manifest.json", manifest).ConfigureAwait(false);
        var results = new ConcurrentDictionary<string, TacticalCaseResult>(StringComparer.Ordinal);
        var instances = new ConcurrentDictionary<IGameAgent, byte>(ReferenceEqualityComparer.Instance);
        await Parallel.ForEachAsync(manifest.Positions, new ParallelOptions { MaxDegreeOfParallelism = workers }, async (position, workerCancellation) =>
        {
            TacticalFixture fixture = fixtures[position.Metadata.Id];
            IGameAgent? agent = null;
            IGameAgent? twinAgent = null;
            if (!verifyFixtures)
            {
                try
                {
                    // Factories and policies are extension points. Retire late
                    // creations; a timeout never substitutes another policy.
                    Task<(IGameAgent, IGameAgent?)> creation = Task.Run(() => (Create(), fixture.HiddenTwin is null ? null : Create()), CancellationToken.None);
                    _ = creation.ContinueWith(task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                    (agent, twinAgent) = await creation.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    agent = new FailedCreationAgent(exception.GetType().Name + ": " + exception.Message);
                    if (fixture.HiddenTwin is not null) twinAgent = new FailedCreationAgent("Twin agent creation failed.");
                }
            }
            TacticalArtifacts artifacts = await TacticalExecution.RunAsync(fixture, agent, oracle: verifyFixtures,
                twinFactory: twinAgent is null ? null : () => twinAgent, cancellationToken: cancellationToken).ConfigureAwait(false);
            string directory = position.Metadata.Id;
            Directory.CreateDirectory(Path.Combine(output, directory));
            await Write(directory + "/replay.json", artifacts.Replay).ConfigureAwait(false);
            if (artifacts.HiddenTwinReplay is { } twinReplay) await Write(directory + "/hidden-twin-replay.json", twinReplay).ConfigureAwait(false);
            if (artifacts.HiddenTwinArtifacts is { Result.Passed: false } failedTwin)
                await Write(directory + "/hidden-twin-failure.json", new { position, failedTwin.Result, failedTwin.FinalSnapshot, failedTwin.RecentSteps, failedTwin.Evaluations, failedTwin.Checkpoint }).ConfigureAwait(false);
            if (!artifacts.Result.Passed)
                await Write(directory + "/failure.json", new { position, artifacts.Result, artifacts.FinalSnapshot, artifacts.RecentSteps, artifacts.Evaluations, artifacts.Checkpoint }).ConfigureAwait(false);
            results[position.Metadata.Id] = artifacts.Result;

            IGameAgent Create()
            {
                IGameAgent created = _agents.Create(position.Agent, fixture.Board.Runtime,
                    new GatewayEvaluationCatalog(fixture.Board.Runtime, fixture.Board.Behavior));
                if (!instances.TryAdd(created, 0)) throw new InvalidOperationException("Tactical factory reused an agent across positions or twins.");
                return created;
            }
        }).ConfigureAwait(false);
        ImmutableArray<TacticalCaseResult> ordered = results.Values.OrderBy(result => result.Id, StringComparer.Ordinal).ToImmutableArray();
        var scores = ImmutableArray.CreateBuilder<TacticalScore>();
        foreach (PlayerSide side in Enum.GetValues<PlayerSide>())
        {
            TacticalCaseResult[] sideResults = ordered.Where(result => result.Side == side).ToArray();
            if (sideResults.Length == 0) continue;
            scores.Add(Score(sideResults, "all", "all"));
            foreach (var group in sideResults.GroupBy(result => result.Category).OrderBy(group => group.Key, StringComparer.Ordinal))
                scores.Add(Score(group.ToArray(), group.Key, "all"));
            foreach (var group in sideResults.GroupBy(result => result.Split).OrderBy(group => group.Key, StringComparer.Ordinal))
                scores.Add(Score(group.ToArray(), "all", group.Key));
        }
        bool passed = verifyFixtures ? ordered.All(result => result.Passed)
            : scores.Where(score => score.Category == "all" && score.Split == "all").All(score => score.MeetsStandardTarget);
        var report = new TacticalBenchmarkReport(1, "m5-tactical-report", manifest.SuiteId, verifyFixtures ? "fixture-validation" : "policy-evaluation",
            BenchmarkHash.Value(manifest), manifest.Pins, buildRevision, workers, BenchmarkHash.Value(ordered), AiBenchmarkRunner.AssemblyHashes(),
            ordered, scores.ToImmutable(), passed);
        await Write("report.json", report).ConfigureAwait(false);
        return report;

        Task Write<T>(string path, T value) => File.WriteAllTextAsync(Path.Combine(output, path), EngineJson.Serialize(value), CancellationToken.None);
        static TacticalScore Score(TacticalCaseResult[] values, string category, string split) => new(values[0].Side, category, split,
            values.Length, values.Count(value => value.Passed), values.Count(value => value.Critical), values.Count(value => value.Critical && value.Passed),
            values.Count(value => !value.ReplayVerified || value.HiddenTwinReplayVerified == false),
            values.Count(value => value.Error is not null || value.HiddenTwinError is not null || !value.Completed));
    }

    private sealed class FailedCreationAgent(string message) : IGameAgent
    {
        public ValueTask<ActionId> ChooseActionAsync(Observation observation, CancellationToken cancellationToken = default) => throw new InvalidOperationException(message);
    }
}

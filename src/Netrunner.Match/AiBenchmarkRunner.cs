using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>Offline batch execution. Each game owns its state, agents, RNG and artifacts.</summary>
public sealed class AiBenchmarkRunner
{
    private readonly CardDataSet _data;
    private readonly IBehaviorCatalog _behavior;
    private readonly IRuntimeCardCatalog _runtime;
    private readonly GatewayEvaluationCatalog _evaluation;
    private readonly IBenchmarkAgentFactory _agents;

    public AiBenchmarkRunner(CardDataSet data, IBehaviorCatalog behavior, IBenchmarkAgentFactory? agents = null)
        : this(data, behavior, GatewayRuntimeCardCatalog.Create(data), agents)
    {
    }

    internal AiBenchmarkRunner(CardDataSet data, IBehaviorCatalog behavior, IRuntimeCardCatalog runtime, IBenchmarkAgentFactory? agents = null)
    {
        _data = data;
        _behavior = behavior;
        _runtime = runtime;
        _evaluation = new GatewayEvaluationCatalog(_runtime, behavior);
        _agents = agents ?? new BenchmarkAgentFactory();
    }

    public BenchmarkPins Pins => BenchmarkPins.Capture(_data, _behavior, _runtime);

    public FullMatchDefinition Compose(string contentMode, string corpPreset, string runnerPreset, ulong seed)
    {
        if (contentMode == "official")
        {
            if (_data.Manifest.PackCode != CardDataValidator.OfficialPackCode)
                throw new ArgumentException("Official benchmarks require explicitly provisioned official content.");
            MatchRequest request = MatchRequestComposer.ComposeOfficial(corpPreset, runnerPreset, _data, seed, behaviorCatalog: _behavior);
            return FullMatchDefinition.FromRequest(request, _data, _behavior);
        }
        if (contentMode != "developer" || corpPreset != "developer-corp" || runnerPreset != "developer-runner"
            || _behavior.Version != DeveloperMatchCards.BehaviorVersion)
            throw new ArgumentException("Unsupported benchmark content mode or deck pairing.");
        DeveloperMatchCards developer = DeveloperMatchCards.Create();
        return FullMatchDefinition.FromSimulation(FullMatchDefinition.DeveloperFixture(developer, seed), _data, _behavior);
    }

    public BenchmarkGameSpec CreateRow(string id, string contentMode, string corpPreset, string runnerPreset, ulong seed)
    {
        FullMatchDefinition definition = Compose(contentMode, corpPreset, runnerPreset, seed);
        return new(id, corpPreset, runnerPreset, seed, BenchmarkHash.Value(definition.Request.CorpDeck), BenchmarkHash.Value(definition.Request.RunnerDeck),
            BenchmarkAgentSpec.Contract(PlayerSide.Corp, AgentLifecycleHost.DerivePolicySeed(seed, PlayerSide.Corp)),
            BenchmarkAgentSpec.Contract(PlayerSide.Runner, AgentLifecycleHost.DerivePolicySeed(seed, PlayerSide.Runner)));
    }

    public async Task<AiBenchmarkReport> RunAsync(AiBenchmarkManifest manifest, string outputDirectory, int workers = 1,
        string buildRevision = "unknown", bool writePolicyDiagnostics = false, CancellationToken cancellationToken = default)
    {
        manifest.Validate();
        if (workers is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(workers));
        if (manifest.Pins != Pins) throw new ArgumentException("Benchmark content/catalog pins do not match this offline build.", nameof(manifest));
        if (writePolicyDiagnostics && !manifest.Games.Any(game => game.Corp.Kind == "offline" || game.Runner.Kind == "offline"))
            throw new ArgumentException("Policy diagnostics require at least one offline policy in the manifest.", nameof(manifest));
        string manifestHash = BenchmarkHash.Value(manifest);
        PolicyDiagnosticContext? diagnostics = writePolicyDiagnostics ? new(manifest.SuiteId, manifestHash, buildRevision, manifest.Pins) : null;
        var definitions = new Dictionary<string, FullMatchDefinition>(StringComparer.Ordinal);
        foreach (BenchmarkGameSpec game in manifest.Games)
        {
            _agents.Validate(game.Corp);
            _agents.Validate(game.Runner);
            FullMatchDefinition definition = Compose(manifest.ContentMode, game.CorpPreset, game.RunnerPreset, game.GameSeed);
            if (game.CorpDeckHash != BenchmarkHash.Value(definition.Request.CorpDeck)
                || game.RunnerDeckHash != BenchmarkHash.Value(definition.Request.RunnerDeck))
                throw new ArgumentException($"Deck hash mismatch for '{game.Id}'.", nameof(manifest));
            definitions.Add(game.Id, definition);
        }
        string output = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Choose a new output directory; existing benchmark evidence is never overwritten.");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "PRIVATE-ARTIFACTS.txt"),
            "Local private debug artifacts: replays, failures and optional policy logs contain hidden cards, decks and strategic reasoning. No automatic upload.\n", CancellationToken.None).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(output, "manifest.json"), EngineJson.Serialize(manifest), CancellationToken.None).ConfigureAwait(false);
        var results = new ConcurrentDictionary<string, BenchmarkGameResult>(StringComparer.Ordinal);
        var agents = new ConcurrentDictionary<IGameAgent, byte>(ReferenceEqualityComparer.Instance);
        // Schedule every declared row even after cancellation. Cancelled rows
        // retain an initial replay and stay in the report denominator.
        await Parallel.ForEachAsync(manifest.Games, new ParallelOptions { MaxDegreeOfParallelism = workers }, async (game, _) =>
        {
            BenchmarkGameResult result = await RunPositionAsync(game, definitions[game.Id].Start().State, manifest.Limits, output, agents,
                diagnostics, cancellationToken).ConfigureAwait(false);
            results[game.Id] = result;
        }).ConfigureAwait(false);
        ImmutableArray<BenchmarkGameResult> ordered = results.Values.OrderBy(result => result.Game.Id, StringComparer.Ordinal).ToImmutableArray();
        var report = new AiBenchmarkReport(1, "m5-ai-benchmark-report", manifest.SuiteId, manifestHash, manifest.Pins,
            buildRevision, RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(),
            workers, BenchmarkHash.Value(ordered.Select(result => new { result.Game, result.Result }).ToArray()), Aggregate(ordered), ordered, Stratify(ordered), AssemblyHashes());
        await File.WriteAllTextAsync(Path.Combine(output, "report.json"), EngineJson.Serialize(report), CancellationToken.None).ConfigureAwait(false);
        return report;
    }

    internal async Task<BenchmarkGameResult> RunPositionAsync(BenchmarkGameSpec game, GameState state,
        BenchmarkLimits limits, string output, ConcurrentDictionary<IGameAgent, byte>? agents = null,
        PolicyDiagnosticContext? diagnosticContext = null, CancellationToken cancellationToken = default)
    {
        agents ??= new(ReferenceEqualityComparer.Instance);
        string directory = Path.Combine(output, game.Id);
        Directory.CreateDirectory(directory);
        await using PolicyDecisionLog? policyLog = diagnosticContext is null ? null
            : new(Path.Combine(directory, "policy-decisions.jsonl"), diagnosticContext, game.Id);
        // Preserve even an invalid injected scenario as failure evidence;
        // validation below classifies it before an agent receives anything.
        GameSnapshot initial = JsonSerializer.Deserialize<GameSnapshot>(EngineJson.Serialize(state), EngineJson.Options)!;
        var actions = new List<ReplayAction>();
        var recent = new Queue<BenchmarkDecisionRecord>();
        var metrics = new BenchmarkMetrics();
        var progress = new BenchmarkProgressDetector(limits);
        progress.Observe(state);
        var elapsed = Stopwatch.StartNew();
        BenchmarkTermination termination = BenchmarkTermination.Terminal;
        string? error = null;
        IGameAgent? corp = null;
        IGameAgent? runner = null;
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(limits.WatchdogSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, watchdog.Token);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            StateInvariantValidator.Validate(state, _behavior, _runtime);
            (corp, runner) = await AwaitAgentAsync(Task.Run(() =>
            {
                IGameAgent corpAgent = CreateIndependent(game.Corp);
                IGameAgent runnerAgent = CreateIndependent(game.Runner);
                return (corpAgent, runnerAgent);
            }, CancellationToken.None), linked.Token).ConfigureAwait(false);
            AgentLifecycleHost.ValidateSeat(corp, PlayerSide.Corp);
            AgentLifecycleHost.ValidateSeat(runner, PlayerSide.Runner);
            await AwaitAgentAsync(Task.Run(() =>
            {
                AgentLifecycleHost.Observe(corp, state, _behavior);
                AgentLifecycleHost.Observe(runner, state, _behavior);
                return true;
            }, CancellationToken.None), linked.Token).ConfigureAwait(false);
            while (state.Outcome.Kind == GameOutcomeKind.Ongoing)
            {
                linked.Token.ThrowIfCancellationRequested();
                if (state.Turn.TurnNumber - 1 >= limits.TurnPairs) { termination = BenchmarkTermination.TurnCap; break; }
                if (actions.Count >= limits.Actions) { termination = BenchmarkTermination.ActionCap; break; }
                if (state.PendingDecision is not { } pending || pending.LegalActions.Count == 0)
                {
                    termination = BenchmarkTermination.NoDecision;
                    break;
                }
                PlayerSide side = state.GetPlayer(pending.Owner).Side;
                IGameAgent agent = side == PlayerSide.Corp ? corp : runner;
                Observation observation = ObservationBuilder.Build(state, side, _behavior);
                string digest = ObservationReader.Digest(ObservationReader.Read(observation,
                    agent is OfflineAgent reader ? reader.Configuration.FeatureVersion : AgentFeatures.Version));
                string? checkpoint = agent is OfflineAgent offline ? BenchmarkHash.Value(offline.Checkpoint) : null;
                var timer = Stopwatch.StartNew();
                // The task boundary also bounds a synchronously blocking agent.
                // A late completion owns only its private agent/observation and
                // can never Apply after this wait has timed out.
                Task<ActionId> choice = Task.Run(async () => await agent.ChooseActionAsync(observation, linked.Token).ConfigureAwait(false), CancellationToken.None);
                ActionId selected;
                try { selected = await choice.WaitAsync(linked.Token).ConfigureAwait(false); }
                catch
                {
                    _ = choice.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    Remember(null, null);
                    throw;
                }
                finally { timer.Stop(); metrics.Decision(side, pending.Kind, timer.Elapsed.TotalMilliseconds); }
                linked.Token.ThrowIfCancellationRequested();
                if (policyLog is not null && agent is OfflineAgent diagnosticAgent)
                {
                    await policyLog.WriteAsync(diagnosticAgent, pending, state.ActionSequence, timer.Elapsed.TotalMilliseconds).ConfigureAwait(false);
                    linked.Token.ThrowIfCancellationRequested();
                }
                metrics.PolicyEvaluation(side, (agent as OfflineAgent)?.LastEvaluation);
                LegalAction? action = pending.LegalActions.FirstOrDefault(value => value.Id == selected);
                Remember(selected, action?.Kind);
                if (action is null)
                {
                    termination = BenchmarkTermination.IllegalAction;
                    error = "Agent returned an action ID that was not issued for its decision.";
                    break;
                }
                var submission = new ActionSubmission(pending.Owner, pending.Id, pending.Revision, selected);
                GameState candidate = EngineJson.DeserializeState(EngineJson.Serialize(state), _behavior, _runtime);
                ApplyResult applied = GameEngine.Apply(candidate, submission, _behavior, _runtime);
                if (!applied.Accepted)
                {
                    termination = applied.Rejection?.Code switch
                    {
                        ApplyRejectionCode.InvalidInputState => BenchmarkTermination.InvariantFailure,
                        ApplyRejectionCode.ResolutionFailure => BenchmarkTermination.ResolutionFailure,
                        _ => BenchmarkTermination.IllegalAction,
                    };
                    error = applied.Rejection?.Message;
                    break;
                }
                StateInvariantValidator.Validate(applied.State, _behavior, _runtime);
                metrics.Accepted(side, action, state, applied);
                state = applied.State;
                actions.Add(ReplayAction.From(applied.AcceptedAction!));
                await AwaitAgentAsync(Task.Run(() =>
                {
                    AgentLifecycleHost.Accepted(corp, submission, state, _behavior);
                    AgentLifecycleHost.Accepted(runner, submission, state, _behavior);
                    return true;
                }, CancellationToken.None), linked.Token).ConfigureAwait(false);
                bool repeated = progress.Observe(state);
                if (repeated && state.Outcome.Kind == GameOutcomeKind.Ongoing)
                {
                    termination = BenchmarkTermination.RepeatedProgress;
                    break;
                }

                void Remember(ActionId? id, ActionKind? kind)
                {
                    recent.Enqueue(new(state.ActionSequence, side, pending.Kind, pending.Id.Value, pending.Revision, id?.Value, kind, digest, checkpoint));
                    if (recent.Count > 50) recent.Dequeue();
                }
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            termination = cancellationToken.IsCancellationRequested ? BenchmarkTermination.Cancelled : BenchmarkTermination.WatchdogTimeout;
            error = "Execution cancelled before another action could commit.";
        }
        catch (StateInvariantException exception) { termination = BenchmarkTermination.InvariantFailure; error = exception.Message; }
        catch (ResolutionFailureException exception) { termination = BenchmarkTermination.ResolutionFailure; error = exception.Message; }
        catch (Exception exception) { termination = BenchmarkTermination.AgentException; error = $"{exception.GetType().Name}: {exception.Message}"; }
        // Discard is agent code too: bound it, and never call into an agent
        // that may still be running after cancellation. Retired instances are
        // never reused, including when their cleanup fails.
        if (termination is not (BenchmarkTermination.WatchdogTimeout or BenchmarkTermination.Cancelled))
        {
            try
            {
                await AwaitAgentAsync(Task.Run(() =>
                {
                    if (corp is not null) AgentLifecycleHost.Discard(corp);
                    if (runner is not null) AgentLifecycleHost.Discard(runner);
                    return true;
                }, CancellationToken.None), linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                error = $"{termination}: {error}; agent discard did not complete before cancellation.";
                termination = cancellationToken.IsCancellationRequested ? BenchmarkTermination.Cancelled : BenchmarkTermination.WatchdogTimeout;
            }
            catch (Exception exception)
            {
                error = $"{termination}: {error}; agent discard: {exception.GetType().Name}: {exception.Message}";
                termination = BenchmarkTermination.AgentException;
            }
        }
        BenchmarkTermination executionTermination = termination;
        string finalHash = DeterministicHash.ForState(state);
        var replay = new ReplayArtifact(initial, state.InitialSeed, actions.ToArray(), expectedFinalStateHash: finalHash);
        string replayJson = EngineJson.Serialize(replay);
        await File.WriteAllTextAsync(Path.Combine(directory, "replay.json"), replayJson, CancellationToken.None).ConfigureAwait(false);
        bool replayVerified = false;
        try
        {
            ReplayVerificationResult verification = await Task.Run(() => ReplayVerifier.Verify(replay, _behavior, _runtime), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(limits.ReplayWatchdogSeconds), CancellationToken.None).ConfigureAwait(false);
            replayVerified = verification.Success;
            if (!replayVerified)
            {
                error = $"{termination}: {error}; replay: {verification.Message}";
                termination = BenchmarkTermination.ReplayMismatch;
            }
        }
        catch (TimeoutException)
        {
            error = $"{termination}: {error}; accepted-prefix replay verification timed out.";
            termination = BenchmarkTermination.WatchdogTimeout;
        }
        catch (Exception exception)
        {
            error = $"{termination}: {error}; replay: {exception.GetType().Name}: {exception.Message}";
            termination = BenchmarkTermination.ReplayMismatch;
        }
        string? failurePath = null;
        if (termination != BenchmarkTermination.Terminal)
        {
            failurePath = game.Id + "/failure.json";
            bool isolatedTimeout = executionTermination is BenchmarkTermination.WatchdogTimeout or BenchmarkTermination.Cancelled;
            var failure = new
            {
                ArtifactKind = "private-benchmark-failure",
                SchemaVersion = 1,
                Game = game,
                Termination = termination,
                ExecutionTermination = executionTermination,
                Error = error,
                Snapshot = JsonSerializer.Deserialize<GameSnapshot>(EngineJson.Serialize(state), EngineJson.Options),
                RecentDecisions = recent.ToArray(),
                Progress = progress.Current,
                CorpCheckpoint = !isolatedTimeout && corp is OfflineAgent corpPolicy ? corpPolicy.Checkpoint : null,
                RunnerCheckpoint = !isolatedTimeout && runner is OfflineAgent runnerPolicy ? runnerPolicy.Checkpoint : null,
            };
            await File.WriteAllTextAsync(Path.Combine(directory, "failure.json"), EngineJson.Serialize(failure), CancellationToken.None).ConfigureAwait(false);
        }
        var outcome = new BenchmarkGameOutcome(termination, actions.Count, Math.Max(0, state.Turn.TurnNumber - 1), state.Outcome,
            finalHash, BenchmarkHash.Bytes(CanonicalJson.Canonicalize(replayJson)), replayVerified,
            metrics.Snapshot(state, PlayerSide.Corp), metrics.Snapshot(state, PlayerSide.Runner), progress.Current, executionTermination);
        return new(game, outcome, new(elapsed.Elapsed.TotalMilliseconds, metrics.Latency(PlayerSide.Corp), metrics.Latency(PlayerSide.Runner)),
            error, game.Id + "/replay.json", failurePath);

        IGameAgent CreateIndependent(BenchmarkAgentSpec specification)
        {
            IGameAgent agent = _agents.Create(specification, _runtime, _evaluation);
            if (!agents.TryAdd(agent, 0)) throw new InvalidOperationException("Benchmark factories must create a fresh agent for every seat and game.");
            return agent;
        }
    }

    private static async Task<T> AwaitAgentAsync<T>(Task<T> task, CancellationToken cancellationToken)
    {
        try { return await task.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch
        {
            _ = task.ContinueWith(static completed => { _ = completed.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
    }

    private static BenchmarkAggregate Aggregate(ImmutableArray<BenchmarkGameResult> games) => new(games.Length,
        games.Count(game => game.Result.Termination == BenchmarkTermination.Terminal),
        games.Count(game => game.Result.Termination == BenchmarkTermination.TurnCap),
        games.Count(game => game.Result.Termination is not (BenchmarkTermination.Terminal or BenchmarkTermination.TurnCap)),
        games.Count(game => game.Result.Outcome.Winner == PlayerSide.Corp), games.Count(game => game.Result.Outcome.Winner == PlayerSide.Runner),
        games.GroupBy(game => game.Result.Termination.ToString()).ToImmutableSortedDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal));

    private static ImmutableArray<BenchmarkStratum> Stratify(ImmutableArray<BenchmarkGameResult> games) => games
        .GroupBy(game => (game.Game.ComparisonGroup, game.Game.CorpPreset, game.Game.RunnerPreset))
        .OrderBy(group => group.Key.ComparisonGroup, StringComparer.Ordinal)
        .ThenBy(group => group.Key.CorpPreset, StringComparer.Ordinal)
        .ThenBy(group => group.Key.RunnerPreset, StringComparer.Ordinal)
        .Select(group => new BenchmarkStratum(group.Key.ComparisonGroup, group.Key.CorpPreset, group.Key.RunnerPreset,
            Aggregate(group.ToImmutableArray()), group.Sum(game => (long)game.Result.Actions), group.Sum(game => (long)game.Result.CompletedTurnPairs),
            Totals(group.Select(game => game.Result.Corp).ToArray()), Totals(group.Select(game => game.Result.Runner).ToArray())))
        .ToImmutableArray();

    private static BenchmarkSeatTotals Totals(BenchmarkSideMetrics[] sides) => new(sides.Sum(side => side.Decisions),
        sides.Sum(side => (long)side.CreditsGained), sides.Sum(side => (long)side.CreditsSpent), sides.Sum(side => side.Installs),
        sides.Sum(side => side.ReplacementInstalls), sides.Sum(side => side.Runs), sides.Sum(side => side.SuccessfulRuns),
        sides.Sum(side => side.Pumps), sides.Sum(side => side.WastedPumps), sides.All(side => side.Fallbacks.HasValue) ? sides.Sum(side => side.Fallbacks!.Value) : null,
        sides.Sum(side => (long)side.FinalCredits), sides.Sum(side => side.FinalAgendaPoints));

    internal static ImmutableSortedDictionary<string, string> AssemblyHashes() => new[]
    {
        typeof(GameState).Assembly, typeof(GatewayEvaluationCatalog).Assembly,
        typeof(OfflineAgent).Assembly, typeof(AiBenchmarkRunner).Assembly, Assembly.GetEntryAssembly(),
    }.OfType<Assembly>().Distinct().Where(assembly => !string.IsNullOrEmpty(assembly.Location))
        .ToImmutableSortedDictionary(assembly => assembly.GetName().Name!, assembly => BenchmarkHash.Bytes(File.ReadAllBytes(assembly.Location)), StringComparer.Ordinal);
}

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.M5Baseline;

/// <summary>A small capture utility for M5.0, not the M5.2 benchmark harness.</summary>
internal static class Program
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
                throw new InvalidOperationException("M5 baseline requires a build with authenticated official content.");
            CardDataSet data = CardDataResource.LoadEmbedded();
            BehaviorCatalog behavior = GatewayBehaviorCatalog.Create();
            RuntimeCardCatalog runtime = GatewayRuntimeCardCatalog.Create(data);
            using Stream presetStream = typeof(OfficialPresetCatalog).Assembly.GetManifestResourceStream("Netrunner.Cards.Data.m4.presets.json")
                ?? throw new InvalidOperationException("Embedded official presets are missing.");
            using var presetBytes = new MemoryStream();
            presetStream.CopyTo(presetBytes);
            var pins = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["rulesVersion"] = EngineCompatibility.RulesVersion,
                ["stateSchemaVersion"] = EngineCompatibility.StateSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["cardDataVersion"] = data.Manifest.CommitSha,
                ["cardDataSha256"] = data.Manifest.OutputSha256,
                ["behaviorVersion"] = behavior.Version,
                ["behaviorFingerprint"] = behavior.Fingerprint,
                ["runtimeVersion"] = runtime.Version,
                ["runtimeFingerprint"] = runtime.Fingerprint,
                ["agentVersion"] = FullGameContractAgent.Version,
                ["policyVersion"] = FullGameContractPolicy.Version,
                ["presetSha256"] = Hash(presetBytes.ToArray()),
            };
            if (args is ["--describe"])
            {
                Console.WriteLine(JsonSerializer.Serialize(pins, Json));
                return 0;
            }
            if (args.Length != 2)
                throw new ArgumentException("Usage: m5-baseline --describe | <manifest.json> <new-output-directory>");
            byte[] manifestBytes = File.ReadAllBytes(args[0]);
            using JsonDocument manifest = JsonDocument.Parse(manifestBytes);
            JsonElement root = manifest.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1
                || root.GetProperty("kind").GetString() != "m5-contract-baseline")
                throw new InvalidOperationException("Unsupported baseline manifest.");
            foreach ((string key, string value) in pins)
                if (root.GetProperty("pins").GetProperty(key).GetString() != value)
                    throw new InvalidOperationException($"Baseline pin mismatch: {key}.");
            int actionCap = root.GetProperty("actionCap").GetInt32();
            int repeats = root.GetProperty("repeats").GetInt32();
            int timeoutSeconds = root.GetProperty("timeoutSecondsPerGame").GetInt32();
            if (repeats != 2 || actionCap is < 1 or > SimulationFixtureDocument.MaximumActionCap
                || timeoutSeconds is < 1 or > 3600)
                throw new InvalidOperationException("Expected two repeats, a valid action cap and a bounded timeout.");
            var cases = new List<(string Id, FullMatchDefinition Definition)>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement row in root.GetProperty("games").EnumerateArray())
            {
                string id = row.GetProperty("id").GetString()!;
                if (string.IsNullOrWhiteSpace(id) || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-') || !ids.Add(id))
                    throw new InvalidOperationException("Game IDs must be unique safe filenames.");
                MatchRequest request = MatchRequestComposer.ComposeOfficial(
                    row.GetProperty("corpPreset").GetString()!, row.GetProperty("runnerPreset").GetString()!,
                    data, row.GetProperty("seed").GetUInt64(), behaviorCatalog: behavior);
                cases.Add((id, FullMatchDefinition.FromRequest(request, data, behavior)));
            }
            if (cases.Count == 0) throw new InvalidOperationException("The baseline contains no games.");
            string output = Path.GetFullPath(args[1]);
            if (Directory.Exists(output)) throw new IOException("Choose a new output directory; existing evidence is never overwritten.");
            Directory.CreateDirectory(output);
            var reports = new List<object>();
            bool failed = false;
            foreach ((string id, FullMatchDefinition definition) in cases)
            {
                string? firstReplayHash = null;
                string? firstMetrics = null;
                string? firstStatus = null;
                for (int repeat = 1; repeat <= repeats; repeat++)
                {
                    var corp = new MeteredAgent(PlayerSide.Corp, definition.Runtime);
                    var runner = new MeteredAgent(PlayerSide.Runner, definition.Runtime);
                    var elapsed = Stopwatch.StartNew();
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                    ReplayArtifact? replay = null;
                    string status = "terminal";
                    string? error = null;
                    try
                    {
                        FullMatchResult result = await FullMatchRunner.RunAsync(definition, actionCap, corp, runner, timeout.Token).ConfigureAwait(false);
                        replay = result.Replay;
                    }
                    catch (FullMatchStallException exception)
                    {
                        replay = exception.Replay;
                        status = replay.Actions.Count == actionCap ? "action-cap" : "no-decision";
                        error = exception.Message;
                        File.WriteAllText(Path.Combine(output, $"{id}-{repeat}-snapshot.json"), EngineJson.Serialize(exception.Snapshot));
                    }
                    catch (OperationCanceledException)
                    {
                        status = "timeout";
                        error = "Cooperative per-game timeout elapsed.";
                    }
                    catch (Exception exception)
                    {
                        status = "error";
                        error = exception.ToString();
                    }
                    elapsed.Stop();
                    string? replayHash = null;
                    GameState? final = null;
                    bool verified = false;
                    if (replay is not null)
                    {
                        byte[] bytes = EngineJson.SerializeUtf8(replay);
                        replayHash = Hash(bytes);
                        File.WriteAllBytes(Path.Combine(output, $"{id}-{repeat}-replay.json"), bytes);
                        ReplayVerificationResult verification = ReplayVerifier.Verify(replay, behavior, runtime);
                        verified = verification.Success;
                        final = verification.FinalState;
                    }
                    string metrics = JsonSerializer.Serialize(new { corp = corp.Counts, runner = runner.Counts }, Json);
                    bool? deterministic = repeat == 1 ? null : replayHash is not null && replayHash == firstReplayHash
                        && status == firstStatus && metrics == firstMetrics;
                    if (repeat == 1) { firstReplayHash = replayHash; firstMetrics = metrics; firstStatus = status; }
                    failed |= status != "terminal" || !verified || deterministic == false;
                    reports.Add(new
                    {
                        id,
                        repeat,
                        seed = definition.Request.Seed,
                        corpPreset = definition.Request.CorpDeck.PresetId,
                        runnerPreset = definition.Request.RunnerDeck.PresetId,
                        status,
                        error,
                        actionCount = replay?.Actions.Count,
                        finalStateHash = replay?.ExpectedFinalStateHash,
                        replaySha256 = replayHash,
                        replayVerified = verified,
                        deterministicRepeat = deterministic,
                        winner = final?.Outcome.Winner?.ToString(),
                        reason = final?.Outcome.Reason?.ToString(),
                        turn = final?.Turn.TurnNumber,
                        corpAgendaPoints = final?.Players.Single(player => player.Side == PlayerSide.Corp).AgendaPoints,
                        runnerAgendaPoints = final?.Players.Single(player => player.Side == PlayerSide.Runner).AgendaPoints,
                        corp = corp.Counts,
                        runner = runner.Counts,
                        elapsedMilliseconds = elapsed.ElapsedMilliseconds,
                    });
                    WriteReport();
                    Console.WriteLine($"{id} repeat={repeat} status={status} actions={replay?.Actions.Count} replay={verified} deterministic={deterministic}");
                }
            }
            return failed ? 1 : 0;

            void WriteReport() => File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                kind = "m5-contract-baseline-report",
                pins,
                manifestSha256 = Hash(manifestBytes),
                sourceRevision = Environment.GetEnvironmentVariable("NETRUNNER_BASELINE_SOURCE_REVISION"),
                runtime = RuntimeInformation.FrameworkDescription,
                os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                assemblies = new[] { typeof(GameState).Assembly, typeof(FullGameContractAgent).Assembly,
                    typeof(GatewayBehaviorCatalog).Assembly, typeof(FullMatchRunner).Assembly, typeof(Program).Assembly }
                    .ToDictionary(assembly => assembly.GetName().Name!, assembly => new
                    {
                        version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                        sha256 = Hash(File.ReadAllBytes(assembly.Location)),
                    }, StringComparer.Ordinal),
                plannedGames = cases.Count,
                repeats,
                completedAttempts = reports.Count,
                complete = reports.Count == cases.Count * repeats,
                successful = !failed,
                results = reports,
            }, Json) + "\n");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

/// <summary>Observes only the same input as the unchanged contract policy.</summary>
internal sealed class MeteredAgent(PlayerSide side, IRuntimeCardCatalog runtime) : IGameAgent
{
    private readonly FullGameContractAgent _inner = new(side, runtime);
    public SortedDictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);

    public async ValueTask<ActionId> ChooseActionAsync(Observation observation, CancellationToken cancellationToken = default)
    {
        ActionId chosen = await _inner.ChooseActionAsync(observation, cancellationToken).ConfigureAwait(false);
        LegalAction action = observation.LegalActions.Single(candidate => candidate.Id == chosen);
        Count("decision:" + observation.Decision!.Kind);
        Count("action:" + action.Kind);
        if (action.Parameters is InstallCardParameters { FinalizeSelection: true } install)
        {
            Count("finalizedInstalls");
            if (install.SelectedReplacementCardIds.Count > 0) Count("installsSelectingReplacement");
        }
        if (action.Kind == ActionKind.GainCredit && observation.LegalActions.Any(candidate => candidate.Kind == ActionKind.DrawCard))
            Count("creditWhileDrawAvailable");
        return chosen;
    }

    private void Count(string key) => Counts[key] = Counts.GetValueOrDefault(key) + 1;
}

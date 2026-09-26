using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;

namespace Netrunner.Match;

public sealed record BenchmarkAgentSpec(string Kind, PlayerSide Side, string AgentVersion, string PolicyVersion,
    string ProfileId, ulong PolicySeed, OfflineAgentConfiguration? Configuration = null)
{
    public static BenchmarkAgentSpec Contract(PlayerSide side, ulong policySeed) => new("contract", side,
        FullGameContractAgent.Version, FullGameContractPolicy.Version, "contract-baseline", policySeed);

    public static BenchmarkAgentSpec Offline(OfflineAgentConfiguration configuration) => new("offline", configuration.Side,
        configuration.AgentVersion, configuration.PolicyVersion, configuration.ProfileId, configuration.PolicySeed, configuration);

    public void Validate()
    {
        if (!Enum.IsDefined(Side) || string.IsNullOrWhiteSpace(Kind) || string.IsNullOrWhiteSpace(AgentVersion)
            || string.IsNullOrWhiteSpace(PolicyVersion) || string.IsNullOrWhiteSpace(ProfileId))
            throw new ArgumentException("Incomplete benchmark agent specification.");
        if (Configuration is { } configuration)
        {
            configuration.Validate();
            if (configuration.Side != Side || configuration.AgentVersion != AgentVersion || configuration.PolicyVersion != PolicyVersion
                || configuration.ProfileId != ProfileId || configuration.PolicySeed != PolicySeed)
                throw new ArgumentException("Benchmark agent and policy configuration disagree.");
        }
    }
}

public sealed record BenchmarkLimits(int TurnPairs = 100, int Actions = 20000, int RepetitionCount = 8,
    int ProgressWindow = 256, int WatchdogSeconds = 180, int ReplayWatchdogSeconds = 180)
{
    public void Validate()
    {
        if (TurnPairs is < 1 or > 10000 || Actions is < 1 or > 100000 || RepetitionCount < 2
            || ProgressWindow < RepetitionCount || ProgressWindow > 10000
            || WatchdogSeconds is < 1 or > 3600 || ReplayWatchdogSeconds is < 1 or > 3600)
            throw new ArgumentException("Invalid benchmark limits.");
    }
}

public sealed record BenchmarkPins(string RulesVersion, int StateSchemaVersion, string CardDataVersion, string CardDataSha256,
    string BehaviorVersion, string BehaviorFingerprint, string RuntimeVersion, string RuntimeFingerprint,
    string EvaluationVersion, string EvaluationFingerprint, string PresetSha256)
{
    public static BenchmarkPins Capture(CardDataSet data, IBehaviorCatalog behavior, IRuntimeCardCatalog runtime)
    {
        var evaluation = new GatewayEvaluationCatalog(runtime, behavior);
        using Stream stream = typeof(OfficialPresetCatalog).Assembly.GetManifestResourceStream("Netrunner.Cards.Data.m4.presets.json")
            ?? throw new InvalidOperationException("Official preset resource is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return new(EngineCompatibility.RulesVersion, EngineCompatibility.StateSchemaVersion,
            data.Manifest.CommitSha, data.Manifest.OutputSha256, behavior.Version, behavior.Fingerprint,
            runtime.Version, runtime.Fingerprint, evaluation.Version, evaluation.Fingerprint,
            BenchmarkHash.Bytes(buffer.ToArray()));
    }
}

public sealed record BenchmarkGameSpec(string Id, string CorpPreset, string RunnerPreset, ulong GameSeed,
    string CorpDeckHash, string RunnerDeckHash, BenchmarkAgentSpec Corp, BenchmarkAgentSpec Runner,
    string ComparisonGroup = "baseline", string Split = "held-out");

public sealed record AiBenchmarkManifest(int SchemaVersion, string Kind, string SuiteId, string ContentMode,
    BenchmarkPins Pins, BenchmarkLimits Limits, ImmutableArray<BenchmarkGameSpec> Games)
{
    public const int CurrentSchemaVersion = 1;
    public const string ArtifactKind = "m5-ai-benchmark";

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion || Kind != ArtifactKind || !SafeId(SuiteId)
            || ContentMode is not ("official" or "developer") || Pins is null || Limits is null || Games.IsDefaultOrEmpty)
            throw new ArgumentException("Unsupported or incomplete AI benchmark manifest.");
        Limits.Validate();
        if (Games.Length > 10000 || Games.Any(game => game is null) || Games.Select(game => game.Id).Distinct(StringComparer.Ordinal).Count() != Games.Length)
            throw new ArgumentException("Benchmark rows must have unique IDs and contain at most 10000 games.");
        foreach (BenchmarkGameSpec game in Games)
        {
            if (!SafeId(game.Id) || string.IsNullOrWhiteSpace(game.CorpPreset) || string.IsNullOrWhiteSpace(game.RunnerPreset)
                || !IsHash(game.CorpDeckHash) || !IsHash(game.RunnerDeckHash) || game.Corp is null || game.Runner is null
                || game.Corp.Side != PlayerSide.Corp || game.Runner.Side != PlayerSide.Runner
                || string.IsNullOrWhiteSpace(game.ComparisonGroup) || game.Split is not ("tuning" or "held-out"))
                throw new ArgumentException($"Invalid benchmark row '{game.Id}'.");
            game.Corp.Validate();
            game.Runner.Validate();
        }
    }

    internal static bool SafeId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 100
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    internal static bool IsHash(string? value) => value?.Length == 64 && value.All(char.IsAsciiHexDigit);
}

public enum BenchmarkTermination
{
    Terminal,
    TurnCap,
    ActionCap,
    RepeatedProgress,
    NoDecision,
    IllegalAction,
    InvariantFailure,
    ResolutionFailure,
    AgentException,
    ReplayMismatch,
    WatchdogTimeout,
    Cancelled,
}

public sealed record BenchmarkSideMetrics(int Decisions, ImmutableSortedDictionary<string, int> DecisionKinds,
    ImmutableSortedDictionary<string, int> ActionKinds, int CreditsGained, int CreditsSpent, int Installs,
    int ReplacementInstalls, int Runs, int SuccessfulRuns, int Pumps, int WastedPumps, int? Fallbacks,
    int FinalCredits, int FinalAgendaPoints);
public sealed record BenchmarkDecisionRecord(long Sequence, PlayerSide Side, DecisionKind Kind, string DecisionId,
    long Revision, string? SelectedActionId, ActionKind? ActionKind, string ObservationDigest, string? CheckpointDigest);
public sealed record BenchmarkProgressDiagnostic(string Fingerprint, int Repetitions, int ObservationsInWindow);
public sealed record BenchmarkGameOutcome(BenchmarkTermination Termination, int Actions, int CompletedTurnPairs,
    GameOutcome Outcome, string FinalStateHash, string ReplayHash, bool ReplayVerified,
    BenchmarkSideMetrics Corp, BenchmarkSideMetrics Runner, BenchmarkProgressDiagnostic Progress,
    BenchmarkTermination ExecutionTermination);
public sealed record BenchmarkLatency(int Count, double MedianMilliseconds, double P95Milliseconds, double MaximumMilliseconds);
public sealed record BenchmarkGameTiming(double ElapsedMilliseconds, BenchmarkLatency Corp, BenchmarkLatency Runner);
public sealed record BenchmarkGameResult(BenchmarkGameSpec Game, BenchmarkGameOutcome Result, BenchmarkGameTiming Timing,
    string? Error, string ReplayPath, string? FailurePath);
public sealed record BenchmarkAggregate(int Games, int Terminal, int TurnCaps, int Failures, int CorpWins, int RunnerWins,
    ImmutableSortedDictionary<string, int> Terminations);
public sealed record BenchmarkSeatTotals(int Decisions, long CreditsGained, long CreditsSpent, int Installs,
    int ReplacementInstalls, int Runs, int SuccessfulRuns, int Pumps, int WastedPumps, int? Fallbacks,
    long FinalCredits, int FinalAgendaPoints);
public sealed record BenchmarkStratum(string ComparisonGroup, string CorpPreset, string RunnerPreset,
    BenchmarkAggregate Aggregate, long Actions, long CompletedTurnPairs, BenchmarkSeatTotals Corp, BenchmarkSeatTotals Runner);
public sealed record AiBenchmarkReport(int SchemaVersion, string Kind, string SuiteId, string ManifestHash,
    BenchmarkPins Pins, string BuildRevision, string Runtime, string OperatingSystem, string Architecture,
    int Workers, string DeterministicDigest, BenchmarkAggregate Aggregate, ImmutableArray<BenchmarkGameResult> Games,
    ImmutableArray<BenchmarkStratum> Strata, ImmutableSortedDictionary<string, string> AssemblyHashes);

public interface IBenchmarkAgentFactory
{
    void Validate(BenchmarkAgentSpec specification);
    IGameAgent Create(BenchmarkAgentSpec specification, IRuntimeCardCatalog runtime, ICardEvaluationCatalog evaluation);
}

/// <summary>Later policy slices extend this registry; unavailable policies never silently become the baseline.</summary>
public sealed class BenchmarkAgentFactory : IBenchmarkAgentFactory
{
    public void Validate(BenchmarkAgentSpec specification)
    {
        specification.Validate();
        if (specification.Kind == "offline" && OfflinePolicies.IsSupported(specification.Side, specification.PolicyVersion)
            && specification.Configuration is { } configuration)
        {
            OfflinePolicies.Validate(configuration);
            return;
        }
        if (specification.Kind != "contract" || specification.AgentVersion != FullGameContractAgent.Version
            || specification.PolicyVersion != FullGameContractPolicy.Version || specification.ProfileId != "contract-baseline"
            || specification.Configuration is not null)
            throw new ArgumentException($"Benchmark policy '{specification.Kind}/{specification.PolicyVersion}' is not installed.");
    }

    public IGameAgent Create(BenchmarkAgentSpec specification, IRuntimeCardCatalog runtime, ICardEvaluationCatalog evaluation)
    {
        Validate(specification);
        if (specification.Kind == "offline")
            return new OfflineAgent(specification.Configuration!, OfflinePolicies.Create(specification.Configuration!),
                evaluation, captureDiagnostics: true);
        return new FullGameContractAgent(specification.Side, runtime);
    }
}

internal static class BenchmarkHash
{
    public static string Value<T>(T value) => Bytes(Encoding.UTF8.GetBytes(CanonicalJson.Serialize(value)));
    public static string Bytes(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}

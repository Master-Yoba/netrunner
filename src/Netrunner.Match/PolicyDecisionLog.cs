using System.Text;
using Netrunner.AI;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>Private, opt-in evidence of a choice before Apply; never a player-facing report.</summary>
public sealed record PolicyDecisionDiagnostic(int SchemaVersion, string ArtifactKind, string Stage,
    string SuiteId, string GameId, string ManifestHash, string BuildRevision, BenchmarkPins Pins,
    OfflineAgentConfiguration Configuration, PlayerSide Side, long CommittedActionSequence,
    DecisionId DecisionId, long DecisionRevision, DecisionKind DecisionKind,
    double AgentCallMilliseconds, AgentEvaluation Evaluation);

internal sealed record PolicyDiagnosticContext(string SuiteId, string ManifestHash, string BuildRevision, BenchmarkPins Pins);

internal sealed class PolicyDecisionLog(string path, PolicyDiagnosticContext context, string gameId) : IAsyncDisposable
{
    private readonly StreamWriter _writer = new(new FileStream(path, FileMode.CreateNew, FileAccess.Write,
        FileShare.Read, 4096, FileOptions.Asynchronous), new UTF8Encoding(false));

    public async Task WriteAsync(OfflineAgent agent, PendingDecision decision, long actionSequence, double milliseconds)
    {
        AgentEvaluation evaluation = agent.LastEvaluation
            ?? throw new InvalidOperationException("The requested policy diagnostics were not captured by this agent.");
        var record = new PolicyDecisionDiagnostic(1, "private-policy-decision", "selected-before-apply",
            context.SuiteId, gameId, context.ManifestHash, context.BuildRevision, context.Pins,
            agent.Configuration, agent.ControlledSide, actionSequence, decision.Id, decision.Revision, decision.Kind,
            milliseconds, evaluation);
        await _writer.WriteLineAsync(EngineJson.Serialize(record)).ConfigureAwait(false);
        // Preserve a selected action even when its subsequent Apply fails.
        // This flush is outside the measured agent call and cannot affect policy input.
        await _writer.FlushAsync().ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => _writer.DisposeAsync();
}

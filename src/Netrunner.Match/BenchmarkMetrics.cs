using System.Collections.Immutable;
using Netrunner.AI;
using Netrunner.Core;

namespace Netrunner.Match;

internal sealed class BenchmarkMetrics
{
    private readonly SideCounter _corp = new();
    private readonly SideCounter _runner = new();
    private readonly Dictionary<CardInstanceId, int> _unspentPumps = [];

    public void PolicyEvaluation(PlayerSide side, AgentEvaluation? evaluation)
    {
        if (evaluation is null) return;
        SideCounter counter = Side(side);
        counter.Fallbacks ??= 0;
        if (evaluation.Candidates.Single(candidate => candidate.ActionId == evaluation.SelectedAction).Utility.UsedFallback)
            counter.Fallbacks++;
    }

    public void Decision(PlayerSide side, DecisionKind kind, double milliseconds)
    {
        SideCounter counter = Side(side);
        counter.Decisions++;
        Increment(counter.DecisionKinds, kind.ToString());
        counter.Latencies.Add(milliseconds);
    }

    public void Accepted(PlayerSide actor, LegalAction action, GameState before, ApplyResult result)
    {
        Increment(Side(actor).ActionKinds, action.Kind.ToString());
        if (action.Parameters is InstallCardParameters { FinalizeSelection: true, SelectedReplacementCardIds.Count: > 0 })
            Side(actor).ReplacementInstalls++;
        if (action.Kind == ActionKind.PumpBreaker && action.Parameters is RunBreakerParameters pump)
        {
            _runner.Pumps++;
            _unspentPumps[pump.BreakerId] = _unspentPumps.GetValueOrDefault(pump.BreakerId) + 1;
        }
        if (action.Kind == ActionKind.BreakSubroutines && action.Parameters is RunBreakParameters broken)
            _unspentPumps.Remove(broken.BreakerId);
        if (before.RunState?.Encounter?.Id != result.State.RunState?.Encounter?.Id)
        {
            _runner.WastedPumps += _unspentPumps.Values.Sum();
            _unspentPumps.Clear();
        }
        foreach (EngineEvent engineEvent in result.Events)
        {
            switch (engineEvent.Payload)
            {
                case CreditsAdjustedEventPayload credit:
                    SideCounter counter = Side(result.State.GetPlayer(credit.Target).Side);
                    if (credit.Delta > 0) counter.CreditsGained += credit.Delta;
                    else counter.CreditsSpent -= credit.Delta;
                    break;
                case CardInstalledEventPayload installed: Side(installed.Side).Installs++; break;
                case RunInitiatedEventPayload run: Side(run.InitiatingSide).Runs++; break;
                case RunSuccessfulEventPayload: _runner.SuccessfulRuns++; break;
            }
        }
    }

    public BenchmarkSideMetrics Snapshot(GameState state, PlayerSide side)
    {
        SideCounter count = Side(side);
        PlayerState player = state.Players.Single(value => value.Side == side);
        return new(count.Decisions, count.DecisionKinds.ToImmutableSortedDictionary(StringComparer.Ordinal),
            count.ActionKinds.ToImmutableSortedDictionary(StringComparer.Ordinal), count.CreditsGained, count.CreditsSpent,
            count.Installs, count.ReplacementInstalls, count.Runs, count.SuccessfulRuns, count.Pumps, count.WastedPumps,
            count.Fallbacks, player.Credits, player.AgendaPoints);
    }

    public BenchmarkLatency Latency(PlayerSide side)
    {
        double[] values = Side(side).Latencies.Order().ToArray();
        return values.Length == 0 ? new(0, 0, 0, 0)
            : new(values.Length, values[(values.Length - 1) / 2], values[(int)Math.Ceiling(values.Length * 0.95) - 1], values[^1]);
    }

    private SideCounter Side(PlayerSide side) => side == PlayerSide.Corp ? _corp : _runner;
    private static void Increment(Dictionary<string, int> counters, string key) => counters[key] = counters.GetValueOrDefault(key) + 1;

    private sealed class SideCounter
    {
        public int Decisions { get; set; }
        public Dictionary<string, int> DecisionKinds { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> ActionKinds { get; } = new(StringComparer.Ordinal);
        public List<double> Latencies { get; } = [];
        public int CreditsGained { get; set; }
        public int CreditsSpent { get; set; }
        public int Installs { get; set; }
        public int ReplacementInstalls { get; set; }
        public int Runs { get; set; }
        public int SuccessfulRuns { get; set; }
        public int Pumps { get; set; }
        public int WastedPumps { get; set; }
        public int? Fallbacks { get; set; }
    }
}

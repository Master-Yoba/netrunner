using System.Text.Json.Nodes;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>Harness-only inspection. None of this authoritative state reaches a policy.</summary>
public sealed class BenchmarkProgressDetector(BenchmarkLimits limits)
{
    private static readonly HashSet<string> ChurnKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "identity", "frameId", "frameIdentity", "parentFrameIdentity", "continuationId", "decisionId", "revision",
        "decisionRevision", "sequence", "eventSequence", "actionSequence", "runId", "encounterId", "occurrenceId",
        "sourceOccurrenceId", "triggerId", "interruptId", "effectId", "costScopeId", "scopeId", "turnNumber",
    };
    private readonly Queue<string> _window = new();
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    public BenchmarkProgressDiagnostic Current { get; private set; } = new("", 0, 0);

    public bool Observe(GameState state)
    {
        string fingerprint = Fingerprint(state);
        _window.Enqueue(fingerprint);
        _counts[fingerprint] = _counts.GetValueOrDefault(fingerprint) + 1;
        if (_window.Count > limits.ProgressWindow)
        {
            string old = _window.Dequeue();
            if (--_counts[old] == 0) _counts.Remove(old);
        }
        Current = new(fingerprint, _counts[fingerprint], _window.Count);
        return Current.Repetitions >= limits.RepetitionCount;
    }

    public static string Fingerprint(GameState state)
    {
        // Card instance identity, deck/zone order, counters and resources are
        // semantic. Allocators, RNG cursor and generated decision/frame IDs are
        // not evidence of progress. Turn count has its own explicit cap.
        var projection = new
        {
            Players = state.Players.Select(player => new { player.Side, player.Credits, player.Clicks, player.AgendaPoints }).ToArray(),
            Cards = state.Cards.Select(card => new
            {
                CardId = card.Id,
                card.DefinitionId,
                card.ZoneId,
                card.OwnerSide,
                card.FaceUp,
                card.Rezzed,
                card.AdvancementCounters,
                card.HostCardId,
            }).ToArray(),
            Zones = state.Zones.Select(zone => new { ZoneId = zone.Id, zone.Kind, zone.ServerId, zone.CardIds }).ToArray(),
            state.Servers,
            state.Turn,
            state.Outcome,
            state.RunState,
            state.RulesState,
            state.ResolutionStack,
            Decision = state.PendingDecision is { } pending ? new
            {
                pending.Owner,
                pending.Kind,
                Actions = pending.LegalActions.Select(action => new { action.Kind, action.Parameters }).ToArray(),
            } : null,
        };
        JsonNode node = JsonNode.Parse(EngineJson.Serialize(projection))!;
        Strip(node);
        return BenchmarkHash.Bytes(CanonicalJson.Canonicalize(node.ToJsonString()));
    }

    private static void Strip(JsonNode? node)
    {
        if (node is JsonObject value)
        {
            foreach (string key in value.Select(pair => pair.Key).ToArray())
            {
                if (ChurnKeys.Contains(key)) value.Remove(key);
                else Strip(value[key]);
            }
        }
        else if (node is JsonArray array) foreach (JsonNode? item in array) Strip(item);
    }
}

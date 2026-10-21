using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

/// <summary>
/// Versioned, card-independent SCN-400 timing fixture. This deliberately is
/// not a <see cref="ScenarioArtifact"/>: it describes the CR turn trace and
/// its citations, rather than authoring a board state or a card resolution.
/// </summary>
public sealed class TurnScenarioArtifact
{
    [JsonConstructor]
    public TurnScenarioArtifact(
        int schemaVersion,
        string scenarioId,
        string rulesVersion,
        int engineStateSchemaVersion,
        string scope,
        IReadOnlyList<TurnScenarioStep>? turnSequence,
        IReadOnlyList<string>? requiredEvents,
        IReadOnlyList<string>? citations,
        string crossCheckNote,
        IReadOnlyList<string>? excluded = null)
    {
        ArgumentNullException.ThrowIfNull(scenarioId);
        ArgumentNullException.ThrowIfNull(rulesVersion);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(crossCheckNote);
        ArgumentNullException.ThrowIfNull(turnSequence);
        ArgumentNullException.ThrowIfNull(requiredEvents);
        ArgumentNullException.ThrowIfNull(citations);
        SchemaVersion = schemaVersion;
        ScenarioId = scenarioId;
        RulesVersion = rulesVersion;
        EngineStateSchemaVersion = engineStateSchemaVersion;
        Scope = scope;
        TurnSequence = new ReadOnlyCollection<TurnScenarioStep>(turnSequence.Select(CloneStep).ToArray());
        RequiredEvents = new ReadOnlyCollection<string>(requiredEvents.ToArray());
        Citations = new ReadOnlyCollection<string>(citations.ToArray());
        CrossCheckNote = crossCheckNote;
        Excluded = new ReadOnlyCollection<string>((excluded ?? []).ToArray());
    }

    [JsonInclude]
    [JsonRequired]
    public int SchemaVersion { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string ScenarioId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string RulesVersion { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int EngineStateSchemaVersion { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string Scope { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<TurnScenarioStep> TurnSequence { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<string> RequiredEvents { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<string> Citations { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string CrossCheckNote { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<string> Excluded { get; private set; }

    public IReadOnlyList<StateInvariantViolation> CollectViolations()
    {
        var violations = new List<StateInvariantViolation>();
        if (SchemaVersion != 1)
        {
            Add(violations, "turn-scenario.unsupported-schema", "schemaVersion", "SCN-400 requires schema version 1.");
        }

        if (!string.Equals(ScenarioId, "SCN-400", StringComparison.Ordinal))
        {
            Add(violations, "turn-scenario.id-mismatch", "scenarioId", "The turn fixture must be SCN-400.");
        }

        if (!string.Equals(RulesVersion, EngineCompatibility.RulesVersion, StringComparison.Ordinal))
        {
            Add(violations, "turn-scenario.unsupported-rules", "rulesVersion", $"SCN-400 requires rules version '{EngineCompatibility.RulesVersion}'.");
        }

        if (EngineStateSchemaVersion != EngineCompatibility.StateSchemaVersion)
        {
            Add(violations, "turn-scenario.state-schema-mismatch", "engineStateSchemaVersion", "SCN-400 pins the current engine state schema.");
        }

        if (!string.Equals(Scope, "ENG-401", StringComparison.Ordinal))
        {
            Add(violations, "turn-scenario.scope-mismatch", "scope", "SCN-400 is scoped to ENG-401.");
        }

        if (TurnSequence.Count == 0)
        {
            Add(violations, "turn-scenario.empty-sequence", "turnSequence", "SCN-400 must contain an ordered turn sequence.");
        }

        var seenEvents = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? eventKind in RequiredEvents)
        {
            if (string.IsNullOrWhiteSpace(eventKind) || !seenEvents.Add(eventKind))
            {
                Add(violations, "turn-scenario.duplicate-event", "requiredEvents", "SCN-400 event requirements must be nonempty and unique.");
            }
        }

        foreach (string required in new[]
        {
            EngineEventKinds.TurnClicksAllotted,
            EngineEventKinds.TurnTimingWindowOpened,
            EngineEventKinds.TurnStarted,
            EngineEventKinds.TurnEnded,
            EngineEventKinds.TurnCardDiscarded,
            EngineEventKinds.TurnClicksLost,
        })
        {
            if (!seenEvents.Contains(required))
            {
                Add(violations, "turn-scenario.missing-event", "requiredEvents", $"SCN-400 must require '{required}'.");
            }
        }

        foreach (string? citation in Citations)
        {
            if (string.IsNullOrWhiteSpace(citation) || !citation.StartsWith("CR 26.03 ", StringComparison.Ordinal))
            {
                Add(violations, "turn-scenario.invalid-citation", "citations", "SCN-400 citations must identify CR 26.03.");
            }
        }

        if (!CrossCheckNote.Contains("Jinteki.net", StringComparison.OrdinalIgnoreCase))
        {
            Add(violations, "turn-scenario.missing-cross-check", "crossCheckNote", "SCN-400 must record the Jinteki.net cross-check.");
        }

        if (!Excluded.Any(value => value.Contains("ENG-402", StringComparison.Ordinal)))
        {
            Add(violations, "turn-scenario.scope-leak", "excluded", "SCN-400 must explicitly exclude ENG-402 basic actions.");
        }

        for (int index = 0; index < TurnSequence.Count; index++)
        {
            TurnScenarioStep step = TurnSequence[index];
            string path = $"turnSequence[{index}]";
            if (step is null)
            {
                Add(violations, "turn-scenario.null-step", path, "Turn sequence steps must not be null.");
                continue;
            }

            if (step.Side is not ("corp" or "runner"))
            {
                Add(violations, "turn-scenario.invalid-side", $"{path}.side", "Turn steps must identify Corp or Runner.");
            }

            if (string.IsNullOrWhiteSpace(step.Step))
            {
                Add(violations, "turn-scenario.empty-step", $"{path}.step", "Turn steps must identify a CR timing step.");
            }

            if (step.Clicks is < 0)
            {
                Add(violations, "turn-scenario.invalid-clicks", $"{path}.clicks", "Allotted clicks must be nonnegative.");
            }

            if (step.Capabilities is not null
                && step.Capabilities.Any(capability => capability is not ("P" or "R" or "S")))
            {
                Add(violations, "turn-scenario.invalid-capability", $"{path}.capabilities", "Timing capabilities must be P, R, or S.");
            }
        }

        return violations.AsReadOnly();
    }

    public void Validate()
    {
        IReadOnlyList<StateInvariantViolation> violations = CollectViolations();
        if (violations.Count > 0)
        {
            throw new StateInvariantException(violations);
        }
    }

    private static TurnScenarioStep CloneStep(TurnScenarioStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return new TurnScenarioStep(
        step.Side,
        step.Step,
        step.Clicks,
        step.Window,
        step.Capabilities,
        step.ForcedToMaximumHandSize,
        step.LeftoverClicksLost);
    }

    private static void Add(List<StateInvariantViolation> violations, string code, string path, string message) =>
        violations.Add(new StateInvariantViolation(code, path, message));
}

/// <summary>One serializable, card-independent step in SCN-400.</summary>
public sealed record TurnScenarioStep
{
    [JsonConstructor]
    public TurnScenarioStep(
        string side,
        string step,
        int? clicks = null,
        string? window = null,
        IReadOnlyList<string>? capabilities = null,
        bool? forcedToMaximumHandSize = null,
        bool? leftoverClicksLost = null)
    {
        ArgumentNullException.ThrowIfNull(side);
        ArgumentNullException.ThrowIfNull(step);
        Side = side;
        Step = step;
        Clicks = clicks;
        Window = window;
        Capabilities = capabilities is null ? null : new ReadOnlyCollection<string>(capabilities.ToArray());
        ForcedToMaximumHandSize = forcedToMaximumHandSize;
        LeftoverClicksLost = leftoverClicksLost;
    }

    [JsonInclude]
    [JsonRequired]
    public string Side { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string Step { get; private set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Clicks { get; private set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Window { get; private set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Capabilities { get; private set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ForcedToMaximumHandSize { get; private set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LeftoverClicksLost { get; private set; }
}

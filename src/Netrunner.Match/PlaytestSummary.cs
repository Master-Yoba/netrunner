using System.Globalization;
using System.Text.Json;

namespace Netrunner.Match;

/// <summary>
/// The U6 playtest protocol's numbers for one recorded client session, read
/// from its <c>ui-diagnostics.jsonl</c>: time per turn of the local player,
/// rejected drops and submissions, how often "All legal actions…" and skipping
/// were used, and which first-game hints came up. It reads only UI telemetry,
/// which names no hidden cards.
/// </summary>
public sealed record PlaytestSummary(
    string? HumanSide,
    string? CorpDeck,
    string? RunnerDeck,
    double MinutesPlayed,
    IReadOnlyList<double> TurnSeconds,
    int Submissions,
    int RejectedSubmissions,
    int Drops,
    int InvalidDrops,
    int CardInspections,
    int AllLegalActions,
    int SkipAnimation,
    int StopAtWindow,
    IReadOnlyList<string> HintsShown,
    int HintsDismissed,
    bool HintsTurnedOff,
    bool Finished)
{
    public double MedianTurnSeconds => Median(TurnSeconds);

    public static PlaytestSummary Read(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        string? side = null, corp = null, runner = null;
        DateTimeOffset? first = null, last = null, turnStart = null;
        string? turnKey = null;
        List<double> turns = [];
        int submissions = 0, rejected = 0, drops = 0, invalidDrops = 0, inspections = 0, allActions = 0, skips = 0, stops = 0, dismissed = 0;
        List<string> hints = [];
        bool hintsOff = false, finished = false;
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            DateTimeOffset time = DateTimeOffset.Parse(root.GetProperty("timestampUtc").GetString()!, CultureInfo.InvariantCulture);
            first ??= time;
            last = time;
            JsonElement payload = root.TryGetProperty("payload", out JsonElement value) ? value : default;
            switch (root.GetProperty("kind").GetString())
            {
                case "startup":
                    side = Text(payload, "humanSide");
                    corp = Text(payload, "corpPreset");
                    runner = Text(payload, "runnerPreset");
                    break;
                case "displayed-snapshot":
                    {
                        string? viewer = Text(payload, "viewer");
                        string? active = Text(payload, "activeSide");
                        string key = $"{Text(payload, "turn")}:{active}";
                        bool ownTurn = viewer is not null && string.Equals(viewer, active, StringComparison.OrdinalIgnoreCase);
                        if (key != turnKey)
                        {
                            if (turnStart is { } start) turns.Add((time - start).TotalSeconds);
                            turnStart = ownTurn ? time : null;
                            turnKey = key;
                        }
                        if (payload.TryGetProperty("terminal", out JsonElement terminal) && terminal.ValueKind == JsonValueKind.True)
                        {
                            if (!finished && turnStart is { } open) turns.Add((time - open).TotalSeconds);
                            turnStart = null;
                            finished = true;
                        }
                        break;
                    }
                case "submission": submissions++; break;
                case "submission-result":
                    if (payload.TryGetProperty("accepted", out JsonElement accepted) && accepted.ValueKind == JsonValueKind.False) rejected++;
                    break;
                case "drag-outcome":
                    string? outcome = Text(payload, "outcome");
                    if (outcome == "drop") drops++;
                    else if (outcome == "invalid-drop") invalidDrops++;
                    break;
                case "selection":
                    string? selection = Text(payload, "value");
                    switch (Text(payload, "selectionKind"))
                    {
                        case "card-inspect": inspections++; break;
                        case "hint" when selection is not null: hints.Add(selection); break;
                        case "hint-dismissed": dismissed++; break;
                        case "ui-control":
                            if (selection == "all-legal-actions") allActions++;
                            else if (selection == "skip-animation") skips++;
                            else if (selection == "stop-at-window") stops++;
                            else if (selection == "hints-off") hintsOff = true;
                            break;
                    }
                    break;
            }
        }
        double minutes = first is { } from && last is { } to ? (to - from).TotalMinutes : 0;
        return new PlaytestSummary(side, corp, runner, minutes, turns, submissions, rejected, drops, invalidDrops, inspections,
            allActions, skips, stops, hints, dismissed, hintsOff, finished);
    }

    public string Format()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"playtest side={HumanSide ?? "?"} corp={CorpDeck ?? "?"} runner={RunnerDeck ?? "?"} minutes={MinutesPlayed:0.0} finished={Finished}");
        text.AppendLine(CultureInfo.InvariantCulture, $"turns={TurnSeconds.Count} median-seconds={MedianTurnSeconds:0} longest-seconds={(TurnSeconds.Count == 0 ? 0 : TurnSeconds.Max()):0}");
        text.AppendLine(CultureInfo.InvariantCulture, $"submissions={Submissions} rejected={RejectedSubmissions} drops={Drops} invalid-drops={InvalidDrops} inspections={CardInspections}");
        text.AppendLine(CultureInfo.InvariantCulture, $"all-legal-actions={AllLegalActions} skip-animation={SkipAnimation} stop-at-window={StopAtWindow}");
        text.Append(CultureInfo.InvariantCulture, $"hints={(HintsShown.Count == 0 ? "none" : string.Join(',', HintsShown))} dismissed={HintsDismissed} turned-off={HintsTurnedOff}");
        return text.ToString();
    }

    private static string? Text(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out JsonElement value) && value.ValueKind != JsonValueKind.Null
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText()
            : null;

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        double[] sorted = [.. values.Order()];
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }
}

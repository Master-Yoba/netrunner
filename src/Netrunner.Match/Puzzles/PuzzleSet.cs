using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Netrunner.Core;
using Netrunner.Match.Regret;

namespace Netrunner.Match.Puzzles;

/// <summary>
/// Matches an engine-issued action (bot plan S2.1): its kind, and a subset of its parameters as JSON with every card
/// instance read as its definition (<c>def:ID</c>; <see cref="RegretRoots.Parameters"/>). A property in the pattern must
/// be equal in the action; an array in the pattern must have every element in the action's array.
/// </summary>
public sealed record ActionMatch(ActionKind? Kind = null, JsonObject? Parameters = null)
{
    public bool Matches(GameState state, LegalAction action)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(action);
        if (Kind is { } kind && action.Kind != kind) return false;
        if (Parameters is null) return true;
        return JsonNode.Parse(RegretRoots.Parameters(state, action)) is JsonObject actual && Contains(actual, Parameters);
    }

    private static bool Contains(JsonNode? actual, JsonNode? pattern) => pattern switch
    {
        null => actual is null,
        JsonObject expected => actual is JsonObject found
            && expected.All(property => found.TryGetPropertyValue(property.Key, out JsonNode? value) && Contains(value, property.Value)),
        JsonArray expected => actual is JsonArray found && expected.All(element => found.Any(item => Contains(item, element))),
        _ => JsonNode.DeepEquals(actual, pattern),
    };
}

/// <summary>
/// A predicate over a line (bot plan S2.1): the puzzle side's actions from the root to the turn's end, and the state there.
/// <list type="bullet">
/// <item><c>installed</c>: a card of definition <see cref="Card"/> is installed (in a server or the rig).</item>
/// <item><c>counters</c>: an installed <see cref="Card"/> has at least <see cref="AtLeast"/> <see cref="Counter"/> counters
/// (<c>advancement</c> by default).</item>
/// <item><c>credits</c>: the puzzle side ends with at least <see cref="AtLeast"/> credits.</item>
/// <item><c>grip</c>: the Runner ends with at least <see cref="AtLeast"/> cards in the grip.</item>
/// <item><c>successfulRun</c>: a run on <see cref="Server"/> succeeded this turn.</item>
/// <item><c>action</c>: at least <see cref="AtLeast"/> (default 1) of the puzzle side's actions match <see cref="Match"/>.</item>
/// <item><c>before</c>: an action matching <see cref="Match"/> comes before any action matching <see cref="Then"/>, and one does.</item>
/// <item><c>first</c>: the puzzle side's first action matches <see cref="Match"/>.</item>
/// <item><c>precedes</c>: if an action matches <see cref="Then"/>, one matching <see cref="Match"/> came before it (true when none does).</item>
/// </list>
/// <see cref="Not"/> negates the predicate.
/// </summary>
public sealed record LinePredicate(string Kind, string? Card = null, string? Server = null, string? Counter = null, int? AtLeast = null,
    ActionMatch? Match = null, ActionMatch? Then = null, bool Not = false);

/// <summary>
/// How a puzzle is judged. <c>decision</c> and <c>nested</c>: the choice must match one of <see cref="Accept"/> (when there
/// are any) and none of <see cref="Reject"/>. <c>nested</c> plays the line from the root to the first decision of the
/// puzzle side where an action matches <see cref="At"/>, and judges the choice there. <c>line</c>: every predicate holds.
/// </summary>
public sealed record PuzzleAcceptance(ImmutableArray<ActionMatch> Accept = default, ImmutableArray<ActionMatch> Reject = default,
    ImmutableArray<LinePredicate> Line = default, ActionMatch? At = null);

/// <summary>
/// One puzzle (bot plan 5.3): a root in the S1.1 format (a game of the set and a sequence), whose decision it is, its kind,
/// category and split, and its acceptance. A negative control is a puzzle whose acceptance rejects the tempting action.
/// </summary>
public sealed record Puzzle(string Id, string Title, string Category, string Kind, string Split, string GameId, long Sequence, PlayerSide Side,
    PuzzleAcceptance Acceptance, bool NegativeControl = false, string? Source = null, string? Note = null)
{
    public const string DecisionKind = "decision";
    public const string LineKind = "line";
    public const string NestedKind = "nested";

    /// <summary>
    /// Bot plan S1.5 and its follow-up: teacher-certified puzzles a later teacher no longer certifies, kept out of the gate
    /// (<c>ai-puzzles run --split teacher-v1</c> runs them). <c>teacher-v1</c> failed the line teacher (v1-lines);
    /// <c>teacher-v2</c> failed teacher v2, the line teacher with the live opponent; <c>teacher-v3</c> failed teacher v3,
    /// the line search played to the game's end.
    /// </summary>
    public const string RetiredSplit = "teacher-v1";
    public const string RetiredSplitV2 = "teacher-v2";
    public const string RetiredSplitV3 = "teacher-v3";

    /// <summary>Whether a split is out of the gate.</summary>
    public static bool Retired(string split) => split is RetiredSplit or RetiredSplitV2 or RetiredSplitV3;

    /// <summary>The ten categories of the plan's 5.3.</summary>
    public static IReadOnlyList<string> Categories { get; } =
        ["sequencing", "agenda-exposure", "punisher-risk", "run-targeting", "economy", "access-and-trash", "ice-placement", "rez-timing",
            "hand-management", "inference"];
}

/// <summary>A pool's puzzles on disk: <c>games.jsonl.gz</c> (the regret suite's compact games) and <c>puzzles.json</c>.</summary>
public sealed record PuzzleSet(ImmutableArray<RegretGame> Games, ImmutableArray<Puzzle> Puzzles)
{
    public const string PuzzlesFile = "puzzles.json";

    public static JsonSerializerOptions Json { get; } = CreateJson();

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    public static PuzzleSet Load(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        string games = Path.Combine(directory, RegretSuite.GamesFile);
        PuzzleSet set = new(File.Exists(games) ? RegretSuite.ReadLines<RegretGame>(games) : [],
            JsonSerializer.Deserialize<ImmutableArray<Puzzle>>(File.ReadAllText(Path.Combine(directory, PuzzlesFile)), Json));
        set.Validate();
        return set;
    }

    public void Save(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        Validate();
        Directory.CreateDirectory(directory);
        RegretSuite.WriteLines(Path.Combine(directory, RegretSuite.GamesFile), Games.OrderBy(game => game.Id, StringComparer.Ordinal));
        File.WriteAllText(Path.Combine(directory, PuzzlesFile), JsonSerializer.Serialize(Puzzles, Json) + "\n");
    }

    /// <summary>What is wrong with a line predicate: an unknown kind, or a missing operand its kind needs; empty when nothing is.</summary>
    private static string Problem(LinePredicate predicate) => predicate.Kind switch
    {
        "installed" or "counters" when predicate.Card is null => $"a '{predicate.Kind}' predicate without its card",
        "successfulRun" when predicate.Server is null => "a 'successfulRun' predicate without its server",
        "action" or "first" when predicate.Match is null => $"a '{predicate.Kind}' predicate without the action to match",
        "before" or "precedes" when predicate.Match is null || predicate.Then is null => $"a '{predicate.Kind}' predicate without both actions",
        "installed" or "counters" or "credits" or "grip" or "successfulRun" or "action" or "first" or "before" or "precedes" => "",
        _ => $"an unknown line predicate '{predicate.Kind}'",
    };

    /// <summary>
    /// Every puzzle has a unique id, a known kind, category and split, a game of the set, and an acceptance its kind uses,
    /// with every line predicate of a known kind and with the operands that kind needs.
    /// </summary>
    public void Validate()
    {
        HashSet<string> games = [.. Games.Select(game => game.Id)];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (Puzzle puzzle in Puzzles)
        {
            string problem = !ids.Add(puzzle.Id) ? "a duplicate id"
                : puzzle.Kind is not (Puzzle.DecisionKind or Puzzle.LineKind or Puzzle.NestedKind) ? $"an unknown kind '{puzzle.Kind}'"
                : !Puzzle.Categories.Contains(puzzle.Category) ? $"an unknown category '{puzzle.Category}'"
                : puzzle.Split is not ("tuning" or "held-out") && !Puzzle.Retired(puzzle.Split) ? $"an unknown split '{puzzle.Split}'"
                : !games.Contains(puzzle.GameId) ? $"no game '{puzzle.GameId}'"
                : puzzle.Kind == Puzzle.LineKind && puzzle.Acceptance.Line.IsDefaultOrEmpty ? "a line puzzle without predicates"
                : puzzle.Kind != Puzzle.LineKind && puzzle.Acceptance.Accept.IsDefaultOrEmpty && puzzle.Acceptance.Reject.IsDefaultOrEmpty
                    ? "a decision without accepted or rejected actions"
                : puzzle.Kind == Puzzle.NestedKind && puzzle.Acceptance.At is null ? "a nested puzzle without the decision to stop at"
                : (puzzle.Acceptance.Line.IsDefault ? [] : puzzle.Acceptance.Line).Select(Problem).FirstOrDefault(found => found.Length > 0) ?? "";
            if (problem.Length > 0) throw new InvalidDataException($"Puzzle '{puzzle.Id}' has {problem}.");
        }
    }
}

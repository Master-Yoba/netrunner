using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Netrunner.Core;

namespace Netrunner.Match.Regret;

/// <summary>
/// A game of the regret suite, stored compactly (bot plan S1.1): its deal and, at every decision, the index of the
/// chosen action among the engine's legal actions. Replaying it from the deal reproduces the game exactly, which
/// <see cref="FinalStateHash"/> checks, so the suite carries no engine state of its own.
/// </summary>
public sealed record RegretGame(string Id, string ContentId, ulong GameSeed, string Corp, string Runner, string Split, string Source,
    ImmutableArray<int> Choices, string FinalStateHash);

/// <summary>Actions that are the same choice (copies of one card): one kind and one description. One of them is labelled.</summary>
public sealed record RegretCandidate(string Group, ActionKind Kind, string Description, ImmutableArray<string> ActionIds);

/// <summary>
/// A decision of the regret suite: where it is (game and sequence), whose it is, and the candidate groups the teacher
/// labels. A version whose choice falls outside them is counted as unlabelled, not guessed.
/// </summary>
public sealed record RegretRoot(string Id, string GameId, long Sequence, PlayerSide Side, string Kind, string Split,
    ImmutableArray<string> Categories, ImmutableArray<RegretCandidate> Candidates);

/// <summary>
/// A labelled candidate group: the teacher's mean win probability for the acting side, and its standard error. With the
/// turn-line search, <paramref name="Line"/> is its best line: the heuristic's choice at each later turn action (bot plan S1.5).
/// </summary>
public sealed record RegretValue(string Group, double Mean, double StandardError, int PlayOuts, int[]? Line = null);

/// <summary>
/// The teacher's labels for one root and how they were made; <paramref name="Lines"/> is the turn-line search's beam (0
/// without it) and <paramref name="ScreenTurns"/> its screen's horizon (<see cref="TeacherSettings.ScreenTurns"/>).
/// </summary>
public sealed record RegretLabel(string RootId, int Samples, int HorizonTurns, int Evaluation, ulong Seed, ImmutableArray<RegretValue> Values,
    int Lines = 0, int ScreenTurns = 0)
{
    /// <summary>The best labelled group, or null when no group has a value.</summary>
    [JsonIgnore]
    public RegretValue? Best => Values.Where(value => !double.IsNaN(value.Mean))
        .OrderByDescending(value => value.Mean).ThenBy(value => value.Group, StringComparer.Ordinal).FirstOrDefault();
}

/// <summary>
/// The regret suite on disk (bot plan 5.1): <c>games.jsonl.gz</c>, <c>roots.jsonl.gz</c>, and the teacher's
/// <c>labels.jsonl.gz</c> once labelled. Every file is ordered, so a suite rebuilt from the same inputs is the same bytes.
/// </summary>
public sealed record RegretSuite(ImmutableArray<RegretGame> Games, ImmutableArray<RegretRoot> Roots, ImmutableArray<RegretLabel> Labels)
{
    public const string GamesFile = "games.jsonl.gz";
    public const string RootsFile = "roots.jsonl.gz";
    public const string LabelsFile = "labels.jsonl.gz";

    /// <summary>The suite's JSON settings: web defaults with enums as camel-case strings.</summary>
    public static JsonSerializerOptions Json { get; } = CreateJson();

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = false };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    /// <summary>
    /// The suite with one root per position and held out by deal (bot plan S1.1). A deal's games share their positions
    /// until play diverges, so a position drawn in two of them is kept once (the first by game id), and a deal's games
    /// are all held out or all not, so no position is in both splits. Labels of dropped roots are dropped with them.
    /// </summary>
    public RegretSuite Deduplicated()
    {
        static bool HeldOut(RegretGame game) => game.Source == "finding" || RegretRoots.StableHash($"{game.ContentId}|{game.GameSeed}") % 5 == 0;
        var games = Games.ToDictionary(game => game.Id, game => game with { Split = HeldOut(game) ? "held-out" : "train" }, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var roots = ImmutableArray.CreateBuilder<RegretRoot>();
        foreach (RegretRoot root in Roots.OrderBy(root => root.GameId, StringComparer.Ordinal).ThenBy(root => root.Sequence))
        {
            RegretGame game = games[root.GameId];
            string position = $"{game.ContentId}|{game.GameSeed}|{string.Join(',', game.Choices.Take((int)root.Sequence - 1))}";
            if (seen.Add(position)) roots.Add(root with { Split = game.Split });
        }
        HashSet<string> kept = [.. roots.Select(root => root.Id)];
        return new RegretSuite([.. games.Values.OrderBy(game => game.Id, StringComparer.Ordinal)], roots.ToImmutable(),
            Labels.IsDefault ? [] : [.. Labels.Where(label => kept.Contains(label.RootId))]);
    }

    public static RegretSuite Load(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        return new RegretSuite(ReadLines<RegretGame>(Path.Combine(directory, GamesFile)),
            ReadLines<RegretRoot>(Path.Combine(directory, RootsFile)),
            File.Exists(Path.Combine(directory, LabelsFile)) ? ReadLines<RegretLabel>(Path.Combine(directory, LabelsFile)) : []);
    }

    public void Save(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        Directory.CreateDirectory(directory);
        WriteLines(Path.Combine(directory, GamesFile), Games.OrderBy(game => game.Id, StringComparer.Ordinal));
        WriteLines(Path.Combine(directory, RootsFile), Roots.OrderBy(root => root.GameId, StringComparer.Ordinal).ThenBy(root => root.Sequence));
        if (!Labels.IsDefaultOrEmpty)
            WriteLines(Path.Combine(directory, LabelsFile), Labels.OrderBy(label => label.RootId, StringComparer.Ordinal));
    }

    public static ImmutableArray<T> ReadLines<T>(string path)
    {
        using var stream = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var reader = new StreamReader(stream);
        var items = ImmutableArray.CreateBuilder<T>();
        while (reader.ReadLine() is { } line)
            if (line.Length > 0) items.Add(JsonSerializer.Deserialize<T>(line, Json) ?? throw new InvalidDataException($"An empty line in '{path}'."));
        return items.ToImmutable();
    }

    public static void WriteLines<T>(string path, IEnumerable<T> items)
    {
        using var stream = new GZipStream(File.Create(path), CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(stream) { NewLine = "\n" };
        foreach (T item in items) writer.WriteLine(JsonSerializer.Serialize(item, Json));
    }
}

/// <summary>One decision of a replayed suite game: the state before it, its sequence number, and the chosen action's index.</summary>
public readonly record struct RegretPosition(GameState State, long Sequence, int Choice);

/// <summary>Encodes saved games compactly and replays them.</summary>
public static class RegretGames
{
    /// <summary>The compact form of a saved ladder game, checked by replaying it from its deal.</summary>
    public static RegretGame FromSaved(LadderSavedGame saved, FullMatchDefinition definition, string split, string source)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(definition);
        GameState state = saved.Replay.InitialSnapshot.ToState();
        var choices = ImmutableArray.CreateBuilder<int>(saved.Replay.Actions.Count);
        foreach (ReplayAction recorded in saved.Replay.Actions)
        {
            PendingDecision pending = state.PendingDecision ?? throw new InvalidDataException($"'{saved.Game.Id}' has no decision at {recorded.Sequence}.");
            if (recorded.Sequence != state.ActionSequence + 1) throw new InvalidDataException($"'{saved.Game.Id}' skips a sequence at {recorded.Sequence}.");
            int choice = IndexOf(pending, recorded.Submission.ActionId);
            if (choice < 0) throw new InvalidDataException($"'{saved.Game.Id}' chose an action the engine did not issue at {recorded.Sequence}.");
            choices.Add(choice);
            state = GameEngine.Apply(state, recorded.Submission, definition.Behavior, definition.Runtime).State;
        }
        var game = new RegretGame(saved.Game.Id, saved.Game.ContentId, saved.Game.GameSeed, saved.Game.Corp, saved.Game.Runner, split, source,
            choices.ToImmutable(), DeterministicHash.ForState(state));
        string replayed = Replay(game, definition, _ => { });
        if (replayed != game.FinalStateHash) throw new InvalidDataException($"'{saved.Game.Id}' does not replay from its deal.");
        return game;
    }

    /// <summary>Replays a game from its deal, visiting every decision before it is applied; returns the final state's hash.</summary>
    public static string Replay(RegretGame game, FullMatchDefinition definition, Action<RegretPosition> visit)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(visit);
        GameState state = definition.Start().State;
        foreach (int choice in game.Choices)
        {
            PendingDecision pending = state.PendingDecision ?? throw new InvalidDataException($"'{game.Id}' runs past its game's end.");
            visit(new RegretPosition(state, state.ActionSequence + 1, choice));
            LegalAction action = pending.LegalActions[choice];
            ApplyResult result = GameEngine.Apply(state, new ActionSubmission(pending.Owner, pending.Id, pending.Revision, action.Id),
                definition.Behavior, definition.Runtime);
            state = result.Accepted ? result.State : throw new InvalidDataException($"'{game.Id}' was rejected at {state.ActionSequence + 1}.");
        }
        return DeterministicHash.ForState(state);
    }

    /// <summary>Every decision of a game, before it is applied.</summary>
    public static IEnumerable<RegretPosition> Positions(RegretGame game, FullMatchDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(definition);
        GameState state = definition.Start().State;
        foreach (int choice in game.Choices)
        {
            PendingDecision pending = state.PendingDecision ?? throw new InvalidDataException($"'{game.Id}' runs past its game's end.");
            yield return new RegretPosition(state, state.ActionSequence + 1, choice);
            ApplyResult result = GameEngine.Apply(state, new ActionSubmission(pending.Owner, pending.Id, pending.Revision, pending.LegalActions[choice].Id),
                definition.Behavior, definition.Runtime);
            state = result.Accepted ? result.State : throw new InvalidDataException($"'{game.Id}' was rejected at {state.ActionSequence + 1}.");
        }
    }

    /// <summary>The deal's definition.</summary>
    public static FullMatchDefinition Definition(RegretGame game) => RulesFuzzRunner.CreateDefinition(game.ContentId, game.GameSeed);

    private static int IndexOf(PendingDecision pending, ActionId action)
    {
        for (int index = 0; index < pending.LegalActions.Count; index++)
            if (pending.LegalActions[index].Id == action) return index;
        return -1;
    }
}

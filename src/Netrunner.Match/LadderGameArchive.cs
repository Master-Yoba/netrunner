using System.Text.Json.Nodes;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>A ladder game kept for the oddity lints (Wave 4.5, P1): its deal, both entrants, its result and its replay.</summary>
public sealed record LadderSavedGame(LadderGame Game, double CorpScore, bool Capped, ReplayArtifact Replay);

/// <summary>
/// Saves ladder games as one JSON file each (<c>ai-ladder --save-games</c>) and reads them back
/// with the definition their deal needs, so every game can be replayed exactly.
/// </summary>
public static class LadderGameArchive
{
    public const int SchemaVersion = 1;

    public static void Write(string directory, LadderSavedGame saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        var document = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["id"] = saved.Game.Id,
            ["contentId"] = saved.Game.ContentId,
            ["gameSeed"] = saved.Game.GameSeed,
            ["corp"] = saved.Game.Corp,
            ["runner"] = saved.Game.Runner,
            ["deal"] = saved.Game.Deal,
            ["corpScore"] = saved.CorpScore,
            ["capped"] = saved.Capped,
            ["replay"] = JsonNode.Parse(EngineJson.Serialize(saved.Replay)),
        };
        File.WriteAllText(Path.Combine(directory, FileName(saved.Game.Id)), document.ToJsonString());
    }

    /// <summary>Every saved game in a directory, in file-name order, with the definition its deal replays under.</summary>
    public static IEnumerable<(LadderSavedGame Game, FullMatchDefinition Definition)> Read(string directory) =>
        Paths(directory).Select(Load);

    /// <summary>Every saved game, loaded on up to <paramref name="workers"/> threads, in file order.</summary>
    public static IReadOnlyList<(LadderSavedGame Game, FullMatchDefinition Definition)> ReadAll(string directory, int workers) =>
        Paths(directory).AsParallel().AsOrdered().WithDegreeOfParallelism(Math.Clamp(workers, 1, 64)).Select(Load).ToArray();

    /// <summary>One saved game file, with the definition its deal replays under.</summary>
    public static (LadderSavedGame Game, FullMatchDefinition Definition) ReadFile(string path) => Load(path);

    private static IEnumerable<string> Paths(string directory) =>
        Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal);

    private static (LadderSavedGame Game, FullMatchDefinition Definition) Load(string path)
    {
        JsonNode document = JsonNode.Parse(File.ReadAllText(path))
            ?? throw new InvalidDataException($"{path} is not a saved ladder game.");
        if ((int?)document["schemaVersion"] != SchemaVersion)
            throw new InvalidDataException($"{path} has an unsupported schema version.");
        var game = new LadderGame((string)document["id"]!, (string)document["contentId"]!, (ulong)document["gameSeed"]!,
            (string)document["corp"]!, (string)document["runner"]!, (int?)document["deal"] ?? -1);
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(game.ContentId, game.GameSeed);
        ReplayArtifact replay = EngineJson.Deserialize<ReplayArtifact>(document["replay"]!.ToJsonString(), definition.Behavior, definition.Runtime);
        return (new LadderSavedGame(game, (double)document["corpScore"]!, (bool)document["capped"]!, replay), definition);
    }

    private static string FileName(string id) =>
        string.Concat(id.Select(character => char.IsLetterOrDigit(character) || character is '-' or '.' ? character : '_')) + ".json";
}

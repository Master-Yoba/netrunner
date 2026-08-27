namespace Netrunner.Core;

/// <summary>
/// A deterministic five-event presentation fixture used by the M0 Godot bridge
/// and its smoke tests. It is not a substitute for M1 rules-engine events.
/// </summary>
public static class FoundationEventBatch
{
    public static IReadOnlyList<GameEvent> Create() =>
    [
        new(1, "match-created", Data("Seed 42", "A deterministic match is ready.")),
        new(2, "card-drawn", Data("Corp draws", "A card moves into HQ.")),
        new(3, "credits-changed", Data("Credits +1", "The Corp gains one credit.")),
        new(4, "run-started", Data("Run on R&D", "The attacked server receives focus.")),
        new(5, "run-ended", Data("Run complete", "Presentation returns to the board.")),
    ];

    private static Dictionary<string, string> Data(string title, string detail) =>
        new Dictionary<string, string>
        {
            ["title"] = title,
            ["detail"] = detail,
        };
}

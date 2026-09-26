using System.Text.Json;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>Private developer I/O; never used by the player save browser.</summary>
public static class DeveloperSnapshotStore
{
    public static DeveloperSnapshotDocument Read(string path, PlayerSide rawHumanSide,
        IBehaviorCatalog behavior, IRuntimeCardCatalog runtime)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length is <= 0 or > SoloSaveStore.MaximumFileBytes) throw new IOException("Invalid or oversized snapshot file.");
        var bytes = new byte[checked((int)input.Length)];
        input.ReadExactly(bytes);
        if (input.ReadByte() != -1) throw new IOException("Snapshot changed while being read.");
        using JsonDocument json = JsonDocument.Parse(bytes);
        DeveloperSnapshotDocument document = json.RootElement.TryGetProperty("artifactKind", out _)
            ? EngineJson.Deserialize<DeveloperSnapshotDocument>(bytes)
            : DeveloperSnapshotDocument.Import(EngineJson.Deserialize<GameSnapshot>(bytes, behavior, runtime), rawHumanSide, behavior, runtime);
        document.Validate(behavior, runtime);
        return document;
    }

    public static void Write(string path, DeveloperSnapshotDocument document, IBehaviorCatalog behavior,
        IRuntimeCardCatalog runtime, bool raw = false)
    {
        document.Validate(behavior, runtime);
        byte[] bytes = raw ? EngineJson.SerializeUtf8(document.Snapshot) : EngineJson.SerializeUtf8(document);
        if (bytes.LongLength > SoloSaveStore.MaximumFileBytes) throw new IOException("Snapshot exceeds the supported file size.");
        AtomicFile.Write(path, bytes, temporary => Read(temporary, document.HumanSide, behavior, runtime));
    }
}

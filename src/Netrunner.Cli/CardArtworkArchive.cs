using System.IO.Compression;
using System.Text.Json;
using Netrunner.Cards;

namespace Netrunner.Cli;

/// <summary>Portable, checksummed artwork bundles. No network access or Godot dependency.</summary>
public static class CardArtworkArchive
{
    private static readonly string[] MetadataNames = ["cards.json", "manifest.json", "SOURCE_COPYRIGHT.md"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const long MaxArchiveBytes = 512L * 1024 * 1024;

    public static void Pack(string cacheDirectory, string output, string? metadataDirectory = null)
    {
        CardArtworkManifest manifest = CardArtworkCache.Validate(cacheDirectory);
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [CardArtworkCache.ManifestName] = CardArtworkCache.Serialize(manifest),
        };
        foreach (CardArtworkEntry entry in manifest.Cards)
            files[entry.Path] = CardArtworkCache.ReadImage(cacheDirectory, entry);
        if (metadataDirectory is not null)
        {
            foreach (string name in MetadataNames)
                files[$"metadata/{name}"] = CardArtworkCache.ReadBounded(
                    CardArtworkCache.SafePath(metadataDirectory, name), CardArtworkCache.MaxImageBytes);
            ValidateMetadata(files);
        }
        if (files.Sum(pair => (long)pair.Value.Length) > MaxArchiveBytes)
            throw new InvalidDataException("Artwork bundle exceeds 512 MiB.");

        var inventory = new BundleInventory(1, files.Select(pair =>
            new BundleFile(pair.Key, pair.Value.LongLength, CardArtworkCache.Hash(pair.Value))).ToArray());
        files["bundle.json"] = JsonSerializer.SerializeToUtf8Bytes(inventory, JsonOptions);
        output = Path.GetFullPath(output);
        CardArtworkCache.RejectLinks(output);
        if (File.Exists(output)) throw new IOException("Archive already exists; choose a new output filename.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach ((string name, byte[] bytes) in files)
                {
                    ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                    entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    using Stream target = entry.Open();
                    target.Write(bytes);
                }
            }
            File.Move(temporary, output);
        }
        finally { File.Delete(temporary); }
    }

    public static CardArtworkManifest Unpack(string input, string cacheDirectory,
        string? metadataOutput = null, bool replace = false)
    {
        cacheDirectory = Path.GetFullPath(cacheDirectory);
        CardArtworkCache.RejectLinks(cacheDirectory);
        if (metadataOutput is not null)
        {
            metadataOutput = Path.GetFullPath(metadataOutput);
            CardArtworkCache.RejectLinks(metadataOutput);
            if (Overlaps(cacheDirectory, metadataOutput))
                throw new InvalidDataException("Metadata output and artwork cache must be separate directories.");
        }
        string staging = Path.Combine(Path.GetTempPath(), "netrunner-art-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var files = ReadArchive(input, staging);
            CardArtworkManifest manifest = CardArtworkCache.Validate(staging);
            var expected = manifest.Cards.Select(card => card.Path).ToHashSet(StringComparer.Ordinal);
            expected.Add(CardArtworkCache.ManifestName);
            bool hasMetadata = files.Keys.Any(path => path.StartsWith("metadata/", StringComparison.Ordinal));
            if (hasMetadata)
            {
                foreach (string name in MetadataNames) expected.Add($"metadata/{name}");
                ValidateMetadata(files);
            }
            else if (metadataOutput is not null)
            {
                throw new InvalidDataException("This archive contains no official metadata.");
            }
            if (!expected.SetEquals(files.Keys)) throw new InvalidDataException("Unexpected files in artwork bundle.");

            byte[] manifestBytes = files[CardArtworkCache.ManifestName];
            string manifestPath = CardArtworkCache.SafePath(cacheDirectory, CardArtworkCache.ManifestName);
            CheckConflict(manifestPath, manifestBytes, replace);
            foreach (CardArtworkEntry entry in manifest.Cards)
                CheckConflict(CardArtworkCache.SafePath(cacheDirectory, entry.Path), files[entry.Path], replace);
            if (metadataOutput is not null)
                foreach (string name in MetadataNames)
                    CheckConflict(CardArtworkCache.SafePath(metadataOutput, name), files[$"metadata/{name}"], replace);

            // Content-addressed files can be installed before publishing the manifest.
            foreach (string path in expected.Where(path => path.StartsWith("images/", StringComparison.Ordinal)))
                WriteIfDifferent(CardArtworkCache.SafePath(cacheDirectory, path), files[path]);

            var originals = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
            try
            {
                if (metadataOutput is not null)
                {
                    foreach (string name in MetadataNames)
                    {
                        string path = CardArtworkCache.SafePath(metadataOutput, name);
                        originals[path] = File.Exists(path) ? CardArtworkCache.ReadBounded(path, CardArtworkCache.MaxImageBytes) : null;
                        WriteIfDifferent(path, files[$"metadata/{name}"]);
                    }
                }
                WriteIfDifferent(manifestPath, manifestBytes);
            }
            catch
            {
                foreach ((string path, byte[]? original) in originals)
                {
                    if (original is null) File.Delete(path);
                    else CardArtworkCache.WriteAtomically(path, original);
                }
                throw;
            }
            return manifest;
        }
        finally { Directory.Delete(staging, recursive: true); }
    }

    private static SortedDictionary<string, byte[]> ReadArchive(string input, string staging)
    {
        CardArtworkCache.RejectLinks(input);
        using FileStream stream = File.OpenRead(input);
        if (stream.Length > MaxArchiveBytes) throw new InvalidDataException("Artwork archive exceeds 512 MiB.");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        if (archive.Entries.Count is < 2 or > 100
            || archive.Entries.Any(entry => entry.Length is <= 0 or > CardArtworkCache.MaxImageBytes)
            || archive.Entries.Sum(entry => entry.Length) > MaxArchiveBytes)
            throw new InvalidDataException("Artwork archive exceeds entry or expanded size limits.");
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string path = CardArtworkCache.SafePath(staging, entry.FullName);
            int unixType = (entry.ExternalAttributes >> 16) & 0xf000;
            if (!names.Add(entry.FullName) || unixType is not (0 or 0x8000)
                || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0
                || entry.Length is <= 0 or > CardArtworkCache.MaxImageBytes)
                throw new InvalidDataException("Artwork archive contains duplicate, linked, or oversized entries.");
            if (entry.FullName is "manifest.json" or "bundle.json" && entry.Length > CardArtworkCache.MaxManifestBytes)
                throw new InvalidDataException("Artwork archive manifest exceeds size limit.");
            using Stream source = entry.Open();
            byte[] bytes = new byte[checked((int)entry.Length)];
            source.ReadExactly(bytes);
            if (source.ReadByte() != -1) throw new InvalidDataException("Artwork archive entry exceeds its declared size.");
            files.Add(entry.FullName, bytes);
            CardArtworkCache.WriteAtomically(path, bytes);
        }
        if (!files.Remove("bundle.json", out byte[]? inventoryBytes)) throw new InvalidDataException("Missing bundle inventory.");
        BundleInventory inventory = JsonSerializer.Deserialize<BundleInventory>(inventoryBytes, JsonOptions)
            ?? throw new InvalidDataException("Empty bundle inventory.");
        if (inventory.SchemaVersion != 1 || inventory.Files is null || inventory.Files.Count != files.Count
            || inventory.Files.Any(file => file is null || file.Path is null)
            || inventory.Files.Select(file => file.Path).Distinct(StringComparer.Ordinal).Count() != files.Count)
            throw new InvalidDataException("Invalid bundle inventory version or entries.");
        foreach (BundleFile file in inventory.Files)
        {
            if (!files.TryGetValue(file.Path, out byte[]? bytes) || file.ByteSize != bytes.LongLength
                || file.Sha256 != CardArtworkCache.Hash(bytes))
                throw new InvalidDataException($"Bundle checksum mismatch: {file.Path}");
        }
        return files;
    }

    private static void ValidateMetadata(SortedDictionary<string, byte[]> files)
    {
        if (MetadataNames.Any(name => !files.ContainsKey($"metadata/{name}")))
            throw new InvalidDataException("Incomplete official metadata in artwork bundle.");
        CardDataResource.LoadBytes(files["metadata/cards.json"], files["metadata/manifest.json"],
            files["metadata/SOURCE_COPYRIGHT.md"], requireOfficialGateway: true);
    }

    private static void CheckConflict(string path, byte[] bytes, bool replace)
    {
        if (Directory.Exists(path)) throw new IOException($"A directory occupies the destination: {path}");
        if (File.Exists(path) && !replace
            && !CardArtworkCache.ReadBounded(path, CardArtworkCache.MaxImageBytes).AsSpan().SequenceEqual(bytes))
            throw new IOException($"Different content already exists at {path}; use --replace to replace it.");
    }

    private static void WriteIfDifferent(string path, byte[] bytes)
    {
        if (!File.Exists(path) || !CardArtworkCache.ReadBounded(path, CardArtworkCache.MaxImageBytes).AsSpan().SequenceEqual(bytes))
            CardArtworkCache.WriteAtomically(path, bytes);
    }

    private static bool Overlaps(string first, string second)
    {
        string a = Path.TrimEndingDirectorySeparator(first) + Path.DirectorySeparatorChar;
        string b = Path.TrimEndingDirectorySeparator(second) + Path.DirectorySeparatorChar;
        return a.StartsWith(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record BundleInventory(int SchemaVersion, IReadOnlyList<BundleFile> Files);
    private sealed record BundleFile(string Path, long ByteSize, string Sha256);
}

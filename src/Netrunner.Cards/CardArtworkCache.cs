using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Netrunner.Cards;

public sealed record CardArtworkEntry(
    string DefinitionId,
    string PrintingId,
    string Path,
    string SourceUrl,
    string Artist,
    DateTimeOffset RetrievedAt,
    long ByteSize,
    string Sha256);

public sealed record CardArtworkManifest(
    int SchemaVersion,
    string Set,
    string Language,
    IReadOnlyList<CardArtworkEntry> Cards);

/// <summary>Local-only artwork contracts shared by tooling and presentation.</summary>
public static class CardArtworkCache
{
    public const string DirectoryVariable = "NETRUNNER_CARD_ART_DIRECTORY";
    public const string ManifestName = "manifest.json";
    public const int MaxImageBytes = 10 * 1024 * 1024;
    public const int MaxManifestBytes = 1024 * 1024;
    public static IReadOnlyList<string> GatewayCodes { get; } = Array.AsReadOnly(
        Enumerable.Range(30001, 77).Select(code => code.ToString(CultureInfo.InvariantCulture)).ToArray());

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static string ResolveDirectory(string? explicitDirectory = null) => Path.GetFullPath(
        !string.IsNullOrWhiteSpace(explicitDirectory) ? explicitDirectory
        : !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DirectoryVariable))
            ? Environment.GetEnvironmentVariable(DirectoryVariable)!
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NetrunnerSolo", "card-art"));

    public static byte[] Serialize(CardArtworkManifest manifest)
    {
        ValidateManifest(manifest);
        return JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
    }

    public static CardArtworkManifest ReadManifest(string directory)
    {
        string path = SafePath(directory, ManifestName);
        byte[] bytes = ReadBounded(path, MaxManifestBytes);
        CardArtworkManifest manifest = JsonSerializer.Deserialize<CardArtworkManifest>(bytes, JsonOptions)
            ?? throw new InvalidDataException("Artwork manifest is empty.");
        ValidateManifest(manifest);
        return manifest;
    }

    public static void ValidateManifest(CardArtworkManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SchemaVersion != 1 || manifest.Set != "system_gateway" || manifest.Language != "en"
            || manifest.Cards is null || manifest.Cards.Count != GatewayCodes.Count)
        {
            throw new InvalidDataException("Expected artwork schema 1 with all 77 English System Gateway cards.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (CardArtworkEntry entry in manifest.Cards)
        {
            if (entry is null || !GatewayCodes.Contains(entry.DefinitionId, StringComparer.Ordinal)
                || !seen.Add(entry.DefinitionId) || entry.PrintingId != entry.DefinitionId
                || entry.ByteSize is <= 0 or > MaxImageBytes || entry.RetrievedAt == default
                || entry.Artist is null || !IsSha256(entry.Sha256)
                || !Uri.TryCreate(entry.SourceUrl, UriKind.Absolute, out Uri? source)
                || source.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidDataException("Artwork manifest contains an invalid or duplicate card entry.");
            }

            string extension = Path.GetExtension(entry.Path);
            if (extension is not (".png" or ".jpg" or ".webp")
                || entry.Path != $"images/{entry.Sha256}{extension}")
            {
                throw new InvalidDataException("Artwork paths must be content-addressed images inside the cache.");
            }
        }
    }

    public static CardArtworkManifest Validate(string directory)
    {
        CardArtworkManifest manifest = ReadManifest(directory);
        foreach (CardArtworkEntry entry in manifest.Cards) ReadImage(directory, entry);
        return manifest;
    }

    public static byte[] ReadImage(string directory, CardArtworkEntry entry)
    {
        byte[] bytes = ReadBounded(SafePath(directory, entry.Path), MaxImageBytes);
        if (bytes.LongLength != entry.ByteSize || Hash(bytes) != entry.Sha256
            || ImageExtension(bytes) != Path.GetExtension(entry.Path))
        {
            throw new InvalidDataException($"Artwork checksum, size, or image format mismatch for {entry.DefinitionId}.");
        }
        return bytes;
    }

    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string ImageExtension(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 24 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ".png";
        if (bytes.Length >= 12 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255
            && bytes[^2] == 255 && bytes[^1] == 217) return ".jpg";
        if (bytes.Length >= 20 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)) return ".webp";
        throw new InvalidDataException("Downloaded artwork is not a supported PNG, JPEG, or WebP image.");
    }

    public static byte[] ReadBounded(string path, int maxBytes)
    {
        RejectLinks(path);
        using FileStream input = File.OpenRead(path);
        if (input.Length > maxBytes) throw new InvalidDataException($"File exceeds the size limit: {path}");
        byte[] bytes = new byte[checked((int)input.Length)];
        input.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>Reject both unsafe relative names and links in existing destination ancestors.</summary>
    public static string SafePath(string directory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Contains('\\') || relativePath.Contains(':')
            || relativePath.Split('/').Any(part => part is "" or "." or "..") || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("Unsafe artwork cache path.");
        }
        string result = Path.GetFullPath(Path.Combine(directory, relativePath));
        RejectLinks(result);
        return result;
    }

    public static void RejectLinks(string path)
    {
        // macOS exposes its OS-owned temporary roots through /var and /tmp aliases.
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if (OperatingSystem.IsMacOS() && current is "/tmp" or "/var") break;
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Artwork paths cannot contain symbolic links: {current}");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    public static void WriteAtomically(string path, ReadOnlySpan<byte> bytes)
    {
        RejectLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public static bool IsSha256(string? value) => value is { Length: 64 }
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

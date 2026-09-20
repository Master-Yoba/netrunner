using Netrunner.Cards;

namespace Netrunner.Cli.Tests;

internal sealed class CardArtworkTestCache : IDisposable
{
    internal static byte[] ImageBytes => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a9WQAAAAASUVORK5CYII=");
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "netrunner-art-test-" + Guid.NewGuid().ToString("N"));
    internal string Cache => Path.Combine(Root, "cache");

    internal CardArtworkTestCache() => Directory.CreateDirectory(Root);

    internal CardArtworkManifest Create(string? directory = null, int revision = 0)
    {
        directory ??= Cache;
        byte[] image = ImageBytes;
        string hash = CardArtworkCache.Hash(image);
        string path = $"images/{hash}.png";
        CardArtworkCache.WriteAtomically(CardArtworkCache.SafePath(directory, path), image);
        var entries = CardArtworkCache.GatewayCodes.Select(code => new CardArtworkEntry(
            code, code, path, $"https://card-images.netrunnerdb.com/{code}.png", "Synthetic test image",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(revision), image.Length, hash)).ToArray();
        var manifest = new CardArtworkManifest(1, "system_gateway", "en", entries);
        CardArtworkCache.WriteAtomically(CardArtworkCache.SafePath(directory, CardArtworkCache.ManifestName), CardArtworkCache.Serialize(manifest));
        return manifest;
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}

using Xunit;

namespace Netrunner.Cards.Tests;

public sealed class CardArtworkCacheTests
{
    [Fact]
    public void ManifestRequiresExactSupportedCodesAndVersion()
    {
        CardArtworkManifest manifest = CreateManifest();
        CardArtworkCache.ValidateManifest(manifest);
        Assert.Throws<InvalidDataException>(() => CardArtworkCache.ValidateManifest(manifest with { SchemaVersion = 2 }));
        Assert.Throws<InvalidDataException>(() => CardArtworkCache.ValidateManifest(manifest with { Language = "fr" }));
        Assert.Throws<InvalidDataException>(() => CardArtworkCache.ValidateManifest(manifest with { Cards = manifest.Cards.Skip(1).ToArray() }));
        Assert.Throws<InvalidDataException>(() => CardArtworkCache.ValidateManifest(manifest with
        {
            Cards = manifest.Cards.Skip(1).Append(manifest.Cards[1]).ToArray(),
        }));
    }

    [Theory]
    [InlineData("../file.png")]
    [InlineData("/file.png")]
    [InlineData("images/../file.png")]
    [InlineData("images\\file.png")]
    [InlineData("images//file.png")]
    [InlineData("C:/file.png")]
    public void RelativePathsCannotEscapeCache(string path)
    {
        Assert.Throws<InvalidDataException>(() => CardArtworkCache.SafePath(Path.GetTempPath(), path));
    }

    [Fact]
    public void ManifestPathsAreTiedToTheirChecksumAndSupportedFormat()
    {
        CardArtworkManifest manifest = CreateManifest();
        CardArtworkEntry entry = manifest.Cards[0];
        Assert.Throws<InvalidDataException>(() => CardArtworkCache.ValidateManifest(manifest with
        {
            Cards = manifest.Cards.Skip(1).Append(entry with { Path = "images/other.png" }).ToArray(),
        }));
        Assert.Throws<InvalidDataException>(() => CardArtworkCache.ValidateManifest(manifest with
        {
            Cards = manifest.Cards.Skip(1).Append(entry with { ByteSize = CardArtworkCache.MaxImageBytes + 1L }).ToArray(),
        }));
    }

    [Fact]
    public void HtmlAndTruncatedPayloadsAreNotImages()
    {
        Assert.Throws<InvalidDataException>(() => CardArtworkCache.ImageExtension("<html>denied</html>"u8));
        Assert.Throws<InvalidDataException>(() => CardArtworkCache.ImageExtension([255, 216, 255]));
        Assert.Throws<InvalidDataException>(() => CardArtworkCache.ImageExtension("RIFF"u8));
    }

    private static CardArtworkManifest CreateManifest() => new(1, "system_gateway", "en",
        CardArtworkCache.GatewayCodes.Select(code => new CardArtworkEntry(code, code,
            $"images/{new string('a', 64)}.png", $"https://card-images.netrunnerdb.com/{code}.png", "Test illustrator",
            DateTimeOffset.UnixEpoch, 50, new string('a', 64))).ToArray());
}

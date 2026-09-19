using System.IO.Compression;
using System.Text.Json;
using Netrunner.Cards;
using Xunit;

namespace Netrunner.Cli.Tests;

public sealed class CardArtworkArchiveTests
{
    [Fact]
    public void ArchiveRoundTripWorksWithoutSourcesAndPreservesBytes()
    {
        using var fixture = new CardArtworkTestCache();
        CardArtworkManifest original = fixture.Create();
        string archive = Path.Combine(fixture.Root, "art.zip");
        CardArtworkArchive.Pack(fixture.Cache, archive);
        Directory.Delete(fixture.Cache, recursive: true);
        string restored = Path.Combine(fixture.Root, "offline cache");
        CardArtworkArchive.Unpack(archive, restored);
        CardArtworkManifest actual = CardArtworkCache.Validate(restored);
        Assert.Equal(CardArtworkCache.Serialize(original), CardArtworkCache.Serialize(actual));
        Assert.Equal(CardArtworkTestCache.ImageBytes, CardArtworkCache.ReadImage(restored, actual.Cards[0]));
        // Reimporting identical content does not require replacement approval.
        CardArtworkArchive.Unpack(archive, restored);
    }

    [Fact]
    public void ChangedCacheRequiresReplaceAndFailedImportPreservesExistingManifest()
    {
        using var fixture = new CardArtworkTestCache();
        CardArtworkManifest original = fixture.Create();
        string other = Path.Combine(fixture.Root, "other");
        CardArtworkManifest changed = fixture.Create(other, revision: 1);
        string archive = Path.Combine(fixture.Root, "art.zip");
        CardArtworkArchive.Pack(other, archive);
        Assert.Throws<IOException>(() => CardArtworkArchive.Unpack(archive, fixture.Cache));
        Assert.Equal(CardArtworkCache.Serialize(original), File.ReadAllBytes(Path.Combine(fixture.Cache, "manifest.json")));
        CardArtworkArchive.Unpack(archive, fixture.Cache, replace: true);
        Assert.Equal(CardArtworkCache.Serialize(changed), File.ReadAllBytes(Path.Combine(fixture.Cache, "manifest.json")));
    }

    [Fact]
    public void PackRejectsCorruptImageAndNeverPublishesArchive()
    {
        using var fixture = new CardArtworkTestCache();
        CardArtworkManifest manifest = fixture.Create();
        File.WriteAllText(Path.Combine(fixture.Cache, manifest.Cards[0].Path), "not an image");
        string archive = Path.Combine(fixture.Root, "art.zip");
        Assert.Throws<InvalidDataException>(() => CardArtworkArchive.Pack(fixture.Cache, archive));
        Assert.False(File.Exists(archive));
    }

    [Fact]
    public void ArchiveChecksumDetectsTamperingBeforeDestinationChanges()
    {
        using var fixture = new CardArtworkTestCache();
        CardArtworkManifest manifest = fixture.Create();
        string archive = Path.Combine(fixture.Root, "art.zip");
        CardArtworkArchive.Pack(fixture.Cache, archive);
        using (ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Update))
        {
            zip.GetEntry(manifest.Cards[0].Path)!.Delete();
            using var writer = new StreamWriter(zip.CreateEntry(manifest.Cards[0].Path).Open());
            writer.Write("corrupt bytes");
        }
        Assert.Throws<InvalidDataException>(() => CardArtworkArchive.Unpack(archive, fixture.Cache, replace: true));
        CardArtworkCache.Validate(fixture.Cache);
    }

    [Theory]
    [InlineData("../escape.png", 0)]
    [InlineData("/absolute.png", 0)]
    [InlineData("C:\\escape.png", 0)]
    [InlineData("images/link.png", 0xa000)]
    [InlineData("images/pipe.png", 0x1000)]
    public void UnsafeArchiveEntriesAreRejected(string name, int unixType)
    {
        using var fixture = new CardArtworkTestCache();
        string archive = Path.Combine(fixture.Root, "unsafe.zip");
        using (ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = zip.CreateEntry(name);
            entry.ExternalAttributes = unixType << 16;
            using (var writer = new StreamWriter(entry.Open())) writer.Write("unsafe");
            using (var writer = new StreamWriter(zip.CreateEntry("bundle.json").Open())) writer.Write("{}");
        }
        Assert.Throws<InvalidDataException>(() => CardArtworkArchive.Unpack(archive, fixture.Cache));
        Assert.False(Directory.Exists(fixture.Cache));
        Assert.False(File.Exists(Path.Combine(fixture.Root, "escape.png")));
    }

    [Fact]
    public void CaseCollidingArchiveEntriesAreRejected()
    {
        using var fixture = new CardArtworkTestCache();
        string archive = Path.Combine(fixture.Root, "unsafe.zip");
        using (ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            foreach (string name in new[] { "manifest.json", "MANIFEST.JSON" })
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write("{}");
            }
        }
        Assert.Throws<InvalidDataException>(() => CardArtworkArchive.Unpack(archive, fixture.Cache));
    }

    [Fact]
    public void SymlinkDestinationIsRejectedWithoutChangingItsTarget()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new CardArtworkTestCache();
        fixture.Create();
        string archive = Path.Combine(fixture.Root, "art.zip");
        CardArtworkArchive.Pack(fixture.Cache, archive);
        string outside = Path.Combine(fixture.Root, "outside");
        Directory.CreateDirectory(outside);
        string destination = Path.Combine(fixture.Root, "destination");
        Directory.CreateDirectory(destination);
        Directory.CreateSymbolicLink(Path.Combine(destination, "images"), outside);
        Assert.Throws<InvalidDataException>(() => CardArtworkArchive.Unpack(archive, destination));
        Assert.Empty(Directory.GetFiles(outside));
    }

    [Fact]
    public void OptionalMetadataMustBePresentAndOfficial()
    {
        using var fixture = new CardArtworkTestCache();
        fixture.Create();
        string metadata = Path.Combine(fixture.Root, "metadata");
        Directory.CreateDirectory(metadata);
        File.WriteAllBytes(Path.Combine(metadata, "cards.json"), CardDataResource.CopyEmbeddedCardsBytes());
        File.WriteAllText(Path.Combine(metadata, "manifest.json"), "{}");
        File.WriteAllText(Path.Combine(metadata, "SOURCE_COPYRIGHT.md"), "Synthetic test notice.");
        Assert.Throws<CardDataValidationException>(() => CardArtworkArchive.Pack(fixture.Cache,
            Path.Combine(fixture.Root, "bad.zip"), metadata));
        string archive = Path.Combine(fixture.Root, "art.zip");
        CardArtworkArchive.Pack(fixture.Cache, archive);
        Assert.Throws<InvalidDataException>(() => CardArtworkArchive.Unpack(archive, fixture.Cache, metadata));
    }

    [Theory]
    [InlineData("fetch", "--output", "file.zip")]
    [InlineData("pack", "--json", "--json")]
    [InlineData("unpack", "--input", "--replace")]
    public void CliRejectsInvalidOptions(string verb, string option, string value)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(2, CliCommands.Run(["card-art", verb, option, value], output, error));
    }

    [Fact]
    public void ValidateCommandReportsUsableCacheAsJsonAndCorruptionAsFailure()
    {
        using var fixture = new CardArtworkTestCache();
        CardArtworkManifest manifest = fixture.Create();
        using var output = new StringWriter();
        using var error = new StringWriter();
        string[] args = ["card-art", "validate", "--cache-dir", fixture.Cache, "--json"];
        Assert.Equal(0, CliCommands.Run(args, output, error));
        using JsonDocument report = JsonDocument.Parse(output.ToString());
        Assert.Equal(77, report.RootElement.GetProperty("cards").GetInt32());
        File.WriteAllText(Path.Combine(fixture.Cache, manifest.Cards[0].Path), "bad image");
        Assert.Equal(1, CliCommands.Run(args, output, error));
        File.Delete(Path.Combine(fixture.Cache, manifest.Cards[0].Path));
        Assert.Equal(1, CliCommands.Run(args, output, error));
    }

    [Fact]
    public void UnsupportedBundleVersionAndOversizedEntryLeaveDestinationUntouched()
    {
        using var fixture = new CardArtworkTestCache();
        fixture.Create();
        string archive = Path.Combine(fixture.Root, "art.zip");
        CardArtworkArchive.Pack(fixture.Cache, archive);
        using (ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Update))
        {
            ZipArchiveEntry inventory = zip.GetEntry("bundle.json")!;
            string json;
            using (var reader = new StreamReader(inventory.Open())) json = reader.ReadToEnd();
            inventory.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("bundle.json").Open());
            writer.Write(json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal));
        }
        string destination = Path.Combine(fixture.Root, "destination");
        Assert.Throws<InvalidDataException>(() => CardArtworkArchive.Unpack(archive, destination));
        Assert.False(Directory.Exists(destination));
        using (ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Update))
        {
            using Stream entry = zip.CreateEntry("oversized.png").Open();
            entry.Write(new byte[CardArtworkCache.MaxImageBytes + 1]);
        }
        Assert.Throws<InvalidDataException>(() => CardArtworkArchive.Unpack(archive, destination));
        Assert.False(Directory.Exists(destination));
    }
}

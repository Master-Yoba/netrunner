using System.Text;

using Xunit;

namespace Netrunner.Cards.Tests;

/// <summary>
/// EL.1 DATA-400: the Elevation pack is a second authenticated snapshot with
/// its own manifest. These tests use small in-memory packs, so they run in
/// the synthetic lane without the local-only official data.
/// </summary>
public sealed class ElevationCardDataTests
{
    private const string Commit = "236140cf84491c23995b028bc791281837520b97";

    [Fact]
    public void PacksCombineIntoOnePoolThatKeepsEachManifest()
    {
        CardDataSet gateway = Pack("sg-test", ("30001", "Alpha"), ("30002", "Beta"));
        CardDataSet elevation = Pack("elev-test", ("35001", "Gamma"));

        CardDataSet pool = CardDataSet.Combine([gateway, elevation]);

        Assert.Equal(3, pool.Cards.Count);
        Assert.Equal(["sg-test", "elev-test"], pool.PackCodes);
        Assert.Same(gateway.Manifest, pool.Manifest);
        Assert.Equal("elev-test", pool.PackCodeOf("35001"));
        Assert.Equal("sg-test", pool.PackCodeOf("30002"));
        Assert.Null(pool.PackCodeOf("99999"));
        Assert.Equal(2, pool.Packs.Count);
        Assert.Same(gateway, pool.Packs[0]);
    }

    [Fact]
    public void APoolRejectsARepeatedPackCardCodeOrCommit()
    {
        CardDataSet gateway = Pack("sg-test", ("30001", "Alpha"));

        Assert.Contains(
            "appears twice",
            Assert.Throws<CardDataValidationException>(() => CardDataSet.Combine([gateway, gateway])).Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "more than one pack",
            Assert.Throws<CardDataValidationException>(() =>
                CardDataSet.Combine([gateway, Pack("elev-test", ("30001", "Alpha"))])).Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "comes from commit",
            Assert.Throws<CardDataValidationException>(() =>
                CardDataSet.Combine([gateway, Pack("elev-test", [("35001", "Gamma")], new string('b', 40))])).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MetadataCatalogValidatesAPoolPackByPack()
    {
        // Titles are ordered within each pack but not across them.
        CardDataSet pool = CardDataSet.Combine([
            Pack("sg-test", ("30001", "Zeta")),
            Pack("elev-test", ("35001", "Alpha")),
        ]);

        var catalog = new CardMetadataCatalog(pool.Cards);

        Assert.Equal(2, catalog.Count);
        Assert.Equal("Alpha", catalog["35001"].Title);
    }

    [Fact]
    public void CorruptElevationBytesFailTheirOwnChecksum()
    {
        (byte[] cards, byte[] manifest) = Serialize(Pack("elev-test", ("35001", "Gamma")));
        cards[^1] = cards[^1] == (byte)' ' ? (byte)'\n' : (byte)' ';

        CardDataValidationException exception = Assert.Throws<CardDataValidationException>(() =>
            CardDataResource.LoadBytes(cards, manifest, Encoding.UTF8.GetBytes("notice")));
        Assert.Contains("checksum mismatch", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnElevationManifestIsCheckedAgainstThePinnedElevationPack()
    {
        CardDataManifest manifest = Manifest("elev", 81, "pack/elev.json", "Elevation");

        CardDataValidationException exception = Assert.Throws<CardDataValidationException>(() =>
            CardDataValidator.ValidateManifest(manifest, requireOfficialGateway: true));
        Assert.Contains("pinned Elevation", exception.Message, StringComparison.Ordinal);

        CardDataValidator.ValidateManifest(
            Manifest("elev", 82, "pack/elev.json", "Elevation"),
            requireOfficialGateway: true);
    }

    [Fact]
    public void AMissingElevationDirectoryIsAnActionableError()
    {
        string root = Path.Combine(Path.GetTempPath(), "netrunner-el1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            CardDataValidationException exception = Assert.Throws<CardDataValidationException>(() =>
                CardDataLoader.LoadOfficialPack(OfficialCardPacks.Elevation, root));
            Assert.Contains("elevation", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void OfficialPackTableNamesEveryPinnedPack()
    {
        Assert.Equal(["sg", "elev", "vp"], OfficialCardPacks.All.Select(static pack => pack.PackCode));
        Assert.Equal(77, OfficialCardPacks.SystemGateway.CardCount);
        Assert.Equal(82, OfficialCardPacks.Elevation.CardCount);
        Assert.Equal("data/vendor/elevation", OfficialCardPacks.Elevation.RelativeDirectory);
        Assert.Null(OfficialCardPacks.Find("liberation"));
    }

    [Fact]
    public void EmbeddedPackListMatchesTheBuildMode()
    {
        IReadOnlyList<string> packs = CardDataResource.EmbeddedPackCodes;
        if (CardDataResource.EmbeddedMode == CardDataResource.OfficialMode)
        {
            Assert.Equal(["sg", "elev", "vp"], packs);
            CardDataSet pool = CardDataResource.LoadEmbeddedPool();
            Assert.Equal(225, pool.Cards.Count);
            Assert.Equal(82, CardDataResource.LoadEmbeddedPack("elev").Cards.Count);
        }
        else
        {
            Assert.Equal([CardDataResource.LoadEmbedded().Manifest.PackCode], packs);
            Assert.Throws<CardDataValidationException>(() => CardDataResource.LoadEmbeddedPack("elev"));
        }
    }

    [Fact]
    public void TheElevationPackIsAuthenticatedWhenProvisionedLocally()
    {
        string? root = FindRepositoryRoot();
        if (root is null || !File.Exists(Path.Combine(root, "data/vendor/elevation/cards.json")))
        {
            return;
        }

        CardDataSet elevation = CardDataLoader.LoadOfficialPack(OfficialCardPacks.Elevation, root);
        Assert.Equal(82, elevation.Cards.Count);
        Assert.Equal("elev", elevation.Manifest.PackCode);
        Assert.All(elevation.Cards, card => Assert.StartsWith("35", card.Code, StringComparison.Ordinal));
        if (!File.Exists(Path.Combine(root, "data/vendor/vantage-point/cards.json")))
        {
            return;
        }

        CardDataSet pool = CardDataLoader.LoadOfficialPool(root);
        Assert.Equal(225, pool.Cards.Count);
        Assert.Equal(["sg", "elev", "vp"], pool.PackCodes);
    }

    private static string? FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Netrunner.sln")))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static CardDataSet Pack(string packCode, params (string Code, string Title)[] cards) =>
        Pack(packCode, cards, Commit);

    private static CardDataSet Pack(string packCode, (string Code, string Title)[] cards, string commit)
    {
        NormalizedCardDefinition[] definitions = cards
            .Select(card => new NormalizedCardDefinition(
                card.Code,
                card.Title,
                "runner",
                "anarch",
                "event",
                [],
                "Test text.",
                packCode,
                3,
                3,
                false,
                0,
                null,
                null,
                null,
                null,
                null,
                1,
                null,
                null,
                null))
            .OrderBy(static card => card.Title, StringComparer.Ordinal)
            .ThenBy(static card => card.Code, StringComparer.Ordinal)
            .ToArray();
        byte[] bytes = CardDataJson.SerializeCards(definitions);
        CardDataManifest manifest = Manifest(packCode, definitions.Length, $"test/{packCode}", packCode) with
        {
            CommitSha = commit,
            OutputSha256 = CardDataJson.ComputeSha256Hex(bytes),
        };
        return CardDataResource.LoadBytes(bytes, CardDataJson.SerializeManifest(manifest), Encoding.UTF8.GetBytes("notice"));
    }

    private static (byte[] Cards, byte[] Manifest) Serialize(CardDataSet pack) =>
        (CardDataJson.SerializeCards(pack.Cards), CardDataJson.SerializeManifest(pack.Manifest));

    private static CardDataManifest Manifest(string packCode, int count, string sourcePath, string name) => new(
        CardDataValidator.CurrentManifestSchemaVersion,
        CardDataValidator.OfficialSourceUrl,
        Commit,
        "2026-08-28",
        "v1",
        CardDataValidator.CurrentNormalizedSchemaVersion,
        new string('0', 64),
        "COPYRIGHT.md",
        "NOASSERTION",
        sourcePath,
        packCode,
        name,
        count);
}

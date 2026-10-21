using Netrunner.Core;
using Xunit;

namespace Netrunner.Cards.Tests;

/// <summary>
/// VP.1 DATA-400: the Vantage Point pack is a third authenticated snapshot with its own manifest, embedded beside System
/// Gateway and Elevation, and the Startup pool composes the unchanged Gateway + Elevation catalog. Most of these run in
/// the synthetic lane; the local-only official data is used only where it is provisioned.
/// </summary>
public sealed class VantagePointCardDataTests
{
    private const string Commit = "236140cf84491c23995b028bc791281837520b97";

    [Fact]
    public void TheVantagePointPackIsPinned()
    {
        OfficialCardPack pack = OfficialCardPacks.Get("vp");
        Assert.Same(OfficialCardPacks.VantagePoint, pack);
        Assert.Equal(66, pack.CardCount);
        Assert.Equal("pack/vp.json", pack.SourcePackPath);
        Assert.Equal("data/vendor/vantage-point", pack.RelativeDirectory);
        Assert.Equal(["36009", "36036", "36066"], pack.SentinelTitles.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AVantagePointManifestIsCheckedAgainstThePinnedPack()
    {
        CardDataValidationException exception = Assert.Throws<CardDataValidationException>(() =>
            CardDataValidator.ValidateManifest(Manifest(65), requireOfficialGateway: true));
        Assert.Contains("pinned Vantage Point", exception.Message, StringComparison.Ordinal);

        CardDataValidator.ValidateManifest(Manifest(66), requireOfficialGateway: true);
    }

    [Fact]
    public void AMissingVantagePointDirectoryIsAnActionableError()
    {
        string root = Path.Combine(Path.GetTempPath(), "netrunner-vp1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            CardDataValidationException exception = Assert.Throws<CardDataValidationException>(() =>
                CardDataLoader.LoadOfficialPack(OfficialCardPacks.VantagePoint, root));
            Assert.Contains("vantage-point", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TheStartupPoolComposesTheUnchangedElevationCatalog()
    {
        CardPool startup = CardPools.Get(CardPoolIds.Startup);
        Assert.Equal(["sg", "elev", "vp"], startup.PackCodes);
        Assert.True(CardPools.TryForBehaviorVersion(VantagePointBehaviorCatalog.Version, out CardPool byVersion));
        Assert.Same(startup, byVersion);

        BehaviorCatalog elevation = ElevationBehaviorCatalog.Create();
        BehaviorCatalog catalog = startup.CreateBehavior();
        Assert.Equal(VantagePointBehaviorCatalog.Version, catalog.Version);
        // The pool plays Elevation's cards and the registered Vantage Point batches (all 66), and Elevation's catalog is
        // unchanged: each of its cards keeps its behavior fingerprint, but Mycoweb, whose options are rebuilt over the Startup
        // pool's ICE (VP.8).
        string[] registered = catalog.AllDefinitionIds.Select(static id => id.Value)
            .Where(static code => code.StartsWith("36", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(66, registered.Length);
        Assert.Equal(elevation.AllDefinitionIds.ToHashSet(),
            catalog.AllDefinitionIds.Where(static id => !id.Value.StartsWith("36", StringComparison.Ordinal)).ToHashSet());
        Assert.All(elevation.AllDefinitionIds.Where(static id => id != ElevationCardCodes.Mycoweb),
            id => Assert.Equal(elevation.GetCardBehaviorFingerprint(id), catalog.GetCardBehaviorFingerprint(id)));
        Assert.NotEqual(elevation.GetCardBehaviorFingerprint(ElevationCardCodes.Mycoweb), catalog.GetCardBehaviorFingerprint(ElevationCardCodes.Mycoweb));
        Assert.Equal(ElevationBehaviorCatalog.Create().Fingerprint, elevation.Fingerprint);
    }

    // VP.8: on the Startup pool Mycoweb resolves Vantage Point's code gates and sentries too, but no subroutine that acts on
    // its own ICE (Knowledge Seeker's virus counter would land on Mycoweb). The Elevation pool's Mycoweb is unchanged.
    [Fact]
    public void MycowebOnTheStartupPoolResolvesVantagePointsIceButNotTheirSelfReferences()
    {
        static IReadOnlyList<string> CodeGateOptions(BehaviorCatalog catalog)
        {
            Assert.True(catalog.TryGet(ElevationCardCodes.Mycoweb, out CardBehaviorDefinition? mycoweb));
            return mycoweb!.Triggers.Single(static trigger => trigger.Key == "mycoweb-code-gate").Instructions[0].Choice!.Options
                .Select(static option => option.Key).ToArray();
        }

        IReadOnlyList<string> startup = CodeGateOptions(VantagePointBehaviorCatalog.Create());
        Assert.Contains("resolve-36040-1", startup);
        Assert.DoesNotContain("resolve-36040-0", startup);
        Assert.DoesNotContain(startup, static key => key.StartsWith("resolve-35053-", StringComparison.Ordinal));
        Assert.DoesNotContain(CodeGateOptions(ElevationBehaviorCatalog.Create()), static key => key.StartsWith("resolve-36", StringComparison.Ordinal));
    }

    [Fact]
    public void ElevationGamesDrawOnlyOnTheirOwnPacks()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
        {
            return;
        }

        Assert.Equal(66, CardDataResource.LoadEmbeddedPack("vp").Cards.Count);
        Assert.All(CardDataResource.LoadEmbeddedPack("vp").Cards, static card => Assert.StartsWith("36", card.Code, StringComparison.Ordinal));
        // The Gateway + Elevation pool loads its own two packs, so Elevation games and their pins don't see Vantage Point.
        CardDataSet elevation = CardPools.GatewayElevation.LoadEmbeddedData();
        Assert.Equal(["sg", "elev"], elevation.PackCodes);
        Assert.Equal(159, elevation.Cards.Count);
        Assert.Equal(225, CardPools.Startup.LoadEmbeddedData().Cards.Count);
    }

    [Fact]
    public void TheVantagePointPackIsAuthenticatedWhenProvisionedLocally()
    {
        string? root = FindRepositoryRoot();
        if (root is null || !File.Exists(Path.Combine(root, "data/vendor/vantage-point/cards.json")))
        {
            return;
        }

        CardDataSet vantagePoint = CardDataLoader.LoadOfficialPack(OfficialCardPacks.VantagePoint, root);
        Assert.Equal(66, vantagePoint.Cards.Count);
        Assert.Equal("vp", vantagePoint.Manifest.PackCode);
        Assert.Equal(Commit, vantagePoint.Manifest.CommitSha);
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

    private static CardDataManifest Manifest(int count) => new(
        CardDataValidator.CurrentManifestSchemaVersion,
        CardDataValidator.OfficialSourceUrl,
        Commit,
        "2026-08-28",
        "v1",
        CardDataValidator.CurrentNormalizedSchemaVersion,
        new string('0', 64),
        "COPYRIGHT.md",
        "NOASSERTION",
        "pack/vp.json",
        "vp",
        "Vantage Point",
        count);
}

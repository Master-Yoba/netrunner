using System.Text;
using Xunit;

namespace Netrunner.Cards.Tests;

public sealed class CardDataResourceTests
{
    [Fact]
    public void GeneratedPayloadLoadsWithoutTheSourceDirectory()
    {
        Assert.True(CardDataResource.TryLoadEmbedded(out CardDataSet? data, out string diagnostic), diagnostic);
        Assert.NotNull(data);
        Assert.Equal(CardDataResource.EmbeddedMode, data!.Manifest.PackCode == "sg"
            ? CardDataResource.OfficialMode
            : CardDataResource.SyntheticMode);
        Assert.Equal(data.Cards.Count, data.CardDefinitions.Count);
        Assert.Contains("embedded", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void CorruptGeneratedBytesFailChecksumBeforeCardsAreExposed()
    {
        byte[] cards = CardDataResource.CopyEmbeddedCardsBytes();
        byte[] manifest = CardDataResource.CopyEmbeddedManifestBytes();
        byte[] copyright = Encoding.UTF8.GetBytes("synthetic test notice");
        cards[^1] = cards[^1] == (byte)' ' ? (byte)'\n' : (byte)' ';

        Assert.Throws<CardDataValidationException>(() =>
            CardDataResource.LoadBytes(cards, manifest, copyright));
    }

    [Fact]
    public void StaleOfficialManifestProducesActionableValidation()
    {
        byte[] cards = CardDataResource.CopyEmbeddedCardsBytes();
        byte[] currentManifest = CardDataResource.CopyEmbeddedManifestBytes();
        string manifestText = Encoding.UTF8.GetString(currentManifest);
        CardDataManifest parsed = CardDataJson.ParseManifest(currentManifest);
        string staleText = manifestText.Replace(
            parsed.SourceUrl,
            "https://stale.invalid/netrunner-cards-json",
            StringComparison.Ordinal);

        Assert.NotEqual(manifestText, staleText);
        CardDataValidationException exception = Assert.Throws<CardDataValidationException>(() =>
            CardDataResource.LoadBytes(
                cards,
                Encoding.UTF8.GetBytes(staleText),
                Encoding.UTF8.GetBytes("test copyright"),
                requireOfficialGateway: true));
        Assert.Contains("pinned System Gateway", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddedCatalogIsReadOnlyAndUnknownCodesDoNotResolve()
    {
        CardMetadataCatalog catalog = CardMetadataCatalog.Embedded();
        Assert.True(catalog.Count > 0);
        Assert.False(catalog.ContainsKey("not-a-card"));
        Assert.IsAssignableFrom<IReadOnlyDictionary<string, NormalizedCardDefinition>>(catalog);
    }
}

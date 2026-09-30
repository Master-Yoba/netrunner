using System.Security.Cryptography;
using System.Text.Json;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Cards.Tests;

public sealed class OfficialPresetRevalidationTests
{
    private const string RevalidationSha256 =
        "8b134eb3e828f9388eab1228576e4c1ba282a151606636b73e979bfaf11656bf";

    private static readonly JsonSerializerOptions ArtifactJsonOptions = new()
    {
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void RevalidationArtifactPinsEveryPublishedCodeAndQuantity()
    {
        string root = FindRepositoryRoot();
        string artifactPath = Path.Combine(root, "data", "m4", "preset-revalidation.json");
        byte[] artifactBytes = File.ReadAllBytes(artifactPath);
        string actualSha256 = Convert.ToHexString(SHA256.HashData(artifactBytes)).ToLowerInvariant();
        Assert.Equal(RevalidationSha256, actualSha256);

        RevalidationDocument document = ReadArtifact(artifactBytes);
        Assert.Equal(1, document.SchemaVersion);
        Assert.Equal("2026-09-05", document.RevalidatedDate);
        Assert.Equal(CardDataValidator.OfficialCommitSha, document.MetadataCommitSha);
        Assert.Equal(
            "https://nullsignal.games/products/system-gateway/",
            document.PublishedSourceUrl);
        Assert.Equal(
            "https://nullsignal.games/players/learn-to-play/learn-to-play-runner/",
            document.RunnerLearnToPlayUrl);
        Assert.Equal(
            "https://nullsignal.games/players/learn-to-play/learn-to-play-corp/",
            document.CorpLearnToPlayUrl);
        Assert.Equal(4, document.Presets.Count);

        OfficialPresetCatalog embedded = OfficialPresetCatalog.Embedded();
        embedded.Validate();

        string checkedInPath = Path.Combine(root, "data", "m4", "presets.json");
        OfficialPresetCatalog checkedIn = OfficialPresetCatalog.Parse(File.ReadAllText(checkedInPath));
        AssertCatalogEqual(checkedIn, embedded);

        var expectedCounts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["system-gateway-basic-runner"] = 30,
            ["system-gateway-expanded-runner"] = 40,
            ["system-gateway-basic-corp"] = 34,
            ["system-gateway-expanded-corp"] = 44,
        };

        foreach (RevalidationPreset expected in document.Presets)
        {
            Assert.True(expectedCounts.TryGetValue(expected.PresetId, out int expectedCount));
            OfficialPreset actual = embedded.Get(expected.PresetId);
            Assert.True(actual.Playable);
            Assert.Equal(expected.Side, actual.Side == PlayerSide.Runner ? "runner" : "corp");
            Assert.Equal(expected.IdentityCode, actual.IdentityCode);
            Assert.Equal(expectedCount, expected.Cards.Sum(static card => card.Quantity));
            Assert.Equal(expectedCount, actual.CardCount);
            Assert.Equal(expected.Cards.Count, actual.Cards.Count);
            Assert.Equal(
                expected.Cards.Count,
                expected.Cards.Select(static card => card.Code).Distinct(StringComparer.Ordinal).Count());

            for (int index = 0; index < expected.Cards.Count; index++)
            {
                RevalidationCard expectedCard = expected.Cards[index];
                DeckCardEntry actualCard = actual.Cards[index];
                Assert.Equal(expectedCard.Code, actualCard.Code);
                Assert.Equal(expectedCard.Quantity, actualCard.Quantity);
                Assert.False(string.IsNullOrWhiteSpace(expectedCard.Name));
            }
        }

        Assert.Equal(expectedCounts.Keys.OrderBy(static value => value), document.Presets
            .Select(static preset => preset.PresetId)
            .OrderBy(static value => value));
    }

    [Fact]
    public void RevalidationCardsResolveAgainstAuthenticatedRuntimeCatalog()
    {
        // Clean CI deliberately uses the seven-card synthetic payload. The
        // exact code/quantity checks above still run there; this assertion is
        // exercised by the content-enabled gate with the authenticated 77-card
        // runtime payload.
        if (!string.Equals(CardDataResource.EmbeddedMode, CardDataResource.OfficialMode, StringComparison.Ordinal))
        {
            Assert.Equal(CardDataResource.SyntheticMode, CardDataResource.EmbeddedMode);
            return;
        }

        string root = FindRepositoryRoot();
        RevalidationDocument document = ReadArtifact(
            File.ReadAllBytes(Path.Combine(root, "data", "m4", "preset-revalidation.json")));
        CardDataSet data = CardDataResource.LoadEmbedded();
        OfficialPresetCatalog presets = OfficialPresetCatalog.Embedded();
        presets.Validate(data);
        RuntimeCardCatalog runtime = GatewayRuntimeCardCatalog.Create(data);
        Dictionary<string, NormalizedCardDefinition> metadata = data.Cards
            .ToDictionary(static card => card.Code, StringComparer.Ordinal);

        foreach (RevalidationPreset preset in document.Presets)
        {
            Assert.True(metadata.TryGetValue(preset.IdentityCode, out NormalizedCardDefinition? identity));
            Assert.Equal(preset.IdentityName, identity!.Title);
            Assert.True(runtime.TryGet(new CardDefinitionId(preset.IdentityCode), out _));

            foreach (RevalidationCard card in preset.Cards)
            {
                Assert.True(metadata.TryGetValue(card.Code, out NormalizedCardDefinition? definition));
                Assert.Equal(card.Name, definition!.Title);
                Assert.True(runtime.TryGet(new CardDefinitionId(card.Code), out _));
            }
        }
    }

    private static RevalidationDocument ReadArtifact(byte[] bytes) =>
        JsonSerializer.Deserialize<RevalidationDocument>(
            bytes,
            ArtifactJsonOptions)
        ?? throw new InvalidOperationException("Preset revalidation artifact is empty.");

    private static void AssertCatalogEqual(OfficialPresetCatalog expected, OfficialPresetCatalog actual)
    {
        Assert.Equal(expected.Provenance, actual.Provenance);
        // presets.json holds the teaching decks; the embedded catalog adds the Gateway sample decks after them.
        Assert.Equal(expected.Teaching.Count, actual.Teaching.Count);
        for (int index = 0; index < expected.Teaching.Count; index++)
        {
            OfficialPreset expectedPreset = expected.Teaching[index];
            OfficialPreset actualPreset = actual.Teaching[index];
            Assert.Equal(expectedPreset.PresetId, actualPreset.PresetId);
            Assert.Equal(expectedPreset.Side, actualPreset.Side);
            Assert.Equal(expectedPreset.IdentityCode, actualPreset.IdentityCode);
            Assert.Equal(expectedPreset.Kind, actualPreset.Kind);
            Assert.Equal(expectedPreset.Playable, actualPreset.Playable);
            Assert.Equal(expectedPreset.Cards.Count, actualPreset.Cards.Count);
            for (int cardIndex = 0; cardIndex < expectedPreset.Cards.Count; cardIndex++)
            {
                Assert.Equal(expectedPreset.Cards[cardIndex], actualPreset.Cards[cardIndex]);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Netrunner.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed record RevalidationDocument(
        int SchemaVersion,
        string RevalidatedDate,
        string MetadataCommitSha,
        string PublishedSourceUrl,
        string RunnerLearnToPlayUrl,
        string CorpLearnToPlayUrl,
        string ProvenanceNote,
        IReadOnlyList<RevalidationPreset> Presets);

    private sealed record RevalidationPreset(
        string PresetId,
        string Side,
        string IdentityCode,
        string IdentityName,
        IReadOnlyList<RevalidationCard> Cards);

    private sealed record RevalidationCard(string Code, string Name, int Quantity);
}

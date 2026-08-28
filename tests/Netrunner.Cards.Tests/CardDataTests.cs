using System.Text;

using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

public sealed class CardDataTests
{
    private static readonly string[] AgendaSubtypes = ["Research", "Priority"];

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void NormalizerMapsTheCheckedInUpstreamFixtureAndSortsByTitleThenCode()
    {
        string upstream = File.ReadAllText(FixturePath("upstream-v1-synthetic.json"));

        IReadOnlyList<NormalizedCardDefinition> cards =
            CardDataNormalizer.Normalize(upstream, "synthetic");

        Assert.Equal(6, cards.Count);
        Assert.Equal(
            cards.OrderBy(card => card.Title, StringComparer.Ordinal)
                .ThenBy(card => card.Code, StringComparer.Ordinal)
                .Select(card => card.Code),
            cards.Select(card => card.Code));
        Assert.Equal("Synthetic Corp Identity", cards[0].Title);
        Assert.Equal(AgendaSubtypes, cards[4].Subtypes);
        Assert.Equal(2, cards[4].AgendaPoints);
        Assert.Equal(3, cards[4].AdvancementRequirement);
        Assert.Equal(1, cards[2].MemoryCost);
        Assert.Null(cards[0].InfluenceLimit);
        Assert.Contains("<symbol:credit>", cards[0].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("[credit]", cards[0].Text, StringComparison.Ordinal);
        Assert.Equal(
            CardDataNormalizer.Normalize(upstream, "synthetic").Select(card => card.Code),
            cards.Select(card => card.Code));
    }

    [Fact]
    public void EveryDeclaredSymbolIsLexicallyNormalizedWithoutRuleInterpretation()
    {
        string source = string.Join(
            " ",
            CardTextNormalizer.SupportedSymbols.Select(symbol => $"[{symbol}]"));

        string normalized = CardTextNormalizer.NormalizeSymbols(source);

        foreach (string symbol in CardTextNormalizer.SupportedSymbols)
        {
            Assert.Contains($"<symbol:{symbol}>", normalized, StringComparison.Ordinal);
        }

        Assert.Equal(
            normalized,
            CardTextNormalizer.NormalizeSymbols(normalized));
        Assert.Throws<CardDataValidationException>(() =>
            CardTextNormalizer.NormalizeSymbols("[not-a-supported-symbol]"));
        Assert.Throws<CardDataValidationException>(() =>
            CardTextNormalizer.ValidateNormalized("contains [credit]", "text"));
    }

    [Fact]
    public void StrictNormalizedFixtureLoadsAndAuthenticatesItsChecksum()
    {
        CardDataSet data = CardDataLoader.Load(
            FixturePath("normalized-synthetic.json"),
            FixturePath("manifest-synthetic.json"));

        Assert.Equal(6, data.Cards.Count);
        Assert.Equal("synthetic", data.Manifest.PackCode);
        Assert.Equal("fe562cb6fac1298afdcfc71701780568bd153e82718ad5182d13061cceb36828", data.Manifest.OutputSha256);
        Assert.Equal(data.Cards.Count, data.CardDefinitions.Count);
        Assert.Equal("Research - Priority", data.CardDefinitions[4].Subtypes);
        Assert.IsAssignableFrom<IList<NormalizedCardDefinition>>(data.Cards);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<NormalizedCardDefinition>)data.Cards).Clear());
        Assert.IsAssignableFrom<IList<string>>(data.Cards[4].Subtypes);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<string>)data.Cards[4].Subtypes).Add("Mutated"));
    }

    [Fact]
    public void DeterministicWriterHasStableUtf8AndChecksum()
    {
        IReadOnlyList<NormalizedCardDefinition> cards = CardDataNormalizer.Normalize(
            File.ReadAllText(FixturePath("upstream-v1-synthetic.json")),
            "synthetic");

        byte[] first = CardDataJson.SerializeCards(cards);
        byte[] second = CardDataJson.SerializeCards(
            CardDataJson.ParseCards(first));

        Assert.Equal(first, second);
        Assert.Equal(Encoding.UTF8.GetString(first), Encoding.UTF8.GetString(second));
        Assert.Equal(
            CardDataJson.ComputeSha256Hex(first),
            CardDataJson.ComputeSha256Hex(second));
        Assert.DoesNotContain("\r", Encoding.UTF8.GetString(first), StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownAndMissingNormalizedFieldsAreRejected()
    {
        string valid = File.ReadAllText(FixturePath("normalized-synthetic.json"));
        string unknown = valid.Replace(
            "\"code\": \"91001\"",
            "\"unknown\": true, \"code\": \"91001\"",
            StringComparison.Ordinal);
        string missing = valid.Replace(
            "    \"deckLimit\": 1,\n",
            string.Empty,
            StringComparison.Ordinal);
        string duplicate = valid.Replace(
            "\"code\": \"91001\"",
            "\"code\": \"91001\", \"code\": \"91001\"",
            StringComparison.Ordinal);

        Assert.Throws<CardDataValidationException>(() => CardDataJson.ParseCards(Encoding.UTF8.GetBytes(unknown)));
        Assert.Throws<CardDataValidationException>(() => CardDataJson.ParseCards(Encoding.UTF8.GetBytes(missing)));
        Assert.Throws<CardDataValidationException>(() => CardDataJson.ParseCards(Encoding.UTF8.GetBytes(duplicate)));
    }

    [Fact]
    public void ExplicitEmptyRulesTextIsPreservedButMissingTextIsRejected()
    {
        string valid = File.ReadAllText(FixturePath("normalized-synthetic.json"));
        int textStart = valid.IndexOf("    \"text\": ", StringComparison.Ordinal);
        Assert.True(textStart >= 0);
        int textEnd = valid.IndexOf('\n', textStart);
        Assert.True(textEnd >= 0);
        string emptyText = valid[..textStart]
            + "    \"text\": \"\","
            + valid[textEnd..];

        IReadOnlyList<NormalizedCardDefinition> cards =
            CardDataJson.ParseCards(Encoding.UTF8.GetBytes(emptyText));

        Assert.Equal(string.Empty, cards[0].Text);
        string missingText = valid[..textStart] + valid[(textEnd + 1)..];
        Assert.Throws<CardDataValidationException>(() =>
            CardDataJson.ParseCards(Encoding.UTF8.GetBytes(missingText)));
        string whitespaceText = valid[..textStart]
            + "    \"text\": \"   \","
            + valid[textEnd..];
        Assert.Throws<CardDataValidationException>(() =>
            CardDataJson.ParseCards(Encoding.UTF8.GetBytes(whitespaceText)));
    }

    [Fact]
    public void OfficialManifestRequiresPinnedSourceIdentity()
    {
        CardDataManifest manifest = new(
            CardDataValidator.CurrentManifestSchemaVersion,
            CardDataValidator.OfficialSourceUrl,
            CardDataValidator.OfficialCommitSha,
            "2026-08-28",
            "v1",
            CardDataValidator.CurrentNormalizedSchemaVersion,
            new string('a', 64),
            "COPYRIGHT.md",
            "NOASSERTION",
            "pack/sg.json",
            CardDataValidator.OfficialPackCode,
            CardDataValidator.OfficialPackName,
            CardDataValidator.OfficialCardCount);

        CardDataValidator.ValidateManifest(manifest, requireOfficialGateway: true);

        Assert.Throws<CardDataValidationException>(() =>
            CardDataValidator.ValidateManifest(
                manifest with { CommitSha = new string('0', 40) },
                requireOfficialGateway: true));
        Assert.Throws<CardDataValidationException>(() =>
            CardDataValidator.ValidateManifest(
                manifest with { SourceLicensePath = "LICENSE" },
                requireOfficialGateway: true));
    }

    [Fact]
    public void InvalidRecordsAndManifestVersionsFailClosed()
    {
        IReadOnlyList<NormalizedCardDefinition> valid = CardDataNormalizer.Normalize(
            File.ReadAllText(FixturePath("upstream-v1-synthetic.json")),
            "synthetic");

        NormalizedCardDefinition invalid = new(
            "91007",
            "Synthetic Invalid",
            "runner",
            "neutral-runner",
            "program",
            Array.Empty<string>(),
            "Invalid",
            "synthetic",
            0,
            1,
            false,
            -1,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
        var records = valid.ToList();
        records.Add(invalid);

        Assert.Throws<CardDataValidationException>(() =>
            CardDataValidator.ValidateRecords(records, "synthetic"));

        CardDataManifest manifest = new(
            99,
            "https://example.invalid/netrunner-cards-json",
            "0000000000000000000000000000000000000000",
            "2026-08-28",
            "v1",
            1,
            "fe562cb6fac1298afdcfc71701780568bd153e82718ad5182d13061cceb36828",
            "fixtures/NOTICE.txt",
            "NOASSERTION",
            "fixtures/upstream-v1-synthetic.json",
            "synthetic",
            "Synthetic M1 Fixture",
            6);

        Assert.Throws<CardDataValidationException>(() => CardDataValidator.ValidateManifest(manifest));
    }

    [Fact]
    public void OfficialCodesRemainUnboundFromFixtureBehaviorCatalog()
    {
        IReadOnlyList<NormalizedCardDefinition> cards = CardDataNormalizer.Normalize(
            File.ReadAllText(FixturePath("upstream-v1-synthetic.json")),
            "synthetic");

        CardDataValidator.ValidateOfficialCardsUnbound(cards);
        Assert.False(
            ToyCardBehaviorCatalog.TryGet(new CardDefinitionId("91001"), out _));
    }
}

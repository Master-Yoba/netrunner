using System.Text;

using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

/// <summary>
/// EL.1 C2 (FORMAT-401): the format snapshots parse strictly, validate the
/// Gateway decks under the Gateway format, and reject an illegal deck for
/// each format rule.
/// </summary>
public sealed class CardFormatTests
{
    [Fact]
    public void EmbeddedSnapshotsParseAndOnlyCompleteFormatsArePlayable()
    {
        CardFormatCatalog catalog = CardFormatCatalog.Embedded();

        Assert.Equal(
            [CardFormatIds.Gateway, CardFormatIds.GatewayElevation, CardFormatIds.Startup2025, CardFormatIds.Startup2026],
            catalog.Formats.Select(static format => format.Id));
        Assert.Equal(
            [CardFormatIds.Gateway, CardFormatIds.GatewayElevation],
            catalog.PlayableFormats.Select(static format => format.Id));
        CardFormat startup = catalog.Get(CardFormatIds.Startup2026);
        Assert.False(startup.Complete);
        Assert.Equal(["vantage_point"], startup.MissingPacks);
        Assert.True(startup.IsBanned("35045"));
        Assert.Equal(new HighValueAgendaLimit(3, 3), startup.DeckRules.HighValueAgendaLimit);
        Assert.Null(catalog.Get(CardFormatIds.Gateway).Restriction);
    }

    [Fact]
    public void FormatsNameThePoolsThatPlayThem()
    {
        CardFormatCatalog catalog = CardFormatCatalog.Embedded();

        Assert.Same(CardPools.Gateway, CardPools.ForFormat(catalog.Get(CardFormatIds.Gateway)));
        Assert.Same(CardPools.GatewayElevation, CardPools.ForFormat(catalog.Get(CardFormatIds.GatewayElevation)));
        Assert.True(CardPools.TryForBehaviorVersion(GatewayBehaviorCatalog.Version, out CardPool gateway));
        Assert.Same(CardPools.Gateway, gateway);
        Assert.True(CardPools.TryForBehaviorVersion(ElevationBehaviorCatalog.Version, out CardPool elevation));
        Assert.Same(CardPools.GatewayElevation, elevation);
        Assert.False(CardPools.TryForBehaviorVersion("unknown", out _));
    }

    [Fact]
    public void TheElevationCatalogComposesTheUnchangedGatewayCatalog()
    {
        BehaviorCatalog gateway = GatewayBehaviorCatalog.Create();
        BehaviorCatalog pool = ElevationBehaviorCatalog.Create();

        Assert.Equal(ElevationBehaviorCatalog.Version, pool.Version);
        Assert.NotEqual(gateway.Fingerprint, pool.Fingerprint);
        Assert.Superset(gateway.AllDefinitionIds.ToHashSet(), pool.AllDefinitionIds.ToHashSet());
        Assert.Equal(GatewayBehaviorCatalog.Create().Fingerprint, gateway.Fingerprint);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2}", "schemaVersion must be 1")]
    [InlineData("[]", "must be an object")]
    [InlineData("{", "malformed")]
    public void MalformedSnapshotsAreRejected(string json, string expected)
    {
        CardDataValidationException exception = Assert.Throws<CardDataValidationException>(() =>
            CardFormatCatalog.Parse([("bad.json", Encoding.UTF8.GetBytes(json))]));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASnapshotWithAnUnknownFieldOrInconsistentCompletenessIsRejected()
    {
        string valid = Snapshot();
        Assert.Single(CardFormatCatalog.Parse([("ok.json", Encoding.UTF8.GetBytes(valid))]).Formats);
        Assert.Contains(
            "unknown field",
            Assert.Throws<CardDataValidationException>(() => CardFormatCatalog.Parse(
                [("x.json", Encoding.UTF8.GetBytes(valid.Replace("\"complete\"", "\"extra\":1,\"complete\"", StringComparison.Ordinal)))])).Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "complete exactly when",
            Assert.Throws<CardDataValidationException>(() => CardFormatCatalog.Parse(
                [("x.json", Encoding.UTF8.GetBytes(valid.Replace("\"complete\": true", "\"complete\": false", StringComparison.Ordinal)))])).Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "declared twice",
            Assert.Throws<CardDataValidationException>(() => CardFormatCatalog.Parse(
                [("a.json", Encoding.UTF8.GetBytes(valid)), ("b.json", Encoding.UTF8.GetBytes(valid))])).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ElevationSimulationDecksAreLegalAndFullyBound()
    {
        IReadOnlyList<OfficialPreset> decks = OfficialPresetCatalog.ElevationSimulation().ElevationSimulationDecks;
        Assert.Equal(8, decks.Count(static deck => deck.Side == PlayerSide.Corp));
        Assert.Equal(6, decks.Count(static deck => deck.Side == PlayerSide.Runner));
        Assert.DoesNotContain(OfficialPresetCatalog.Embedded().Presets,
            static preset => preset.PresetId.StartsWith(OfficialPresetCatalog.ElevationSimulationPrefix, StringComparison.Ordinal));
        BehaviorCatalog behavior = ElevationBehaviorCatalog.Create();
        foreach (OfficialPreset deck in decks)
        {
            Assert.All(deck.Cards.Select(static card => card.Code).Append(deck.IdentityCode),
                code => Assert.True(
                    behavior.TryGet(new CardDefinitionId(code), out _)
                    || behavior.TryGetIce(new CardDefinitionId(code), out _)
                    || behavior.TryGetIcebreaker(new CardDefinitionId(code), out _)
                    || behavior.TryGetAgenda(new CardDefinitionId(code), out _)
                    || behavior.TryGetAsset(new CardDefinitionId(code), out _)
                    || behavior.TryGetRunEvent(new CardDefinitionId(code), out _),
                    $"{deck.PresetId}: {code} is unbound."));
        }

        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
        {
            return;
        }

        CardDataSet pool = CardDataResource.LoadEmbeddedPool();
        CardFormat format = CardFormatCatalog.Embedded().Get(CardFormatIds.GatewayElevation);
        foreach (OfficialPreset deck in decks)
        {
            var request = new DeckRequest(DeckRequest.CurrentSchemaVersion, deck.Side, deck.IdentityCode, deck.Cards);
            DeckValidationResult result = FormatDeckValidator.Validate(request, format, pool);
            Assert.True(result.IsValid, $"{deck.PresetId}: {string.Join("; ", result.Diagnostics)}");
        }
    }

    [Fact]
    public void ElevationFormatDecksAreOnePerElevationIdentityLegalAndFullyBound()
    {
        IReadOnlyList<OfficialPreset> decks = OfficialPresetCatalog.GatewayElevation().ElevationDecks;
        Assert.Equal(8, decks.Count(static deck => deck.Side == PlayerSide.Corp));
        Assert.Equal(6, decks.Count(static deck => deck.Side == PlayerSide.Runner));
        Assert.Equal(14, decks.Select(static deck => deck.IdentityCode).Distinct(StringComparer.Ordinal).Count());
        Assert.All(decks, static deck => Assert.StartsWith("35", deck.IdentityCode, StringComparison.Ordinal));
        Assert.All(decks, static deck => Assert.NotEmpty(deck.Archetypes));
        Assert.DoesNotContain(OfficialPresetCatalog.Embedded().Presets,
            static preset => preset.PresetId.StartsWith(OfficialPresetCatalog.ElevationDeckPrefix, StringComparison.Ordinal));
        Assert.DoesNotContain(OfficialPresetCatalog.GatewayElevation().Presets,
            static preset => preset.PresetId.StartsWith(OfficialPresetCatalog.ElevationSimulationPrefix, StringComparison.Ordinal));
        BehaviorCatalog behavior = ElevationBehaviorCatalog.Create();
        foreach (OfficialPreset deck in decks)
        {
            Assert.All(deck.Cards.Select(static card => card.Code).Append(deck.IdentityCode),
                code => Assert.True(
                    behavior.TryGet(new CardDefinitionId(code), out _)
                    || behavior.TryGetIce(new CardDefinitionId(code), out _)
                    || behavior.TryGetIcebreaker(new CardDefinitionId(code), out _)
                    || behavior.TryGetAgenda(new CardDefinitionId(code), out _)
                    || behavior.TryGetAsset(new CardDefinitionId(code), out _)
                    || behavior.TryGetRunEvent(new CardDefinitionId(code), out _),
                    $"{deck.PresetId}: {code} is unbound."));
        }

        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
        {
            return;
        }

        CardDataSet pool = CardDataResource.LoadEmbeddedPool();
        OfficialPresetCatalog.GatewayElevation().Validate(pool);
        CardFormat format = CardFormatCatalog.Embedded().Get(CardFormatIds.GatewayElevation);
        foreach (OfficialPreset deck in decks)
        {
            var request = new DeckRequest(DeckRequest.CurrentSchemaVersion, deck.Side, deck.IdentityCode, deck.Cards);
            DeckValidationResult result = FormatDeckValidator.Validate(request, format, pool);
            Assert.True(result.IsValid, $"{deck.PresetId}: {string.Join("; ", result.Diagnostics)}");
        }
    }

    [Fact]
    public void GatewayDecksValidateUnderTheGatewayFormatAndIllegalOnesFailPerRule()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
        {
            return;
        }

        CardFormatCatalog catalog = CardFormatCatalog.Embedded();
        CardDataSet pool = CardDataResource.LoadEmbeddedPool();
        catalog.ValidateAgainst(pool);
        CardFormat gateway = catalog.Get(CardFormatIds.Gateway);
        CardFormat elevation = catalog.Get(CardFormatIds.GatewayElevation);
        foreach (OfficialPreset deck in OfficialPresetCatalog.Embedded().Gateway)
        {
            DeckValidationResult result = FormatDeckValidator.Validate(deck.ToDeckRequest(), gateway, pool);
            Assert.True(result.IsValid, $"{deck.PresetId}: {string.Join("; ", result.Diagnostics)}");
        }

        // Legal packs: an Elevation card is not legal in the Gateway format.
        DeckRequest withElevation = Runner("30010", [("30030", 3), ("35014", 3)]);
        Assert.Contains(FormatDeckValidator.Validate(withElevation, gateway, pool).Diagnostics,
            static diagnostic => diagnostic.Code == "format-pack");
        Assert.DoesNotContain(FormatDeckValidator.Validate(withElevation, elevation, pool).Diagnostics,
            static diagnostic => diagnostic.Code == "format-pack");

        // Ban list: Cleaver is banned in Gateway + Elevation but legal in Gateway.
        DeckRequest withCleaver = Runner("30010", [("30006", 1)]);
        Assert.Contains(FormatDeckValidator.Validate(withCleaver, elevation, pool).Diagnostics,
            static diagnostic => diagnostic.Code == "format-banned");
        Assert.DoesNotContain(FormatDeckValidator.Validate(withCleaver, gateway, pool).Diagnostics,
            static diagnostic => diagnostic.Code == "format-banned");

        // High-value agendas: four agendas worth 3 points break the Startup limit.
        NormalizedCardDefinition threePointer = pool.Cards.First(static card =>
            card.Type == "agenda" && card.AgendaPoints == 3 && card.Faction == "nbn");
        NormalizedCardDefinition otherThreePointer = pool.Cards.First(card =>
            card.Type == "agenda" && card.AgendaPoints == 3 && card.Code != threePointer.Code);
        DeckRequest agendas = new(
            DeckRequest.CurrentSchemaVersion,
            PlayerSide.Corp,
            "35058",
            [new DeckCardEntry(threePointer.Code, 3), new DeckCardEntry(otherThreePointer.Code, 1)]);
        Assert.Contains(FormatDeckValidator.Validate(agendas, elevation, pool).Diagnostics,
            static diagnostic => diagnostic.Code == "format-agenda-limit");
        Assert.DoesNotContain(FormatDeckValidator.Validate(agendas, gateway, pool).Diagnostics,
            static diagnostic => diagnostic.Code == "format-agenda-limit");
    }

    private static DeckRequest Runner(string identity, (string Code, int Quantity)[] cards) => new(
        DeckRequest.CurrentSchemaVersion,
        PlayerSide.Runner,
        identity,
        cards.Select(static card => new DeckCardEntry(card.Code, card.Quantity)).ToArray());

    private static string Snapshot() => """
        {
          "schemaVersion": 1,
          "id": "test-format",
          "name": "Test",
          "effectiveDate": "2026-01-01",
          "complete": true,
          "cardPool": "gateway",
          "legalPacks": ["sg"],
          "missingPacks": [],
          "restriction": null,
          "deckRules": { "maxCopiesByName": 3, "highValueAgendaLimit": null },
          "source": { "note": "test" }
        }
        """;
}

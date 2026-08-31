using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class CardMetadataPresentationTests
{
    [Fact]
    public void FormatterKeepsAllowedMarkupAsReadableNonColorText()
    {
        string formatted = CardMarkupFormatter.Format(
            "<ul><li><strong>Pay</strong> 1<symbol:credit></li><li><em>Run</em> any server</li></ul>");

        Assert.Equal("• Pay 1[credit]\n• Run any server\n", formatted);
        Assert.DoesNotContain("<", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatterHandlesGatewayNestedListShape()
    {
        const string gatewayText =
            "Resolve 1 of the following. If the Runner is tagged, you may resolve both instead."
            + "<ul><li>Gain 3<symbol:credit>.</li><li>Draw 3 cards.</li></ul>";

        Assert.Equal(
            "Resolve 1 of the following. If the Runner is tagged, you may resolve both instead."
            + "\n• Gain 3[credit].\n• Draw 3 cards.\n",
            CardMarkupFormatter.Format(gatewayText));
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<strong class=unsafe>text</strong>")]
    [InlineData("<ul><li>unclosed")]
    [InlineData("<ul><li>mismatched</ul></li>")]
    [InlineData("<symbol:not-allowed>")]
    public void FormatterFailsClosedForUnsupportedOrMalformedMarkup(string text)
    {
        Assert.Throws<CardMarkupValidationException>(() => CardMarkupFormatter.Format(text));
    }

    [Fact]
    public void ResolverSnapshotsMetadataAndDoesNotExposeSourceMutability()
    {
        var card = new NormalizedCardDefinition(
            "91001",
            "Synthetic Resolver Card",
            "runner",
            "neutral-runner",
            "event",
            ["Run"],
            "<strong>Run</strong> <symbol:click>.",
            "synthetic",
            3,
            3,
            false,
            1,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
        var source = new List<NormalizedCardDefinition> { card };
        CardCatalogMetadataResolver resolver = new(source);
        source.Clear();

        Assert.True(resolver.TryResolve("91001", out CardPresentationMetadata metadata));
        Assert.Equal("Synthetic Resolver Card", metadata.Title);
        Assert.Equal("[click]", CardMarkupFormatter.Format(metadata.RulesText).Split(' ').Last().TrimEnd('.'));
        Assert.False(resolver.TryResolve("hidden", out _));
        Assert.Single(resolver.Cards);
    }

    [Fact]
    public void HiddenAndNullObservationSlotsNeverInvokeMetadataResolver()
    {
        var resolver = new SpyMetadataResolver();
        var hiddenCard = new Dictionary<string, object?>
        {
            ["id"] = "card:hidden",
            ["definitionId"] = null,
            ["rezzed"] = false,
            ["advancementCounters"] = 0,
        };
        var publicState = new Dictionary<string, object?>
        {
            ["players"] = new Dictionary<string, object?>
            {
                ["corp"] = Player("corp"),
                ["runner"] = Player("runner"),
            },
            ["hiddenZoneCounts"] = new Dictionary<string, object?>
            {
                ["corpHand"] = 1,
                ["corpDeck"] = 1,
                ["runnerGrip"] = 1,
                ["runnerStack"] = 1,
            },
            ["servers"] = new object?[]
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "remote",
                    ["kind"] = ServerKind.Remote,
                    ["ice"] = new object?[] { null },
                    ["root"] = new object?[] { null },
                },
            },
            ["runnerRig"] = Array.Empty<object?>(),
            ["corpScore"] = Array.Empty<object?>(),
            ["runnerScore"] = Array.Empty<object?>(),
            ["outcome"] = new Dictionary<string, object?>
            {
                ["kind"] = GameOutcomeKind.Ongoing,
                ["winner"] = null,
                ["reason"] = null,
            },
        };
        var privateState = new Dictionary<string, object?>
        {
            ["grip"] = new object?[] { hiddenCard },
            ["stack"] = Array.Empty<object?>(),
        };

        MatchViewModel view = ObservationViewMapper.Build(
            new Observation(
                PlayerSide.Runner,
                "",
                Array.Empty<LegalAction>(),
                publicState,
                privateState),
            resolver);

        Assert.Empty(resolver.LookupIds);
        Assert.All(view.Servers.SelectMany(server => server.Ice.Concat(server.Root)), Assert.Null);
        CardViewModel card = Assert.Single(view.RunnerHand.VisibleCards);
        Assert.Null(card.DefinitionId);
        Assert.False(card.IsKnown);
        Assert.Equal("Unknown card", card.Title);
    }

    private static Dictionary<string, object?> Player(string id) => new()
    {
        ["id"] = id,
        ["credits"] = 5,
        ["clicks"] = 4,
        ["agendaPoints"] = 0,
    };

    private sealed class SpyMetadataResolver : ICardMetadataResolver
    {
        public List<string> LookupIds { get; } = new();

        public IReadOnlyList<CardPresentationMetadata> Cards => Array.Empty<CardPresentationMetadata>();

        public bool TryResolve(string definitionId, out CardPresentationMetadata metadata)
        {
            LookupIds.Add(definitionId);
            metadata = CardPresentationMetadata.Unknown(definitionId);
            return false;
        }
    }
}

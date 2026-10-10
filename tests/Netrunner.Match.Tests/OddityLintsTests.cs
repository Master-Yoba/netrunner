using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// The oddity lints (Wave 4.5, P1) find, in the recorded playtest games, the faults those playtests
/// reported; each lint here is tied to the game that showed it.
/// </summary>
public sealed class OddityLintsTests
{
    [Theory]
    [InlineData("playtest-central-ice", "gateway-discretion-advised~gateway-stolen-goods", 16353449718733399204UL, "counters-on-inert-card", "Palisade")]
    [InlineData("playtest-agenda-swap", "gateway-discretion-advised~gateway-stolen-goods", 18095256415510229748UL, "own-agenda-trashed-by-install", "Offworld Office")]
    [InlineData("playtest-spin-doctor", "gateway-hyper-velocity~gateway-stolen-goods", 14438435225079198195UL, "archives-shuffle-empty", "Spin Doctor")]
    [InlineData("playtest-runner-bot", "gateway-discretion-advised~gateway-stolen-goods", 3748720965229884459UL, "runner-repeats-known-trap", "Urtica Cipher")]
    public void APlaytestFaultIsFound(string fixture, string content, ulong seed, string lint, string card)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        IReadOnlyList<OddityFinding> findings = Lint(fixture, content, seed);
        Assert.Contains(findings, finding => finding.LintId == lint && finding.Detail.Contains(card, StringComparison.Ordinal));
    }

    /// <summary>Bot plan H1: the strategy guide's mistakes, found in the recorded playtest games.</summary>
    [Theory]
    [InlineData("playtest-agenda-swap", "gateway-discretion-advised~gateway-stolen-goods", 18095256415510229748UL, "remote-scored-uncontested", 133L,
        "Luminal Transubstantiation scored after 1 Runner turns without a run on its remote; at the Runner's last turn: 8 credits, 0 counters, ICE 0/2 rezzed, ran Archives.")]
    [InlineData("playtest-agenda-swap", "gateway-discretion-advised~gateway-stolen-goods", 18095256415510229748UL, "agenda-stolen-from-remote", 587L,
        "Send a Message (2 counters) behind 2 rezzed of 2 ICE; installed 1 Runner turns before; the Corp had 24 credits as the run began, 0 ICE left unrezzed, 0 of them affordable.")]
    [InlineData("playtest-spin-doctor", "gateway-hyper-velocity~gateway-stolen-goods", 14438435225079198195UL, "runner-ends-turn-tagged", 62L,
        "the turn ended with 2 tags.")]
    [InlineData("playtest-spin-doctor", "gateway-hyper-velocity~gateway-stolen-goods", 14438435225079198195UL, "agenda-stolen-from-remote", 185L,
        "Orbital Superiority (0 counters) behind 0 rezzed of 1 ICE; installed 1 Runner turns before; the Corp had 4 credits as the run began, 1 ICE left unrezzed, 1 of them affordable.")]
    [InlineData("playtest-runner-bot", "gateway-discretion-advised~gateway-stolen-goods", 3748720965229884459UL, "runner-runs-on-thin-grip", 271L,
        "a run on Remote with 2 cards in the grip.")]
    public void AGuideMistakeIsFound(string fixture, string content, ulong seed, string lint, long sequence, string detail)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        Assert.Contains(Lint(fixture, content, seed), finding => finding.LintId == lint && finding.Sequence == sequence && finding.Detail == detail);
    }

    /// <summary>
    /// The review of #131: a deck whose only damage is native ICE (Karunā, Tithe, Diviner: ICE specifications, not card
    /// behaviors) deals damage for <c>runner-runs-on-thin-grip</c>; a taxing code gate does not.
    /// </summary>
    [Theory]
    [InlineData("30047", true)]
    [InlineData("30074", false)]
    public void NativeIceDamageCountsAsTheCorpDealingDamage(string code, bool deals)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(RulesFuzzRunner.GatewayElevationContentIds[0], 1);
        var id = new CardDefinitionId(code);
        Assert.True(definition.Behavior.TryGetIce(id, out IceBehaviorSpec ice));
        Assert.Equal(deals, OddityLints.DealsDamage(definition.Behavior.TryGet(id, out CardBehaviorDefinition? behavior) ? behavior : null, ice));
    }

    [Fact]
    public void EveryGuideLintNamesItsRule()
    {
        string[] guide = ["runner-banks-credits", "runner-credit-over-draw-early", "runner-runs-on-thin-grip", "runner-ends-turn-tagged",
            "runner-flatlined", "remote-scored-uncontested", "agenda-stolen-from-remote", "agenda-score-delayed", "draw-into-hq-flood",
            "agenda-unprotected-at-turn-end", "decorative-remote-ice"];
        Assert.All(guide, id => Assert.Matches(@"^[RC]\d\d(, [RC]\d\d)*: ", OddityLints.All.Single(lint => lint.Id == id).Description));
    }

    [Fact]
    public void EveryLintIsDescribedOnce()
    {
        Assert.Equal(OddityLints.All.Count, OddityLints.All.Select(lint => lint.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(OddityLints.All, lint => Assert.False(string.IsNullOrWhiteSpace(lint.Description)));
    }

    [Fact]
    public void AUniqueUpgradeBesideItsOwnCopyIsFound()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // Found by the LLM reviewer (Wave 4.5 P2) in a Corp v9 game: a second Malapert Data Vault beside the first.
        (LadderSavedGame saved, FullMatchDefinition definition) = Assert.Single(LadderGameArchive.Read(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", "saved-games", "unique-beside-copy")));
        OddityFinding finding = Assert.Single(OddityLints.Run(saved, definition), value => value.LintId == "unique-replaces-own-copy");
        Assert.Equal((296L, "Malapert Data Vault beside its own installed copy."), (finding.Sequence, finding.Detail));
    }

    [Fact]
    public void ASavedGameReadsBackAndLintsTheSame()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LadderSavedGame saved, FullMatchDefinition definition) = Load("playtest-central-ice", "gateway-discretion-advised~gateway-stolen-goods", 16353449718733399204UL);
        string directory = Path.Combine(Path.GetTempPath(), "netrunner-lint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            LadderGameArchive.Write(directory, saved);
            (LadderSavedGame read, FullMatchDefinition readDefinition) = Assert.Single(LadderGameArchive.Read(directory));
            Assert.Equal(saved.Game, read.Game);
            Assert.Equal(OddityLints.Run(saved, definition), OddityLints.Run(read, readDefinition));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static IReadOnlyList<OddityFinding> Lint(string fixture, string content, ulong seed)
    {
        (LadderSavedGame saved, FullMatchDefinition definition) = Load(fixture, content, seed);
        return OddityLints.Run(saved, definition);
    }

    private static (LadderSavedGame Saved, FullMatchDefinition Definition) Load(string fixture, string content, ulong seed)
    {
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(content, seed);
        ReplayArtifact replay = EngineJson.Deserialize<ReplayArtifact>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", $"{fixture}-replay.json")), definition.Behavior, definition.Runtime);
        return (new LadderSavedGame(new LadderGame(fixture, content, seed, "playtest-corp", "playtest-runner"), 0.5, false, replay), definition);
    }
}

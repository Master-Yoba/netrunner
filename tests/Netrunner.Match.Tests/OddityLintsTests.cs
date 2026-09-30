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

    [Fact]
    public void EveryLintIsDescribedOnce()
    {
        Assert.Equal(OddityLints.All.Count, OddityLints.All.Select(lint => lint.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(OddityLints.All, lint => Assert.False(string.IsNullOrWhiteSpace(lint.Description)));
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

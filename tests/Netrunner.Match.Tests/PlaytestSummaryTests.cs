using Xunit;

namespace Netrunner.Match.Tests;

public sealed class PlaytestSummaryTests
{
    [Fact]
    public void TheSummaryTimesTheLocalPlayersTurnsAndCountsTheProtocolsSignals()
    {
        string[] lines =
        [
            Line("00:00", "startup", """{"humanSide":"corp","corpPreset":"gateway-quick-and-dirty","runnerPreset":"gateway-party-hard"}"""),
            Line("00:01", "displayed-snapshot", """{"viewer":"Corp","turn":1,"activeSide":"Corp","terminal":false}"""),
            Line("00:05", "selection", """{"selectionKind":"hint","value":"basics"}"""),
            Line("00:08", "selection", """{"selectionKind":"hint-dismissed","value":"basics"}"""),
            Line("00:10", "drag-outcome", """{"outcome":"invalid-drop","cardId":"card:1"}"""),
            Line("00:12", "drag-outcome", """{"outcome":"drop","cardId":"card:1"}"""),
            Line("00:13", "submission", """{"actionKind":"InstallCard"}"""),
            Line("00:13", "submission-result", """{"accepted":false}"""),
            Line("00:14", "selection", """{"selectionKind":"ui-control","value":"all-legal-actions"}"""),
            Line("00:31", "displayed-snapshot", """{"viewer":"Corp","turn":1,"activeSide":"Runner","terminal":false}"""),
            Line("00:40", "selection", """{"selectionKind":"ui-control","value":"skip-animation"}"""),
            Line("01:01", "displayed-snapshot", """{"viewer":"Corp","turn":2,"activeSide":"Corp","terminal":false}"""),
            Line("01:11", "displayed-snapshot", """{"viewer":"Corp","turn":2,"activeSide":"Corp","terminal":true}"""),
        ];

        PlaytestSummary summary = PlaytestSummary.Read(lines);

        Assert.Equal("corp", summary.HumanSide);
        Assert.Equal([30.0, 10.0], summary.TurnSeconds);
        Assert.Equal(20, summary.MedianTurnSeconds);
        Assert.Equal((1, 1, 1, 1), (summary.Drops, summary.InvalidDrops, summary.RejectedSubmissions, summary.AllLegalActions));
        Assert.Equal(1, summary.SkipAnimation);
        Assert.Equal(["basics"], summary.HintsShown);
        Assert.True(summary.Finished);
        Assert.Contains("median-seconds=20", summary.Format(), StringComparison.Ordinal);
    }

    private static string Line(string time, string kind, string payload) =>
        $$"""{"timestampUtc":"2026-09-28T10:{{time}}+00:00","kind":"{{kind}}","payload":{{payload}}}""";
}

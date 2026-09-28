using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class SoundCuesTests
{
    [Theory]
    [InlineData(PresentationEventKind.AgendaScored, PlayerSide.Corp, SoundCue.ScoreGain)]
    [InlineData(PresentationEventKind.AgendaScored, PlayerSide.Runner, SoundCue.ScoreLoss)]
    [InlineData(PresentationEventKind.AgendaStolen, PlayerSide.Runner, SoundCue.ScoreGain)]
    [InlineData(PresentationEventKind.AgendaStolen, PlayerSide.Corp, SoundCue.ScoreLoss)]
    public void PointsAreHeardFromTheViewersSide(PresentationEventKind kind, PlayerSide viewer, SoundCue expected)
    {
        Assert.Equal(expected, SoundCues.For(Beat(kind), viewer));
    }

    [Fact]
    public void ResultsAndTurnsDependOnWhoTheyAreFor()
    {
        Assert.Equal(SoundCue.Win, SoundCues.For(Beat(PresentationEventKind.GameEnded) with { Side = PlayerSide.Runner }, PlayerSide.Runner));
        Assert.Equal(SoundCue.Loss, SoundCues.For(Beat(PresentationEventKind.GameEnded) with { Side = PlayerSide.Corp }, PlayerSide.Runner));
        Assert.Equal(SoundCue.YourTurn, SoundCues.For(Beat(PresentationEventKind.TurnStarted) with { Side = PlayerSide.Corp }, PlayerSide.Corp));
        Assert.Equal(SoundCue.None, SoundCues.For(Beat(PresentationEventKind.TurnStarted) with { Side = PlayerSide.Runner }, PlayerSide.Corp));
        Assert.Equal(SoundCue.RunFail, SoundCues.For(Beat(PresentationEventKind.RunEnded) with { Successful = false }, PlayerSide.Corp));
        Assert.Equal(SoundCue.None, SoundCues.For(Beat(PresentationEventKind.RunEnded) with { Successful = true }, PlayerSide.Corp));
        Assert.Equal(SoundCue.Credit, SoundCues.For(Beat(PresentationEventKind.CreditsChanged) with { Amount = 2 }, PlayerSide.Corp));
        Assert.Equal(SoundCue.None, SoundCues.For(Beat(PresentationEventKind.CreditsChanged) with { Amount = -2 }, PlayerSide.Corp));
    }

    private static Beat Beat(PresentationEventKind kind) => new(kind, kind.ToString(), 0.5, BeatEmphasis.Normal);
}

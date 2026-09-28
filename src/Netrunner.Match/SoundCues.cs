using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>The sounds the client can play (U5); each is synthesized, so no audio asset is shipped.</summary>
public enum SoundCue
{
    None,
    Click,
    Credit,
    CardSlide,
    CardPlace,
    CardFlip,
    Rez,
    Advance,
    ScoreGain,
    ScoreLoss,
    RunStart,
    Encounter,
    Break,
    SubroutineFire,
    RunSuccess,
    RunFail,
    Damage,
    Tag,
    YourTurn,
    Win,
    Loss,
}

/// <summary>
/// Maps a presentation beat to its sound. Points and results are heard from
/// the viewer's side: a score or steal that helps the viewer rises, one that
/// helps the opponent falls.
/// </summary>
public static class SoundCues
{
    public static SoundCue For(Beat beat, PlayerSide viewer)
    {
        ArgumentNullException.ThrowIfNull(beat);
        return beat.Kind switch
        {
            PresentationEventKind.ClickSpent => SoundCue.Click,
            PresentationEventKind.CreditsChanged => beat.Amount > 0 ? SoundCue.Credit : SoundCue.None,
            PresentationEventKind.CardDrawn => SoundCue.CardSlide,
            PresentationEventKind.CardInstalled => SoundCue.CardPlace,
            PresentationEventKind.CardPlayed => SoundCue.CardFlip,
            PresentationEventKind.CardRezzed => SoundCue.Rez,
            PresentationEventKind.CardAdvanced => SoundCue.Advance,
            PresentationEventKind.AgendaScored => Relative(PlayerSide.Corp, viewer, SoundCue.ScoreGain, SoundCue.ScoreLoss),
            PresentationEventKind.AgendaStolen => Relative(PlayerSide.Runner, viewer, SoundCue.ScoreGain, SoundCue.ScoreLoss),
            PresentationEventKind.RunStarted => SoundCue.RunStart,
            PresentationEventKind.EncounterStarted => SoundCue.Encounter,
            PresentationEventKind.SubroutinesBroken => SoundCue.Break,
            PresentationEventKind.SubroutineFired => SoundCue.SubroutineFire,
            PresentationEventKind.RunSuccessful => SoundCue.RunSuccess,
            PresentationEventKind.RunEnded => beat.Successful == true ? SoundCue.None : SoundCue.RunFail,
            PresentationEventKind.DamageDealt => SoundCue.Damage,
            PresentationEventKind.TagsChanged => beat.Amount > 0 ? SoundCue.Tag : SoundCue.None,
            PresentationEventKind.TurnStarted => beat.Side == viewer ? SoundCue.YourTurn : SoundCue.None,
            PresentationEventKind.GameEnded => beat.Side is { } winner ? winner == viewer ? SoundCue.Win : SoundCue.Loss : SoundCue.None,
            _ => SoundCue.None,
        };
    }

    private static SoundCue Relative(PlayerSide gainer, PlayerSide viewer, SoundCue gain, SoundCue loss) => gainer == viewer ? gain : loss;
}

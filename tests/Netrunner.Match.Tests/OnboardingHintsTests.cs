using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class OnboardingHintsTests
{
    [Fact]
    public void TheFirstActionWindowExplainsClicksAndThenTheRun()
    {
        MatchViewModel view = View(PlayerSide.Runner, ActionKind.GainCredit, ActionKind.DrawCard, ActionKind.StartRun);

        Assert.Equal(OnboardingHints.Basics, OnboardingHints.Next(view, new HashSet<string>())?.Id);
        Assert.Equal(OnboardingHints.RunnerRun, OnboardingHints.Next(view, new HashSet<string> { OnboardingHints.Basics })?.Id);
        Assert.Null(OnboardingHints.Next(view, new HashSet<string> { OnboardingHints.Basics, OnboardingHints.RunnerRun }));
    }

    [Theory]
    [InlineData(PlayerSide.Runner, ActionKind.BreakSubroutines, OnboardingHints.RunnerBreak)]
    [InlineData(PlayerSide.Runner, ActionKind.PumpBreaker, OnboardingHints.RunnerBreak)]
    [InlineData(PlayerSide.Runner, ActionKind.StealAgenda, OnboardingHints.RunnerAccess)]
    [InlineData(PlayerSide.Runner, ActionKind.TrashAccessedCard, OnboardingHints.RunnerAccess)]
    [InlineData(PlayerSide.Corp, ActionKind.RezIce, OnboardingHints.CorpRez)]
    [InlineData(PlayerSide.Corp, ActionKind.AdvanceCard, OnboardingHints.CorpAdvance)]
    [InlineData(PlayerSide.Corp, ActionKind.ScoreAgenda, OnboardingHints.CorpAdvance)]
    public void EachHintFollowsTheActionItExplains(PlayerSide viewer, ActionKind kind, string expected)
    {
        Assert.Equal(expected, OnboardingHints.Next(View(viewer, ActionKind.PassPriority, kind), new HashSet<string>())?.Id);
    }

    [Fact]
    public void OnlyTheViewersOwnLiveDecisionShowsHints()
    {
        MatchViewModel view = View(PlayerSide.Corp, ActionKind.GainCredit, ActionKind.InstallCard);
        Assert.Equal(OnboardingHints.Basics, OnboardingHints.Next(view, new HashSet<string>())?.Id);
        Assert.Equal(OnboardingHints.CorpIce, OnboardingHints.Next(view, new HashSet<string> { OnboardingHints.Basics })?.Id);

        Assert.Null(OnboardingHints.Next(view with { Decision = view.Decision! with { OwnerSide = PlayerSide.Runner } }, new HashSet<string>()));
        Assert.Null(OnboardingHints.Next(view with { Decision = null }, new HashSet<string>()));
        Assert.Null(OnboardingHints.Next(view with { Outcome = new OutcomeViewModel(GameOutcomeKind.Won, PlayerSide.Corp, null) }, new HashSet<string>()));
    }

    [Fact]
    public void EveryHintHasAUniqueIdAndText()
    {
        Assert.Equal(OnboardingHints.Hints.Count, OnboardingHints.Hints.Select(hint => hint.Id).Distinct().Count());
        Assert.All(OnboardingHints.Hints, hint => Assert.False(string.IsNullOrWhiteSpace(hint.Text)));
    }

    [Fact]
    public void IntentCuesShowTheCorpsAdvancedRemotesAndANearWin()
    {
        var remote = new ServerViewModel("server:remote-1", ServerKind.Remote, [], [null]) { RootKeys = ["tracked:4"] };
        var empty = new ServerViewModel("server:remote-2", ServerKind.Remote, [], [null]) { RootKeys = ["tracked:5"] };
        MatchViewModel view = View(PlayerSide.Runner) with
        {
            Servers = [remote, empty],
            AdvancementByKey = new Dictionary<string, int> { ["tracked:4"] = 3 },
            Corp = new PlayerViewModel("player:corp", PlayerSide.Corp, 5, 3, 5),
        };

        Assert.Equal(["Corp is advancing a card in Remote 1 · 3 counters", "Corp is 2 points from winning"],
            OpponentIntentCues.From(view, server => server == remote ? "Remote 1" : "Remote 2"));
        // The Corp's own view has no cues about its own board.
        Assert.Empty(OpponentIntentCues.From(view with { Viewer = PlayerSide.Corp }, _ => "Remote 1"));
    }

    private static MatchViewModel View(PlayerSide viewer, params ActionKind[] kinds) => new(
        viewer,
        new PlayerViewModel("player:corp", PlayerSide.Corp, 5, 3, 0),
        new PlayerViewModel("player:runner", PlayerSide.Runner, 5, 4, 0),
        Array.Empty<ServerViewModel>(),
        new ZoneViewModel(ZoneKind.CorpHand, 0, Array.Empty<CardViewModel>()),
        new ZoneViewModel(ZoneKind.CorpDeck, 0, Array.Empty<CardViewModel>()),
        new ZoneViewModel(ZoneKind.RunnerGrip, 0, Array.Empty<CardViewModel>()),
        new ZoneViewModel(ZoneKind.RunnerStack, 0, Array.Empty<CardViewModel>()),
        Array.Empty<CardViewModel>(),
        Array.Empty<CardViewModel>(),
        Array.Empty<CardViewModel>(),
        new DecisionViewModel("decision:1", 1, new PlayerId(viewer == PlayerSide.Corp ? "player:corp" : "player:runner"), viewer, DecisionKind.TurnAction, "Act"),
        null,
        new OutcomeViewModel(GameOutcomeKind.Ongoing, null, null),
        [.. kinds.Select((kind, index) => new LegalActionViewModel(new ActionId($"action:{index}"), kind, kind.ToString(), new NoActionParameters()))]);
}

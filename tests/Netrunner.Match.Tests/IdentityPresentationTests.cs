using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class IdentityPresentationTests
{
    [Fact]
    public void MapsCoreAccessCountRunEndAndPublicIcePositions()
    {
        GameState state = M2ScenarioSetup.Build(M2ScenarioProvider.LoadRepresentative());
        Observation baseline = ObservationBuilder.Build(state, PlayerSide.Runner);
        var publicState = new Dictionary<string, object?>(baseline.PublicState);
        string serverId = state.Servers[0].Id.Value;
        publicState["runEnd"] = new Dictionary<string, object?>
        {
            ["runId"] = "run:representative",
            ["serverId"] = serverId,
            ["accessOccurrenceCount"] = 3,
        };
        publicState["icePositions"] = new object?[]
        {
            new Dictionary<string, object?>
            {
                ["cardId"] = "card:1",
                ["serverId"] = serverId,
                ["index"] = 0,
            },
            new Dictionary<string, object?>
            {
                ["cardId"] = "card:2",
                ["serverId"] = serverId,
                ["index"] = 1,
            },
        };

        MatchViewModel view = ObservationViewMapper.Build(
            baseline with { PublicState = publicState },
            CardMetadataResolvers.Default,
            GatewayBehaviorCatalog.Create());

        // The representative M2 fixture is outside a live run.  The live
        // count is asserted by the typed projection tests; this observation
        // verifies the terminal run-end envelope that survives run teardown.
        Assert.Null(view.Run);
        Assert.Equal(3, view.RunEnd!.AccessOccurrenceCount);
        Assert.Equal(
            ["card:1", "card:2"],
            view.Servers.Single(server => server.Id == serverId).IcePositions.Select(position => position.CardId));
        Assert.Equal("ZAHYA ACCESS COUNT · 3", IdentityChoicePresenter.PresentZahyaAccess(view).Summary);
    }

    [Fact]
    public void ProjectsLiveNbnChoicesAndTaoSwapFromTypedKeys()
    {
        CardViewModel first = Card("card:ice-a", "First ICE", rezzed: false);
        CardViewModel second = Card("card:ice-b", "Second ICE", rezzed: true);
        ServerViewModel server = new(
            "server:remote",
            ServerKind.Remote,
            [first, second],
            Array.Empty<CardViewModel?>())
        {
            IcePositions =
            [
                new IcePositionViewModel(first.Id, "server:remote", 0),
                new IcePositionViewModel(second.Id, "server:remote", 1),
            ],
        };
        LegalActionViewModel gain = BehaviorAction(
            "action:gain-two",
            new BehaviorChoiceParameters("reality-plus-benefit", ["gain-two"]));
        LegalActionViewModel draw = BehaviorAction(
            "action:draw-two",
            new BehaviorChoiceParameters("reality-plus-benefit", ["draw-two"]));
        LegalActionViewModel swap = BehaviorAction(
            "action:tao-ice",
            new BehaviorChoiceParameters(
                "tao-ice",
                cardIds: [new CardInstanceId(first.Id), new CardInstanceId(second.Id)]));
        MatchViewModel view = BaseView([server], [gain, draw, swap]) with
        {
            Decision = new DecisionViewModel(
                "decision:choice",
                1,
                new PlayerId("player:corp"),
                PlayerSide.Corp,
                DecisionKind.BehaviorChoice,
                "Choose a typed option."),
        };

        IdentityChoiceViewModel projected = IdentityChoicePresenter.Present(view);

        Assert.True(projected.HasNbnChoice);
        Assert.Contains(projected.Options, option => option.IsGainTwoChoice
            && option.PresentationLabel == "GAIN 2 CREDITS");
        Assert.Contains(projected.Options, option => option.IsDrawTwoChoice
            && option.PresentationLabel == "DRAW 2 CARDS");
        IceSwapPreviewViewModel preview = Assert.Single(
            projected.Options,
            option => option.SwapPreview is not null).SwapPreview!;
        Assert.Equal(first.Id, preview.FirstBefore.CardId);
        Assert.Equal(second.Id, preview.FirstAfter.CardId);
        Assert.Equal(first.Id, preview.SecondAfter.CardId);
        Assert.Equal(0, preview.FirstBefore.Index);
        Assert.Equal(1, preview.SecondBefore.Index);
    }

    [Fact]
    public void EventFeedUsesNonColourSwapAndDerezCuesAndTerminalOutcomeWins()
    {
        IceSwappedEventPayload swap = new(
            new CardInstanceId("card:ice-a"),
            new ZoneId("zone:a"),
            0,
            new CardInstanceId("card:ice-b"),
            new ZoneId("zone:b"),
            1);
        Assert.Contains("ICE SWAP PREVIEW", EventFeedRegionPresenter.Describe(new EngineEvent(
            1,
            EngineEventKinds.IceSwapped,
            swap)), StringComparison.Ordinal);
        Assert.Contains("DEREZZED", EventFeedRegionPresenter.Describe(new EngineEvent(
            2,
            EngineEventKinds.CardDerezzed,
            new CardDerezzedEventPayload(new CardInstanceId("card:ice-a")))), StringComparison.Ordinal);

        MatchViewModel officialView = BaseView() with
        {
            CorpIdentity = Card("identity:corp", "Official Corp identity", rezzed: true),
        };
        MatchSnapshot snapshot = Snapshot(
            officialView,
            new RunOutcomeViewModel("run:old", PlayerSide.Runner, RunEndReason.BreachCompleted, true));
        Assert.Equal(FocusRegionMode.Empty, FocusRegionPresenter.Present(snapshot).Mode);

        MatchViewModel terminalView = officialView with
        {
            Outcome = new OutcomeViewModel(GameOutcomeKind.Won, PlayerSide.Runner, GameWinReason.AgendaPoints),
        };
        FocusRegionState terminal = FocusRegionPresenter.Present(Snapshot(terminalView));
        Assert.Equal(FocusRegionMode.Outcome, terminal.Mode);
        Assert.True(terminal.GameOutcome!.IsTerminal);
    }

    private static LegalActionViewModel BehaviorAction(
        string actionId,
        BehaviorChoiceParameters parameters) =>
        new(new ActionId(actionId), ActionKind.ChooseBehavior, "opaque description", parameters);

    private static CardViewModel Card(string id, string title, bool rezzed) => new(
        id,
        $"fixture:{id}",
        title,
        "ICE",
        IsKnown: true,
        Rezzed: rezzed,
        AdvancementCounters: 0,
        Cost: null,
        Strength: 3,
        AgendaPoints: null,
        TrashCost: null,
        Subtypes: ["Barrier"],
        RulesText: "");

    private static MatchViewModel BaseView(
        IReadOnlyList<ServerViewModel>? servers = null,
        IReadOnlyList<LegalActionViewModel>? actions = null) => new(
        PlayerSide.Runner,
        new PlayerViewModel("player:corp", PlayerSide.Corp, 5, 3, 0),
        new PlayerViewModel("player:runner", PlayerSide.Runner, 5, 4, 0),
        servers ?? Array.Empty<ServerViewModel>(),
        new ZoneViewModel(ZoneKind.CorpHand, 0, Array.Empty<CardViewModel>()),
        new ZoneViewModel(ZoneKind.CorpDeck, 0, Array.Empty<CardViewModel>()),
        new ZoneViewModel(ZoneKind.RunnerGrip, 0, Array.Empty<CardViewModel>()),
        new ZoneViewModel(ZoneKind.RunnerStack, 0, Array.Empty<CardViewModel>()),
        Array.Empty<CardViewModel>(),
        Array.Empty<CardViewModel>(),
        Array.Empty<CardViewModel>(),
        null,
        null,
        new OutcomeViewModel(GameOutcomeKind.Ongoing, null, null),
        actions ?? Array.Empty<LegalActionViewModel>());

    private static MatchSnapshot Snapshot(
        MatchViewModel view,
        RunOutcomeViewModel? runOutcome = null) => new(
        1,
        new Observation(
            view.Viewer,
            view.Decision?.Id ?? string.Empty,
            Array.Empty<LegalAction>(),
            new Dictionary<string, object?>(),
            new Dictionary<string, object?>()),
        view,
        Array.Empty<EngineEvent>(),
        null,
        false,
        runOutcome);
}

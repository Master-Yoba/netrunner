using System.Reflection;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class MatchHistoryVisibilityTests
{
    [Fact]
    public void HiddenDiscardAndDrawHistoryNeverExposeCardIds()
    {
        GameState state = RepresentativeState();
        ZoneState corpDeck = state.GetZone(ZoneKind.CorpDeck);
        ZoneState corpHand = state.GetZone(ZoneKind.CorpHand);
        ZoneState archives = state.GetZone(ZoneKind.CorpDiscard);
        MatchViewModel view = EmptyView();
        var hiddenDiscard = new CardInstanceId("card:hidden-discard");
        var hiddenDraw = new CardInstanceId("card:hidden-draw");
        EngineEvent moved = new(
            1,
            EngineEventKinds.CardMoved,
            new CardMovedEventPayload(
                hiddenDiscard,
                corpHand.Id,
                archives.Id,
                ZoneKind.CorpHand,
                ZoneKind.CorpDiscard));
        EngineEvent drawn = new(
            2,
            EngineEventKinds.CardDrawn,
            new CardDrawnEventPayload(PlayerSide.Corp, hiddenDraw, corpDeck.Id, corpHand.Id));

        MatchHistoryEntry entry = Project(
            NewProjector(),
            view,
            view,
            state,
            [moved, drawn],
            PlayerSide.Corp,
            Action(ActionKind.DrawCard)).Single();

        string text = Flatten(entry);
        Assert.Contains("a face-down card", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("draws a card", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(hiddenDiscard.Value, text, StringComparison.Ordinal);
        Assert.DoesNotContain(hiddenDraw.Value, text, StringComparison.Ordinal);
        Assert.Empty(entry.Cards);
    }

    [Fact]
    public void PublicDiscardAccessTrashAndShuffleDetailsRedactHiddenIdentityAndCounts()
    {
        GameState state = RepresentativeState();
        ZoneState corpDeck = state.GetZone(ZoneKind.CorpDeck);
        var hiddenDiscard = new CardInstanceId("card:hidden-forced-discard");
        var hiddenAccess = new CardInstanceId("card:hidden-access-trash");
        EngineEvent[] events =
        [
            new EngineEvent(
                1,
                EngineEventKinds.TurnCardDiscarded,
                new TurnCardDiscardedEventPayload(
                    PlayerSide.Corp,
                    1,
                    hiddenDiscard,
                    5,
                    5)),
            new EngineEvent(
                2,
                EngineEventKinds.AccessedCardTrashed,
                new AccessedCardTrashedEventPayload(
                    new RunId("run:hidden-access-trash"),
                    hiddenAccess,
                    2)),
            new EngineEvent(
                3,
                EngineEventKinds.BehaviorCardsShuffled,
                new BehaviorCardsShuffledEventPayload(corpDeck.Id, 9)),
        ];

        MatchHistoryEntry entry = Project(
            NewProjector(),
            EmptyView(),
            EmptyView(),
            state,
            events,
            PlayerSide.Corp,
            Action(ActionKind.ChooseBehavior)).Single();

        string text = Flatten(entry);
        Assert.Contains("Corp discards a face-down card.", text, StringComparison.Ordinal);
        Assert.Contains("Runner trashes a face-down card from access, paying 2 credits.", text, StringComparison.Ordinal);
        Assert.Contains("Corp shuffles R&D.", text, StringComparison.Ordinal);
        Assert.DoesNotContain(hiddenDiscard.Value, text, StringComparison.Ordinal);
        Assert.DoesNotContain(hiddenAccess.Value, text, StringComparison.Ordinal);
        Assert.DoesNotContain("9 card", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AutomaticCorpRezIsAttributedToCorpRatherThanSubmittedRunnerAction()
    {
        GameState state = RepresentativeState();
        ServerState remote = state.Servers.Single(server => server.Kind == ServerKind.Remote);
        CardViewModel ice = VisibleCard("card:automatic-corp-rez", "Automatic Corp ICE") with { Type = "ICE" };
        MatchHistoryEntry entry = Project(
            NewProjector(),
            View(remote.Id.Value, ice),
            View(remote.Id.Value, ice),
            state,
            [new EngineEvent(
                1,
                EngineEventKinds.CardRezzed,
                new CardRezzedEventPayload(new CardInstanceId(ice.Id), 0))],
            PlayerSide.Runner,
            Action(ActionKind.StartRun)).Single();

        Assert.Contains("Corp rezzes Automatic Corp ICE", entry.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Runner rezzes", entry.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void AutomaticIceRezIsAttributedToCorpRatherThanSubmittedRunnerAction()
    {
        GameState state = RepresentativeState();
        ServerState remote = state.Servers.Single(server => server.Kind == ServerKind.Remote);
        CardViewModel ice = VisibleCard("card:automatic-ice-rez", "Automatic ICE") with { Type = "ICE" };
        MatchHistoryEntry entry = Project(
            NewProjector(),
            View(remote.Id.Value, ice),
            View(remote.Id.Value, ice),
            state,
            [new EngineEvent(
                1,
                EngineEventKinds.IceRezzed,
                new IceRezzedEventPayload(new RunId("run:automatic-rez"), new CardInstanceId(ice.Id)))],
            PlayerSide.Runner,
            Action(ActionKind.StartRun)).Single();

        Assert.Contains("Corp rezzes Automatic ICE", entry.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Runner rezzes", entry.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingMovementDestinationUsesProvenanceAndFailsClosed()
    {
        GameState state = RepresentativeState();
        ServerState remote = state.Servers.Single(server => server.Kind == ServerKind.Remote);
        ZoneState root = state.GetZone(remote.RootZoneId);
        CardViewModel card = VisibleCard("card:missing-destination", "Private Destination Card");
        ZoneId missingDestination = new("zone:transient-behavior-set-aside");
        EngineEvent moved = new(
            1,
            EngineEventKinds.CardMoved,
            new CardMovedEventPayload(
                new CardInstanceId(card.Id),
                root.Id,
                missingDestination,
                ZoneKind.ServerRoot,
                ZoneKind.CorpDeck));

        MatchHistoryEntry entry = Project(
            NewProjector(),
            View(remote.Id.Value, card),
            EmptyView(remote.Id.Value),
            state,
            [moved],
            PlayerSide.Corp,
            Action(ActionKind.ChooseBehavior)).Single();

        string text = Flatten(entry);
        Assert.DoesNotContain(card.Title, text, StringComparison.Ordinal);
        Assert.DoesNotContain(card.Id, text, StringComparison.Ordinal);
        Assert.Empty(entry.Cards);
    }

    [Fact]
    public void RevealedIdentityIsNotTrackedAfterPrivateShuffle()
    {
        GameState state = RepresentativeState();
        ServerState remote = state.Servers.Single(server => server.Kind == ServerKind.Remote);
        ZoneState root = state.GetZone(remote.RootZoneId);
        ZoneState corpDeck = state.GetZone(ZoneKind.CorpDeck);
        CardViewModel revealedCard = VisibleCard("card:revealed-then-hidden", "Revealed Agenda");
        MatchViewModel beforeReveal = View(remote.Id.Value, revealedCard);
        MatchViewModel afterReveal = View(remote.Id.Value, revealedCard);
        object projector = NewProjector();

        MatchHistoryEntry reveal = Project(
            projector,
            beforeReveal,
            afterReveal,
            state,
            [new EngineEvent(1, EngineEventKinds.CardRezzed, new CardRezzedEventPayload(
                new CardInstanceId(revealedCard.Id), 2))],
            PlayerSide.Corp,
            Action(ActionKind.RezCard)).Single();

        MatchViewModel privateBefore = View(remote.Id.Value, revealedCard);
        MatchViewModel privateAfter = EmptyView();
        var movedToPrivate = new CardMovedEventPayload(
            new CardInstanceId(revealedCard.Id),
            root.Id,
            corpDeck.Id,
            ZoneKind.ServerRoot,
            ZoneKind.CorpDeck);
        var rawReveal = new BehaviorCardRevealedEventPayload(
            new CardInstanceId(revealedCard.Id),
            new CardDefinitionId("secret-definition-after-shuffle"),
            corpDeck.Id,
            PlayerSide.Corp);
        MatchHistoryEntry privateMove = Project(
            projector,
            privateBefore,
            privateAfter,
            state,
            [
                new EngineEvent(2, EngineEventKinds.CardMoved, movedToPrivate),
                new EngineEvent(3, EngineEventKinds.BehaviorCardRevealed, rawReveal),
                new EngineEvent(
                    4,
                    EngineEventKinds.BehaviorCardsShuffled,
                    new BehaviorCardsShuffledEventPayload(corpDeck.Id, 1)),
            ],
            PlayerSide.Corp,
            Action(ActionKind.ChooseBehavior)).Single();

        Assert.Contains(revealedCard.Title, Flatten(reveal), StringComparison.Ordinal);
        Assert.DoesNotContain(revealedCard.Title, Flatten(privateMove), StringComparison.Ordinal);
        Assert.DoesNotContain(revealedCard.Id, Flatten(privateMove), StringComparison.Ordinal);
        Assert.DoesNotContain("secret-definition-after-shuffle", Flatten(privateMove), StringComparison.Ordinal);
        Assert.Empty(privateMove.Cards);
    }

    [Fact]
    public void KnownCardMovedThroughHiddenDeckCannotBeNamedWhenDiscardedInSameBatch()
    {
        GameState state = RepresentativeState();
        ServerState remote = state.Servers.Single(server => server.Kind == ServerKind.Remote);
        ZoneState root = state.GetZone(remote.RootZoneId);
        ZoneState corpDeck = state.GetZone(ZoneKind.CorpDeck);
        ZoneState archives = state.GetZone(ZoneKind.CorpDiscard);
        CardViewModel card = VisibleCard("card:retired-knowledge", "Retired Hidden Agenda");
        MatchViewModel before = View(remote.Id.Value, card);
        MatchViewModel after = EmptyView(remote.Id.Value);

        MatchHistoryEntry entry = Project(
            NewProjector(),
            before,
            after,
            state,
            [
                new EngineEvent(
                    1,
                    EngineEventKinds.CardMoved,
                    new CardMovedEventPayload(
                        new CardInstanceId(card.Id),
                        root.Id,
                        corpDeck.Id,
                        ZoneKind.ServerRoot,
                        ZoneKind.CorpDeck)),
                new EngineEvent(
                    2,
                    EngineEventKinds.CardMoved,
                    new CardMovedEventPayload(
                        new CardInstanceId(card.Id),
                        corpDeck.Id,
                        archives.Id,
                        ZoneKind.CorpDeck,
                        ZoneKind.CorpDiscard)),
            ],
            PlayerSide.Corp,
            Action(ActionKind.ChooseBehavior)).Single();

        string text = Flatten(entry);
        Assert.Contains("a face-down card → Archives", text, StringComparison.Ordinal);
        Assert.DoesNotContain(card.Title, text, StringComparison.Ordinal);
        Assert.DoesNotContain(card.Id, text, StringComparison.Ordinal);
        Assert.Empty(entry.Cards);
    }

    [Fact]
    public void HistoricalKnownCardsScrubPhysicalCueReferences()
    {
        GameState state = RepresentativeState();
        ServerState remote = state.Servers.Single(server => server.Kind == ServerKind.Remote);
        CardViewModel card = VisibleCard("card:historical-cue", "Historical Cue Card") with
        {
            HostCardId = "card:private-host",
            AbilityCue = AbilityCueLabels.Used,
            FirstXUses = [new FirstXUseViewModel("card:private-source", "once-per-turn", 1)],
        };
        MatchHistoryEntry entry = Project(
            NewProjector(),
            View(remote.Id.Value, card),
            View(remote.Id.Value, card),
            state,
            [new EngineEvent(
                1,
                EngineEventKinds.CardRezzed,
                new CardRezzedEventPayload(new CardInstanceId(card.Id), 2))],
            PlayerSide.Corp,
            Action(ActionKind.RezCard)).Single();

        CardViewModel historical = Assert.Single(entry.Cards);
        Assert.Equal($"history-{state.ActionSequence}-0", historical.Id);
        Assert.Null(historical.HostCardId);
        Assert.Equal(historical.Id, Assert.Single(historical.FirstXUses).SourceCardId);
        Assert.Equal(AbilityCueLabels.Used, historical.AbilityCue);
        string serialized = EngineJson.Serialize(entry);
        Assert.DoesNotContain("card:private-host", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("card:private-source", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void LaterRevealDoesNotRewriteEarlierFaceDownInstallation()
    {
        GameState state = RepresentativeState();
        ServerState remote = state.Servers.Single(server => server.Kind == ServerKind.Remote);
        ZoneState corpHand = state.GetZone(ZoneKind.CorpHand);
        ZoneState root = state.GetZone(remote.RootZoneId);
        CardViewModel revealedCard = VisibleCard("card:later-reveal", "Later Revealed Asset");
        MatchViewModel hiddenView = EmptyView(remote.Id.Value);
        object projector = NewProjector();

        MatchHistoryEntry hiddenInstall = Project(
            projector,
            hiddenView,
            hiddenView,
            state,
            [
                new EngineEvent(
                    1,
                    EngineEventKinds.CardInstalled,
                    new CardInstalledEventPayload(
                        PlayerSide.Corp,
                        new CardInstanceId(revealedCard.Id),
                        corpHand.Id,
                        root.Id,
                        2)),
            ],
            PlayerSide.Corp,
            Action(ActionKind.InstallCard)).Single();

        MatchHistoryEntry laterReveal = Project(
            projector,
            hiddenView,
            View(remote.Id.Value, revealedCard),
            state,
            [
                new EngineEvent(
                    2,
                    EngineEventKinds.CardRezzed,
                    new CardRezzedEventPayload(new CardInstanceId(revealedCard.Id), 2)),
            ],
            PlayerSide.Corp,
            Action(ActionKind.RezCard)).Single();

        Assert.Contains("face-down card", hiddenInstall.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(revealedCard.Title, Flatten(hiddenInstall), StringComparison.Ordinal);
        Assert.DoesNotContain(revealedCard.Id, Flatten(hiddenInstall), StringComparison.Ordinal);
        Assert.Empty(hiddenInstall.Cards);
        Assert.Contains(revealedCard.Title, Flatten(laterReveal), StringComparison.Ordinal);
    }

    private static GameState RepresentativeState() =>
        M2ScenarioSetup.Build(M2ScenarioProvider.LoadRepresentative());

    private static MatchViewModel EmptyView(string? serverId = null) => new(
        PlayerSide.Runner,
        new PlayerViewModel("player:corp", PlayerSide.Corp, 5, 3, 0),
        new PlayerViewModel("player:runner", PlayerSide.Runner, 5, 4, 0),
        serverId is null
            ? Array.Empty<ServerViewModel>()
            : [new ServerViewModel(serverId, ServerKind.Remote, Array.Empty<CardViewModel?>(), [null])],
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
        Array.Empty<LegalActionViewModel>())
    {
        Turn = new TurnViewModel(true, PlayerSide.Corp, TurnPhase.Corp, TurnStep.CorpAction, 1, null),
    };

    private static MatchViewModel View(string serverId, CardViewModel rootCard) => new(
        PlayerSide.Runner,
        new PlayerViewModel("player:corp", PlayerSide.Corp, 5, 3, 0),
        new PlayerViewModel("player:runner", PlayerSide.Runner, 5, 4, 0),
        [new ServerViewModel(serverId, ServerKind.Remote, Array.Empty<CardViewModel?>(), [rootCard])],
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
        Array.Empty<LegalActionViewModel>())
    {
        Turn = new TurnViewModel(true, PlayerSide.Corp, TurnPhase.Corp, TurnStep.CorpAction, 1, null),
    };

    private static CardViewModel VisibleCard(string id, string title) => new(
        id,
        $"fixture:{id}",
        title,
        "Asset",
        IsKnown: true,
        Rezzed: true,
        AdvancementCounters: 0,
        Cost: 2,
        Strength: null,
        AgendaPoints: null,
        TrashCost: 1,
        Subtypes: ["Asset"],
        RulesText: "A public test card.",
        Faction: "neutral-corp");

    private static object NewProjector() => Activator.CreateInstance(
        ProjectorType,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        args: null,
        culture: null)!;

    private static IReadOnlyList<MatchHistoryEntry> Project(
        object projector,
        MatchViewModel before,
        MatchViewModel after,
        GameState state,
        IReadOnlyList<EngineEvent> events,
        PlayerSide actor,
        LegalAction action)
    {
        object result = ProjectMethod.Invoke(projector, [before, after, state, events, actor, action])!;
        return Assert.IsAssignableFrom<IReadOnlyList<MatchHistoryEntry>>(result);
    }

    private static LegalAction Action(ActionKind kind) => new(
        new ActionId($"test:{kind}"),
        kind,
        kind.ToString(),
        new NoActionParameters());

    private static string Flatten(MatchHistoryEntry entry) => string.Join(
        "\n",
        new[] { entry.Summary }
            .Concat(entry.Details)
            .Concat(entry.Cards.SelectMany(card => new[]
            {
                card.Id,
                card.DefinitionId ?? string.Empty,
                card.Title,
            })));

    private static readonly Type ProjectorType = typeof(MatchHistoryEntry).Assembly.GetType(
        "Netrunner.Match.MatchHistoryProjector",
        throwOnError: true)!;

    private static readonly MethodInfo ProjectMethod = ProjectorType.GetMethod(
        "Project",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("MatchHistoryProjector.Project was not found.");
}

using System.Reflection;
using Netrunner.AI;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class BoardInteractionProjectionTests
{
    [Theory]
    [InlineData(PlayerSide.Runner)]
    [InlineData(PlayerSide.Corp)]
    public void DiscardProjectionTracksEngineSelectionsAcrossSaveRestore(PlayerSide side)
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer(47042);
        GameState state = GameEngine.CreateMatch(definition.Request.Seed, definition.Request.CorpDeck,
            definition.Request.RunnerDeck, definition.Request.CardDataVersion, definition.Behavior, definition.Runtime).State;
        for (int step = 0; step < 128; step++)
        {
            PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
            if (decision.Kind == DecisionKind.TurnDiscard && state.GetPlayer(decision.Owner).Side == side) break;
            LegalAction action = decision.LegalActions.FirstOrDefault(candidate => candidate.Kind == ActionKind.DrawCard)
                ?? decision.LegalActions.FirstOrDefault(candidate => candidate.Kind == ActionKind.PassPriority)
                ?? decision.LegalActions[0];
            state = Apply(state, action.Id).State;
        }

        Assert.Equal(DecisionKind.TurnDiscard, state.PendingDecision!.Kind);
        Assert.Equal(side, state.GetPlayer(state.PendingDecision.Owner).Side);
        MatchViewModel initial = Project(state);
        int count = Hand(initial).Count;
        int maximum = Assert.IsType<int>(initial.ForcedDiscard!.Hand.MaximumHandSize);
        Assert.True(count > maximum);
        // The reduced Corp deck has six cards; the Runner fixture exercises batching.
        if (side == PlayerSide.Runner) Assert.True(count - maximum > 1);
        var selected = new List<string>();
        while (state.PendingDecision?.Kind == DecisionKind.TurnDiscard)
        {
            // Selection must be derivable after a resume, without UI gesture history.
            state = EngineJson.DeserializeState(EngineJson.Serialize(state), definition.Behavior, definition.Runtime);
            MatchViewModel view = Project(state);
            Assert.Equal(count, Hand(view).Count);
            Assert.Equal(selected.Order(), BoardInteractionProjector.SelectedDiscards(view).Select(card => card.Id).Order());
            BoardInteractionViewModel drag = BoardInteractionProjector.Project(view);
            Assert.Equal(count - selected.Count, drag.Sources.Count);
            BoardInteractionSource source = drag.Sources[0];
            BoardInteractionDestination target = Assert.Single(source.Destinations);
            Assert.Equal(side == PlayerSide.Corp ? BoardInteractionDestinationKind.CorpArchives : BoardInteractionDestinationKind.RunnerHeap, target.Kind);
            LegalActionViewModel dropped = Assert.Single(target.Actions);
            LegalAction menu = state.PendingDecision!.LegalActions.Single(action =>
                action.Parameters is CardChoiceParameters choice && choice.CardId.Value == source.CardId);
            ApplyResult fromMenu = Apply(state, menu.Id);
            ApplyResult fromDrop = Apply(state, dropped.Id);
            Assert.Equal(EngineJson.Serialize(fromMenu.State), EngineJson.Serialize(fromDrop.State));
            Assert.Equal(EngineJson.Serialize(fromMenu.Events), EngineJson.Serialize(fromDrop.Events));
            selected.Add(source.CardId);
            state = fromDrop.State;
        }
        MatchViewModel complete = Project(state);
        Assert.Equal(maximum, Hand(complete).Count);
        Assert.Empty(BoardInteractionProjector.SelectedDiscards(complete));
        Assert.All(selected, id => Assert.Contains(side == PlayerSide.Corp ? complete.CorpDiscard : complete.RunnerHeap, card => card.Id == id));

        MatchViewModel Project(GameState current) => ObservationViewMapper.Build(
            ObservationBuilder.Build(current, side, definition.Behavior), definition.Metadata, definition.Behavior);
        ZoneViewModel Hand(MatchViewModel view) => side == PlayerSide.Corp ? view.CorpHand : view.RunnerHand;
        ApplyResult Apply(GameState current, ActionId id)
        {
            PendingDecision decision = current.PendingDecision!;
            ApplyResult result = GameEngine.Apply(current, new ActionSubmission(decision.Owner, decision.Id, decision.Revision, id),
                definition.Behavior, definition.Runtime);
            Assert.True(result.Accepted, result.Rejection?.Message);
            return result;
        }
    }

    [Theory]
    [InlineData(PlayerSide.Runner, BoardInteractionDestinationKind.RunnerHeap)]
    [InlineData(PlayerSide.Corp, BoardInteractionDestinationKind.CorpArchives)]
    public void DiscardDropsUseOnlyIssuedLocalHandCandidates(PlayerSide side, BoardInteractionDestinationKind pile)
    {
        CardViewModel eligible = Card("eligible", "Anything", true);
        CardViewModel selected = Card("selected", "Anything", true);
        CardViewModel hidden = Card("hidden", "Anything", false);
        LegalActionViewModel choose = new(new("discard-issued"), ActionKind.ChooseCard, "ignored", new CardChoiceParameters(new(eligible.Id)));
        LegalActionViewModel notInHand = choose with { Id = new("outside"), Parameters = new CardChoiceParameters(new("outside-hand")) };
        LegalActionViewModel concealed = choose with { Id = new("concealed"), Parameters = new CardChoiceParameters(new(hidden.Id)) };
        MatchViewModel view = View(side, [eligible, selected, hidden], [], [choose, notInHand, concealed], decisionKind: DecisionKind.TurnDiscard);

        BoardInteractionSource source = Assert.Single(BoardInteractionProjector.Project(view).Sources);
        Assert.Equal(eligible.Id, source.CardId);
        BoardInteractionDestination destination = Assert.Single(source.Destinations);
        Assert.Equal(pile, destination.Kind);
        Assert.Same(choose, Assert.Single(destination.Actions));
        Assert.Same(selected, Assert.Single(BoardInteractionProjector.SelectedDiscards(view)));
        foreach (DecisionKind other in new[] { DecisionKind.ChooseCard, DecisionKind.MemoryCleanup, DecisionKind.BehaviorChoice, DecisionKind.TurnAction })
        {
            MatchViewModel unrelated = view with { Decision = view.Decision! with { Kind = other } };
            Assert.Empty(BoardInteractionProjector.Project(unrelated).Sources);
            Assert.Empty(BoardInteractionProjector.SelectedDiscards(unrelated));
        }
        MatchViewModel opponent = view with { Decision = view.Decision! with { OwnerSide = side == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp } };
        Assert.Empty(BoardInteractionProjector.Project(opponent).Sources);
        Assert.Empty(BoardInteractionProjector.SelectedDiscards(opponent));
        MatchViewModel noChoices = view with { LegalActions = [] };
        Assert.Empty(BoardInteractionProjector.Project(noChoices).Sources);
        Assert.Empty(BoardInteractionProjector.SelectedDiscards(noChoices));
    }

    [Fact]
    public void ProjectsTypedPlayAndInstallDestinationsWithoutInspectingSubtypes()
    {
        CardViewModel general = Card("card:general", type: "Unknown", isKnown: true);
        CardViewModel run = Card("card:run", type: "NotAnEvent", isKnown: true);
        CardViewModel rig = Card("card:program", type: "Arbitrary", isKnown: true);
        LegalActionViewModel generalAction = Play("action:general", general.Id);
        LegalActionViewModel runAction = Play(
            "action:run",
            run.Id,
            new ServerId("server:remote"));
        LegalActionViewModel secondRunAction = Play(
            "action:run:alternate",
            run.Id,
            new ServerId("server:remote"));
        LegalActionViewModel rigAction = Install(
            "action:rig",
            rig.Id,
            InstallDestinationKind.RunnerRig);
        LegalActionViewModel hostAction = Install(
            "action:host",
            rig.Id,
            InstallDestinationKind.RunnerRig,
            hostCardId: new CardInstanceId("card:host"));

        MatchViewModel view = View(
            PlayerSide.Runner,
            [general, run, rig],
            [rig with { Id = "card:host" }],
            [generalAction, runAction, secondRunAction, rigAction, hostAction]);

        BoardInteractionViewModel projected = BoardInteractionProjector.Project(view);

        BoardInteractionSource generalSource = Assert.Single(
            projected.Sources,
            source => source.CardId == general.Id);
        BoardInteractionDestination generalDestination = Assert.Single(generalSource.Destinations);
        Assert.Equal(BoardInteractionDestinationKind.GeneralPlay, generalDestination.Kind);
        Assert.Null(generalDestination.TargetId);
        Assert.Same(generalAction, Assert.Single(generalDestination.Actions));

        BoardInteractionSource runSource = Assert.Single(
            projected.Sources,
            source => source.CardId == run.Id);
        Assert.Equal(
            [
                BoardInteractionDestinationKind.GeneralPlay,
                BoardInteractionDestinationKind.ServerTarget,
            ],
            runSource.Destinations.Select(destination => destination.Kind));
        BoardInteractionDestination runDestination = runSource.Destinations[1];
        Assert.Equal("server:remote", runDestination.TargetId);
        Assert.Equal(
            [runAction.Id, secondRunAction.Id],
            runDestination.Actions.Select(action => action.Id));

        BoardInteractionSource installSource = Assert.Single(
            projected.Sources,
            source => source.CardId == rig.Id);
        Assert.Equal(
            [BoardInteractionDestinationKind.RunnerRig, BoardInteractionDestinationKind.HostCard],
            installSource.Destinations.Select(destination => destination.Kind));
        Assert.Equal("card:host", installSource.Destinations[1].TargetId);
    }

    [Fact]
    public void ProjectsCorpServerRootsIceAndNewRemoteFromTypedInstallPayloads()
    {
        CardViewModel root = Card("card:root", type: "Anything", isKnown: true);
        CardViewModel ice = Card("card:ice", type: "Anything", isKnown: true);
        CardViewModel remoteRoot = Card("card:remote-root", type: "Anything", isKnown: true);
        CardViewModel newRemoteIce = Card("card:new-remote-ice", type: "Anything", isKnown: true);
        LegalActionViewModel rootAction = Install(
            "action:root",
            root.Id,
            InstallDestinationKind.ServerRoot,
            serverId: new ServerId("server:headquarters"));
        LegalActionViewModel iceAction = Install(
            "action:ice",
            ice.Id,
            InstallDestinationKind.ServerIce,
            serverId: new ServerId("server:remote"));
        LegalActionViewModel remoteRootAction = Install(
            "action:remote-root",
            remoteRoot.Id,
            InstallDestinationKind.ServerRoot,
            createNewRemote: true);
        LegalActionViewModel newRemoteIceAction = Install(
            "action:new-remote-ice",
            newRemoteIce.Id,
            InstallDestinationKind.ServerIce,
            createNewRemote: true);

        MatchViewModel view = View(
            PlayerSide.Corp,
            [root, ice, remoteRoot, newRemoteIce],
            [],
            [rootAction, iceAction, remoteRootAction, newRemoteIceAction]);

        BoardInteractionViewModel projected = BoardInteractionProjector.Project(view);

        AssertDestination(projected, root.Id, BoardInteractionDestinationKind.ServerRoot, "server:headquarters");
        AssertDestination(projected, ice.Id, BoardInteractionDestinationKind.ServerIce, "server:remote");
        AssertDestination(projected, remoteRoot.Id, BoardInteractionDestinationKind.NewRemote, null);
        AssertDestination(projected, newRemoteIce.Id, BoardInteractionDestinationKind.NewRemote, null);
    }

    [Fact]
    public void DoesNotOfferHiddenHandOrHostTargetsAndDoesNotDependOnCardSubtype()
    {
        CardViewModel known = Card("card:known", type: "", isKnown: true);
        CardViewModel hidden = Card("card:hidden", type: "Program", isKnown: false);
        LegalActionViewModel knownAction = Play("action:known", known.Id);
        LegalActionViewModel hiddenAction = Play("action:hidden", hidden.Id);
        LegalActionViewModel hiddenHostAction = Install(
            "action:hidden-host",
            known.Id,
            InstallDestinationKind.RunnerRig,
            hostCardId: new CardInstanceId(hidden.Id));

        MatchViewModel view = View(
            PlayerSide.Runner,
            [known],
            [],
            [knownAction, hiddenAction, hiddenHostAction]);

        BoardInteractionViewModel projected = BoardInteractionProjector.Project(view);

        BoardInteractionSource source = Assert.Single(projected.Sources);
        Assert.Equal(known.Id, source.CardId);
        Assert.Same(knownAction, Assert.Single(source.Destinations).Actions.Single());
        Assert.DoesNotContain(
            source.Destinations,
            destination => destination.TargetId == hidden.Id);
    }

    [Fact]
    public void ExposedFaceDownHostRemainsAValidPositionalDestination()
    {
        CardViewModel incoming = Card("card:incoming", type: "Unknown", isKnown: true);
        CardViewModel faceDownHost = Card("card:face-down-host", type: "Unknown", isKnown: false);
        LegalActionViewModel install = Install(
            "action:host-face-down",
            incoming.Id,
            InstallDestinationKind.RunnerRig,
            hostCardId: new CardInstanceId(faceDownHost.Id));

        MatchViewModel view = View(
            PlayerSide.Runner,
            [incoming],
            [faceDownHost],
            [install]);

        BoardInteractionDestination destination = Assert.Single(
            Assert.Single(BoardInteractionProjector.Project(view).Sources).Destinations);
        Assert.Equal(BoardInteractionDestinationKind.HostCard, destination.Kind);
        Assert.Equal(faceDownHost.Id, destination.TargetId);
    }

    [Fact]
    public void ExposedIcePositionRemainsAValidHostWhenTheIceSlotIsRedacted()
    {
        CardViewModel incoming = Card("card:incoming", type: "Unknown", isKnown: true);
        const string hiddenIceId = "card:hidden-ice";
        ServerViewModel server = new(
            "server:remote",
            ServerKind.Remote,
            [null],
            Array.Empty<CardViewModel?>())
        {
            IcePositions =
            [
                new IcePositionViewModel(hiddenIceId, "server:remote", 0),
            ],
        };
        LegalActionViewModel install = Install(
            "action:hidden-ice-host",
            incoming.Id,
            InstallDestinationKind.RunnerRig,
            hostCardId: new CardInstanceId(hiddenIceId));

        MatchViewModel view = View(
            PlayerSide.Runner,
            [incoming],
            [],
            [install]) with
        {
            Servers = [server],
        };

        BoardInteractionDestination destination = Assert.Single(
            Assert.Single(BoardInteractionProjector.Project(view).Sources).Destinations);
        Assert.Equal(BoardInteractionDestinationKind.HostCard, destination.Kind);
        Assert.Equal(hiddenIceId, destination.TargetId);
    }

    [Fact]
    public void DoesNotExposeHandDragSourcesDuringAnActiveRun()
    {
        CardViewModel card = Card("card:card", type: "Unknown", isKnown: true);
        MatchViewModel view = View(
            PlayerSide.Runner,
            [card],
            [],
            [Play("action:card", card.Id)]) with
        {
            Run = new RunFocusViewModel(
                "run:active",
                "server:remote",
                PlayerSide.Runner,
                RunCrPhase.Approach,
                RunTimingStep.Approach,
                -1,
                null,
                false,
                false,
                null,
                null,
                null,
                new AccessViewModel(
                    Array.Empty<CardViewModel?>(),
                    0,
                    null,
                    Array.Empty<CardViewModel?>(),
                    Array.Empty<CardViewModel?>(),
                    RunAccessState.NotStarted),
                0,
                null),
        };

        BoardInteractionViewModel projected = BoardInteractionProjector.Project(view);

        Assert.Empty(projected.Sources);
        Assert.Empty(projected.Choices);
        Assert.Null(BoardInteractionProjector.TargetsForAction(view, view.LegalActions[0]));
    }

    [Fact]
    public void FollowupTargetsFilterHiddenCardsAndPreserveTypedOptions()
    {
        CardViewModel known = Card("card:known", type: "NoSubtype", isKnown: true);
        CardViewModel hidden = Card("card:hidden", type: "NoSubtype", isKnown: false);
        LegalActionViewModel cardChoice = new(
            new ActionId("action:card-choice"),
            ActionKind.ChooseCard,
            "ignored",
            new CardChoiceParameters(new CardInstanceId(known.Id)));
        LegalActionViewModel behaviorChoice = new(
            new ActionId("action:behavior-choice"),
            ActionKind.ChooseBehavior,
            "ignored",
            new BehaviorChoiceParameters(
                "choice:discard",
                optionKeys: ["mode-a", "mode-b"],
                cardIds: [new CardInstanceId(known.Id), new CardInstanceId(hidden.Id)]));
        LegalActionViewModel installChoice = Install(
            "action:install-choice",
            "card:install",
            InstallDestinationKind.RunnerRig,
            candidateReplacementCardIds: [
                new CardInstanceId(known.Id),
                new CardInstanceId(hidden.Id)]);

        MatchViewModel view = View(
            PlayerSide.Runner,
            [known],
            [],
            [cardChoice, behaviorChoice, installChoice]);

        BoardInteractionChoiceViewModel? cardTarget =
            BoardInteractionProjector.TargetsForAction(view, cardChoice);
        Assert.NotNull(cardTarget);
        Assert.Equal([known.Id], cardTarget!.TargetCardIds);

        BoardInteractionChoiceViewModel? behaviorTarget =
            BoardInteractionProjector.TargetsForAction(view, behaviorChoice);
        Assert.NotNull(behaviorTarget);
        Assert.Equal([known.Id], behaviorTarget!.TargetCardIds);
        Assert.Equal(["mode-a", "mode-b"], behaviorTarget.OptionKeys);

        BoardInteractionChoiceViewModel? installTarget =
            BoardInteractionProjector.TargetsForAction(view, installChoice);
        Assert.NotNull(installTarget);
        Assert.Equal([known.Id], installTarget!.TargetCardIds);

        Assert.Equal(3, BoardInteractionProjector.TargetsForAction(view).Count);
    }

    [Fact]
    public void ReturnsEmptyProjectionWhenDecisionBelongsToOpponent()
    {
        CardViewModel card = Card("card:card", type: "Anything", isKnown: true);
        MatchViewModel view = View(
            PlayerSide.Runner,
            [card],
            [],
            [Play("action:card", card.Id)],
            decisionOwner: PlayerSide.Corp);

        BoardInteractionViewModel projected = BoardInteractionProjector.Project(view);

        Assert.Empty(projected.Sources);
        Assert.Empty(projected.Choices);
        Assert.Null(BoardInteractionProjector.TargetsForAction(view, view.LegalActions[0]));
    }

    [Fact]
    public void SetupTerminalAndNoActionViewsExposeNoDragSurface()
    {
        CardViewModel card = Card("card:card", type: "Anything", isKnown: true);
        LegalActionViewModel action = Play("action:card", card.Id);
        foreach (DecisionKind kind in new[] { DecisionKind.Setup, DecisionKind.TurnTiming })
        {
            MatchViewModel setup = View(
                PlayerSide.Runner,
                [card],
                [],
                [action],
                decisionKind: kind);
            Assert.Empty(BoardInteractionProjector.Project(setup).Sources);
        }

        MatchViewModel noActions = View(PlayerSide.Runner, [card], [], []);
        Assert.Empty(BoardInteractionProjector.Project(noActions).Sources);

        MatchViewModel terminal = View(PlayerSide.Runner, [card], [], [action]) with
        {
            Outcome = new OutcomeViewModel(GameOutcomeKind.Won, PlayerSide.Runner, GameWinReason.DeckOut),
        };
        Assert.Empty(BoardInteractionProjector.Project(terminal).Sources);
    }

    [Fact]
    public void AmbiguousTargetedPlayPreservesEveryServerCandidate()
    {
        CardViewModel run = Card("card:run", type: "Arbitrary", isKnown: true);
        LegalActionViewModel headquarters = Play(
            "action:run-hq",
            run.Id,
            new ServerId("server:headquarters"));
        LegalActionViewModel remote = Play(
            "action:run-remote",
            run.Id,
            new ServerId("server:remote"));

        BoardInteractionSource source = Assert.Single(
            BoardInteractionProjector.Project(
                View(PlayerSide.Runner, [run], [], [headquarters, remote])).Sources);

        Assert.Equal(
            [
                BoardInteractionDestinationKind.GeneralPlay,
                BoardInteractionDestinationKind.ServerTarget,
                BoardInteractionDestinationKind.ServerTarget,
            ],
            source.Destinations.Select(destination => destination.Kind));
        Assert.Equal(
            [null, "server:headquarters", "server:remote"],
            source.Destinations.Select(destination => destination.TargetId));
    }

    [Fact]
    public async Task ProjectedDropSubmitsTheSameIssuedActionAsTheMenuToTheSameEngineState()
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer(47042);
        string menuTrace = NewTraceDirectory();
        string dragTrace = NewTraceDirectory();
        MatchCoordinator menu = CreateRunnerCoordinator(definition, menuTrace);
        MatchCoordinator drag = CreateRunnerCoordinator(definition, dragTrace);
        try
        {
            MatchSnapshot menuTurn = await ReachProjectedTurnAction(menu);
            MatchSnapshot dragTurn = await ReachProjectedTurnAction(drag);
            Assert.Equal(
                EngineJson.Serialize(menuTurn.Observation),
                EngineJson.Serialize(dragTurn.Observation));

            BoardInteractionViewModel projected = BoardInteractionProjector.Project(dragTurn.View);
            BoardInteractionDestination destination = projected.Sources
                .SelectMany(source => source.Destinations)
                .FirstOrDefault(candidate => candidate.Actions.Count > 0)
                ?? throw new Xunit.Sdk.XunitException("The turn has no projected play/install destination.");
            LegalActionViewModel projectedAction = destination.Actions[0];
            LegalAction menuAction = menuTurn.Observation.LegalActions.Single(
                action => action.Id == projectedAction.Id);

            MatchSubmissionResult menuResult = await menu.SubmitHumanActionAsync(menuAction.Id)
                .WaitAsync(TimeSpan.FromSeconds(5));
            MatchSubmissionResult dragResult = await drag.SubmitHumanActionAsync(projectedAction.Id)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(menuResult.Accepted, menuResult.Feedback);
            Assert.True(dragResult.Accepted, dragResult.Feedback);

            GameState menuState = PrivateState(menu);
            GameState dragState = PrivateState(drag);
            Assert.Equal(EngineJson.Serialize(menuState), EngineJson.Serialize(dragState));
            Assert.Equal(
                EngineJson.Serialize(menuResult.Snapshot.Events),
                EngineJson.Serialize(dragResult.Snapshot.Events));
            Assert.Equal(
                EngineJson.Serialize(menuResult.Snapshot.Observation),
                EngineJson.Serialize(dragResult.Snapshot.Observation));
            Assert.Equal(
                File.ReadAllText(Path.Combine(menuTrace, SessionTrace.ReplayFileName)),
                File.ReadAllText(Path.Combine(dragTrace, SessionTrace.ReplayFileName)));
        }
        finally
        {
            await menu.DisposeAsync();
            await drag.DisposeAsync();
            DeleteTraceDirectory(menuTrace);
            DeleteTraceDirectory(dragTrace);
        }
    }

    private static void AssertDestination(
        BoardInteractionViewModel projected,
        string cardId,
        BoardInteractionDestinationKind kind,
        string? targetId)
    {
        BoardInteractionSource source = Assert.Single(
            projected.Sources,
            candidate => candidate.CardId == cardId);
        BoardInteractionDestination destination = Assert.Single(
            source.Destinations,
            candidate => candidate.Kind == kind);
        Assert.Equal(targetId, destination.TargetId);
    }

    private static LegalActionViewModel Play(
        string actionId,
        string cardId,
        ServerId? targetServerId = null) => new(
        new ActionId(actionId),
        ActionKind.PlayCard,
        "ignored",
        new PlayCardParameters(new CardInstanceId(cardId), 0, targetServerId));

    private static LegalActionViewModel Install(
        string actionId,
        string cardId,
        InstallDestinationKind destinationKind,
        ServerId? serverId = null,
        bool createNewRemote = false,
        CardInstanceId? hostCardId = null,
        IReadOnlyList<CardInstanceId>? candidateReplacementCardIds = null) => new(
        new ActionId(actionId),
        ActionKind.InstallCard,
        "ignored",
        new InstallCardParameters(
            new CardInstanceId(cardId),
            destinationKind,
            serverId,
            createNewRemote,
            creditCost: 0,
            candidateReplacementCardIds: candidateReplacementCardIds,
            hostCardId: hostCardId));

    private static CardViewModel Card(string id, string type, bool isKnown) => new(
        id,
        isKnown ? $"definition:{id}" : null,
        id,
        type,
        isKnown,
        Rezzed: false,
        AdvancementCounters: 0,
        Cost: null,
        Strength: null,
        AgendaPoints: null,
        TrashCost: null,
        Subtypes: [],
        RulesText: "");

    private static MatchViewModel View(
        PlayerSide viewer,
        IReadOnlyList<CardViewModel> hand,
        IReadOnlyList<CardViewModel> visibleHosts,
        IReadOnlyList<LegalActionViewModel> actions,
        PlayerSide? decisionOwner = null,
        DecisionKind decisionKind = DecisionKind.TurnAction) => new(
        viewer,
        new PlayerViewModel("player:corp", PlayerSide.Corp, 5, 3, 0),
        new PlayerViewModel("player:runner", PlayerSide.Runner, 5, 4, 0),
        Array.Empty<ServerViewModel>(),
        new ZoneViewModel(ZoneKind.CorpHand, viewer == PlayerSide.Corp ? hand.Count : 0,
            viewer == PlayerSide.Corp ? hand : Array.Empty<CardViewModel>()),
        new ZoneViewModel(ZoneKind.CorpDeck, 0, Array.Empty<CardViewModel>()),
        new ZoneViewModel(ZoneKind.RunnerGrip, viewer == PlayerSide.Runner ? hand.Count : 0,
            viewer == PlayerSide.Runner ? hand : Array.Empty<CardViewModel>()),
        new ZoneViewModel(ZoneKind.RunnerStack, 0, Array.Empty<CardViewModel>()),
        visibleHosts,
        Array.Empty<CardViewModel>(),
        Array.Empty<CardViewModel>(),
        new DecisionViewModel(
            "decision:drag",
            1,
            new PlayerId(decisionOwner == PlayerSide.Corp ? "player:corp" : "player:runner"),
            decisionOwner ?? viewer,
            decisionKind,
            "drag"),
        null,
        new OutcomeViewModel(GameOutcomeKind.Ongoing, null, null),
            actions);

    private static MatchCoordinator CreateRunnerCoordinator(
        FullMatchDefinition definition,
        string traceDirectory) => new(
        definition,
        new FullGameContractAgent(
            new FullGameContractPolicy(PlayerSide.Corp),
            definition.Runtime),
        new MatchCoordinatorOptions
        {
            HumanSide = PlayerSide.Runner,
            Delay = static (_, _) => ValueTask.CompletedTask,
            SessionTraceDirectory = traceDirectory,
        });

    private static async Task<MatchSnapshot> ReachProjectedTurnAction(
        MatchCoordinator coordinator)
    {
        MatchSnapshot snapshot = await coordinator.StartAsync().WaitAsync(TimeSpan.FromSeconds(10));
        for (int step = 0; step < 32; step++)
        {
            snapshot = await AwaitHumanDecision(coordinator);
            if (snapshot.IsTerminal)
            {
                throw new Xunit.Sdk.XunitException("The deterministic developer match reached terminal state too early.");
            }

            if (snapshot.View.Decision?.Kind == DecisionKind.TurnAction
                && BoardInteractionProjector.Project(snapshot.View).Sources.Count > 0)
            {
                return snapshot;
            }

            if (snapshot.Observation.LegalActions.Count == 0)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Decision '{snapshot.View.Decision?.Kind}' did not expose a legal action.");
            }

            LegalAction action = snapshot.Observation.LegalActions[0];
            MatchSubmissionResult result = await coordinator.SubmitHumanActionAsync(action.Id)
                .WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(result.Accepted, result.Feedback);
        }

        throw new Xunit.Sdk.XunitException(
            "The deterministic developer match did not reach a projected play/install action.");
    }

    private static async Task<MatchSnapshot> AwaitHumanDecision(MatchCoordinator coordinator)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            MatchSnapshot? snapshot = coordinator.CurrentSnapshot;
            if (snapshot is { AwaitingHumanDecision: true } || snapshot?.IsTerminal == true)
            {
                return snapshot;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("The match did not return to a human decision.");
            }

            await Task.Delay(10);
        }
    }

    private static GameState PrivateState(MatchCoordinator coordinator) =>
        (GameState)typeof(MatchCoordinator)
            .GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(coordinator)!;

    private static string NewTraceDirectory() =>
        Path.Combine(Path.GetTempPath(), $"netrunner-drag-projection-{Guid.NewGuid():N}");

    private static void DeleteTraceDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

/// <summary>
/// Real-turn lifecycle evidence for the executable Slice 4.2 Batch-1 cards.
/// The test fixture deliberately composes the authenticated normalized card
/// data with the immutable Gateway behavior catalog; it does not recreate
/// printed costs or card classes in the test.
///
/// These tests require the official content-enabled Cards build because the
/// clean synthetic fixture intentionally contains only the seven M4.1 cards.
/// The skip keeps the ordinary synthetic test lane honest while the official
/// content lane supplies the twelve Batch-1 definitions.
/// </summary>
public sealed class GatewayBatch1LifecycleTests
{
    [Fact]
    [Trait("GatewayCard", "30030")]
    public void SureGambleUsesPrintedCostOnceAndMovesEventToHeap()
    {
        if (!LifecycleAvailable())
        {
            return;
        }

        Fixture fixture = CreateFixture(
            PlayerSide.Runner,
            GatewayBatch1CardCodes.SureGamble,
            credits: 5,
            runnerStackCount: 1);

        GameState state = OpenTurnAction(fixture);
        PlayerState before = state.GetPlayer(fixture.ActivePlayerId);
        LegalAction play = SinglePlay(state, fixture.CardId);

        ApplyResult result = Apply(fixture, state, play);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(before.Clicks - 1, result.State.GetPlayer(fixture.ActivePlayerId).Clicks);
        Assert.Equal(9, result.State.GetPlayer(fixture.ActivePlayerId).Credits);
        Assert.Single(result.State.GetZone(ZoneKind.RunnerHeap).CardIds, fixture.CardId);
        Assert.Empty(result.State.GetZone(ZoneKind.RunnerPlayArea).CardIds);
        Assert.Equal(
            [-5, 9],
            result.Events
                .Where(value => value.Kind == EngineEventKinds.CreditsAdjusted)
                .Select(value => Assert.IsType<CreditsAdjustedEventPayload>(value.Payload).Delta));
        Assert.Equal(
            [3],
            result.Events
                .Where(value => value.Kind == EngineEventKinds.ClickSpent)
                .Select(value => Assert.IsType<ClickSpentEventPayload>(value.Payload).ClicksRemaining));
    }

    [Fact]
    [Trait("GatewayCard", "30064")]
    public void GovernmentSubsidyUsesPrintedCostOnceAndMovesOperationToArchives()
    {
        if (!LifecycleAvailable())
        {
            return;
        }

        Fixture fixture = CreateFixture(
            PlayerSide.Corp,
            GatewayBatch1CardCodes.GovernmentSubsidy,
            credits: 10,
            corpDeckCount: 1);

        GameState state = OpenTurnAction(fixture);
        PlayerState before = state.GetPlayer(fixture.ActivePlayerId);
        LegalAction play = SinglePlay(state, fixture.CardId);

        ApplyResult result = Apply(fixture, state, play);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(before.Clicks - 1, result.State.GetPlayer(fixture.ActivePlayerId).Clicks);
        Assert.Equal(15, result.State.GetPlayer(fixture.ActivePlayerId).Credits);
        Assert.Single(result.State.GetZone(ZoneKind.CorpDiscard).CardIds, fixture.CardId);
        Assert.Empty(result.State.GetZone(ZoneKind.CorpPlayArea).CardIds);
        Assert.Equal(
            [-10, 15],
            result.Events
                .Where(value => value.Kind == EngineEventKinds.CreditsAdjusted)
                .Select(value => Assert.IsType<CreditsAdjustedEventPayload>(value.Payload).Delta));
    }

    [Fact]
    [Trait("GatewayCard", "30075")]
    public void HedgeFundUsesPrintedCostOnceAndMovesOperationToArchives()
    {
        if (!LifecycleAvailable())
        {
            return;
        }

        Fixture fixture = CreateFixture(
            PlayerSide.Corp,
            GatewayBatch1CardCodes.HedgeFund,
            credits: 5,
            corpDeckCount: 1);

        GameState state = OpenTurnAction(fixture);
        LegalAction play = SinglePlay(state, fixture.CardId);
        ApplyResult result = Apply(fixture, state, play);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(9, result.State.GetPlayer(fixture.ActivePlayerId).Credits);
        Assert.Single(result.State.GetZone(ZoneKind.CorpDiscard).CardIds, fixture.CardId);
        Assert.Equal(
            [-5, 9],
            result.Events
                .Where(value => value.Kind == EngineEventKinds.CreditsAdjusted)
                .Select(value => Assert.IsType<CreditsAdjustedEventPayload>(value.Payload).Delta));
    }

    [Fact]
    [Trait("GatewayCard", "30020")]
    public void CreativeCommissionPaysOnceAndLosesThePostPlayClick()
    {
        if (!LifecycleAvailable())
        {
            return;
        }

        Fixture fixture = CreateFixture(
            PlayerSide.Runner,
            GatewayBatch1CardCodes.CreativeCommission,
            credits: 1,
            runnerStackCount: 1);

        GameState state = OpenTurnAction(fixture);
        ApplyResult result = Apply(fixture, state, SinglePlay(state, fixture.CardId));

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(5, result.State.GetPlayer(fixture.ActivePlayerId).Credits);
        // Four clicks are allotted; the PlayCard action spends one and the
        // conditional card text spends one more while two remain.
        Assert.Equal(2, result.State.GetPlayer(fixture.ActivePlayerId).Clicks);
        Assert.Single(result.State.GetZone(ZoneKind.RunnerHeap).CardIds, fixture.CardId);
        Assert.Equal(
            [-1, 5],
            result.Events
                .Where(value => value.Kind == EngineEventKinds.CreditsAdjusted)
                .Select(value => Assert.IsType<CreditsAdjustedEventPayload>(value.Payload).Delta));
    }

    [Fact]
    [Trait("GatewayCard", "30021")]
    public void VRcationPaysOnceDrawsFourAndLosesThePostPlayClick()
    {
        if (!LifecycleAvailable())
        {
            return;
        }

        Fixture fixture = CreateFixture(
            PlayerSide.Runner,
            GatewayBatch1CardCodes.VRcation,
            credits: 1,
            runnerStackCount: 4);

        GameState state = OpenTurnAction(fixture);
        ApplyResult result = Apply(fixture, state, SinglePlay(state, fixture.CardId));

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(0, result.State.GetPlayer(fixture.ActivePlayerId).Credits);
        Assert.Equal(2, result.State.GetPlayer(fixture.ActivePlayerId).Clicks);
        Assert.Empty(result.State.GetZone(ZoneKind.RunnerStack).CardIds);
        Assert.Equal(4, result.State.GetZone(ZoneKind.RunnerGrip).CardIds.Count);
        Assert.Single(result.State.GetZone(ZoneKind.RunnerHeap).CardIds, fixture.CardId);
    }

    [Fact]
    [Trait("GatewayCard", "30031")]
    public void T400InstallPaysOnceProjectsHandSizeAndAddsTheMemoryUnit()
    {
        if (!LifecycleAvailable())
        {
            return;
        }

        Fixture fixture = CreateFixture(
            PlayerSide.Runner,
            GatewayBatch1CardCodes.T400MemoryDiamond,
            credits: 6,
            runnerGripExtras: [GatewayCardCodes.Buzzsaw],
            preInstalledRunnerPrograms:
            [
                GatewayCardCodes.Buzzsaw,
                GatewayCardCodes.Buzzsaw,
                GatewayCardCodes.Buzzsaw,
                GatewayCardCodes.Buzzsaw,
            ]);

        GameState state = OpenTurnAction(fixture);
        LegalAction install = Assert.Single(
            state.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.InstallCard
                && Assert.IsType<InstallCardParameters>(action.Parameters).CardId == fixture.CardId);

        ApplyResult installed = Apply(fixture, state, install);

        Assert.True(installed.Accepted, installed.Rejection?.Message);
        Assert.Equal(3, installed.State.GetPlayer(fixture.ActivePlayerId).Clicks);
        Assert.Equal(4, installed.State.GetPlayer(fixture.ActivePlayerId).Credits);
        Assert.Equal(ZoneKind.RunnerRig, installed.State.GetZone(installed.State.GetCard(fixture.CardId).ZoneId).Kind);
        Assert.True(installed.State.GetCard(fixture.CardId).FaceUp);
        IReadOnlyDictionary<string, object?> runnerView = RunnerPlayerView(installed.State, fixture.BehaviorCatalog);
        Assert.Equal(6, runnerView["maximumHandSize"]);

        // Four one-MU programs already consume the base four MU. A second
        // real InstallCard in the same action window proves T400's active
        // static +1 MU projection without modifying BaseMemory.
        GameState followUpTurn = ContinueToTurnAction(fixture, installed.State);
        LegalAction secondInstall = Assert.Single(
            followUpTurn.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.InstallCard
                && Assert.IsType<InstallCardParameters>(action.Parameters).CardId != fixture.CardId);
        ApplyResult programChoiceOpened = Apply(fixture, followUpTurn, secondInstall);

        Assert.True(programChoiceOpened.Accepted, programChoiceOpened.Rejection?.Message);
        Assert.Equal(3, programChoiceOpened.State.GetPlayer(fixture.ActivePlayerId).Clicks);
        PendingDecision installChoice = Assert.IsType<PendingDecision>(programChoiceOpened.State.PendingDecision);
        Assert.Equal(DecisionKind.InstallChoice, installChoice.Kind);
        Assert.Equal(fixture.ActivePlayerId, installChoice.Owner);
        Assert.Empty(ObservationBuilder.Build(
            programChoiceOpened.State,
            PlayerSide.Corp,
            fixture.BehaviorCatalog).LegalActions);

        string installChoiceJson = EngineJson.Serialize(programChoiceOpened.State);
        GameState installChoiceRestored = EngineJson.DeserializeState(
            installChoiceJson,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.Equal(installChoiceJson, EngineJson.Serialize(installChoiceRestored));
        Assert.Equal(
            DeterministicHash.ForState(programChoiceOpened.State),
            DeterministicHash.ForState(installChoiceRestored));

        LegalAction finalizeInstall = Assert.Single(
            installChoiceRestored.PendingDecision!.LegalActions,
            action => action.Parameters is InstallCardParameters parameters
                && parameters.FinalizeSelection
                && parameters.SelectedReplacementCardIds.Count == 0);
        Assert.Empty(Assert.IsType<InstallCardParameters>(finalizeInstall.Parameters).SelectedReplacementCardIds);
        ApplyResult programInstalled = Apply(fixture, installChoiceRestored, finalizeInstall);

        Assert.True(programInstalled.Accepted, programInstalled.Rejection?.Message);
        Assert.Equal(2, programInstalled.State.GetPlayer(fixture.ActivePlayerId).Clicks);
        Assert.Equal(5, programInstalled.State.RulesState!.Players
            .Single(player => player.PlayerId == fixture.ActivePlayerId).UsedMemory);
        Assert.Equal(6, programInstalled.State.GetZone(ZoneKind.RunnerRig).CardIds.Count);
    }

    [Fact]
    [Trait("GatewayCard", "30040")]
    public void SeamlessLaunchPlaysThroughRealTurnChoiceAndAdvancesOnlyEligibleCorpInstall()
    {
        if (!LifecycleAvailable())
        {
            return;
        }

        Fixture fixture = CreateFixture(
            PlayerSide.Corp,
            GatewayBatch1CardCodes.SeamlessLaunch,
            credits: 1,
            preInstalledRunnerPrograms: [GatewayCardCodes.Buzzsaw]);
        CardInstanceId installedCorpCardId = new("card:gateway-lifecycle-corp-installed");
        CardInstanceId currentTurnCorpCardId = new("card:gateway-lifecycle-corp-current-turn");
        CardInstanceId movedCorpCardId = new("card:gateway-lifecycle-corp-moved");
        fixture = fixture with
        {
            State = AddCorpInstalledCard(
                fixture.State,
                installedCorpCardId,
                GatewayBatch1CardCodes.GovernmentSubsidy),
        };
        fixture = fixture with
        {
            State = AddCorpInstalledCard(
                fixture.State,
                currentTurnCorpCardId,
                GatewayBatch1CardCodes.GovernmentSubsidy),
        };
        fixture = fixture with
        {
            State = MoveCardToZone(
                AddCorpInstalledCard(
                    fixture.State,
                    movedCorpCardId,
                    GatewayBatch1CardCodes.GovernmentSubsidy),
                movedCorpCardId,
                ZoneKind.CorpDiscard),
        };

        GameState turnAction = MarkCorpInstalledThisTurn(
            OpenTurnAction(fixture),
            currentTurnCorpCardId);
        Assert.Contains(currentTurnCorpCardId, turnAction.Turn.History!.CorpThisTurn!.InstalledCardIds);
        LegalAction play = SinglePlay(turnAction, fixture.CardId);
        ApplyResult opened = Apply(fixture, turnAction, play);

        Assert.True(opened.Accepted, opened.Rejection?.Message);
        Assert.Equal(0, opened.State.GetPlayer(fixture.ActivePlayerId).Credits);
        Assert.Equal(2, opened.State.GetPlayer(fixture.ActivePlayerId).Clicks);
        Assert.Single(opened.Events, value => value.Kind == EngineEventKinds.CreditsAdjusted);
        CreditsAdjustedEventPayload playCost = Assert.IsType<CreditsAdjustedEventPayload>(
            Assert.Single(opened.Events, value => value.Kind == EngineEventKinds.CreditsAdjusted).Payload);
        Assert.Equal(-1, playCost.Delta);
        Assert.Equal(fixture.CorpPlayerId, opened.State.PendingDecision!.Owner);
        Assert.Equal(DecisionKind.BehaviorChoice, opened.State.PendingDecision.Kind);
        BehaviorChoiceParameters choice = Assert.IsType<BehaviorChoiceParameters>(
            Assert.Single(opened.State.PendingDecision.LegalActions).Parameters);
        Assert.Equal("seamless-launch-target", choice.ChoiceKey);
        Assert.Equal([installedCorpCardId], choice.CardIds);
        Assert.Contains(
            installedCorpCardId,
            opened.State.GetZone(opened.State.GetCard(installedCorpCardId).ZoneId).CardIds);
        Assert.DoesNotContain(
            installedCorpCardId,
            opened.State.Zones
                .Where(zone => zone.Kind == ZoneKind.BehaviorSetAside)
                .SelectMany(zone => zone.CardIds));

        Observation corpObservation = ObservationBuilder.Build(
            opened.State,
            PlayerSide.Corp,
            fixture.BehaviorCatalog);
        Observation runnerObservation = ObservationBuilder.Build(
            opened.State,
            PlayerSide.Runner,
            fixture.BehaviorCatalog);
        Assert.Equal(opened.State.PendingDecision.LegalActions, corpObservation.LegalActions);
        Assert.Empty(runnerObservation.LegalActions);

        string json = EngineJson.Serialize(opened.State);
        GameState restored = EngineJson.DeserializeState(
            json,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(opened.State), DeterministicHash.ForState(restored));

        LegalAction target = Assert.Single(restored.PendingDecision!.LegalActions);
        ApplyResult completed = Apply(fixture, restored, target);

        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Equal(0, completed.State.GetPlayer(fixture.ActivePlayerId).Credits);
        Assert.Equal(2, completed.State.GetCard(installedCorpCardId).AdvancementCounters);
        Assert.Equal(
            ZoneKind.ServerRoot,
            completed.State.GetZone(completed.State.GetCard(installedCorpCardId).ZoneId).Kind);
        Assert.Single(completed.State.GetZone(ZoneKind.CorpDiscard).CardIds, fixture.CardId);
        Assert.Empty(completed.State.GetZone(ZoneKind.CorpPlayArea).CardIds);
        BehaviorCardAdvancedEventPayload advanced = Assert.Single(
            completed.Events
                .Where(value => value.Kind == EngineEventKinds.BehaviorCardAdvanced)
                .Select(value => Assert.IsType<BehaviorCardAdvancedEventPayload>(value.Payload)));
        Assert.Equal(installedCorpCardId, advanced.CardId);
        Assert.Equal(2, advanced.Amount);
        Assert.DoesNotContain(
            completed.Events,
            value => value.Kind == EngineEventKinds.AgendaAdvanced);
        Assert.DoesNotContain(
            completed.Events,
            value => value.Kind == EngineEventKinds.CreditsAdjusted
                && value.Payload is CreditsAdjustedEventPayload credit
                && credit.Delta == -1);
        Assert.Equal(DecisionKind.TurnTiming, completed.State.PendingDecision!.Kind);
    }

    [Fact]
    [Trait("GatewayCard", "30040")]
    public void SeamlessLaunchIsAbsentWhenNoEligibleCorpInstalledCardExists()
    {
        if (!LifecycleAvailable())
        {
            return;
        }

        Fixture noInstalledFixture = CreateFixture(
            PlayerSide.Corp,
            GatewayBatch1CardCodes.SeamlessLaunch,
            credits: 1);
        GameState noInstalled = OpenTurnAction(noInstalledFixture);
        Assert.DoesNotContain(noInstalled.PendingDecision!.LegalActions, action => action.Kind == ActionKind.PlayCard);

        // A real Corp install marks its card in the current-turn facts. That
        // card is installed, but it is not an eligible Seamless Launch target
        // until a later Corp turn.
        Fixture currentTurnFixture = CreateFixture(
            PlayerSide.Corp,
            GatewayBatch1CardCodes.SeamlessLaunch,
            credits: 5,
            corpHandExtras: [GatewayCardCodes.NicoCampaign]);
        GameState beforeInstall = OpenTurnAction(currentTurnFixture);
        CardInstanceId nicoId = new("card:gateway-lifecycle-corp-hand-0");
        LegalAction install = Assert.Single(
            beforeInstall.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.InstallCard
                && Assert.IsType<InstallCardParameters>(action.Parameters).CardId == nicoId);
        ApplyResult installed = Apply(currentTurnFixture, beforeInstall, install);
        Assert.True(installed.Accepted, installed.Rejection?.Message);
        Assert.Contains(nicoId, installed.State.Turn.History!.CorpThisTurn!.InstalledCardIds);
        Assert.DoesNotContain(
            installed.State.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.PlayCard);
    }

    [Fact]
    [Trait("GatewayCard", "30040")]
    public void SeamlessLaunchRejectsMovedOrCurrentTurnFrozenTargetAtomically()
    {
        if (!LifecycleAvailable())
        {
            return;
        }

        CardInstanceId installedCorpCardId = new("card:gateway-lifecycle-corp-installed");
        Fixture fixture = CreateFixture(
            PlayerSide.Corp,
            GatewayBatch1CardCodes.SeamlessLaunch,
            credits: 1);
        fixture = fixture with
        {
            State = AddCorpInstalledCard(
                fixture.State,
                installedCorpCardId,
                GatewayBatch1CardCodes.GovernmentSubsidy),
        };

        GameState turnAction = OpenTurnAction(fixture);
        ApplyResult opened = Apply(fixture, turnAction, SinglePlay(turnAction, fixture.CardId));
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        LegalAction target = Assert.Single(opened.State.PendingDecision!.LegalActions);

        GameState moved = MoveCardToZone(opened.State, installedCorpCardId, ZoneKind.CorpDiscard);
        ApplyResult movedRejected = Apply(fixture, moved, target);
        Assert.False(movedRejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, movedRejected.Rejection?.Code);
        Assert.Same(moved, movedRejected.State);
        Assert.Empty(movedRejected.Events);
        Assert.Equal(0, moved.GetCard(installedCorpCardId).AdvancementCounters);

        GameState fresh = OpenTurnAction(fixture);
        ApplyResult freshOpened = Apply(fixture, fresh, SinglePlay(fresh, fixture.CardId));
        Assert.True(freshOpened.Accepted, freshOpened.Rejection?.Message);
        GameState markedThisTurn = MarkCorpInstalledThisTurn(
            freshOpened.State,
            installedCorpCardId);
        LegalAction freshTarget = Assert.Single(markedThisTurn.PendingDecision!.LegalActions);
        ApplyResult factRejected = Apply(fixture, markedThisTurn, freshTarget);
        Assert.False(factRejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, factRejected.Rejection?.Code);
        Assert.Same(markedThisTurn, factRejected.State);
        Assert.Empty(factRejected.Events);
        Assert.Equal(0, markedThisTurn.GetCard(installedCorpCardId).AdvancementCounters);
    }

    [Fact]
    [Trait("GatewayCard", "30002")]
    public void WildcatChoiceBelongsToCorpAndItsDrawResultBelongsToRunner()
    {
        if (!LifecycleAvailable())
        {
            return;
        }

        Fixture fixture = CreateFixture(
            PlayerSide.Runner,
            GatewayBatch1CardCodes.WildcatStrike,
            credits: 2,
            runnerStackCount: 4);

        GameState turnAction = OpenTurnAction(fixture);
        ApplyResult opened = Apply(fixture, turnAction, SinglePlay(turnAction, fixture.CardId));

        Assert.True(opened.Accepted, opened.Rejection?.Message);
        Assert.Equal(fixture.CorpPlayerId, opened.State.PendingDecision!.Owner);
        Assert.Equal(DecisionKind.BehaviorChoice, opened.State.PendingDecision.Kind);
        Assert.Equal(
            [-2],
            opened.Events
                .Where(value => value.Kind == EngineEventKinds.CreditsAdjusted)
                .Select(value => Assert.IsType<CreditsAdjustedEventPayload>(value.Payload).Delta));

        Observation corpObservation = ObservationBuilder.Build(
            opened.State,
            PlayerSide.Corp,
            fixture.BehaviorCatalog);
        Observation runnerObservation = ObservationBuilder.Build(
            opened.State,
            PlayerSide.Runner,
            fixture.BehaviorCatalog);
        Assert.Equal(opened.State.PendingDecision.LegalActions, corpObservation.LegalActions);
        Assert.Empty(runnerObservation.LegalActions);
        Assert.DoesNotContain(
            fixture.StackCardIds.Select(cardId => cardId.Value),
            cardId => EngineJson.Serialize(corpObservation).Contains(cardId, StringComparison.Ordinal));

        string json = EngineJson.Serialize(opened.State);
        GameState restored = EngineJson.DeserializeState(
            json,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(opened.State), DeterministicHash.ForState(restored));

        LegalAction draw = Assert.Single(
            restored.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters choice
                && choice.ChoiceKey == "wildcat-strike-effect"
                && choice.OptionKeys.SequenceEqual(["draw"]));
        ApplyResult completed = Apply(fixture, restored, draw);

        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Equal(0, completed.State.GetPlayer(fixture.ActivePlayerId).Credits);
        Assert.Equal(4, completed.State.GetZone(ZoneKind.RunnerGrip).CardIds.Count);
        Assert.Empty(completed.State.GetZone(ZoneKind.RunnerStack).CardIds);
        Assert.Single(completed.State.GetZone(ZoneKind.RunnerHeap).CardIds, fixture.CardId);
        Assert.Equal(DecisionKind.TurnTiming, completed.State.PendingDecision!.Kind);

        Fixture gainFixture = CreateFixture(
            PlayerSide.Runner,
            GatewayBatch1CardCodes.WildcatStrike,
            credits: 2);
        GameState gainTurnAction = OpenTurnAction(gainFixture);
        ApplyResult gainOpened = Apply(
            gainFixture,
            gainTurnAction,
            SinglePlay(gainTurnAction, gainFixture.CardId));
        Assert.True(gainOpened.Accepted, gainOpened.Rejection?.Message);
        LegalAction gain = Assert.Single(
            gainOpened.State.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters choice
                && choice.ChoiceKey == "wildcat-strike-effect"
                && choice.OptionKeys.SequenceEqual(["gain"]));
        ApplyResult gainCompleted = Apply(gainFixture, gainOpened.State, gain);
        Assert.True(gainCompleted.Accepted, gainCompleted.Rejection?.Message);
        Assert.Equal(6, gainCompleted.State.GetPlayer(gainFixture.ActivePlayerId).Credits);
        Assert.Single(gainCompleted.State.GetZone(ZoneKind.RunnerHeap).CardIds, gainFixture.CardId);
    }

    [Fact]
    [Trait("GatewayCard", "30041")]
    public void SprintDrawsThenPrivatelySelectsExactlyTwoHeadquartersCardsAndShufflesThem()
    {
        if (!LifecycleAvailable())
        {
            return;
        }

        Fixture fixture = CreateFixture(
            PlayerSide.Corp,
            GatewayBatch1CardCodes.Sprint,
            credits: 0,
            corpDeckCount: 4,
            corpHandExtras:
            [
                GatewayBatch1CardCodes.HedgeFund,
                GatewayBatch1CardCodes.GovernmentSubsidy,
            ]);

        GameState turnAction = OpenTurnAction(fixture);
        ApplyResult opened = Apply(fixture, turnAction, SinglePlay(turnAction, fixture.CardId));

        Assert.True(opened.Accepted, opened.Rejection?.Message);
        Assert.Equal(DecisionKind.BehaviorChoice, opened.State.PendingDecision!.Kind);
        Assert.Equal(fixture.CorpPlayerId, opened.State.PendingDecision.Owner);
        Assert.Equal(6, opened.State.GetZone(ZoneKind.CorpHand).CardIds.Count);

        Observation runnerObservation = ObservationBuilder.Build(
            opened.State,
            PlayerSide.Runner,
            fixture.BehaviorCatalog);
        string runnerJson = EngineJson.Serialize(runnerObservation);
        Assert.DoesNotContain(
            opened.State.GetZone(ZoneKind.CorpHand).CardIds.Select(cardId => cardId.Value),
            cardId => runnerJson.Contains(cardId, StringComparison.Ordinal));

        string json = EngineJson.Serialize(opened.State);
        GameState restored = EngineJson.DeserializeState(
            json,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(opened.State), DeterministicHash.ForState(restored));

        LegalAction selection = restored.PendingDecision!.LegalActions.First(
            action => action.Parameters is BehaviorChoiceParameters choice
                && choice.ChoiceKey == "sprint-headquarters"
                && choice.CardIds.Count == 2);
        IReadOnlyList<CardInstanceId> selected =
            Assert.IsType<BehaviorChoiceParameters>(selection.Parameters).CardIds;
        ApplyResult completed = Apply(fixture, restored, selection);

        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Equal(4, completed.State.GetZone(ZoneKind.CorpHand).CardIds.Count);
        Assert.Equal(2, completed.State.GetZone(ZoneKind.CorpDeck).CardIds.Count);
        Assert.All(selected, cardId =>
            Assert.Contains(cardId, completed.State.GetZone(ZoneKind.CorpDeck).CardIds));
        Assert.Single(completed.State.GetZone(ZoneKind.CorpDiscard).CardIds, fixture.CardId);
        Assert.Empty(completed.State.GetZone(ZoneKind.CorpPlayArea).CardIds);
        Assert.Equal(DecisionKind.TurnTiming, completed.State.PendingDecision!.Kind);
    }

    [Fact]
    [Trait("GatewayCard", "30048")]
    public void HanseiReviewPrivatelyTrashesOneHeadquartersCardAndContinuesWhenEmpty()
    {
        if (!LifecycleAvailable())
        {
            return;
        }

        Fixture fixture = CreateFixture(
            PlayerSide.Corp,
            GatewayBatch1CardCodes.HanseiReview,
            credits: 5,
            corpDeckCount: 1,
            corpHandExtras: [GatewayBatch1CardCodes.HedgeFund]);
        GameState turnAction = OpenTurnAction(fixture);
        ApplyResult opened = Apply(fixture, turnAction, SinglePlay(turnAction, fixture.CardId));

        Assert.True(opened.Accepted, opened.Rejection?.Message);
        Assert.Equal(DecisionKind.BehaviorChoice, opened.State.PendingDecision!.Kind);
        Observation runnerObservation = ObservationBuilder.Build(
            opened.State,
            PlayerSide.Runner,
            fixture.BehaviorCatalog);
        Assert.Empty(runnerObservation.LegalActions);
        Assert.DoesNotContain(
            opened.State.GetZone(ZoneKind.CorpHand).CardIds.Select(cardId => cardId.Value),
            cardId => EngineJson.Serialize(runnerObservation).Contains(cardId, StringComparison.Ordinal));

        string json = EngineJson.Serialize(opened.State);
        GameState restored = EngineJson.DeserializeState(json, fixture.BehaviorCatalog, fixture.RuntimeCatalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(opened.State), DeterministicHash.ForState(restored));
        LegalAction[] trashChoices = restored.PendingDecision!.LegalActions
            .Where(action => action.Parameters is BehaviorChoiceParameters choice
                && choice.ChoiceKey == "hansei-review-headquarters")
            .ToArray();
        Assert.NotEmpty(trashChoices);
        Assert.All(trashChoices, action =>
        {
            BehaviorChoiceParameters choice = Assert.IsType<BehaviorChoiceParameters>(action.Parameters);
            Assert.Single(choice.CardIds);
        });
        LegalAction trash = trashChoices.First();
        CardInstanceId trashedHeadquartersCard =
            Assert.IsType<BehaviorChoiceParameters>(trash.Parameters).CardIds.Single();
        ApplyResult completed = Apply(fixture, restored, trash);

        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Equal(10, completed.State.GetPlayer(fixture.ActivePlayerId).Credits);
        Assert.Equal(DecisionKind.TurnTiming, completed.State.PendingDecision!.Kind);
        Assert.Equal(
            [trashedHeadquartersCard, fixture.CardId],
            completed.State.GetZone(ZoneKind.CorpDiscard).CardIds);
        Assert.False(completed.State.GetCard(trashedHeadquartersCard).FaceUp);

        Fixture emptyFixture = CreateFixture(
            PlayerSide.Corp,
            GatewayBatch1CardCodes.HanseiReview,
            credits: 5,
            primaryInCorpDeck: true);
        GameState emptyTurnAction = OpenTurnAction(emptyFixture);
        ApplyResult emptyResult = Apply(
            emptyFixture,
            emptyTurnAction,
            SinglePlay(emptyTurnAction, emptyFixture.CardId));
        Assert.True(emptyResult.Accepted, emptyResult.Rejection?.Message);
        Assert.Equal(DecisionKind.TurnTiming, emptyResult.State.PendingDecision!.Kind);
        Assert.Equal(10, emptyResult.State.GetPlayer(emptyFixture.ActivePlayerId).Credits);
        Assert.Single(emptyResult.State.GetZone(ZoneKind.CorpDiscard).CardIds, emptyFixture.CardId);
    }

    [Fact]
    [Trait("GatewayCard", "30056")]
    public void PredictivePlanogramExposesUntaggedAndTaggedModesAndBothOrders()
    {
        if (!LifecycleAvailable())
        {
            return;
        }

        Fixture untaggedFixture = CreateFixture(
            PlayerSide.Corp,
            GatewayBatch1CardCodes.PredictivePlanogram,
            credits: 0,
            corpDeckCount: 3,
            corpTags: 1,
            runnerTags: 0);
        GameState untaggedTurn = OpenTurnAction(untaggedFixture);
        ApplyResult untaggedOpened = Apply(
            untaggedFixture,
            untaggedTurn,
            SinglePlay(untaggedTurn, untaggedFixture.CardId));
        Assert.True(untaggedOpened.Accepted, untaggedOpened.Rejection?.Message);
        Assert.Equal(
            ["gain", "draw"],
            BehaviorOptions(untaggedOpened.State, "predictive-planogram-single-effect"));
        LegalAction untaggedGain = Assert.Single(
            untaggedOpened.State.PendingDecision!.LegalActions,
            action => HasBehaviorOption(action, "predictive-planogram-single-effect", "gain"));
        ApplyResult untaggedCompleted = Apply(untaggedFixture, untaggedOpened.State, untaggedGain);
        Assert.True(untaggedCompleted.Accepted, untaggedCompleted.Rejection?.Message);
        Assert.Equal(3, untaggedCompleted.State.GetPlayer(untaggedFixture.ActivePlayerId).Credits);
        Assert.Single(untaggedCompleted.State.GetZone(ZoneKind.CorpHand).CardIds);
        Assert.Equal(2, untaggedCompleted.State.GetZone(ZoneKind.CorpDeck).CardIds.Count);

        foreach (string order in new[] { "gain-first", "draw-first" })
        {
            Fixture taggedFixture = CreateFixture(
                PlayerSide.Corp,
                GatewayBatch1CardCodes.PredictivePlanogram,
                credits: 0,
                corpDeckCount: 4,
                corpTags: 0,
                runnerTags: 1);
            GameState taggedTurn = OpenTurnAction(taggedFixture);
            ApplyResult taggedOpened = Apply(
                taggedFixture,
                taggedTurn,
                SinglePlay(taggedTurn, taggedFixture.CardId));
            Assert.True(taggedOpened.Accepted, taggedOpened.Rejection?.Message);
            Assert.Equal(
                ["gain", "draw", "both"],
                BehaviorOptions(taggedOpened.State, "predictive-planogram-tagged-effect"));

            string outerJson = EngineJson.Serialize(taggedOpened.State);
            GameState outerRestored = EngineJson.DeserializeState(
                outerJson,
                taggedFixture.BehaviorCatalog,
                taggedFixture.RuntimeCatalog);
            Assert.Equal(outerJson, EngineJson.Serialize(outerRestored));
            Assert.Equal(DeterministicHash.ForState(taggedOpened.State), DeterministicHash.ForState(outerRestored));
            LegalAction both = Assert.Single(
                outerRestored.PendingDecision!.LegalActions,
                action => HasBehaviorOption(action, "predictive-planogram-tagged-effect", "both"));
            ApplyResult orderPrompt = Apply(taggedFixture, outerRestored, both);
            Assert.True(orderPrompt.Accepted, orderPrompt.Rejection?.Message);
            Assert.Equal(
                ["gain-first", "draw-first"],
                BehaviorOptions(orderPrompt.State, "predictive-planogram-both-order"));

            string orderJson = EngineJson.Serialize(orderPrompt.State);
            GameState orderRestored = EngineJson.DeserializeState(
                orderJson,
                taggedFixture.BehaviorCatalog,
                taggedFixture.RuntimeCatalog);
            Assert.Equal(orderJson, EngineJson.Serialize(orderRestored));
            Assert.Equal(DeterministicHash.ForState(orderPrompt.State), DeterministicHash.ForState(orderRestored));
            LegalAction selectedOrder = Assert.Single(
                orderRestored.PendingDecision!.LegalActions,
                action => HasBehaviorOption(action, "predictive-planogram-both-order", order));
            ApplyResult completed = Apply(taggedFixture, orderRestored, selectedOrder);
            Assert.True(completed.Accepted, completed.Rejection?.Message);
            Assert.Equal(3, completed.State.GetPlayer(taggedFixture.ActivePlayerId).Credits);
            Assert.Equal(4, completed.State.GetZone(ZoneKind.CorpHand).CardIds.Count);
            Assert.Empty(completed.State.GetZone(ZoneKind.CorpDeck).CardIds);

            int creditIndex = completed.Events
                .Select((value, index) => (value, index))
                .Where(entry => entry.value.Kind == EngineEventKinds.CreditsAdjusted)
                .Select(entry => entry.index)
                .Single();
            int drawIndex = completed.Events
                .Select((value, index) => (value, index))
                .Where(entry => entry.value.Kind == EngineEventKinds.CardDrawn)
                .Select(entry => entry.index)
                .First();
            Assert.Equal(order == "gain-first", creditIndex < drawIndex);
        }
    }

    [Fact]
    [Trait("GatewayCard", "30056")]
    public void PredictivePlanogramDrawDeckOutCompletesResumedPlayBeforeTerminalCleanup()
    {
        if (!LifecycleAvailable())
        {
            return;
        }

        Fixture fixture = CreateFixture(
            PlayerSide.Corp,
            GatewayBatch1CardCodes.PredictivePlanogram,
            credits: 0,
            corpDeckCount: 2,
            runnerTags: 1);
        GameState turnAction = OpenTurnAction(fixture);
        CardInstanceId remainingDeckCardId = new("card:gateway-lifecycle-corp-deck-1");
        Assert.Equal([remainingDeckCardId], turnAction.GetZone(ZoneKind.CorpDeck).CardIds);
        ApplyResult opened = Apply(
            fixture,
            turnAction,
            SinglePlay(turnAction, fixture.CardId));
        Assert.True(opened.Accepted, opened.Rejection?.Message);

        LegalAction both = Assert.Single(
            opened.State.PendingDecision!.LegalActions,
            action => HasBehaviorOption(action, "predictive-planogram-tagged-effect", "both"));
        ApplyResult orderOpened = Apply(fixture, opened.State, both);
        Assert.True(orderOpened.Accepted, orderOpened.Rejection?.Message);
        string historyJson = EngineJson.Serialize(orderOpened.State.Turn.History);

        string orderJson = EngineJson.Serialize(orderOpened.State);
        GameState restored = EngineJson.DeserializeState(
            orderJson,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.Equal(orderJson, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(orderOpened.State), DeterministicHash.ForState(restored));
        LegalAction drawFirst = Assert.Single(
            restored.PendingDecision!.LegalActions,
            action => HasBehaviorOption(action, "predictive-planogram-both-order", "draw-first"));

        ApplyResult completed = Apply(fixture, restored, drawFirst);

        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Equal(GameOutcomeKind.Won, completed.State.Outcome.Kind);
        Assert.Equal(PlayerSide.Runner, completed.State.Outcome.Winner);
        Assert.Equal(GameWinReason.DeckOut, completed.State.Outcome.Reason);
        var expectedHistory = System.Text.Json.Nodes.JsonNode.Parse(historyJson)!;
        // Finishing the accepted operation trashes its play-area card even
        // after the draw ended the game; the current turn records that fact.
        expectedHistory["corpThisTurn"]!["trashedCardOccurrenceIds"]!.AsArray().Add(fixture.CardId.Value);
        Assert.Equal(expectedHistory.ToJsonString(), System.Text.Json.Nodes.JsonNode.Parse(EngineJson.Serialize(completed.State.Turn.History))!.ToJsonString());
        Assert.All(completed.State.Players, player => Assert.Equal(0, player.Clicks));
        Assert.Single(completed.State.GetZone(ZoneKind.CorpDiscard).CardIds, fixture.CardId);
        Assert.True(completed.State.GetCard(fixture.CardId).FaceUp);
        Assert.Empty(completed.State.GetZone(ZoneKind.CorpPlayArea).CardIds);
        Assert.Empty(completed.State.GetZone(ZoneKind.CorpDeck).CardIds);
        Assert.Contains(remainingDeckCardId, completed.State.GetZone(ZoneKind.CorpHand).CardIds);
        Assert.Equal(2, completed.State.GetZone(ZoneKind.CorpHand).CardIds.Count);
        Assert.Null(completed.State.PendingDecision);
        Assert.Empty(completed.State.ResolutionStack);

        int drawIndex = completed.Events.ToList().FindIndex(value => value.Kind == EngineEventKinds.CardDrawn);
        int completedIndex = completed.Events.ToList().FindIndex(value => value.Kind == EngineEventKinds.CardPlayCompleted);
        int wonIndex = completed.Events.ToList().FindIndex(value => value.Kind == EngineEventKinds.GameWon);
        Assert.True(drawIndex >= 0);
        Assert.True(completedIndex > drawIndex);
        Assert.True(wonIndex > completedIndex);
        Assert.Equal(completed.Events.Count - 1, wonIndex);
        Assert.Empty(StateInvariantValidator.Collect(
            completed.State,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog));

        string terminalJson = EngineJson.Serialize(completed.State);
        GameState terminalRestored = EngineJson.DeserializeState(
            terminalJson,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.Equal(terminalJson, EngineJson.Serialize(terminalRestored));
        Assert.Equal(DeterministicHash.ForState(completed.State), DeterministicHash.ForState(terminalRestored));
    }

    [Fact]
    [Trait("GatewayCard", "30011")]
    public void MutualFavorPrivatelyFindsIcebreakerAndReturnsItToGripWithoutARun()
    {
        if (!LifecycleAvailable())
        {
            return;
        }

        Fixture fixture = CreateFixture(
            PlayerSide.Runner,
            GatewayBatch1CardCodes.MutualFavor,
            credits: 8,
            runnerStackCards: [GatewayCardCodes.Buzzsaw, GatewayBatch1CardCodes.SureGamble]);
        CardInstanceId buzzsawId = fixture.StackCardIds[0];
        CardInstanceId nonIcebreakerId = fixture.StackCardIds[1];
        GameState turnAction = OpenTurnAction(fixture);
        int clicksBefore = turnAction.GetPlayer(fixture.ActivePlayerId).Clicks;
        int creditsBefore = turnAction.GetPlayer(fixture.ActivePlayerId).Credits;
        ApplyResult opened = Apply(fixture, turnAction, SinglePlay(turnAction, fixture.CardId));

        Assert.True(opened.Accepted, opened.Rejection?.Message);
        Assert.Equal(DecisionKind.BehaviorChoice, opened.State.PendingDecision!.Kind);
        Assert.Equal(fixture.ActivePlayerId, opened.State.PendingDecision.Owner);
        Observation corpObservation = ObservationBuilder.Build(
            opened.State,
            PlayerSide.Corp,
            fixture.BehaviorCatalog);
        Assert.Empty(corpObservation.LegalActions);
        string corpJson = EngineJson.Serialize(corpObservation);
        Assert.DoesNotContain(buzzsawId.Value, corpJson, StringComparison.Ordinal);
        Assert.DoesNotContain(nonIcebreakerId.Value, corpJson, StringComparison.Ordinal);

        string searchJson = EngineJson.Serialize(opened.State);
        GameState restored = EngineJson.DeserializeState(
            searchJson,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.Equal(searchJson, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(opened.State), DeterministicHash.ForState(restored));
        LegalAction chooseBuzzsaw = Assert.Single(
            restored.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters choice
                && choice.ChoiceKey == "mutual-favor-search"
                && choice.CardIds.SequenceEqual([buzzsawId]));
        Assert.DoesNotContain(
            restored.PendingDecision.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters choice
                && choice.ChoiceKey == "mutual-favor-search"
                && choice.CardIds.Contains(nonIcebreakerId));

        ApplyResult completed = Apply(fixture, restored, chooseBuzzsaw);

        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Equal(clicksBefore - 1, completed.State.GetPlayer(fixture.ActivePlayerId).Clicks);
        Assert.Equal(creditsBefore, completed.State.GetPlayer(fixture.ActivePlayerId).Credits);
        Assert.DoesNotContain(completed.Events, value => value.Kind == EngineEventKinds.CreditsAdjusted);
        Assert.Contains(buzzsawId, completed.State.GetZone(ZoneKind.RunnerGrip).CardIds);
        Assert.DoesNotContain(buzzsawId, completed.State.GetZone(ZoneKind.RunnerStack).CardIds);
        Assert.Contains(nonIcebreakerId, completed.State.GetZone(ZoneKind.RunnerStack).CardIds);
        Assert.Single(completed.State.GetZone(ZoneKind.RunnerHeap).CardIds, fixture.CardId);
        Assert.Equal(DecisionKind.TurnTiming, completed.State.PendingDecision!.Kind);

        int shuffleIndex = completed.Events
            .Select((value, index) => (value, index))
            .Where(entry => entry.value.Kind == EngineEventKinds.BehaviorCardsShuffled
                // CardCount reports cards moved into the deck, not the
                // remaining deck size shuffled after a search.
                && Assert.IsType<BehaviorCardsShuffledEventPayload>(entry.value.Payload).CardCount == 0)
            .Select(entry => entry.index)
            .Single();
        int revealIndex = completed.Events
            .Select((value, index) => (value, index))
            .Where(entry => entry.value.Kind == EngineEventKinds.BehaviorCardRevealed
                && Assert.IsType<BehaviorCardRevealedEventPayload>(entry.value.Payload).CardId == buzzsawId)
            .Select(entry => entry.index)
            .Single();
        Assert.True(shuffleIndex < revealIndex);
        // Private custody transfers do not publish a stable physical hand
        // location after revealing the searched definition.
        Assert.DoesNotContain(completed.Events, entry => entry.Kind == EngineEventKinds.CardMoved
            && Assert.IsType<CardMovedEventPayload>(entry.Payload).CardId == buzzsawId
            && Assert.IsType<CardMovedEventPayload>(entry.Payload).ToZoneId == completed.State.GetZone(ZoneKind.RunnerGrip).Id);
    }

    [Fact]
    [Trait("GatewayCard", "30011")]
    public void MutualFavorCanExplicitlyFailToFindAndStillShufflesZeroCards()
    {
        if (!LifecycleAvailable())
        {
            return;
        }

        Fixture fixture = CreateFixture(
            PlayerSide.Runner,
            GatewayBatch1CardCodes.MutualFavor,
            credits: 8,
            runnerStackCards: [GatewayCardCodes.Buzzsaw]);
        GameState turnAction = OpenTurnAction(fixture);
        ApplyResult opened = Apply(fixture, turnAction, SinglePlay(turnAction, fixture.CardId));
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        LegalAction skip = Assert.Single(
            opened.State.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters choice
                && choice.ChoiceKey == "mutual-favor-search"
                && choice.CardIds.Count == 0
                && choice.FinalizeSelection);

        ApplyResult completed = Apply(fixture, opened.State, skip);

        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Contains(fixture.StackCardIds[0], completed.State.GetZone(ZoneKind.RunnerStack).CardIds);
        Assert.DoesNotContain(completed.Events, value => value.Kind == EngineEventKinds.BehaviorCardRevealed);
        BehaviorCardsShuffledEventPayload shuffle = Assert.IsType<BehaviorCardsShuffledEventPayload>(
            Assert.Single(completed.Events, value => value.Kind == EngineEventKinds.BehaviorCardsShuffled).Payload);
        Assert.Equal(0, shuffle.CardCount);
        Assert.Equal(DecisionKind.TurnTiming, completed.State.PendingDecision!.Kind);
        Assert.Single(completed.State.GetZone(ZoneKind.RunnerHeap).CardIds, fixture.CardId);
    }

    [Fact]
    [Trait("GatewayCard", "30011")]
    public void MutualFavorWithNoMatchingIcebreakerCompletesWithoutPrompt()
    {
        if (!LifecycleAvailable())
        {
            return;
        }

        Fixture fixture = CreateFixture(
            PlayerSide.Runner,
            GatewayBatch1CardCodes.MutualFavor,
            credits: 8,
            runnerStackCards: [GatewayBatch1CardCodes.SureGamble]);
        GameState turnAction = OpenTurnAction(fixture);
        ApplyResult completed = Apply(
            fixture,
            turnAction,
            SinglePlay(turnAction, fixture.CardId));

        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Equal(DecisionKind.TurnTiming, completed.State.PendingDecision!.Kind);
        Assert.Contains(fixture.StackCardIds[0], completed.State.GetZone(ZoneKind.RunnerStack).CardIds);
        Assert.DoesNotContain(completed.Events, value => value.Kind == EngineEventKinds.BehaviorCardRevealed);
        BehaviorCardsShuffledEventPayload shuffle = Assert.IsType<BehaviorCardsShuffledEventPayload>(
            Assert.Single(completed.Events, value => value.Kind == EngineEventKinds.BehaviorCardsShuffled).Payload);
        Assert.Equal(0, shuffle.CardCount);
        Assert.Single(completed.State.GetZone(ZoneKind.RunnerHeap).CardIds, fixture.CardId);
    }

    [Fact]
    [Trait("GatewayCard", "30011")]
    public void MutualFavorSuccessfulRunOffersGripAndInstallAndGripDeclineRoundTrips()
    {
        if (!LifecycleAvailable())
        {
            return;
        }

        Fixture fixture = CreateFixture(
            PlayerSide.Runner,
            GatewayBatch1CardCodes.MutualFavor,
            credits: 8,
            runnerStackCards: [GatewayCardCodes.Buzzsaw]);
        GameState turnAction = CompleteSuccessfulRun(fixture);
        int clicksBeforePlay = turnAction.GetPlayer(fixture.ActivePlayerId).Clicks;
        int creditsBeforePlay = turnAction.GetPlayer(fixture.ActivePlayerId).Credits;
        ApplyResult opened = Apply(fixture, turnAction, SinglePlay(turnAction, fixture.CardId));
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        LegalAction search = Assert.Single(
            opened.State.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters choice
                && choice.CardIds.SequenceEqual([fixture.StackCardIds[0]]));
        ApplyResult dispositionOpened = Apply(fixture, opened.State, search);
        Assert.True(dispositionOpened.Accepted, dispositionOpened.Rejection?.Message);
        Assert.Equal(
            ["install", "grip"],
            dispositionOpened.State.PendingDecision!.LegalActions
                .Select(action => Assert.IsType<BehaviorChoiceParameters>(action.Parameters).OptionKeys.Single()));
        Observation corpObservation = ObservationBuilder.Build(
            dispositionOpened.State,
            PlayerSide.Corp,
            fixture.BehaviorCatalog);
        Assert.Empty(corpObservation.LegalActions);
        Assert.DoesNotContain(
            fixture.StackCardIds[0].Value,
            EngineJson.Serialize(corpObservation),
            StringComparison.Ordinal);

        string dispositionJson = EngineJson.Serialize(dispositionOpened.State);
        GameState restored = EngineJson.DeserializeState(
            dispositionJson,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.Equal(dispositionJson, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(dispositionOpened.State), DeterministicHash.ForState(restored));
        LegalAction grip = Assert.Single(
            restored.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters choice
                && choice.OptionKeys.SequenceEqual(["grip"]));
        ApplyResult completed = Apply(fixture, restored, grip);

        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Equal(clicksBeforePlay - 1, completed.State.GetPlayer(fixture.ActivePlayerId).Clicks);
        Assert.Equal(creditsBeforePlay, completed.State.GetPlayer(fixture.ActivePlayerId).Credits);
        Assert.Contains(fixture.StackCardIds[0], completed.State.GetZone(ZoneKind.RunnerGrip).CardIds);
        Assert.Single(completed.State.GetZone(ZoneKind.RunnerHeap).CardIds, fixture.CardId);
        Assert.DoesNotContain(completed.Events, value => value.Kind == EngineEventKinds.CardInstalled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("GatewayCard", "30011")]
    public void MutualFavorSuccessfulRunInstallUsesPrintedCostWithoutAnotherClick(bool remainingStack)
    {
        if (!LifecycleAvailable())
        {
            return;
        }

        Fixture fixture = CreateFixture(
            PlayerSide.Runner,
            GatewayBatch1CardCodes.MutualFavor,
            credits: 8,
            runnerStackCards: remainingStack ? [GatewayCardCodes.Buzzsaw, GatewayBatch1CardCodes.SureGamble] : [GatewayCardCodes.Buzzsaw],
            preInstalledRunnerPrograms:
            [
                GatewayCardCodes.Buzzsaw,
                GatewayCardCodes.Buzzsaw,
                GatewayCardCodes.Buzzsaw,
                GatewayCardCodes.Buzzsaw,
            ]);
        GameState turnAction = CompleteSuccessfulRun(fixture);
        int clicksBeforePlay = turnAction.GetPlayer(fixture.ActivePlayerId).Clicks;
        int creditsBeforePlay = turnAction.GetPlayer(fixture.ActivePlayerId).Credits;
        ApplyResult opened = Apply(fixture, turnAction, SinglePlay(turnAction, fixture.CardId));
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        ApplyResult disposition = Apply(
            fixture,
            opened.State,
            Assert.Single(
                opened.State.PendingDecision!.LegalActions,
                action => action.Parameters is BehaviorChoiceParameters choice
                    && choice.CardIds.SequenceEqual([fixture.StackCardIds[0]])));
        Assert.True(disposition.Accepted, disposition.Rejection?.Message);
        LegalAction install = Assert.Single(
            disposition.State.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters choice
                && choice.OptionKeys.SequenceEqual(["install"]));

        ApplyResult installChoiceOpened = Apply(fixture, disposition.State, install);
        Assert.True(installChoiceOpened.Accepted, installChoiceOpened.Rejection?.Message);
        Assert.Equal(DecisionKind.InstallChoice, installChoiceOpened.State.PendingDecision!.Kind);
        Assert.Equal(fixture.ActivePlayerId, installChoiceOpened.State.PendingDecision.Owner);
        string installJson = EngineJson.Serialize(installChoiceOpened.State);
        GameState installRestored = EngineJson.DeserializeState(
            installJson,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.Equal(installJson, EngineJson.Serialize(installRestored));
        Assert.Equal(DeterministicHash.ForState(installChoiceOpened.State), DeterministicHash.ForState(installRestored));

        LegalAction[] replacementActions = installRestored.PendingDecision!.LegalActions
            .Where(action => action.Parameters is InstallCardParameters parameters
                && !parameters.FinalizeSelection)
            .ToArray();
        Assert.NotEmpty(replacementActions);
        LegalAction replacement = replacementActions[0];
        InstallCardParameters replacementParameters = Assert.IsType<InstallCardParameters>(replacement.Parameters);
        Assert.NotEmpty(replacementParameters.CandidateReplacementCardIds);
        ApplyResult selected = Apply(fixture, installRestored, replacement);
        Assert.True(selected.Accepted, selected.Rejection?.Message);
        LegalAction finalize = Assert.Single(
            selected.State.PendingDecision!.LegalActions,
            action => action.Parameters is InstallCardParameters parameters
                && parameters.FinalizeSelection);
        InstallCardParameters finalParameters = Assert.IsType<InstallCardParameters>(finalize.Parameters);
        Assert.Equal(4, finalParameters.CreditCost);
        ApplyResult completed = Apply(fixture, selected.State, finalize);

        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Equal(clicksBeforePlay - 1, completed.State.GetPlayer(fixture.ActivePlayerId).Clicks);
        Assert.Equal(creditsBeforePlay - 4, completed.State.GetPlayer(fixture.ActivePlayerId).Credits);
        Assert.Equal(
            ZoneKind.RunnerRig,
            completed.State.GetZone(completed.State.GetCard(fixture.StackCardIds[0]).ZoneId).Kind);
        Assert.Contains(fixture.StackCardIds[0], completed.State.GetZone(ZoneKind.RunnerRig).CardIds);
        Assert.Contains(fixture.StackCardIds[0], completed.State.Turn.History!.RunnerThisTurn!.InstalledCardIds);
        PlayerRulesState runnerRules = completed.State.RulesState!.Players
            .Single(player => player.PlayerId == fixture.ActivePlayerId);
        Assert.Equal(4, runnerRules.UsedMemory);
        Assert.Single(completed.State.RulesState.Programs, program =>
            program.CardId == fixture.StackCardIds[0]
            && program.MemoryCost == fixture.RuntimeCatalog.GetSpec(GatewayCardCodes.Buzzsaw).MemoryUnits);
        Assert.Equal(1, completed.Events.Count(value => value.Kind == EngineEventKinds.CardInstalled));
        Assert.Equal(1, completed.Events.Count(value => value.Kind == EngineEventKinds.MemoryAdjusted));
        Assert.DoesNotContain(completed.Events, value => value.Kind == EngineEventKinds.ClickSpent);
        CardInstalledEventPayload installed = Assert.IsType<CardInstalledEventPayload>(
            completed.Events.Single(value => value.Kind == EngineEventKinds.CardInstalled).Payload);
        Assert.Equal(4, installed.Cost);
        Assert.Single(completed.State.GetZone(ZoneKind.RunnerHeap).CardIds, fixture.CardId);
    }

    [Fact]
    [Trait("GatewayCard", "30011")]
    public void MutualFavorSuccessfulRunExposesOnlyGripWhenInstallIsUnaffordable()
    {
        if (!LifecycleAvailable())
        {
            return;
        }

        Fixture fixture = CreateFixture(
            PlayerSide.Runner,
            GatewayBatch1CardCodes.MutualFavor,
            credits: 3,
            runnerStackCards: [GatewayCardCodes.Buzzsaw]);
        GameState turnAction = CompleteSuccessfulRun(fixture);
        ApplyResult opened = Apply(fixture, turnAction, SinglePlay(turnAction, fixture.CardId));
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        ApplyResult disposition = Apply(
            fixture,
            opened.State,
            Assert.Single(
                opened.State.PendingDecision!.LegalActions,
                action => action.Parameters is BehaviorChoiceParameters choice
                    && choice.CardIds.SequenceEqual([fixture.StackCardIds[0]])));
        Assert.True(disposition.Accepted, disposition.Rejection?.Message);
        BehaviorChoiceParameters only = Assert.IsType<BehaviorChoiceParameters>(
            Assert.Single(disposition.State.PendingDecision!.LegalActions).Parameters);
        Assert.Equal("mutual-favor-disposition", only.ChoiceKey);
        Assert.Equal(["grip"], only.OptionKeys);
    }

    private static GameState CompleteSuccessfulRun(Fixture fixture)
    {
        GameState state = OpenTurnAction(fixture);
        ServerId remoteId = state.Servers.Single(server => server.Kind == ServerKind.Remote).Id;
        LegalAction startRun = Assert.Single(
            state.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.StartRun
                && Assert.IsType<RunServerParameters>(action.Parameters).ServerId == remoteId);
        ApplyResult started = Apply(fixture, state, startRun);
        Assert.True(started.Accepted, started.Rejection?.Message);
        state = started.State;
        for (int step = 0; step < 32 && (state.RunState is not null
            || state.PendingDecision?.Kind != DecisionKind.TurnAction); step++)
        {
            PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction pass = Assert.Single(
                pending.LegalActions,
                action => action.Kind == ActionKind.PassPriority);
            ApplyResult result = Apply(fixture, state, pass);
            Assert.True(result.Accepted, result.Rejection?.Message);
            state = result.State;
        }

        Assert.Null(state.RunState);
        Assert.Equal(DecisionKind.TurnAction, state.PendingDecision!.Kind);
        Assert.True(state.Turn.History?.RunnerThisTurn?.SuccessfulRun == true);
        return state;
    }

    private static bool HasBehaviorOption(
        LegalAction action,
        string choiceKey,
        string optionKey) => action.Parameters is BehaviorChoiceParameters choice
            && choice.ChoiceKey == choiceKey
            && choice.OptionKeys.SequenceEqual([optionKey]);

    private static string[] BehaviorOptions(GameState state, string choiceKey) => state
        .PendingDecision!
        .LegalActions
        .Select(action => Assert.IsType<BehaviorChoiceParameters>(action.Parameters))
        .Where(choice => choice.ChoiceKey == choiceKey)
        .SelectMany(choice => choice.OptionKeys)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    private static bool LifecycleAvailable()
    {
        CardDataSet data = CardDataResource.LoadEmbedded();
        bool available = GatewayBatch1BehaviorSpecs.GenericSpecs
            .Select(static definition => definition.DefinitionId.Value)
            .All(code => data.Cards.Any(card => card.Code == code));
        if (available)
        {
            Assert.Equal(CardDataResource.OfficialMode, CardDataResource.EmbeddedMode);
        }
        else
        {
            Assert.Equal(CardDataResource.SyntheticMode, CardDataResource.EmbeddedMode);
        }

        return available;
    }

    private static LegalAction SinglePlay(GameState state, CardInstanceId cardId) => Assert.Single(
        state.PendingDecision!.LegalActions,
        action => action.Kind == ActionKind.PlayCard
            && Assert.IsType<PlayCardParameters>(action.Parameters).CardId == cardId);

    private static IReadOnlyDictionary<string, object?> RunnerPlayerView(
        GameState state,
        IBehaviorCatalog behaviorCatalog)
    {
        Observation observation = ObservationBuilder.Build(state, PlayerSide.Runner, behaviorCatalog);
        IReadOnlyDictionary<string, object?> players =
            Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(observation.PublicState["players"]);
        return Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(players["runner"]);
    }

    private static ApplyResult Apply(Fixture fixture, GameState state, LegalAction action)
    {
        PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
        return GameEngine.Apply(
            state,
            new ActionSubmission(pending.Owner, pending.Id, pending.Revision, action.Id),
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
    }

    private static GameState OpenTurnAction(Fixture fixture)
    {
        GameState startingState = EnsureCorpDeckForTurn(fixture.State, fixture.Side);
        GameState state = GameEngine.BeginTurn(
            startingState,
            fixture.Side,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        while (state.PendingDecision?.Kind != DecisionKind.TurnAction)
        {
            PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction pass = Assert.Single(
                pending.LegalActions,
                action => action.Kind == ActionKind.PassPriority);
            state = Apply(fixture, state, pass).State;
        }

        return state;
    }

    private static GameState EnsureCorpDeckForTurn(GameState state, PlayerSide side)
    {
        if (side != PlayerSide.Corp)
        {
            return state;
        }

        ZoneState deck = state.GetZone(ZoneKind.CorpDeck);
        if (deck.CardIds.Count > 0)
        {
            return state;
        }

        CardInstanceId fillerId = new("card:gateway-lifecycle-corp-turn-filler");
        CardInstanceState filler = new(
            fillerId,
            GatewayCardCodes.Buzzsaw,
            deck.Id,
            PlayerSide.Corp,
            faceUp: false);
        ZoneState[] zones = state.Zones
            .Select(zone => zone.Id == deck.Id
                ? new ZoneState(
                    zone.Id,
                    zone.Kind,
                    zone.OwnerSide,
                    zone.ServerId,
                    zone.CardIds.Append(fillerId).ToArray())
                : zone)
            .ToArray();
        return RebuildState(state, cards: state.Cards.Append(filler).ToArray(), zones: zones);
    }

    private static GameState ContinueToTurnAction(Fixture fixture, GameState state)
    {
        while (state.PendingDecision?.Kind != DecisionKind.TurnAction)
        {
            PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction pass = Assert.Single(
                pending.LegalActions,
                action => action.Kind == ActionKind.PassPriority);
            ApplyResult result = Apply(fixture, state, pass);
            Assert.True(result.Accepted, result.Rejection?.Message);
            state = result.State;
        }

        return state;
    }

    private static GameState AddCorpInstalledCard(
        GameState state,
        CardInstanceId cardId,
        CardDefinitionId definitionId)
    {
        ServerState rnd = state.Servers.Single(server => server.Kind == ServerKind.RAndD);
        ZoneState destination = state.GetZone(rnd.RootZoneId);
        CardInstanceState card = new(
            cardId,
            definitionId,
            destination.Id,
            PlayerSide.Corp,
            faceUp: true);
        ZoneState[] zones = state.Zones
            .Select(zone => zone.Id == destination.Id
                ? new ZoneState(
                    zone.Id,
                    zone.Kind,
                    zone.OwnerSide,
                    zone.ServerId,
                    zone.CardIds.Append(cardId).ToArray())
                : zone)
            .ToArray();
        return RebuildState(state, state.Cards.Append(card), zones: zones);
    }

    private static GameState MoveCardToZone(
        GameState state,
        CardInstanceId cardId,
        ZoneKind destinationKind)
    {
        CardInstanceState card = state.GetCard(cardId);
        ZoneState source = state.GetZone(card.ZoneId);
        ZoneState destination = state.GetZone(destinationKind);
        if (source.Id == destination.Id)
        {
            return state;
        }

        ZoneState[] zones = state.Zones
            .Select(zone => zone.Id == source.Id
                ? new ZoneState(
                    zone.Id,
                    zone.Kind,
                    zone.OwnerSide,
                    zone.ServerId,
                    zone.CardIds.Where(id => id != cardId).ToArray())
                : zone.Id == destination.Id
                    ? new ZoneState(
                        zone.Id,
                        zone.Kind,
                        zone.OwnerSide,
                        zone.ServerId,
                        zone.CardIds.Append(cardId).ToArray())
                    : zone)
            .ToArray();
        CardInstanceState moved = new(
            card.Id,
            card.DefinitionId,
            destination.Id,
            card.OwnerSide,
            faceUp: destinationKind is ZoneKind.CorpDiscard or ZoneKind.RunnerHeap
                || card.FaceUp,
            rezzed: card.Rezzed,
            advancementCounters: card.AdvancementCounters);
        return RebuildState(
            state,
            state.Cards.Select(candidate => candidate.Id == cardId ? moved : candidate).ToArray(),
            zones: zones);
    }

    private static GameState MarkCorpInstalledThisTurn(
        GameState state,
        CardInstanceId cardId)
    {
        TurnHistoryState history = state.Turn.History
            ?? throw new InvalidOperationException("The real-turn fixture must have turn facts.");
        TurnFactsState current = history.CorpThisTurn
            ?? throw new InvalidOperationException("The Corp must have current turn facts.");
        TurnFactsState marked = new(
            current.Version,
            current.Side,
            current.TurnNumber,
            current.ClicksAllotted,
            current.ClicksLost,
            current.TurnBegan,
            current.MandatoryDrawPerformed,
            current.ActionPhaseEnded,
            current.DiscardPhaseEnded,
            current.InstalledCardIds.Append(cardId).ToArray(),
            current.SuccessfulRun);
        TurnHistoryState markedHistory = new(
            history.Version,
            marked,
            history.CorpLastTurn,
            history.RunnerThisTurn,
            history.RunnerLastTurn);
        TurnState markedTurn = new(
            state.Turn.ActiveSide,
            state.Turn.Phase,
            state.Turn.TurnNumber,
            state.Turn.Step,
            markedHistory);
        return RebuildState(state, turn: markedTurn);
    }

    private static GameState RebuildState(
        GameState state,
        IEnumerable<CardInstanceState>? cards = null,
        IEnumerable<ZoneState>? zones = null,
        TurnState? turn = null) =>
        new(
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            players: state.Players,
            cards: cards ?? state.Cards,
            zones: zones ?? state.Zones,
            servers: state.Servers,
            turn: turn ?? state.Turn,
            outcome: state.Outcome,
            randomState: state.RandomState,
            allocator: state.Allocator,
            schemaVersion: state.SchemaVersion,
            decisionRevision: state.DecisionRevision,
            eventSequence: state.EventSequence,
            resolutionStack: state.ResolutionStack,
            pendingDecision: state.PendingDecision,
            actionSequence: state.ActionSequence,
            initialSeed: state.InitialSeed,
            runState: state.RunState,
            rulesState: state.RulesState,
            behaviorRegistryVersion: state.BehaviorRegistryVersion,
            behaviorCatalogFingerprint: state.BehaviorCatalogFingerprint,
            runtimeCardCatalogVersion: state.RuntimeCardCatalogVersion,
            runtimeCardCatalogFingerprint: state.RuntimeCardCatalogFingerprint);

    private static Fixture CreateFixture(
        PlayerSide side,
        CardDefinitionId definitionId,
        int credits,
        int corpDeckCount = 0,
        int runnerStackCount = 0,
        IReadOnlyList<CardDefinitionId>? corpHandExtras = null,
        IReadOnlyList<CardDefinitionId>? runnerGripExtras = null,
        IReadOnlyList<CardDefinitionId>? preInstalledRunnerPrograms = null,
        int corpTags = 0,
        int runnerTags = 0,
        int? clicks = null,
        bool primaryInCorpDeck = false,
        IReadOnlyList<CardDefinitionId>? runnerStackCards = null)
    {
        CardDataSet data = CardDataResource.LoadEmbedded();

        RuntimeCardCatalog runtimeCatalog = GatewayRuntimeCardCatalog.Create(data);
        BehaviorCatalog behaviorCatalog = GatewayBehaviorCatalog.Create();
        GameState baseline = GameState.CreateNew(
            seed: 4402,
            behaviorRegistryVersion: behaviorCatalog.Version,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);

        PlayerId activePlayerId = baseline.Players.Single(player => player.Side == side).Id;
        PlayerState[] players = baseline.Players
            .Select(player => player.Id == activePlayerId
                ? new PlayerState(player.Id, player.Side, credits, clicks ?? player.Clicks, player.AgendaPoints)
                : player)
            .ToArray();

        ZoneState corpHand = baseline.GetZone(ZoneKind.CorpHand);
        ZoneState corpDeck = baseline.GetZone(ZoneKind.CorpDeck);
        ZoneState runnerGrip = baseline.GetZone(ZoneKind.RunnerGrip);
        ZoneState runnerStack = baseline.GetZone(ZoneKind.RunnerStack);
        ZoneState runnerRig = baseline.GetZone(ZoneKind.RunnerRig);

        var cardStates = new List<CardInstanceState>();
        var stackCardIds = new List<CardInstanceId>();
        var zoneCards = new Dictionary<ZoneId, List<CardInstanceId>>
        {
            [corpHand.Id] = [],
            [corpDeck.Id] = [],
            [runnerGrip.Id] = [],
            [runnerStack.Id] = [],
            [runnerRig.Id] = [],
        };

        CardInstanceId primaryCardId = new($"card:gateway-lifecycle-{definitionId.Value}");
        ZoneState primaryZone = primaryInCorpDeck
            ? corpDeck
            : side == PlayerSide.Corp ? corpHand : runnerGrip;
        AddCard(primaryCardId, definitionId, primaryZone, side, faceUp: false);

        int extraIndex = 0;
        foreach (CardDefinitionId extraDefinitionId in corpHandExtras ?? [])
        {
            AddCard(
                new CardInstanceId($"card:gateway-lifecycle-corp-hand-{extraIndex++}"),
                extraDefinitionId,
                corpHand,
                PlayerSide.Corp,
                faceUp: false);
        }

        extraIndex = 0;
        foreach (CardDefinitionId extraDefinitionId in runnerGripExtras ?? [])
        {
            AddCard(
                new CardInstanceId($"card:gateway-lifecycle-runner-grip-{extraIndex++}"),
                extraDefinitionId,
                runnerGrip,
                PlayerSide.Runner,
                faceUp: false);
        }

        int installedIndex = 0;
        foreach (CardDefinitionId installedDefinitionId in preInstalledRunnerPrograms ?? [])
        {
            AddCard(
                new CardInstanceId($"card:gateway-lifecycle-installed-{installedIndex++}"),
                installedDefinitionId,
                runnerRig,
                PlayerSide.Runner,
                faceUp: true);
        }

        for (int index = 0; index < corpDeckCount; index++)
        {
            CardInstanceId cardId = new($"card:gateway-lifecycle-corp-deck-{index}");
            AddCard(cardId, GatewayBatch1CardCodes.HedgeFund, corpDeck, PlayerSide.Corp, faceUp: false);
        }

        for (int index = 0; index < runnerStackCount; index++)
        {
            CardInstanceId cardId = new($"card:gateway-lifecycle-runner-stack-{index}");
            stackCardIds.Add(cardId);
            AddCard(cardId, GatewayBatch1CardCodes.SureGamble, runnerStack, PlayerSide.Runner, faceUp: false);
        }

        int explicitStackIndex = 0;
        foreach (CardDefinitionId stackDefinitionId in runnerStackCards ?? [])
        {
            CardInstanceId cardId = new($"card:gateway-lifecycle-runner-stack-explicit-{explicitStackIndex++}");
            stackCardIds.Add(cardId);
            AddCard(cardId, stackDefinitionId, runnerStack, PlayerSide.Runner, faceUp: false);
        }

        var fixtureAllocator = new StableIdAllocator(baseline.Allocator);
        ServerId remoteId = fixtureAllocator.NextServerId();
        ZoneId remoteIceZoneId = fixtureAllocator.NextZoneId();
        ZoneId remoteRootZoneId = fixtureAllocator.NextZoneId();
        ServerState[] servers = side == PlayerSide.Runner
            ? baseline.Servers
                .Append(new ServerState(remoteId, ServerKind.Remote, remoteIceZoneId, remoteRootZoneId))
                .ToArray()
            : baseline.Servers.ToArray();
        IEnumerable<ZoneState> zoneSequence = baseline.Zones
            .Select(zone => zoneCards.TryGetValue(zone.Id, out List<CardInstanceId>? ids)
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, ids)
                : zone)
            .ToArray();
        if (side == PlayerSide.Runner)
        {
            zoneSequence = zoneSequence
                .Append(new ZoneState(remoteIceZoneId, ZoneKind.ServerIce, PlayerSide.Corp, remoteId))
                .Append(new ZoneState(remoteRootZoneId, ZoneKind.ServerRoot, PlayerSide.Corp, remoteId));
        }
        ZoneState[] zones = zoneSequence.ToArray();

        RulesState defaults = RulesState.CreateDefault(players);
        PlayerId runnerId = players.Single(player => player.Side == PlayerSide.Runner).Id;
        PlayerRulesState[] rulePlayers = defaults.Players
            .Select(player => new PlayerRulesState(
                player.PlayerId,
                player.BaseHandSize,
                player.CoreDamage,
                player.PlayerId == runnerId ? runnerTags : corpTags,
                player.BaseMemory,
                player.UsedMemory))
            .ToArray();
        CardInstanceId[] installedProgramIds = zoneCards[runnerRig.Id]
            .Where(cardId => cardId != primaryCardId)
            .ToArray();
        ProgramMemoryState[] programs = installedProgramIds
            .Select(cardId => new ProgramMemoryState(
                cardId,
                runnerId,
                runtimeCatalog.GetSpec(cardStates.Single(card => card.Id == cardId).DefinitionId).MemoryUnits))
            .ToArray();
        int usedMemory = programs.Sum(program => program.MemoryCost);
        rulePlayers = rulePlayers
            .Select(player => player.PlayerId == runnerId
                ? new PlayerRulesState(
                    player.PlayerId,
                    player.BaseHandSize,
                    player.CoreDamage,
                    player.Tags,
                    player.BaseMemory,
                    usedMemory)
                : player)
            .ToArray();
        RulesState rules = new(
            defaults.Version,
            rulePlayers,
            programs,
            defaults.Counters,
            defaults.LingeringEffects,
            defaults.Triggers,
            defaults.Interrupts);
        GameState state = new(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            players,
            cardStates,
            zones,
            servers,
            new TurnState(side, side == PlayerSide.Corp ? TurnPhase.Corp : TurnPhase.Runner, baseline.Turn.TurnNumber),
            baseline.Outcome,
            baseline.RandomState,
            fixtureAllocator.State,
            baseline.DecisionRevision,
            baseline.EventSequence,
            baseline.ResolutionStack,
            baseline.PendingDecision,
            baseline.ActionSequence,
            baseline.InitialSeed,
            baseline.RunState,
            rules,
            behaviorCatalog.Version,
            behaviorCatalog.Fingerprint,
            runtimeCatalog.Version,
            runtimeCatalog.Fingerprint);
        PlayerId corpPlayerId = players.Single(player => player.Side == PlayerSide.Corp).Id;
        return new Fixture(
            state,
            behaviorCatalog,
            runtimeCatalog,
            side,
            activePlayerId,
            corpPlayerId,
            primaryCardId,
            stackCardIds);

        void AddCard(
            CardInstanceId cardId,
            CardDefinitionId cardDefinitionId,
            ZoneState zone,
            PlayerSide owner,
            bool faceUp)
        {
            cardStates.Add(new CardInstanceState(cardId, cardDefinitionId, zone.Id, owner, faceUp));
            zoneCards[zone.Id].Add(cardId);
        }
    }

    private sealed record Fixture(
        GameState State,
        IBehaviorCatalog BehaviorCatalog,
        RuntimeCardCatalog RuntimeCatalog,
        PlayerSide Side,
        PlayerId ActivePlayerId,
        PlayerId CorpPlayerId,
        CardInstanceId CardId,
        IReadOnlyList<CardInstanceId> StackCardIds);
}

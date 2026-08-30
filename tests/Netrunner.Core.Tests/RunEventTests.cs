using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// M2 Slice 3.4 run-event (Overclock) tests. CR 26.03 1.10.3c/1.10.4: the
/// event's printed cost is paid from the Runner's wallet only; its hosted
/// credits stay on the event and form a run-scoped pool spendable (not
/// losable) during the run. CR 26.03 1.10.3b: losing credits always comes
/// from the wallet and never from the hosted pool. The played event moves to
/// the public play area faceup for the run and is trashed to the public heap
/// faceup at run end; a run plays at most one event, and the run source is
/// validated.
/// </summary>
public sealed class RunEventTests
{
    [Fact]
    public void BeginRunWithRegisteredRunEventChargesCostOnlyAndSeedsRunCredits()
    {
        (GameState preRun, CardInstanceId? eventId) = CreateRunStateWithEvent(runnerCredits: 8);
        PlayerId runnerId = RunnerId(preRun);

        GameEngine.RunStartResult start = GameEngine.BeginRunWithEvents(
            preRun,
            RunTimingTests.RemoteId(preRun),
            initiatingSource: eventId);
        GameState prompt = start.State;

        // CR 1.10.3c: the wallet pays the printed cost (1) only; the hosted
        // credits (5) are not paid — they seed the run-scoped pool.
        Assert.Equal(7, prompt.GetPlayer(runnerId).Credits);
        Assert.Equal(5, prompt.RunState!.RunCredits);
        Assert.Equal(eventId, prompt.RunState.InitiatingSource);
        Assert.Equal(eventId, prompt.RunState.ActiveRunEventId);
        Assert.Equal(RunTimingStep.InitiationPaidWindow, prompt.RunState.Step);

        // The played event leaves the grip and stays faceup in the public play
        // area while the run is live.
        CardInstanceState eventCard = prompt.GetCard(eventId!.Value);
        ZoneState playArea = prompt.Zones.Single(zone => zone.Kind == ZoneKind.RunnerPlayArea && zone.OwnerSide == PlayerSide.Runner);
        Assert.Equal(playArea.Id, eventCard.ZoneId);
        Assert.True(eventCard.FaceUp);
        int playedIndex = start.Events.ToList().FindIndex(value => value.Kind == EngineEventKinds.RunEventPlayed);
        int initiatedIndex = start.Events.ToList().FindIndex(value => value.Kind == EngineEventKinds.RunInitiated);
        Assert.True(playedIndex >= 0 && initiatedIndex >= 0);
        Assert.True(playedIndex < initiatedIndex);
        StateInvariantValidator.Validate(prompt);
    }

    [Fact]
    public void BeginRunWithRunEventFailsClosedWhenRunnerCannotAffordTheCost()
    {
        (GameState preRun, CardInstanceId? eventId) = CreateRunStateWithEvent(runnerCredits: 0);

        ResolutionFailureException exception = Assert.Throws<ResolutionFailureException>(
            () => BeginRunWithSource(preRun, eventId));
        Assert.Contains("costs 1 credits to play", exception.Message);
    }

    [Fact]
    public void RunEventSourceOutsideTheGripIsRejected()
    {
        // A program in the rig is not a legal run source, even though it is
        // Runner-owned and faceup.
        (GameState preRun, CardInstanceId? eventId) = CreateRunStateWithEvent(
            runnerCredits: 8,
            eventZoneKind: ZoneKind.RunnerRig);

        ResolutionFailureException exception = Assert.Throws<ResolutionFailureException>(
            () => BeginRunWithSource(preRun, eventId));
        Assert.Contains("must be a card in the Runner's grip", exception.Message);
    }

    [Fact]
    public void UnregisteredEventSourceIsRejected()
    {
        CardDefinitionId notARunEvent = new("fixture:event-not-a-run-event");
        (GameState preRun, CardInstanceId? eventId) = CreateRunStateWithEvent(
            runnerCredits: 8,
            eventDefinition: notARunEvent);

        ResolutionFailureException exception = Assert.Throws<ResolutionFailureException>(
            () => BeginRunWithSource(preRun, eventId));
        Assert.Contains("not a registered run event", exception.Message);
    }

    [Fact]
    public void FacedownGripCardThatIsNotARunEventIsRejected()
    {
        CardDefinitionId notARunEvent = new("fixture:event-not-a-run-event");
        (GameState preRun, CardInstanceId? eventId) = CreateRunStateWithEvent(
            runnerCredits: 8,
            eventDefinition: notARunEvent,
            eventFaceUp: false);

        ResolutionFailureException exception = Assert.Throws<ResolutionFailureException>(
            () => BeginRunWithSource(preRun, eventId));
        Assert.Contains("not a registered run event", exception.Message);
    }

    [Fact]
    public void BreakCostCanBePaidFromHostedRunCreditsByChoice()
    {
        (GameState preRun, CardInstanceId? eventId) = CreateRunStateWithEvent(
            iceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines,
            runnerCredits: 6);
        GameState prompt = BeginRunWithSource(preRun, eventId);
        Assert.Equal(5, prompt.GetPlayer(RunnerId(prompt)).Credits);
        Assert.Equal(5, prompt.RunState!.RunCredits);
        GameState encounter = ReachEncounterWithSource(preRun, eventId);

        // CR 1.10.3c: both legal divisions are materialized. Select the
        // all-hosted division explicitly.
        LegalAction breakOne = Assert.Single(encounter.PendingDecision!.LegalActions, action =>
            action.Kind == ActionKind.BreakSubroutines
            && Assert.IsType<RunBreakParameters>(action.Parameters).Quantity == 1
            && Assert.IsType<RunBreakParameters>(action.Parameters).HostedCredits == 1);

        ApplyResult broken = RunTimingTests.ApplyAction(encounter, breakOne);
        Assert.True(broken.Accepted, broken.Rejection?.Message);
        Assert.Equal(5, broken.State.GetPlayer(RunnerId(broken.State)).Credits);
        Assert.Equal(4, broken.State.RunState!.RunCredits);

        RunCreditsAdjustedEventPayload adjusted = Assert.IsType<RunCreditsAdjustedEventPayload>(
            broken.Events.Single(value => value.Kind == EngineEventKinds.RunCreditsAdjusted).Payload);
        Assert.Equal(broken.State.RunState.Id, adjusted.RunId);
        Assert.Equal(-1, adjusted.Delta);
        Assert.Equal(4, adjusted.RunCreditsAfter);
    }

    [Fact]
    public void PumpCostCanBePaidFromHostedRunCreditsByChoice()
    {
        (GameState preRun, CardInstanceId? eventId) = CreateRunStateWithEvent(
            iceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines,
            runnerCredits: 6);
        BeginRunWithSource(preRun, eventId);
        GameState encounter = ReachEncounterWithSource(preRun, eventId);

        LegalAction pump = Assert.Single(encounter.PendingDecision!.LegalActions, action =>
            action.Kind == ActionKind.PumpBreaker
            && Assert.IsType<RunBreakerParameters>(action.Parameters).HostedCredits == 2);

        ApplyResult pumped = RunTimingTests.ApplyAction(encounter, pump);
        Assert.True(pumped.Accepted, pumped.Rejection?.Message);
        Assert.Equal(5, pumped.State.GetPlayer(RunnerId(pumped.State)).Credits);
        Assert.Equal(3, pumped.State.RunState!.RunCredits);

        RunCreditsAdjustedEventPayload adjusted = Assert.IsType<RunCreditsAdjustedEventPayload>(
            pumped.Events.Single(value => value.Kind == EngineEventKinds.RunCreditsAdjusted).Payload);
        Assert.Equal(-2, adjusted.Delta);
        Assert.Equal(3, adjusted.RunCreditsAfter);
        Assert.Contains(pumped.Events, value => value.Kind == EngineEventKinds.BreakerPumped);
    }

    [Fact]
    public void PumpCostSplittingOffersEveryLegalWalletHostedDivision()
    {
        // Spark hosts a 1-credit pool. Starting wallet 5, the event cost (1)
        // leaves wallet 4 and pool 1. The pump costs 2, so CR 1.10.3c gives
        // the Runner two legal divisions: wallet-only and 1 wallet + 1
        // hosted. The actions are serialized choices, not an engine policy.
        (GameState preRun, CardInstanceId? eventId) = CreateRunStateWithEvent(
            iceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines,
            runnerCredits: 5,
            eventDefinition: FixtureRunEventSpecs.Spark);
        GameState prompt = BeginRunWithSource(preRun, eventId);
        PlayerId runnerId = RunnerId(prompt);
        Assert.Equal(4, prompt.GetPlayer(runnerId).Credits);
        Assert.Equal(1, prompt.RunState!.RunCredits);
        GameState encounter = ReachEncounterWithSource(preRun, eventId);

        LegalAction walletOnly = Assert.Single(encounter.PendingDecision!.LegalActions, action =>
            action.Kind == ActionKind.PumpBreaker
            && Assert.IsType<RunBreakerParameters>(action.Parameters).HostedCredits == 0);
        LegalAction split = Assert.Single(encounter.PendingDecision!.LegalActions, action =>
            action.Kind == ActionKind.PumpBreaker
            && Assert.IsType<RunBreakerParameters>(action.Parameters).HostedCredits == 1);

        ApplyResult walletPaid = RunTimingTests.ApplyAction(encounter, walletOnly);
        Assert.True(walletPaid.Accepted, walletPaid.Rejection?.Message);
        Assert.Equal(2, walletPaid.State.GetPlayer(runnerId).Credits);
        Assert.Equal(1, walletPaid.State.RunState!.RunCredits);
        Assert.DoesNotContain(walletPaid.Events, value => value.Kind == EngineEventKinds.RunCreditsAdjusted);
        Assert.Contains(walletPaid.Events, value => value.Kind == EngineEventKinds.CreditsAdjusted);
        Assert.Contains(walletPaid.Events, value => value.Kind == EngineEventKinds.BreakerPumped);

        ApplyResult splitPaid = RunTimingTests.ApplyAction(encounter, split);
        Assert.True(splitPaid.Accepted, splitPaid.Rejection?.Message);
        Assert.Equal(3, splitPaid.State.GetPlayer(runnerId).Credits);
        Assert.Equal(0, splitPaid.State.RunState!.RunCredits);
        RunCreditsAdjustedEventPayload runDraw = Assert.IsType<RunCreditsAdjustedEventPayload>(
            splitPaid.Events.Single(value => value.Kind == EngineEventKinds.RunCreditsAdjusted).Payload);
        Assert.Equal(-1, runDraw.Delta);
        Assert.Equal(0, runDraw.RunCreditsAfter);
        CreditsAdjustedEventPayload walletDraw = Assert.IsType<CreditsAdjustedEventPayload>(
            splitPaid.Events.Single(value => value.Kind == EngineEventKinds.CreditsAdjusted).Payload);
        Assert.Equal(runnerId, walletDraw.Target);
        Assert.Equal(-1, walletDraw.Delta);
        Assert.Equal(3, walletDraw.CreditsAfter);
        Assert.Contains(splitPaid.Events, value => value.Kind == EngineEventKinds.BreakerPumped);
    }

    [Fact]
    public void CreditLossSubroutineComesFromTheWalletOnly()
    {
        // CR 1.10.3b: the loss is taken from the debtor's credit pool (wallet)
        // only; the run-scoped hosted pool (Overclock) is never touched.
        (GameState preRun, CardInstanceId? eventId) = CreateRunStateWithEvent(
            iceDefinition: FixtureIceBehaviorSpecs.LoseCreditsOnly,
            runnerCredits: 6);
        GameState prompt = BeginRunWithSource(preRun, eventId);
        Assert.Equal(5, prompt.GetPlayer(RunnerId(prompt)).Credits);
        Assert.Equal(5, prompt.RunState!.RunCredits);

        ApplyResult movement = PassTwice(PassTwice(PassTwice(prompt).State).State);
        Assert.True(movement.Accepted, movement.Rejection?.Message);
        Assert.Equal(RunTimingStep.MovementPaidWindow, movement.State.RunState!.Step);
        Assert.Equal(3, movement.State.GetPlayer(RunnerId(movement.State)).Credits);
        Assert.Equal(5, movement.State.RunState.RunCredits);

        CreditsAdjustedEventPayload walletLoss = Assert.IsType<CreditsAdjustedEventPayload>(
            movement.Events.Single(value => value.Kind == EngineEventKinds.CreditsAdjusted).Payload);
        Assert.Equal(-2, walletLoss.Delta);
        Assert.Equal(3, walletLoss.CreditsAfter);
        Assert.DoesNotContain(movement.Events, value => value.Kind == EngineEventKinds.RunCreditsAdjusted);
    }

    [Fact]
    public void CreditLossIsClampedToTheWalletAndLeavesTheRunPoolUntouched()
    {
        // CR 1.10.3b: "or 1 if that is all they have" — the loss is clamped
        // to the wallet's balance and the hosted pool is still untouched.
        (GameState preRun, CardInstanceId? eventId) = CreateRunStateWithEvent(
            iceDefinition: FixtureIceBehaviorSpecs.LoseCreditsOnly,
            runnerCredits: 2);
        GameState prompt = BeginRunWithSource(preRun, eventId);
        Assert.Equal(1, prompt.GetPlayer(RunnerId(prompt)).Credits);
        Assert.Equal(5, prompt.RunState!.RunCredits);

        ApplyResult movement = PassTwice(PassTwice(PassTwice(prompt).State).State);
        Assert.True(movement.Accepted, movement.Rejection?.Message);
        Assert.Equal(RunTimingStep.MovementPaidWindow, movement.State.RunState!.Step);
        Assert.Equal(0, movement.State.GetPlayer(RunnerId(movement.State)).Credits);
        Assert.Equal(5, movement.State.RunState.RunCredits);
        Assert.DoesNotContain(movement.Events, value => value.Kind == EngineEventKinds.RunCreditsAdjusted);
    }

    [Fact]
    public void EndRunIfCreditsAtMostCountsAvailableCreditsWithoutARunEvent()
    {
        // Wallet 7: lose 3 -> 4, which is 6 or fewer, so the run ends.
        (GameState preRun, _) = CreateRunStateWithEvent(
            iceDefinition: FixtureIceBehaviorSpecs.EndRunIfCredits,
            runnerCredits: 7,
            includeEventSource: false);
        GameState prompt = GameEngine.BeginRun(preRun, RunTimingTests.RemoteId(preRun));
        ApplyResult ended = PassTwice(PassTwice(PassTwice(prompt).State).State);
        Assert.True(ended.Accepted, ended.Rejection?.Message);
        Assert.Null(ended.State.RunState);
        Assert.Equal(4, ended.State.GetPlayer(RunnerId(ended.State)).Credits);
        RunEndedEventPayload runEnded = Assert.IsType<RunEndedEventPayload>(
            ended.Events.Single(value => value.Kind == EngineEventKinds.RunEnded).Payload);
        Assert.Equal(RunEndReason.EndedByRule, runEnded.Reason);

        // Wallet 10: lose 3 -> 7, above the limit, so the run continues.
        (GameState preRun2, _) = CreateRunStateWithEvent(
            iceDefinition: FixtureIceBehaviorSpecs.EndRunIfCredits,
            runnerCredits: 10,
            includeEventSource: false);
        GameState prompt2 = GameEngine.BeginRun(preRun2, RunTimingTests.RemoteId(preRun2));
        ApplyResult continued = PassTwice(PassTwice(PassTwice(prompt2).State).State);
        Assert.True(continued.Accepted, continued.Rejection?.Message);
        Assert.Equal(RunTimingStep.MovementPaidWindow, continued.State.RunState!.Step);
        Assert.Equal(7, continued.State.GetPlayer(RunnerId(continued.State)).Credits);
    }

    [Fact]
    public void EndRunIfCreditsAtMostIgnoresHostedRunCredits()
    {
        // CR 1.13.3 checks only the credits in the Runner's wallet. Hosted
        // Overclock credits are a separate spend source and do not satisfy a
        // "has N credits or less" condition.
        (GameState preRun, CardInstanceId? eventId) = CreateRunStateWithEvent(
            iceDefinition: FixtureIceBehaviorSpecs.EndRunIfCredits,
            runnerCredits: 5);
        GameState prompt = BeginRunWithSource(preRun, eventId);
        Assert.Equal(4, prompt.GetPlayer(RunnerId(prompt)).Credits);
        ApplyResult ended = PassTwice(PassTwice(PassTwice(prompt).State).State);
        Assert.True(ended.Accepted, ended.Rejection?.Message);
        Assert.Null(ended.State.RunState);
        Assert.Equal(1, ended.State.GetPlayer(RunnerId(ended.State)).Credits);
        CreditsAdjustedEventPayload walletLoss = Assert.IsType<CreditsAdjustedEventPayload>(
            ended.Events.Single(value => value.Kind == EngineEventKinds.CreditsAdjusted).Payload);
        Assert.Equal(-3, walletLoss.Delta);
        Assert.Equal(1, walletLoss.CreditsAfter);
        RunCreditsAdjustedEventPayload expiry = Assert.IsType<RunCreditsAdjustedEventPayload>(
            ended.Events.Single(value => value.Kind == EngineEventKinds.RunCreditsAdjusted).Payload);
        Assert.Equal(-5, expiry.Delta);
        Assert.Equal(0, expiry.RunCreditsAfter);
        RunEndedEventPayload runEnded = Assert.IsType<RunEndedEventPayload>(
            ended.Events.Single(value => value.Kind == EngineEventKinds.RunEnded).Payload);
        Assert.Equal(RunEndReason.EndedByRule, runEnded.Reason);

        // Wallet 5 + run 5: lose 3 from the wallet -> wallet 2. The wallet is
        // at or below the limit, so the run ends even though hosted credits
        // remain.
        (GameState preRun2, CardInstanceId? eventId2) = CreateRunStateWithEvent(
            iceDefinition: FixtureIceBehaviorSpecs.EndRunIfCredits,
            runnerCredits: 6);
        GameState prompt2 = BeginRunWithSource(preRun2, eventId2);
        ApplyResult ended2 = PassTwice(PassTwice(PassTwice(prompt2).State).State);
        Assert.True(ended2.Accepted, ended2.Rejection?.Message);
        Assert.Null(ended2.State.RunState);
        Assert.Equal(2, ended2.State.GetPlayer(RunnerId(ended2.State)).Credits);
        Assert.Contains(ended2.Events, value => value.Kind == EngineEventKinds.RunEnded);
    }

    [Fact]
    public void UnspentRunCreditsExpireAtRunEndWithoutReturningToTheWallet()
    {
        (GameState preRun, CardInstanceId? eventId) = CreateRunStateWithEvent(runnerCredits: 6, withIce: false);
        GameState prompt = BeginRunWithSource(preRun, eventId);
        PlayerId runnerId = RunnerId(prompt);
        Assert.Equal(5, prompt.GetPlayer(runnerId).Credits);

        // No ICE: the run succeeds immediately and the breach completes.
        ApplyResult finished = RunTimingTests.PassTwice(prompt);
        Assert.True(finished.Accepted, finished.Rejection?.Message);
        Assert.Null(finished.State.RunState);
        Assert.Equal(5, finished.State.GetPlayer(runnerId).Credits);

        RunCreditsAdjustedEventPayload expired = Assert.IsType<RunCreditsAdjustedEventPayload>(
            finished.Events.Single(value => value.Kind == EngineEventKinds.RunCreditsAdjusted).Payload);
        Assert.Equal(-5, expired.Delta);
        Assert.Equal(0, expired.RunCreditsAfter);
        RunEndedEventPayload ended = Assert.IsType<RunEndedEventPayload>(
            finished.Events.Single(value => value.Kind == EngineEventKinds.RunEnded).Payload);
        Assert.Equal(RunEndReason.BreachCompleted, ended.Reason);
    }

    [Fact]
    public void RunEventIsTrashedToTheHeapFaceupAtRunEnd()
    {
        (GameState preRun, CardInstanceId? eventId) = CreateRunStateWithEvent(runnerCredits: 6, withIce: false);
        GameState prompt = BeginRunWithSource(preRun, eventId);
        Assert.True(prompt.GetCard(eventId!.Value).FaceUp);
        ZoneState playArea = prompt.Zones.Single(zone => zone.Kind == ZoneKind.RunnerPlayArea && zone.OwnerSide == PlayerSide.Runner);
        ZoneState heap = prompt.Zones.Single(zone => zone.Kind == ZoneKind.RunnerHeap && zone.OwnerSide == PlayerSide.Runner);

        // No ICE: the run succeeds immediately and the breach completes.
        ApplyResult finished = RunTimingTests.PassTwice(prompt);
        Assert.True(finished.Accepted, finished.Rejection?.Message);
        Assert.Null(finished.State.RunState);

        // CR 6.9.6/8.6.7: the played event is trashed to the public heap
        // faceup.
        CardInstanceState eventCard = finished.State.GetCard(eventId.Value);
        Assert.Equal(heap.Id, eventCard.ZoneId);
        Assert.True(eventCard.FaceUp);
        CardMovedEventPayload moved = Assert.IsType<CardMovedEventPayload>(
            finished.Events.Single(value => value.Kind == EngineEventKinds.CardMoved).Payload);
        Assert.Equal(eventId.Value, moved.CardId);
        Assert.Equal(playArea.Id, moved.FromZoneId);
        Assert.Equal(heap.Id, moved.ToZoneId);
        StateInvariantValidator.Validate(finished.State);
    }

    [Fact]
    public void ConsecutiveEventRunsReuseTheSinglePlayAreaZone()
    {
        (GameState preRun, CardInstanceId? firstEventId) = CreateRunStateWithEvent(
            runnerCredits: 8,
            withIce: false);
        GameState firstPrompt = BeginRunWithSource(preRun, firstEventId);
        ZoneState firstPlayArea = firstPrompt.Zones.Single(zone =>
            zone.Kind == ZoneKind.RunnerPlayArea
            && zone.OwnerSide == PlayerSide.Runner);

        ApplyResult firstFinished = RunTimingTests.PassTwice(firstPrompt);
        Assert.True(firstFinished.Accepted, firstFinished.Rejection?.Message);
        Assert.Null(firstFinished.State.RunState);
        Assert.Empty(firstFinished.State.Zones.Single(zone => zone.Id == firstPlayArea.Id).CardIds);

        (GameState secondPreRun, CardInstanceId secondEventId) = AddEventSource(
            firstFinished.State,
            FixtureRunEventSpecs.Overclock);
        GameEngine.RunStartResult secondStart = GameEngine.BeginRunWithEvents(
            secondPreRun,
            RunTimingTests.RemoteId(secondPreRun),
            initiatingSource: secondEventId);

        Assert.Single(secondStart.State.Zones, zone => zone.Kind == ZoneKind.RunnerPlayArea);
        ZoneState secondPlayArea = secondStart.State.Zones.Single(zone =>
            zone.Kind == ZoneKind.RunnerPlayArea
            && zone.OwnerSide == PlayerSide.Runner);
        Assert.Equal(firstPlayArea.Id, secondPlayArea.Id);
        Assert.Equal(secondPlayArea.Id, secondStart.State.GetCard(secondEventId).ZoneId);
        Assert.Single(secondPlayArea.CardIds, secondEventId);

        ApplyResult secondFinished = RunTimingTests.PassTwice(secondStart.State);
        Assert.True(secondFinished.Accepted, secondFinished.Rejection?.Message);
        Assert.Null(secondFinished.State.RunState);
        Assert.Single(secondFinished.State.Zones, zone => zone.Kind == ZoneKind.RunnerPlayArea);
        StateInvariantValidator.Validate(secondFinished.State);
    }

    [Fact]
    public void ActiveRunEventIsFaceupAndVisibleToBothViewersDuringTheRun()
    {
        (GameState preRun, CardInstanceId? eventId) = CreateRunStateWithEvent(runnerCredits: 8, withIce: false);
        GameState prompt = BeginRunWithSource(preRun, eventId);

        // The run view's initiatingSource candidate view is faceup, so both
        // viewers see the full card view (id and definition) while the run
        // is live (CR 26.03 8.6.7: a played event is faceup while active).
        string expectedSource = $"\"initiatingSource\":{{\"id\":\"{eventId!.Value}\",\"definitionId\":\"{FixtureRunEventSpecs.Overclock.Value}\"";
        foreach (PlayerSide viewer in new[] { PlayerSide.Runner, PlayerSide.Corp })
        {
            string json = EngineJson.Serialize(ObservationBuilder.Build(prompt, viewer));
            Assert.Contains(expectedSource, json);
        }

        // After the run ends the event is public in the heap, so both viewers
        // continue to see the card after the run.
        CardInstanceId activeEvent = eventId!.Value;
        ApplyResult finished = RunTimingTests.PassTwice(prompt);
        Assert.True(finished.Accepted, finished.Rejection?.Message);
        foreach (PlayerSide viewer in new[] { PlayerSide.Runner, PlayerSide.Corp })
        {
            string json = EngineJson.Serialize(ObservationBuilder.Build(finished.State, viewer));
            Assert.Contains(activeEvent.Value, json);
            Assert.Contains(FixtureRunEventSpecs.Overclock.Value, json);
        }
    }

    [Fact]
    public void RunCreditsSurviveSaveLoadAndContinueIdentically()
    {
        (GameState preRun, CardInstanceId? eventId) = CreateRunStateWithEvent(
            iceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines,
            runnerCredits: 6);
        GameState prompt = BeginRunWithSource(preRun, eventId);
        GameState encounter = ReachEncounterWithSource(preRun, eventId);

        string json = EngineJson.Serialize(encounter);
        GameState restored = EngineJson.DeserializeState(json);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(eventId, restored.RunState!.ActiveRunEventId);

        LegalAction leftBreak = Assert.Single(encounter.PendingDecision!.LegalActions, action =>
            action.Kind == ActionKind.BreakSubroutines
            && Assert.IsType<RunBreakParameters>(action.Parameters).Quantity == 1
            && Assert.IsType<RunBreakParameters>(action.Parameters).HostedCredits == 1);
        LegalAction rightBreak = Assert.Single(restored.PendingDecision!.LegalActions, action =>
            action.Kind == ActionKind.BreakSubroutines
            && Assert.IsType<RunBreakParameters>(action.Parameters).Quantity == 1
            && Assert.IsType<RunBreakParameters>(action.Parameters).HostedCredits == 1);
        ApplyResult left = RunTimingTests.ApplyAction(encounter, leftBreak);
        ApplyResult right = RunTimingTests.ApplyAction(restored, rightBreak);
        Assert.True(left.Accepted, left.Rejection?.Message);
        Assert.True(right.Accepted, right.Rejection?.Message);
        Assert.Equal(EngineJson.Serialize(left.State), EngineJson.Serialize(right.State));
        Assert.Equal(DeterministicHash.ForEvents(left.Events), DeterministicHash.ForEvents(right.Events));
        Assert.Equal(4, left.State.RunState!.RunCredits);
    }

    [Fact]
    public void RunEventCatalogIsIdempotentConflictThrowsAndUnregisteredFailsClosed()
    {
        RunEventBehaviorSpec canonical = new(FixtureRunEventSpecs.Overclock, cost: 1, hostedCredits: 5);
        CardBehaviorSpecCatalog.RegisterRunEvent(canonical);
        CardBehaviorSpecCatalog.RegisterRunEvent(canonical);
        Assert.Equal(canonical, CardBehaviorSpecCatalog.GetRunEvent(FixtureRunEventSpecs.Overclock));

        RunEventBehaviorSpec conflicting = new(FixtureRunEventSpecs.Overclock, cost: 1, hostedCredits: 4);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => CardBehaviorSpecCatalog.RegisterRunEvent(conflicting));
        Assert.Contains(FixtureRunEventSpecs.Overclock.Value, exception.Message);
        Assert.Equal(canonical, CardBehaviorSpecCatalog.GetRunEvent(FixtureRunEventSpecs.Overclock));

        CardDefinitionId unregistered = new("fixture:event-unregistered");
        Assert.False(CardBehaviorSpecCatalog.TryGetRunEvent(unregistered, out _));
        Assert.Throws<ArgumentException>(() => CardBehaviorSpecCatalog.GetRunEvent(unregistered));
    }

    [Fact]
    public void NewRunEventPayloadsRoundTripAndValidate()
    {
        RunId runId = new("run:1");
        RunEventPlayedEventPayload played = new(
            runId,
            new CardInstanceId("card:run-event"),
            FixtureRunEventSpecs.Overclock,
            cost: 1,
            hostedCredits: 5,
            new PlayerId("runner:1"),
            runnerCreditsAfter: 2);
        EngineEvent playedEvent = new(1, 42, EngineEventKinds.RunEventPlayed, played);
        EngineEvent restoredPlayed = EngineJson.Deserialize<EngineEvent>(EngineJson.Serialize(playedEvent));
        Assert.Equal(playedEvent, restoredPlayed);
        Assert.Empty(EngineEventValidator.Collect(restoredPlayed));

        RunCreditsAdjustedEventPayload adjusted = new(runId, delta: -3, runCreditsAfter: 2);
        EngineEvent adjustedEvent = new(1, 43, EngineEventKinds.RunCreditsAdjusted, adjusted);
        EngineEvent restoredAdjusted = EngineJson.Deserialize<EngineEvent>(EngineJson.Serialize(adjustedEvent));
        Assert.Equal(adjustedEvent, restoredAdjusted);
        Assert.Empty(EngineEventValidator.Collect(restoredAdjusted));
    }

    private static PlayerId RunnerId(GameState state) =>
        state.Players.Single(player => player.Side == PlayerSide.Runner).Id;

    private static (GameState State, CardInstanceId EventId) AddEventSource(
        GameState state,
        CardDefinitionId definitionId)
    {
        ZoneState grip = state.Zones.Single(zone =>
            zone.Kind == ZoneKind.RunnerGrip
            && zone.OwnerSide == PlayerSide.Runner);
        StableIdAllocator allocator = new(state.Allocator);
        CardInstanceId eventId = allocator.NextCardInstanceId();
        var cards = state.Cards.Append(new CardInstanceState(
            eventId,
            definitionId,
            grip.Id,
            PlayerSide.Runner,
            faceUp: true)).ToArray();
        ZoneState populatedGrip = new(
            grip.Id,
            grip.Kind,
            grip.OwnerSide,
            grip.ServerId,
            [.. grip.CardIds, eventId]);
        GameState result = new(
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            state.Players,
            cards,
            state.Zones.Select(zone => zone.Id == grip.Id ? populatedGrip : zone).ToArray(),
            state.Servers,
            state.Turn,
            state.Outcome,
            state.RandomState,
            allocator.State,
            state.SchemaVersion,
            state.DecisionRevision,
            state.EventSequence,
            state.ResolutionStack,
            state.PendingDecision,
            state.ActionSequence,
            state.InitialSeed,
            runState: state.RunState,
            rulesState: state.RulesState);
        StateInvariantValidator.Validate(result);
        return (result, eventId);
    }

    private static GameState BeginRunWithSource(GameState state, CardInstanceId? source) =>
        GameEngine.BeginRun(state, RunTimingTests.RemoteId(state), initiatingSource: source);

    private static GameState ReachEncounterWithSource(GameState state, CardInstanceId? source) =>
        EncounterBreakerTests.ReachEncounterWindowWithSource(state, source!.Value);

    /// <summary>
    /// Builds the pre-run fixture: one rezzed ICE on a remote server with the
    /// fixture breaker installed in the rig (so the encounter window offers
    /// break and pump), plus an optional Runner-owned event card placed
    /// faceup in the grip (or another Runner zone, for source-validation
    /// tests).
    /// </summary>
    private static (GameState State, CardInstanceId? EventId) CreateRunStateWithEvent(
        CardDefinitionId? iceDefinition = null,
        int runnerCredits = 6,
        bool includeEventSource = true,
        bool withIce = true,
        CardDefinitionId? eventDefinition = null,
        ZoneKind eventZoneKind = ZoneKind.RunnerGrip,
        bool eventFaceUp = true)
    {
        GameState baseState = withIce
            ? EncounterBreakerTests.CreateRunStateWithBreaker(
                FixtureIceBehaviorSpecs.BreakerBarrier,
                iceDefinition: iceDefinition ?? FixtureIceBehaviorSpecs.Plain).State
            : RunTimingTests.CreateRunState(iceCount: 0, rezzed: false);

        var players = baseState.Players.Select(player => player.Side == PlayerSide.Runner
            ? new PlayerState(player.Id, player.Side, runnerCredits, player.Clicks, player.AgendaPoints)
            : player).ToArray();

        GameState state = new(
            baseState.GameId,
            baseState.RulesVersion,
            baseState.CardDataVersion,
            players: players,
            cards: baseState.Cards,
            zones: baseState.Zones,
            servers: baseState.Servers,
            turn: baseState.Turn,
            outcome: baseState.Outcome,
            randomState: baseState.RandomState,
            allocator: baseState.Allocator,
            schemaVersion: baseState.SchemaVersion,
            decisionRevision: baseState.DecisionRevision,
            eventSequence: baseState.EventSequence,
            resolutionStack: baseState.ResolutionStack,
            pendingDecision: baseState.PendingDecision,
            actionSequence: baseState.ActionSequence,
            initialSeed: baseState.InitialSeed,
            runState: baseState.RunState);
        StateInvariantValidator.Validate(state);
        if (!includeEventSource)
        {
            return (state, null);
        }

        ZoneState eventZone = state.Zones.Single(zone => zone.Kind == eventZoneKind && zone.OwnerSide == PlayerSide.Runner);
        StableIdAllocator allocator = new(state.Allocator);
        CardInstanceId eventId = allocator.NextCardInstanceId();
        var cards = state.Cards.Append(new CardInstanceState(
            eventId,
            eventDefinition ?? FixtureRunEventSpecs.Overclock,
            eventZone.Id,
            PlayerSide.Runner,
            faceUp: eventFaceUp)).ToArray();
        ZoneState zoneWithEvent = new(eventZone.Id, eventZone.Kind, eventZone.OwnerSide, eventZone.ServerId, [.. eventZone.CardIds, eventId]);
        GameState next = new(
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            players: state.Players,
            cards: cards,
            zones: state.Zones.Select(zone => zone.Id == eventZone.Id ? zoneWithEvent : zone).ToArray(),
            servers: state.Servers,
            turn: state.Turn,
            outcome: state.Outcome,
            randomState: state.RandomState,
            allocator: allocator.State,
            schemaVersion: state.SchemaVersion,
            decisionRevision: state.DecisionRevision,
            eventSequence: state.EventSequence,
            resolutionStack: state.ResolutionStack,
            pendingDecision: state.PendingDecision,
            actionSequence: state.ActionSequence,
            initialSeed: state.InitialSeed,
            runState: state.RunState);
        StateInvariantValidator.Validate(next);
        return (next, eventId);
    }

    private static ApplyResult PassTwice(GameState state) => RunTimingTests.PassTwice(state);
}

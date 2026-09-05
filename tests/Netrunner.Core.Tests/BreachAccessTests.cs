using Xunit;

namespace Netrunner.Core.Tests;

using static RunTimingTests;

/// <summary>
/// CR 7.x breach and access tests: deterministic candidate selection
/// (7.5.4/7.3.4) and sequential access through the 7.2 mid-access window:
/// a non-agenda card offers the basic trash ability (7.1.5) or no action,
/// and an accessed agenda offers the pass only, with the mandatory steal
/// (7.2.3/7.1.6) resolving automatically when the window is passed.
/// </summary>
public sealed class BreachAccessTests
{
    private static readonly CardDefinitionId FixtureAsset = new("fixture-asset");

    private static readonly CardDefinitionId FixturePlain = new("fixture-plain");

    private static readonly CardDefinitionId FixtureAgenda = new("fixture-agenda");

    [Fact]
    public void RemoteBreachAccessesRootCardsInChosenOrderAndEndsRun()
    {
        GameState fixture = CreateBreachState(remoteRoot: [false, false]);
        ServerId remoteId = fixture.Servers.Single(server => server.Kind == ServerKind.Remote).Id;
        ZoneState root = fixture.Zones.Single(zone => zone.Id == fixture.GetServer(remoteId).RootZoneId);
        CardInstanceId first = root.CardIds[0];
        CardInstanceId second = root.CardIds[1];

        ApplyResult breach = PassTwice(BeginRun(fixture, remoteId));
        Assert.True(breach.Accepted, breach.Rejection?.Message);
        GameState candidateState = breach.State;
        AssertRunPrompt(candidateState, RunTimingStep.BreachCandidateSelection, PlayerSide.Runner, ActionKind.ChooseBreachCandidate);
        BreachStartedEventPayload started = Assert.IsType<BreachStartedEventPayload>(
            breach.Events.Single(value => value.Kind == EngineEventKinds.BreachStarted).Payload);
        Assert.Equal(2, started.CandidateCount);
        Assert.Equal(0, started.RandomAccessLimit);

        PendingDecision decision = candidateState.PendingDecision!;
        var offered = decision.LegalActions.Select(value => (value.Kind, value.Parameters)).ToArray();
        Assert.Equal(2, offered.Length);
        Assert.All(offered, value => Assert.Equal(ActionKind.ChooseBreachCandidate, value.Kind));
        Assert.Equal(first, Assert.IsType<BreachCandidateParameters>(offered[0].Parameters).CardId);
        Assert.Equal(second, Assert.IsType<BreachCandidateParameters>(offered[1].Parameters).CardId);
        Assert.All(offered, value => Assert.Null(Assert.IsType<BreachCandidateParameters>(value.Parameters).Source));

        ApplyResult chooseSecond = ApplyAction(candidateState, decision.LegalActions[1]);
        Assert.True(chooseSecond.Accepted, chooseSecond.Rejection?.Message);
        BreachCandidateChosenEventPayload chosenSecond = Assert.IsType<BreachCandidateChosenEventPayload>(
            chooseSecond.Events.Single(value => value.Kind == EngineEventKinds.BreachCandidateChosen).Payload);
        Assert.Equal(second, chosenSecond.CardId);
        Assert.Null(chosenSecond.Source);
        CardAccessedEventPayload accessedSecond = Assert.IsType<CardAccessedEventPayload>(
            chooseSecond.Events.Single(value => value.Kind == EngineEventKinds.CardAccessed).Payload);
        Assert.Equal(second, accessedSecond.CardId);

        RunState afterSecond = chooseSecond.State.RunState!;
        Assert.Equal(RunTimingStep.BreachAccessAbility, afterSecond.Step);
        Assert.Contains(second, afterSecond.ChosenAccessCards);
        Assert.Contains(second, afterSecond.AccessedCards);
        Assert.DoesNotContain(second, afterSecond.BreachCandidates);
        Assert.Equal(first, afterSecond.BreachCandidates.Single());
        AssertRunPrompt(chooseSecond.State, RunTimingStep.BreachAccessAbility, PlayerSide.Runner, ActionKind.NoAccessAction);
        Assert.Single(chooseSecond.State.PendingDecision!.LegalActions);

        ApplyResult noAction = ApplyKind(chooseSecond.State, ActionKind.NoAccessAction);
        Assert.True(noAction.Accepted, noAction.Rejection?.Message);
        Assert.Equal(RunTimingStep.BreachCandidateSelection, noAction.State.RunState!.Step);
        LegalAction remaining = Assert.Single(noAction.State.PendingDecision!.LegalActions);
        Assert.Equal(ActionKind.ChooseBreachCandidate, remaining.Kind);
        Assert.Equal(first, Assert.IsType<BreachCandidateParameters>(remaining.Parameters).CardId);

        ApplyResult chooseFirst = ApplyAction(noAction.State, remaining);
        Assert.True(chooseFirst.Accepted, chooseFirst.Rejection?.Message);
        ApplyResult finalNoAction = ApplyKind(chooseFirst.State, ActionKind.NoAccessAction);
        Assert.True(finalNoAction.Accepted, finalNoAction.Rejection?.Message);

        GameState terminal = finalNoAction.State;
        Assert.Null(terminal.RunState);
        Assert.Empty(terminal.ResolutionStack);
        Assert.Null(terminal.PendingDecision);
        RunEndedEventPayload ended = Assert.IsType<RunEndedEventPayload>(finalNoAction.Events[^1].Payload);
        Assert.Equal(RunEndReason.BreachCompleted, ended.Reason);
        Assert.True(ended.Successful);

        Assert.Equal(
            new[]
            {
                EngineEventKinds.RunSuccessful,
                EngineEventKinds.BreachStarted,
                EngineEventKinds.BreachCandidateChosen,
                EngineEventKinds.CardAccessed,
                EngineEventKinds.BreachCandidateChosen,
                EngineEventKinds.CardAccessed,
                EngineEventKinds.RunEnded,
            },
            RelevantKinds(breach, chooseSecond, noAction, chooseFirst, finalNoAction));

        // CR 7.2.3: neither accessed card is an agenda, so no automatic
        // steal resolves; both mid-access windows pass empty and the cards
        // stay where they were.
        ApplyResult[] accessResults = [breach, chooseSecond, noAction, chooseFirst, finalNoAction];
        Assert.DoesNotContain(
            accessResults.SelectMany(result => result.Events),
            value => value.Kind == EngineEventKinds.AgendaStolen);
    }

    [Fact]
    public void HqBreachOffersSingleHiddenHandTokenAndDrawsDeterministically()
    {
        CardInstanceId? expectedChosen = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            GameState fixture = CreateBreachState(hand: [false, false, false]);
            ServerId headquarters = fixture.Servers.Single(server => server.Kind == ServerKind.Headquarters).Id;
            ZoneState hand = fixture.Zones.Single(zone => zone.Kind == ZoneKind.CorpHand && zone.ServerId is null);

            ApplyResult breach = PassTwice(BeginRun(fixture, headquarters));
            Assert.True(breach.Accepted, breach.Rejection?.Message);
            AssertRunPrompt(breach.State, RunTimingStep.BreachCandidateSelection, PlayerSide.Runner, ActionKind.ChooseBreachCandidate);
            BreachStartedEventPayload started = Assert.IsType<BreachStartedEventPayload>(
                breach.Events.Single(value => value.Kind == EngineEventKinds.BreachStarted).Payload);
            Assert.Equal(0, started.CandidateCount);
            Assert.Equal(1, started.RandomAccessLimit);

            PendingDecision decision = breach.State.PendingDecision!;
            LegalAction token = Assert.Single(decision.LegalActions);
            Assert.Equal(ActionKind.ChooseBreachCandidate, token.Kind);
            BreachCandidateParameters parameters = Assert.IsType<BreachCandidateParameters>(token.Parameters);
            Assert.Null(parameters.CardId);
            Assert.Equal(BreachHiddenCandidateSource.CorpHand, parameters.Source);

            ApplyResult chosen = ApplyAction(breach.State, token);
            Assert.True(chosen.Accepted, chosen.Rejection?.Message);
            BreachCandidateChosenEventPayload chosenPayload = Assert.IsType<BreachCandidateChosenEventPayload>(
                chosen.Events.Single(value => value.Kind == EngineEventKinds.BreachCandidateChosen).Payload);
            Assert.Equal(BreachHiddenCandidateSource.CorpHand, chosenPayload.Source);
            Assert.Contains(chosenPayload.CardId, hand.CardIds);
            CardAccessedEventPayload accessed = Assert.IsType<CardAccessedEventPayload>(
                chosen.Events.Single(value => value.Kind == EngineEventKinds.CardAccessed).Payload);
            Assert.Equal(chosenPayload.CardId, accessed.CardId);

            // CR 26.03 7.3.1a: the drawn card stays facedown in the
            // authoritative state; breach visibility is temporary.
            CardInstanceState drawn = chosen.State.GetCard(chosenPayload.CardId);
            Assert.False(drawn.FaceUp);
            ZoneState handAfter = chosen.State.Zones.Single(zone => zone.Kind == ZoneKind.CorpHand && zone.ServerId is null);
            Assert.Contains(chosenPayload.CardId, handAfter.CardIds);
            RunState mid = chosen.State.RunState!;
            Assert.Equal(RunTimingStep.BreachAccessAbility, mid.Step);
            Assert.Equal(0, mid.RandomAccessLimit);
            Assert.Empty(mid.BreachCandidates);
            Assert.Contains(chosenPayload.CardId, mid.ChosenAccessCards);
            Assert.Contains(chosenPayload.CardId, mid.AccessedCards);

            ApplyResult noAction = ApplyKind(chosen.State, ActionKind.NoAccessAction);
            Assert.True(noAction.Accepted, noAction.Rejection?.Message);
            Assert.Null(noAction.State.RunState);
            RunEndedEventPayload ended = Assert.IsType<RunEndedEventPayload>(noAction.Events[^1].Payload);
            Assert.Equal(RunEndReason.BreachCompleted, ended.Reason);
            // After the run the drawn card is facedown again in the hand.
            Assert.False(noAction.State.GetCard(chosenPayload.CardId).FaceUp);

            if (expectedChosen is null)
            {
                expectedChosen = chosenPayload.CardId;
            }
            else
            {
                Assert.Equal(expectedChosen, chosenPayload.CardId);
            }
        }
    }

    [Fact]
    public void RndBreachAccessesTopCardOfDeck()
    {
        GameState fixture = CreateBreachState(deck: [false, false, false]);
        ZoneState deck = fixture.Zones.Single(zone => zone.Kind == ZoneKind.CorpDeck && zone.ServerId is null);
        CardInstanceId top = deck.CardIds[0];
        ServerId rnd = fixture.Servers.Single(server => server.Kind == ServerKind.RAndD).Id;

        ApplyResult breach = PassTwice(BeginRun(fixture, rnd));
        Assert.True(breach.Accepted, breach.Rejection?.Message);
        AssertRunPrompt(breach.State, RunTimingStep.BreachCandidateSelection, PlayerSide.Runner, ActionKind.ChooseBreachCandidate);

        LegalAction token = Assert.Single(breach.State.PendingDecision!.LegalActions);
        BreachCandidateParameters parameters = Assert.IsType<BreachCandidateParameters>(token.Parameters);
        Assert.Null(parameters.CardId);
        Assert.Equal(BreachHiddenCandidateSource.CorpDeck, parameters.Source);

        ApplyResult chosen = ApplyAction(breach.State, token);
        Assert.True(chosen.Accepted, chosen.Rejection?.Message);
        BreachCandidateChosenEventPayload chosenPayload = Assert.IsType<BreachCandidateChosenEventPayload>(
            chosen.Events.Single(value => value.Kind == EngineEventKinds.BreachCandidateChosen).Payload);
        Assert.Equal(BreachHiddenCandidateSource.CorpDeck, chosenPayload.Source);
        Assert.Equal(top, chosenPayload.CardId);

        // CR 26.03 7.3.1a: the top card stays facedown in the authoritative
        // state; breach visibility is temporary.
        CardInstanceState topAfter = chosen.State.GetCard(top);
        Assert.False(topAfter.FaceUp);
        ZoneState deckAfter = chosen.State.Zones.Single(zone => zone.Kind == ZoneKind.CorpDeck && zone.ServerId is null);
        Assert.Equal(deck.CardIds, deckAfter.CardIds);

        ApplyResult noAction = ApplyKind(chosen.State, ActionKind.NoAccessAction);
        Assert.True(noAction.Accepted, noAction.Rejection?.Message);
        Assert.Null(noAction.State.RunState);
        RunEndedEventPayload ended = Assert.IsType<RunEndedEventPayload>(noAction.Events[^1].Payload);
        Assert.Equal(RunEndReason.BreachCompleted, ended.Reason);
        // After the run the top card is facedown again in the deck.
        Assert.False(noAction.State.GetCard(top).FaceUp);
    }

    [Fact]
    public void HqBreachOffersRootCardsFirstThenHiddenHandAccess()
    {
        GameState fixture = CreateBreachState(
            hand: [false, false],
            hqRoot: [false, false],
            hqRootDefinitions: [FixtureAsset, FixturePlain]);
        ServerId headquarters = fixture.Servers.Single(server => server.Kind == ServerKind.Headquarters).Id;
        ZoneState root = fixture.Zones.Single(zone => zone.Id == fixture.GetServer(headquarters).RootZoneId);
        CardInstanceId asset = root.CardIds[0];
        CardInstanceId plain = root.CardIds[1];

        ApplyResult breach = PassTwice(BeginRun(fixture, headquarters));
        Assert.True(breach.Accepted, breach.Rejection?.Message);
        AssertRunPrompt(breach.State, RunTimingStep.BreachCandidateSelection, PlayerSide.Runner, ActionKind.ChooseBreachCandidate);

        // CR 26.03 7.4.1a: every card in the HQ root is an explicit
        // candidate, listed first; the Corp hand is the hidden random-access
        // source (7.4.1b/7.3.5) and is not part of the candidate count.
        BreachStartedEventPayload started = Assert.IsType<BreachStartedEventPayload>(
            breach.Events.Single(value => value.Kind == EngineEventKinds.BreachStarted).Payload);
        Assert.Equal(2, started.CandidateCount);
        Assert.Equal(1, started.RandomAccessLimit);
        RunState selection = breach.State.RunState!;
        Assert.Equal(new[] { asset, plain }, selection.BreachCandidates.ToArray());

        PendingDecision decision = breach.State.PendingDecision!;
        var offered = decision.LegalActions.Select(value => (value.Kind, value.Parameters)).ToArray();
        Assert.Equal(3, offered.Length);
        Assert.All(offered, value => Assert.Equal(ActionKind.ChooseBreachCandidate, value.Kind));
        Assert.Equal(asset, Assert.IsType<BreachCandidateParameters>(offered[0].Parameters).CardId);
        Assert.Equal(plain, Assert.IsType<BreachCandidateParameters>(offered[1].Parameters).CardId);
        Assert.Null(Assert.IsType<BreachCandidateParameters>(offered[2].Parameters).CardId);
        Assert.Equal(BreachHiddenCandidateSource.CorpHand, Assert.IsType<BreachCandidateParameters>(offered[2].Parameters).Source);

        // The unrezzed (facedown) root asset is anonymous to the Runner
        // before selection; the owning Corp sees its own root.
        string runnerBefore = EngineJson.Serialize(BuildObservation(breach.State, PlayerSide.Runner));
        string corpBefore = EngineJson.Serialize(BuildObservation(breach.State, PlayerSide.Corp));
        Assert.DoesNotContain(FixtureAsset.Value, runnerBefore);
        Assert.Contains(asset.Value, corpBefore);
        Assert.Contains(FixtureAsset.Value, corpBefore);

        ApplyResult chosen = ApplyAction(breach.State, decision.LegalActions[0]);
        Assert.True(chosen.Accepted, chosen.Rejection?.Message);
        BreachCandidateChosenEventPayload chosenPayload = Assert.IsType<BreachCandidateChosenEventPayload>(
            chosen.Events.Single(value => value.Kind == EngineEventKinds.BreachCandidateChosen).Payload);
        Assert.Equal(asset, chosenPayload.CardId);
        Assert.Null(chosenPayload.Source);

        // CR 26.03 7.3.1a: the asset stays facedown in the authoritative
        // state, but the chosen card is visible to both viewers for the
        // remainder of the breach. A root pick does not consume the random
        // access limit (7.3.5: the limit counts hand/deck picks only).
        Assert.False(chosen.State.GetCard(asset).FaceUp);
        RunState mid = chosen.State.RunState!;
        Assert.Equal(RunTimingStep.BreachAccessAbility, mid.Step);
        Assert.Equal(1, mid.RandomAccessLimit);
        Assert.Equal(plain, mid.BreachCandidates.Single());
        string runnerMid = EngineJson.Serialize(BuildObservation(chosen.State, PlayerSide.Runner));
        string corpMid = EngineJson.Serialize(BuildObservation(chosen.State, PlayerSide.Corp));
        Assert.Contains(asset.Value, runnerMid);
        Assert.Contains(FixtureAsset.Value, runnerMid);
        Assert.Contains(asset.Value, corpMid);
        Assert.Contains(FixtureAsset.Value, corpMid);

        // The accessed asset offers trash + no-action; take no-action.
        PendingDecision accessDecision = chosen.State.PendingDecision!;
        Assert.Equal(2, accessDecision.LegalActions.Count);
        Assert.Equal(ActionKind.TrashAccessedCard, accessDecision.LegalActions[0].Kind);
        Assert.Equal(ActionKind.NoAccessAction, accessDecision.LegalActions[1].Kind);
        ApplyResult noAction = ApplyKind(chosen.State, ActionKind.NoAccessAction);
        Assert.True(noAction.Accepted, noAction.Rejection?.Message);

        // The second root card, then the hidden hand access.
        PendingDecision secondSelection = noAction.State.PendingDecision!;
        Assert.Equal(2, secondSelection.LegalActions.Count);
        LegalAction choosePlain = secondSelection.LegalActions.Single(candidate =>
            (candidate.Parameters as BreachCandidateParameters)?.CardId == plain);
        ApplyResult chosenPlain = ApplyAction(noAction.State, choosePlain);
        Assert.True(chosenPlain.Accepted, chosenPlain.Rejection?.Message);
        ApplyResult plainNoAction = ApplyKind(chosenPlain.State, ActionKind.NoAccessAction);
        Assert.True(plainNoAction.Accepted, plainNoAction.Rejection?.Message);

        PendingDecision hiddenSelection = plainNoAction.State.PendingDecision!;
        LegalAction token = Assert.Single(hiddenSelection.LegalActions);
        Assert.Equal(ActionKind.ChooseBreachCandidate, token.Kind);
        BreachCandidateParameters tokenParameters = Assert.IsType<BreachCandidateParameters>(token.Parameters);
        Assert.Null(tokenParameters.CardId);
        Assert.Equal(BreachHiddenCandidateSource.CorpHand, tokenParameters.Source);

        ApplyResult drawn = ApplyAction(plainNoAction.State, token);
        Assert.True(drawn.Accepted, drawn.Rejection?.Message);
        BreachCandidateChosenEventPayload drawnPayload = Assert.IsType<BreachCandidateChosenEventPayload>(
            drawn.Events.Single(value => value.Kind == EngineEventKinds.BreachCandidateChosen).Payload);
        Assert.Equal(BreachHiddenCandidateSource.CorpHand, drawnPayload.Source);
        Assert.Equal(0, drawn.State.RunState!.RandomAccessLimit);

        ApplyResult finalNoAction = ApplyKind(drawn.State, ActionKind.NoAccessAction);
        Assert.True(finalNoAction.Accepted, finalNoAction.Rejection?.Message);
        GameState terminal = finalNoAction.State;
        Assert.Null(terminal.RunState);
        RunEndedEventPayload ended = Assert.IsType<RunEndedEventPayload>(finalNoAction.Events[^1].Payload);
        Assert.Equal(RunEndReason.BreachCompleted, ended.Reason);
        // After the run the root cards are facedown again in the HQ root.
        Assert.False(terminal.GetCard(asset).FaceUp);
        Assert.False(terminal.GetCard(plain).FaceUp);
        ZoneState rootAfter = terminal.Zones.Single(zone => zone.Id == root.Id);
        Assert.Equal(new[] { asset, plain }, rootAfter.CardIds.ToArray());
    }

    [Fact]
    public void HqBreachWithRootCardsPassesRunTimingTraceValidation()
    {
        GameState fixture = CreateBreachState(
            hand: [false, false],
            hqRoot: [false, false],
            hqRootDefinitions: [FixtureAsset, FixturePlain]);
        ServerId headquarters = fixture.Servers.Single(server => server.Kind == ServerKind.Headquarters).Id;

        var events = new List<EngineEvent>();
        GameEngine.RunStartResult start = BeginRunWithEvents(fixture, headquarters);
        events.AddRange(start.Events);
        ApplyResult first = ApplyKind(start.State, ActionKind.PassPriority);
        Assert.True(first.Accepted, first.Rejection?.Message);
        events.AddRange(first.Events);
        ApplyResult second = ApplyKind(first.State, ActionKind.PassPriority);
        Assert.True(second.Accepted, second.Rejection?.Message);
        events.AddRange(second.Events);
        GameState state = second.State;

        int decisions = 0;
        while (state.RunState is not null && state.PendingDecision is not null)
        {
            Assert.True(++decisions <= 16, "the breach timing loop did not make progress.");
            ApplyResult next = ApplyAction(state, state.PendingDecision!.LegalActions[0]);
            Assert.True(next.Accepted, next.Rejection?.Message);
            events.AddRange(next.Events);
            state = next.State;
        }

        Assert.Null(state.RunState);
        IReadOnlyList<EngineEvent> frozen = EngineEventValidator.FreezeBatch(events);
        RunTimingTrace trace = RunTimingTraceValidator.Validate(frozen);
        Assert.True(trace.IsValid, string.Join("; ", trace.Problems));
        Assert.Empty(trace.Problems);
    }

    [Fact]
    public void RndBreachOffersRootCardFirstThenHiddenDeckAccess()
    {
        GameState fixture = CreateBreachState(
            deck: [false, false],
            rndRoot: [false]);
        ZoneState deck = fixture.Zones.Single(zone => zone.Kind == ZoneKind.CorpDeck && zone.ServerId is null);
        CardInstanceId top = deck.CardIds[0];
        ServerId rnd = fixture.Servers.Single(server => server.Kind == ServerKind.RAndD).Id;
        ZoneState root = fixture.Zones.Single(zone => zone.Id == fixture.GetServer(rnd).RootZoneId);
        CardInstanceId rootCard = root.CardIds[0];

        ApplyResult breach = PassTwice(BeginRun(fixture, rnd));
        Assert.True(breach.Accepted, breach.Rejection?.Message);
        AssertRunPrompt(breach.State, RunTimingStep.BreachCandidateSelection, PlayerSide.Runner, ActionKind.ChooseBreachCandidate);

        // CR 26.03 7.4.1a: the R&D root card is an explicit candidate, listed
        // first; the top of the deck is the hidden random-access source
        // (7.4.1c/7.4.7) and is not part of the candidate count.
        BreachStartedEventPayload started = Assert.IsType<BreachStartedEventPayload>(
            breach.Events.Single(value => value.Kind == EngineEventKinds.BreachStarted).Payload);
        Assert.Equal(1, started.CandidateCount);
        Assert.Equal(1, started.RandomAccessLimit);
        Assert.Equal(new[] { rootCard }, breach.State.RunState!.BreachCandidates.ToArray());

        var offered = breach.State.PendingDecision!.LegalActions.Select(value => (value.Kind, value.Parameters)).ToArray();
        Assert.Equal(2, offered.Length);
        Assert.All(offered, value => Assert.Equal(ActionKind.ChooseBreachCandidate, value.Kind));
        Assert.Equal(rootCard, Assert.IsType<BreachCandidateParameters>(offered[0].Parameters).CardId);
        Assert.Null(Assert.IsType<BreachCandidateParameters>(offered[1].Parameters).CardId);
        Assert.Equal(BreachHiddenCandidateSource.CorpDeck, Assert.IsType<BreachCandidateParameters>(offered[1].Parameters).Source);

        // The facedown root card is anonymous to the Runner before selection.
        string runnerBefore = EngineJson.Serialize(BuildObservation(breach.State, PlayerSide.Runner));
        Assert.DoesNotContain(FixturePlain.Value, runnerBefore);
        Assert.Contains(rootCard.Value, EngineJson.Serialize(BuildObservation(breach.State, PlayerSide.Corp)));

        ApplyResult chosen = ApplyAction(breach.State, breach.State.PendingDecision!.LegalActions[0]);
        Assert.True(chosen.Accepted, chosen.Rejection?.Message);
        BreachCandidateChosenEventPayload chosenPayload = Assert.IsType<BreachCandidateChosenEventPayload>(
            chosen.Events.Single(value => value.Kind == EngineEventKinds.BreachCandidateChosen).Payload);
        Assert.Equal(rootCard, chosenPayload.CardId);
        Assert.Null(chosenPayload.Source);

        // CR 26.03 7.3.1a: the root card stays facedown in the authoritative
        // state; both viewers see it for the remainder of the breach.
        Assert.False(chosen.State.GetCard(rootCard).FaceUp);
        RunState mid = chosen.State.RunState!;
        Assert.Equal(RunTimingStep.BreachAccessAbility, mid.Step);
        Assert.Equal(1, mid.RandomAccessLimit);
        Assert.Empty(mid.BreachCandidates);
        string runnerMid = EngineJson.Serialize(BuildObservation(chosen.State, PlayerSide.Runner));
        string corpMid = EngineJson.Serialize(BuildObservation(chosen.State, PlayerSide.Corp));
        Assert.Contains(rootCard.Value, runnerMid);
        Assert.Contains(FixturePlain.Value, runnerMid);
        Assert.Contains(rootCard.Value, corpMid);
        Assert.Contains(FixturePlain.Value, corpMid);

        ApplyResult noAction = ApplyKind(chosen.State, ActionKind.NoAccessAction);
        Assert.True(noAction.Accepted, noAction.Rejection?.Message);

        // Only the hidden deck-top token remains.
        PendingDecision hiddenSelection = noAction.State.PendingDecision!;
        LegalAction token = Assert.Single(hiddenSelection.LegalActions);
        Assert.Equal(ActionKind.ChooseBreachCandidate, token.Kind);
        BreachCandidateParameters tokenParameters = Assert.IsType<BreachCandidateParameters>(token.Parameters);
        Assert.Null(tokenParameters.CardId);
        Assert.Equal(BreachHiddenCandidateSource.CorpDeck, tokenParameters.Source);

        ApplyResult drawn = ApplyAction(noAction.State, token);
        Assert.True(drawn.Accepted, drawn.Rejection?.Message);
        BreachCandidateChosenEventPayload drawnPayload = Assert.IsType<BreachCandidateChosenEventPayload>(
            drawn.Events.Single(value => value.Kind == EngineEventKinds.BreachCandidateChosen).Payload);
        Assert.Equal(top, drawnPayload.CardId);
        Assert.Equal(BreachHiddenCandidateSource.CorpDeck, drawnPayload.Source);
        Assert.Equal(0, drawn.State.RunState!.RandomAccessLimit);

        ApplyResult finalNoAction = ApplyKind(drawn.State, ActionKind.NoAccessAction);
        Assert.True(finalNoAction.Accepted, finalNoAction.Rejection?.Message);
        GameState terminal = finalNoAction.State;
        Assert.Null(terminal.RunState);
        RunEndedEventPayload ended = Assert.IsType<RunEndedEventPayload>(finalNoAction.Events[^1].Payload);
        Assert.Equal(RunEndReason.BreachCompleted, ended.Reason);
        // After the run the root card and the deck top are facedown again.
        Assert.False(terminal.GetCard(rootCard).FaceUp);
        Assert.False(terminal.GetCard(top).FaceUp);
        ZoneState rootAfter = terminal.Zones.Single(zone => zone.Id == root.Id);
        Assert.Equal(new[] { rootCard }, rootAfter.CardIds.ToArray());
    }

    [Fact]
    public void ArchivesBreachTurnsEveryDiscardFaceupAndKeepsItPublic()
    {
        GameState fixture = CreateBreachState(discard: [false, false, true]);
        ZoneState discard = fixture.Zones.Single(zone => zone.Kind == ZoneKind.CorpDiscard && zone.ServerId is null);
        CardInstanceId first = discard.CardIds[0];
        CardInstanceId second = discard.CardIds[1];
        CardInstanceId third = discard.CardIds[2];
        ServerId archives = fixture.Servers.Single(server => server.Kind == ServerKind.Archives).Id;

        ApplyResult breach = PassTwice(BeginRun(fixture, archives));
        Assert.True(breach.Accepted, breach.Rejection?.Message);
        AssertRunPrompt(breach.State, RunTimingStep.BreachCandidateSelection, PlayerSide.Runner, ActionKind.ChooseBreachCandidate);
        BreachStartedEventPayload started = Assert.IsType<BreachStartedEventPayload>(
            breach.Events.Single(value => value.Kind == EngineEventKinds.BreachStarted).Payload);
        Assert.Equal(3, started.CandidateCount);
        Assert.Equal(0, started.RandomAccessLimit);
        // CR 26.03 7.5.2: every CorpDiscard card is turned faceup at the
        // beginning of the Archives breach, before any candidate is chosen.
        // This is authoritative and persists after the breach.
        Assert.True(breach.State.GetCard(first).FaceUp);
        Assert.True(breach.State.GetCard(second).FaceUp);
        Assert.True(breach.State.GetCard(third).FaceUp);

        var expected = new[] { first, second, third };
        ApplyResult current = breach;
        for (int index = 0; index < expected.Length; index++)
        {
            PendingDecision decision = current.State.PendingDecision!;
            LegalAction action = Assert.Single(decision.LegalActions, candidate =>
                candidate.Kind == ActionKind.ChooseBreachCandidate
                && Assert.IsType<BreachCandidateParameters>(candidate.Parameters).CardId == expected[index]);
            ApplyResult choose = ApplyAction(current.State, action);
            Assert.True(choose.Accepted, choose.Rejection?.Message);
            if (index == expected.Length - 1)
            {
                RunState lastRun = choose.State.RunState!;
                Assert.Equal(expected, lastRun.ChosenAccessCards.ToArray());
                Assert.Equal(expected, lastRun.AccessedCards.ToArray());
            }

            ApplyResult noAction = ApplyKind(choose.State, ActionKind.NoAccessAction);
            Assert.True(noAction.Accepted, noAction.Rejection?.Message);
            current = noAction;
        }

        GameState terminal = current.State;
        Assert.Null(terminal.RunState);
        Assert.Empty(terminal.ResolutionStack);
        Assert.Null(terminal.PendingDecision);
        RunEndedEventPayload ended = Assert.IsType<RunEndedEventPayload>(current.Events[^1].Payload);
        Assert.Equal(RunEndReason.BreachCompleted, ended.Reason);
        // The discard cards remain faceup after the run and are public to both
        // viewers, including a save/load boundary if the state is persisted.
        Assert.True(terminal.GetCard(first).FaceUp);
        Assert.True(terminal.GetCard(second).FaceUp);
        Assert.True(terminal.GetCard(third).FaceUp);
        string runnerAfter = EngineJson.Serialize(BuildObservation(terminal, PlayerSide.Runner));
        string corpAfter = EngineJson.Serialize(BuildObservation(terminal, PlayerSide.Corp));
        Assert.Contains(first.Value, runnerAfter);
        Assert.Contains(second.Value, runnerAfter);
        Assert.Contains(first.Value, corpAfter);
        Assert.Contains(second.Value, corpAfter);
        GameState restored = EngineJson.DeserializeState(EngineJson.Serialize(terminal));
        Assert.True(restored.GetCard(first).FaceUp);
        Assert.True(restored.GetCard(second).FaceUp);
        StateInvariantValidator.Validate(restored);
    }

    [Fact]
    public void ArchivesBreachOffersRootCardsFirstThenDiscardAccess()
    {
        GameState fixture = CreateBreachState(
            archivesRoot: [false],
            archivesRootDefinitions: [FixtureAsset],
            discard: [false, true]);
        ZoneState discard = fixture.Zones.Single(zone => zone.Kind == ZoneKind.CorpDiscard && zone.ServerId is null);
        CardInstanceId discardFacedown = discard.CardIds[0];
        CardInstanceId discardFaceup = discard.CardIds[1];
        ServerId archives = fixture.Servers.Single(server => server.Kind == ServerKind.Archives).Id;
        ZoneState root = fixture.Zones.Single(zone => zone.Id == fixture.GetServer(archives).RootZoneId);
        CardInstanceId rootCard = root.CardIds[0];

        ApplyResult breach = PassTwice(BeginRun(fixture, archives));
        Assert.True(breach.Accepted, breach.Rejection?.Message);
        AssertRunPrompt(breach.State, RunTimingStep.BreachCandidateSelection, PlayerSide.Runner, ActionKind.ChooseBreachCandidate);

        // CR 26.03 7.4.1a: every card in the Archives root is an explicit
        // candidate and is listed first; the discard follows per 7.4.1d, and
        // Archives has no hidden random-access source.
        BreachStartedEventPayload started = Assert.IsType<BreachStartedEventPayload>(
            breach.Events.Single(value => value.Kind == EngineEventKinds.BreachStarted).Payload);
        Assert.Equal(3, started.CandidateCount);
        Assert.Equal(0, started.RandomAccessLimit);
        Assert.Equal(
            new[] { rootCard, discardFacedown, discardFaceup },
            breach.State.RunState!.BreachCandidates.ToArray());

        var offered = breach.State.PendingDecision!.LegalActions.Select(value => (value.Kind, value.Parameters)).ToArray();
        Assert.Equal(3, offered.Length);
        Assert.All(offered, value => Assert.Equal(ActionKind.ChooseBreachCandidate, value.Kind));
        Assert.Equal(rootCard, Assert.IsType<BreachCandidateParameters>(offered[0].Parameters).CardId);
        Assert.Null(Assert.IsType<BreachCandidateParameters>(offered[0].Parameters).Source);
        Assert.Equal(discardFacedown, Assert.IsType<BreachCandidateParameters>(offered[1].Parameters).CardId);
        Assert.Null(Assert.IsType<BreachCandidateParameters>(offered[1].Parameters).Source);
        Assert.Equal(discardFaceup, Assert.IsType<BreachCandidateParameters>(offered[2].Parameters).CardId);
        Assert.Null(Assert.IsType<BreachCandidateParameters>(offered[2].Parameters).Source);

        // The facedown root asset is anonymous to the Runner before
        // selection; the owning Corp sees its own root. Every discard card is
        // now faceup/public from the breach boundary (CR 26.03 7.5.2).
        string runnerBefore = EngineJson.Serialize(BuildObservation(breach.State, PlayerSide.Runner));
        string corpBefore = EngineJson.Serialize(BuildObservation(breach.State, PlayerSide.Corp));
        Assert.DoesNotContain(FixtureAsset.Value, runnerBefore);
        Assert.Contains(rootCard.Value, corpBefore);
        Assert.Contains(FixtureAsset.Value, corpBefore);
        Assert.Contains(discardFacedown.Value, runnerBefore);
        Assert.Contains(discardFacedown.Value, corpBefore);
        Assert.Contains(discardFaceup.Value, runnerBefore);
        Assert.Contains(discardFaceup.Value, corpBefore);

        ApplyResult chosen = ApplyAction(breach.State, breach.State.PendingDecision!.LegalActions[0]);
        Assert.True(chosen.Accepted, chosen.Rejection?.Message);
        BreachCandidateChosenEventPayload chosenPayload = Assert.IsType<BreachCandidateChosenEventPayload>(
            chosen.Events.Single(value => value.Kind == EngineEventKinds.BreachCandidateChosen).Payload);
        Assert.Equal(rootCard, chosenPayload.CardId);
        Assert.Null(chosenPayload.Source);

        // CR 26.03 7.3.1a: the root card stays facedown in the authoritative
        // state, but the chosen card is visible to both viewers for the
        // remainder of the breach.
        Assert.False(chosen.State.GetCard(rootCard).FaceUp);
        RunState mid = chosen.State.RunState!;
        Assert.Equal(RunTimingStep.BreachAccessAbility, mid.Step);
        Assert.Equal(0, mid.RandomAccessLimit);
        Assert.Equal(new[] { discardFacedown, discardFaceup }, mid.BreachCandidates.ToArray());
        string runnerMid = EngineJson.Serialize(BuildObservation(chosen.State, PlayerSide.Runner));
        string corpMid = EngineJson.Serialize(BuildObservation(chosen.State, PlayerSide.Corp));
        Assert.Contains(rootCard.Value, runnerMid);
        Assert.Contains(FixtureAsset.Value, runnerMid);
        Assert.Contains(rootCard.Value, corpMid);
        Assert.Contains(FixtureAsset.Value, corpMid);

        // The accessed asset offers trash + no-action; take no-action.
        PendingDecision accessDecision = chosen.State.PendingDecision!;
        Assert.Equal(2, accessDecision.LegalActions.Count);
        Assert.Equal(ActionKind.TrashAccessedCard, accessDecision.LegalActions[0].Kind);
        Assert.Equal(ActionKind.NoAccessAction, accessDecision.LegalActions[1].Kind);
        ApplyResult noAction = ApplyKind(chosen.State, ActionKind.NoAccessAction);
        Assert.True(noAction.Accepted, noAction.Rejection?.Message);

        // Then the discard follows in order.
        ApplyResult current = noAction;
        foreach (CardInstanceId candidate in new[] { discardFacedown, discardFaceup })
        {
            PendingDecision decision = current.State.PendingDecision!;
            LegalAction action = Assert.Single(decision.LegalActions, value =>
                value.Kind == ActionKind.ChooseBreachCandidate
                && Assert.IsType<BreachCandidateParameters>(value.Parameters).CardId == candidate);
            ApplyResult chosenDiscard = ApplyAction(current.State, action);
            Assert.True(chosenDiscard.Accepted, chosenDiscard.Rejection?.Message);
            current = ApplyKind(chosenDiscard.State, ActionKind.NoAccessAction);
            Assert.True(current.Accepted, current.Rejection?.Message);
        }

        GameState terminal = current.State;
        Assert.Null(terminal.RunState);
        Assert.Empty(terminal.ResolutionStack);
        Assert.Null(terminal.PendingDecision);
        RunEndedEventPayload ended = Assert.IsType<RunEndedEventPayload>(current.Events[^1].Payload);
        Assert.Equal(RunEndReason.BreachCompleted, ended.Reason);

        // After the run: the accessed root card is back in the Archives root
        // facedown again; both discard cards remain faceup/public.
        Assert.False(terminal.GetCard(rootCard).FaceUp);
        Assert.True(terminal.GetCard(discardFacedown).FaceUp);
        Assert.True(terminal.GetCard(discardFaceup).FaceUp);
        ZoneState rootAfter = terminal.Zones.Single(zone => zone.Id == root.Id);
        Assert.Equal(new[] { rootCard }, rootAfter.CardIds.ToArray());
        string runnerAfter = EngineJson.Serialize(BuildObservation(terminal, PlayerSide.Runner));
        string corpAfter = EngineJson.Serialize(BuildObservation(terminal, PlayerSide.Corp));
        string corpPublicAfter = EngineJson.Serialize(BuildObservation(terminal, PlayerSide.Corp).PublicState);
        Assert.Contains(discardFacedown.Value, runnerAfter);
        Assert.Contains(discardFacedown.Value, corpPublicAfter);
        Assert.Contains(discardFacedown.Value, corpAfter);
        Assert.Contains(discardFaceup.Value, runnerAfter);
        Assert.Contains(discardFaceup.Value, corpAfter);
    }

    [Fact]
    public void ArchivesBreachWithRootCardsPassesRunTimingTraceValidation()
    {
        GameState fixture = CreateBreachState(
            archivesRoot: [false],
            archivesRootDefinitions: [FixtureAsset],
            discard: [false, true]);
        ServerId archives = fixture.Servers.Single(server => server.Kind == ServerKind.Archives).Id;

        var events = new List<EngineEvent>();
        GameEngine.RunStartResult start = BeginRunWithEvents(fixture, archives);
        events.AddRange(start.Events);
        ApplyResult first = ApplyKind(start.State, ActionKind.PassPriority);
        Assert.True(first.Accepted, first.Rejection?.Message);
        events.AddRange(first.Events);
        ApplyResult second = ApplyKind(first.State, ActionKind.PassPriority);
        Assert.True(second.Accepted, second.Rejection?.Message);
        events.AddRange(second.Events);
        GameState state = second.State;

        int decisions = 0;
        while (state.RunState is not null && state.PendingDecision is not null)
        {
            Assert.True(++decisions <= 16, "the breach timing loop did not make progress.");
            ApplyResult next = ApplyAction(state, state.PendingDecision!.LegalActions[0]);
            Assert.True(next.Accepted, next.Rejection?.Message);
            events.AddRange(next.Events);
            state = next.State;
        }

        Assert.Null(state.RunState);
        IReadOnlyList<EngineEvent> frozen = EngineEventValidator.FreezeBatch(events);
        RunTimingTrace trace = RunTimingTraceValidator.Validate(frozen);
        Assert.True(trace.IsValid, string.Join("; ", trace.Problems));
        Assert.Empty(trace.Problems);
    }

    [Fact]
    public void ForgedBreachCandidateActionIsRejectedWithoutMutatingState()
    {
        GameState fixture = CreateBreachState(hand: [false, false, false]);
        ServerId headquarters = fixture.Servers.Single(server => server.Kind == ServerKind.Headquarters).Id;
        ApplyResult breach = PassTwice(BeginRun(fixture, headquarters));
        Assert.True(breach.Accepted, breach.Rejection?.Message);
        GameState candidateState = breach.State;
        PendingDecision decision = candidateState.PendingDecision!;
        string before = EngineJson.Serialize(candidateState);

        ApplyResult rejected = Apply(candidateState, new ActionSubmission(
            decision.Owner,
            decision.Id,
            decision.Revision,
            new ActionId("action:forged")));

        Assert.Equal(ApplyRejectionCode.UnknownAction, rejected.Rejection?.Code);
        Assert.Same(candidateState, rejected.State);
        Assert.Equal(before, EngineJson.Serialize(rejected.State));

        ApplyResult accepted = ApplyAction(candidateState, decision.LegalActions[0]);
        Assert.True(accepted.Accepted, accepted.Rejection?.Message);
    }

    [Fact]
    public void SaveLoadMidBreachProducesIdenticalContinuation()
    {
        GameState fixture = CreateBreachState(hand: [false, false, false]);
        ServerId headquarters = fixture.Servers.Single(server => server.Kind == ServerKind.Headquarters).Id;
        GameState original = PassTwice(BeginRun(fixture, headquarters)).State;
        Assert.True(original.RunState!.Step == RunTimingStep.BreachCandidateSelection);

        string candidateJson = EngineJson.Serialize(original);
        GameState restoredCandidate = EngineJson.DeserializeState(candidateJson);
        Assert.Equal(candidateJson, EngineJson.Serialize(restoredCandidate));
        PendingDecision candidateDecision = original.PendingDecision!;
        ActionSubmission candidateSubmission = new(
            candidateDecision.Owner,
            candidateDecision.Id,
            candidateDecision.Revision,
            candidateDecision.LegalActions[0].Id);
        ApplyResult left = Apply(original, candidateSubmission);
        ApplyResult right = Apply(restoredCandidate, candidateSubmission);
        Assert.Equal(left.Status, right.Status);
        Assert.True(left.Accepted, left.Rejection?.Message);
        Assert.Equal(EngineJson.Serialize(left.State), EngineJson.Serialize(right.State));
        Assert.Equal(DeterministicHash.ForEvents(left.Events), DeterministicHash.ForEvents(right.Events));
        original = left.State;
        Assert.Equal(RunTimingStep.BreachAccessAbility, original.RunState!.Step);

        string accessJson = EngineJson.Serialize(original);
        GameState restoredAccess = EngineJson.DeserializeState(accessJson);
        Assert.Equal(accessJson, EngineJson.Serialize(restoredAccess));
        PendingDecision accessDecision = original.PendingDecision!;
        ActionSubmission accessSubmission = new(
            accessDecision.Owner,
            accessDecision.Id,
            accessDecision.Revision,
            accessDecision.LegalActions[0].Id);
        ApplyResult leftEnd = Apply(original, accessSubmission);
        ApplyResult rightEnd = Apply(restoredAccess, accessSubmission);
        Assert.Equal(leftEnd.Status, rightEnd.Status);
        Assert.True(leftEnd.Accepted, leftEnd.Rejection?.Message);
        Assert.Equal(EngineJson.Serialize(leftEnd.State), EngineJson.Serialize(rightEnd.State));
        Assert.Equal(DeterministicHash.ForEvents(leftEnd.Events), DeterministicHash.ForEvents(rightEnd.Events));
    }

    [Fact]
    public void SaveLoadAcrossAgendaMidAccessProducesIdenticalContinuation()
    {
        GameState fixture = CreateBreachState(
            remoteRoot: [false, false],
            remoteRootDefinitions: [FixtureAgenda, FixturePlain]);
        ServerId remoteId = fixture.Servers.Single(server => server.Kind == ServerKind.Remote).Id;
        ZoneState root = fixture.Zones.Single(zone => zone.Id == fixture.GetServer(remoteId).RootZoneId);
        CardInstanceId agenda = root.CardIds[0];

        ApplyResult breach = PassTwice(BeginRun(fixture, remoteId));
        Assert.True(breach.Accepted, breach.Rejection?.Message);

        PendingDecision selection = breach.State.PendingDecision!;
        LegalAction chooseAgenda = selection.LegalActions.Single(action =>
            (action.Parameters as BreachCandidateParameters)?.CardId == agenda);
        ApplyResult chosen = ApplyAction(breach.State, chooseAgenda);
        Assert.True(chosen.Accepted, chosen.Rejection?.Message);
        // CR 7.2.2/7.1.5: the yielded decision is the mid-access window and
        // offers the pass only for the accessed agenda.
        Assert.Equal(RunTimingStep.BreachAccessAbility, chosen.State.RunState!.Step);
        AssertRunPrompt(chosen.State, RunTimingStep.BreachAccessAbility, PlayerSide.Runner, ActionKind.NoAccessAction);
        Assert.Single(chosen.State.PendingDecision!.LegalActions);

        // Serialize at the yielded mid-access decision, restore, and continue
        // from the restored state: the automatic steal (CR 7.2.3) must
        // resolve identically on both sides.
        string midAccessJson = EngineJson.Serialize(chosen.State);
        GameState restored = EngineJson.DeserializeState(midAccessJson);
        Assert.Equal(midAccessJson, EngineJson.Serialize(restored));
        PendingDecision decision = chosen.State.PendingDecision!;
        ActionSubmission submission = new(
            decision.Owner,
            decision.Id,
            decision.Revision,
            decision.LegalActions[0].Id);
        ApplyResult left = Apply(chosen.State, submission);
        ApplyResult right = Apply(restored, submission);
        Assert.Equal(left.Status, right.Status);
        Assert.True(left.Accepted, left.Rejection?.Message);
        Assert.Equal(EngineJson.Serialize(left.State), EngineJson.Serialize(right.State));
        Assert.Equal(DeterministicHash.ForEvents(left.Events), DeterministicHash.ForEvents(right.Events));

        // The continuation is the automatic steal of the accessed agenda.
        AgendaStolenEventPayload stolen = Assert.IsType<AgendaStolenEventPayload>(
            left.Events.Single(value => value.Kind == EngineEventKinds.AgendaStolen).Payload);
        Assert.Equal(agenda, stolen.AgendaId);
        ZoneState runnerScore = left.State.Zones.Single(zone => zone.Kind == ZoneKind.RunnerScore && zone.ServerId is null);
        Assert.Equal(runnerScore.Id, left.State.GetCard(agenda).ZoneId);
        Assert.Equal(RunTimingStep.BreachCandidateSelection, left.State.RunState!.Step);
    }

    [Fact]
    public void RemoteBreachOffersTrashForPayableAssetAndTrashesIt()
    {
        GameState fixture = WithFormalRunnerFacts(CreateBreachState(
            remoteRoot: [false, false],
            remoteRootDefinitions: [FixtureAsset, FixturePlain]));
        ServerId remoteId = fixture.Servers.Single(server => server.Kind == ServerKind.Remote).Id;
        ZoneState root = fixture.Zones.Single(zone => zone.Id == fixture.GetServer(remoteId).RootZoneId);
        CardInstanceId asset = root.CardIds[0];
        CardInstanceId plain = root.CardIds[1];

        ApplyResult breach = PassTwice(BeginRun(fixture, remoteId));
        Assert.True(breach.Accepted, breach.Rejection?.Message);

        PendingDecision selection = breach.State.PendingDecision!;
        LegalAction chooseAsset = selection.LegalActions.Single(action =>
            (action.Parameters as BreachCandidateParameters)?.CardId == asset);
        ApplyResult chosen = ApplyAction(breach.State, chooseAsset);
        Assert.True(chosen.Accepted, chosen.Rejection?.Message);
        // CR 26.03 7.3.1a: selection does not turn the card faceup; it stays
        // facedown until the trash reveals it in the discard.
        Assert.False(chosen.State.GetCard(asset).FaceUp);

        PendingDecision accessDecision = chosen.State.PendingDecision!;
        Assert.Equal(RunTimingStep.BreachAccessAbility, chosen.State.RunState!.Step);
        Assert.Equal(2, accessDecision.LegalActions.Count);
        LegalAction trashAction = accessDecision.LegalActions[0];
        Assert.Equal(ActionKind.TrashAccessedCard, trashAction.Kind);
        RunCardPaymentParameters trashPayment = Assert.IsType<RunCardPaymentParameters>(trashAction.Parameters);
        Assert.Equal(asset, trashPayment.CardId);
        Assert.Equal(0, trashPayment.HostedCredits);
        Assert.Equal(ActionKind.NoAccessAction, accessDecision.LegalActions[1].Kind);

        ApplyResult trashed = ApplyAction(chosen.State, trashAction);
        Assert.True(trashed.Accepted, trashed.Rejection?.Message);
        PlayerState runnerAfter = trashed.State.Players.Single(player => player.Side == PlayerSide.Runner);
        Assert.Equal(2, runnerAfter.Credits);
        CreditsAdjustedEventPayload adjusted = Assert.IsType<CreditsAdjustedEventPayload>(
            trashed.Events.Single(value => value.Kind == EngineEventKinds.CreditsAdjusted).Payload);
        Assert.Equal(-2, adjusted.Delta);
        ZoneState discard = trashed.State.Zones.Single(zone => zone.Kind == ZoneKind.CorpDiscard && zone.ServerId is null);
        CardMovedEventPayload moved = Assert.IsType<CardMovedEventPayload>(
            trashed.Events.Single(value => value.Kind == EngineEventKinds.CardMoved).Payload);
        Assert.Equal(asset, moved.CardId);
        Assert.Equal(discard.Id, moved.ToZoneId);
        AccessedCardTrashedEventPayload trashEvent = Assert.IsType<AccessedCardTrashedEventPayload>(
            trashed.Events.Single(value => value.Kind == EngineEventKinds.AccessedCardTrashed).Payload);
        Assert.Equal(asset, trashEvent.CardId);
        Assert.Equal(2, trashEvent.TrashCost);

        // Trashing the accessed card reveals it: it enters the discard faceup.
        CardInstanceState trashedCard = trashed.State.GetCard(asset);
        Assert.Equal(discard.Id, trashedCard.ZoneId);
        Assert.True(trashedCard.FaceUp);
        Assert.Equal([asset], trashed.State.Turn.ThisTurnFacts!.TrashedCardOccurrenceIds);
        ZoneState rootAfter = trashed.State.Zones.Single(zone => zone.Id == root.Id);
        Assert.DoesNotContain(asset, rootAfter.CardIds);
        Assert.Contains(plain, rootAfter.CardIds);

        Assert.Equal(RunTimingStep.BreachCandidateSelection, trashed.State.RunState!.Step);
        LegalAction remaining = Assert.Single(trashed.State.PendingDecision!.LegalActions);
        Assert.Equal(ActionKind.ChooseBreachCandidate, remaining.Kind);
        Assert.Equal(plain, Assert.IsType<BreachCandidateParameters>(remaining.Parameters).CardId);

        ApplyResult choosePlain = ApplyAction(trashed.State, remaining);
        Assert.True(choosePlain.Accepted, choosePlain.Rejection?.Message);
        AssertRunPrompt(choosePlain.State, RunTimingStep.BreachAccessAbility, PlayerSide.Runner, ActionKind.NoAccessAction);
        Assert.Single(choosePlain.State.PendingDecision!.LegalActions);

        ApplyResult noAction = ApplyKind(choosePlain.State, ActionKind.NoAccessAction);
        Assert.True(noAction.Accepted, noAction.Rejection?.Message);
        Assert.Null(noAction.State.RunState);
        RunEndedEventPayload ended = Assert.IsType<RunEndedEventPayload>(noAction.Events[^1].Payload);
        Assert.Equal(RunEndReason.BreachCompleted, ended.Reason);
    }

    [Fact]
    public void AccessTrashOffersEveryLegalWalletHostedDivision()
    {
        GameState fixture = AddRunEventSource(
            CreateBreachState(
                remoteRoot: [false],
                remoteRootDefinitions: [FixtureAsset],
                runnerCredits: 3),
            new CardDefinitionId("fixture:event-spark"));
        ServerId remoteId = fixture.Servers.Single(server => server.Kind == ServerKind.Remote).Id;
        ZoneState root = fixture.Zones.Single(zone => zone.Id == fixture.GetServer(remoteId).RootZoneId);
        CardInstanceId asset = root.CardIds[0];
        CardInstanceId eventId = fixture.GetZone(ZoneKind.RunnerGrip).CardIds.Single();

        GameState started = BeginRunWithEvents(
            fixture,
            remoteId,
            initiatingSource: eventId).State;
        ApplyResult breach = PassTwice(started);
        Assert.True(breach.Accepted, breach.Rejection?.Message);
        LegalAction choose = Assert.Single(breach.State.PendingDecision!.LegalActions);
        ApplyResult chosen = ApplyAction(breach.State, choose);
        Assert.True(chosen.Accepted, chosen.Rejection?.Message);

        // Spark leaves a 2-credit wallet and 1 hosted run credit after paying
        // its printed cost. The 2-credit trash cost therefore has exactly two
        // legal divisions: wallet-only, or one from each source.
        PendingDecision access = chosen.State.PendingDecision!;
        LegalAction[] trashActions = access.LegalActions
            .Where(action => action.Kind == ActionKind.TrashAccessedCard)
            .ToArray();
        Assert.Equal(2, trashActions.Length);
        Assert.Contains(trashActions, action =>
            Assert.IsType<RunCardPaymentParameters>(action.Parameters).HostedCredits == 0);
        LegalAction split = Assert.Single(trashActions, action =>
            Assert.IsType<RunCardPaymentParameters>(action.Parameters).HostedCredits == 1);
        Assert.All(trashActions, action =>
            Assert.Equal(asset, Assert.IsType<RunCardPaymentParameters>(action.Parameters).CardId));

        ApplyResult trashed = ApplyAction(chosen.State, split);
        Assert.True(trashed.Accepted, trashed.Rejection?.Message);
        Assert.Equal(1, trashed.State.Players.Single(player => player.Side == PlayerSide.Runner).Credits);
        Assert.Null(trashed.State.RunState);
        RunCreditsAdjustedEventPayload hostedPayment = Assert.IsType<RunCreditsAdjustedEventPayload>(
            trashed.Events.Single(value => value.Kind == EngineEventKinds.RunCreditsAdjusted).Payload);
        Assert.Equal(0, hostedPayment.RunCreditsAfter);
        Assert.Contains(trashed.Events, value => value.Kind == EngineEventKinds.CreditsAdjusted);
        Assert.Contains(trashed.Events, value => value.Kind == EngineEventKinds.RunCreditsAdjusted);
        Assert.True(trashed.State.GetCard(asset).FaceUp);
    }

    [Fact]
    public void TrashNotOfferedWhenRunnerCannotAffordIt()
    {
        GameState fixture = CreateBreachState(
            remoteRoot: [false],
            remoteRootDefinitions: [FixtureAsset],
            runnerCredits: 1);
        ServerId remoteId = fixture.Servers.Single(server => server.Kind == ServerKind.Remote).Id;
        ZoneState root = fixture.Zones.Single(zone => zone.Id == fixture.GetServer(remoteId).RootZoneId);
        CardInstanceId asset = root.CardIds[0];

        ApplyResult breach = PassTwice(BeginRun(fixture, remoteId));
        Assert.True(breach.Accepted, breach.Rejection?.Message);

        ApplyResult chosen = ApplyAction(breach.State, Assert.Single(breach.State.PendingDecision!.LegalActions));
        Assert.True(chosen.Accepted, chosen.Rejection?.Message);

        PendingDecision accessDecision = chosen.State.PendingDecision!;
        LegalAction noActionAction = Assert.Single(accessDecision.LegalActions);
        Assert.Equal(ActionKind.NoAccessAction, noActionAction.Kind);

        ApplyResult noAction = ApplyKind(chosen.State, ActionKind.NoAccessAction);
        Assert.True(noAction.Accepted, noAction.Rejection?.Message);
        ZoneState rootAfter = noAction.State.Zones.Single(zone => zone.Id == root.Id);
        Assert.Contains(asset, rootAfter.CardIds);
        Assert.Null(noAction.State.RunState);
        RunEndedEventPayload ended = Assert.IsType<RunEndedEventPayload>(noAction.Events[^1].Payload);
        Assert.Equal(RunEndReason.BreachCompleted, ended.Reason);
    }

    [Fact]
    public void AssetInArchivesIsAccessedButNotTrashed()
    {
        GameState fixture = CreateBreachState(discard: [false], discardDefinitions: [FixtureAsset]);
        ServerId archivesId = fixture.Servers.Single(server => server.Kind == ServerKind.Archives).Id;

        ApplyResult breach = PassTwice(BeginRun(fixture, archivesId));
        Assert.True(breach.Accepted, breach.Rejection?.Message);

        PendingDecision selection = breach.State.PendingDecision!;
        LegalAction onlyCandidate = Assert.Single(selection.LegalActions);
        Assert.Equal(ActionKind.ChooseBreachCandidate, onlyCandidate.Kind);
        CardInstanceId asset = Assert.IsType<BreachCandidateParameters>(onlyCandidate.Parameters).CardId!.Value;

        ApplyResult chosen = ApplyAction(breach.State, onlyCandidate);
        Assert.True(chosen.Accepted, chosen.Rejection?.Message);
        // CR 26.03 7.5.2: the Archives breach turned every discard card
        // faceup before selection.
        Assert.True(chosen.State.GetCard(asset).FaceUp);

        // During the breach both viewers see the chosen card in the run view.
        string runnerMid = EngineJson.Serialize(BuildObservation(chosen.State, PlayerSide.Runner));
        string corpMid = EngineJson.Serialize(BuildObservation(chosen.State, PlayerSide.Corp));
        Assert.Contains(asset.Value, runnerMid);
        Assert.Contains(FixtureAsset.Value, runnerMid);
        Assert.Contains(asset.Value, corpMid);
        Assert.Contains(FixtureAsset.Value, corpMid);

        PendingDecision accessDecision = chosen.State.PendingDecision!;
        LegalAction noActionAction = Assert.Single(accessDecision.LegalActions);
        Assert.Equal(ActionKind.NoAccessAction, noActionAction.Kind);

        ApplyResult noAction = ApplyKind(chosen.State, ActionKind.NoAccessAction);
        Assert.True(noAction.Accepted, noAction.Rejection?.Message);
        ZoneState discardAfter = noAction.State.Zones.Single(zone => zone.Kind == ZoneKind.CorpDiscard && zone.ServerId is null);
        Assert.Contains(asset, discardAfter.CardIds);
        Assert.Null(noAction.State.RunState);
        RunEndedEventPayload ended = Assert.IsType<RunEndedEventPayload>(noAction.Events[^1].Payload);
        Assert.Equal(RunEndReason.BreachCompleted, ended.Reason);

        // After the run the card remains faceup in the public discard.
        GameState terminal = noAction.State;
        Assert.True(terminal.GetCard(asset).FaceUp);
        string runnerAfter = EngineJson.Serialize(BuildObservation(terminal, PlayerSide.Runner));
        string corpAfter = EngineJson.Serialize(BuildObservation(terminal, PlayerSide.Corp));
        string corpPublicAfter = EngineJson.Serialize(BuildObservation(terminal, PlayerSide.Corp).PublicState);
        Assert.Contains(asset.Value, runnerAfter);
        Assert.Contains(FixtureAsset.Value, runnerAfter);
        Assert.Contains(asset.Value, corpPublicAfter);
        Assert.Contains(asset.Value, corpAfter);
    }

    [Fact]
    public void RemoteBreachAccessOfAgendaForcesStealAndScoresIt()
    {
        GameState fixture = CreateBreachState(
            remoteRoot: [false, false],
            remoteRootDefinitions: [FixtureAgenda, FixturePlain]);
        ServerId remoteId = fixture.Servers.Single(server => server.Kind == ServerKind.Remote).Id;
        ZoneState root = fixture.Zones.Single(zone => zone.Id == fixture.GetServer(remoteId).RootZoneId);
        CardInstanceId agenda = root.CardIds[0];
        CardInstanceId plain = root.CardIds[1];

        ApplyResult breach = PassTwice(BeginRun(fixture, remoteId));
        Assert.True(breach.Accepted, breach.Rejection?.Message);

        PendingDecision selection = breach.State.PendingDecision!;
        LegalAction chooseAgenda = selection.LegalActions.Single(action =>
            (action.Parameters as BreachCandidateParameters)?.CardId == agenda);
        ApplyResult chosen = ApplyAction(breach.State, chooseAgenda);
        Assert.True(chosen.Accepted, chosen.Rejection?.Message);
        Assert.Equal(RunTimingStep.BreachAccessAbility, chosen.State.RunState!.Step);

        // CR 7.2.2/7.1.5: the mid-access window for an accessed agenda offers
        // the pass only — no mid-access ability is modeled for agendas in the
        // M2 card set, and the player-facing steal action no longer exists.
        PendingDecision accessDecision = chosen.State.PendingDecision!;
        LegalAction pass = Assert.Single(accessDecision.LegalActions);
        Assert.Equal(ActionKind.NoAccessAction, pass.Kind);
        Assert.IsType<NoActionParameters>(pass.Parameters);

        // CR 7.2.3: passing the mid-access window steals the still-accessed
        // agenda automatically, with the same AgendaStolen event as before.
        ApplyResult stolen = ApplyAction(chosen.State, pass);
        Assert.True(stolen.Accepted, stolen.Rejection?.Message);
        PlayerState runnerAfter = stolen.State.Players.Single(player => player.Side == PlayerSide.Runner);
        Assert.Equal(2, runnerAfter.AgendaPoints);
        AgendaStolenEventPayload stolenEvent = Assert.IsType<AgendaStolenEventPayload>(
            stolen.Events.Single(value => value.Kind == EngineEventKinds.AgendaStolen).Payload);
        Assert.Equal(PlayerSide.Runner, stolenEvent.Winner);
        Assert.Equal(agenda, stolenEvent.AgendaId);
        Assert.Equal(2, stolenEvent.AgendaPoints);

        ZoneState runnerScore = stolen.State.Zones.Single(zone => zone.Kind == ZoneKind.RunnerScore && zone.ServerId is null);
        CardInstanceState scoredCard = stolen.State.GetCard(agenda);
        Assert.Equal(runnerScore.Id, scoredCard.ZoneId);
        Assert.True(scoredCard.FaceUp);
        ZoneState rootAfter = stolen.State.Zones.Single(zone => zone.Id == root.Id);
        Assert.DoesNotContain(agenda, rootAfter.CardIds);

        Assert.Equal(RunTimingStep.BreachCandidateSelection, stolen.State.RunState!.Step);
        LegalAction remaining = Assert.Single(stolen.State.PendingDecision!.LegalActions);
        Assert.Equal(ActionKind.ChooseBreachCandidate, remaining.Kind);
        Assert.Equal(plain, Assert.IsType<BreachCandidateParameters>(remaining.Parameters).CardId);

        ApplyResult choosePlain = ApplyAction(stolen.State, remaining);
        Assert.True(choosePlain.Accepted, choosePlain.Rejection?.Message);
        AssertRunPrompt(choosePlain.State, RunTimingStep.BreachAccessAbility, PlayerSide.Runner, ActionKind.NoAccessAction);
        Assert.Single(choosePlain.State.PendingDecision!.LegalActions);

        ApplyResult noAction = ApplyKind(choosePlain.State, ActionKind.NoAccessAction);
        Assert.True(noAction.Accepted, noAction.Rejection?.Message);
        GameState terminal = noAction.State;
        Assert.Null(terminal.RunState);
        Assert.Equal(GameOutcomeKind.Ongoing, terminal.Outcome.Kind);
        RunEndedEventPayload ended = Assert.IsType<RunEndedEventPayload>(noAction.Events[^1].Payload);
        Assert.Equal(RunEndReason.BreachCompleted, ended.Reason);
    }

    [Fact]
    public void StealingWinningAgendaEndsTheGame()
    {
        GameState fixture = CreateBreachState(
            remoteRoot: [false],
            remoteRootDefinitions: [FixtureAgenda],
            runnerAgendaPoints: 1);
        ServerId remoteId = fixture.Servers.Single(server => server.Kind == ServerKind.Remote).Id;
        ZoneState root = fixture.Zones.Single(zone => zone.Id == fixture.GetServer(remoteId).RootZoneId);
        CardInstanceId agenda = root.CardIds[0];

        ApplyResult breach = PassTwice(BeginRun(fixture, remoteId));
        Assert.True(breach.Accepted, breach.Rejection?.Message);

        ApplyResult chosen = ApplyAction(breach.State, Assert.Single(breach.State.PendingDecision!.LegalActions));
        Assert.True(chosen.Accepted, chosen.Rejection?.Message);

        // CR 7.2.2/7.1.5: the mid-access window offers the pass only; the
        // automatic steal (CR 7.2.3) resolves when it is passed.
        PendingDecision accessDecision = chosen.State.PendingDecision!;
        LegalAction pass = Assert.Single(accessDecision.LegalActions);
        Assert.Equal(ActionKind.NoAccessAction, pass.Kind);

        ApplyResult stolen = ApplyAction(chosen.State, pass);
        Assert.True(stolen.Accepted, stolen.Rejection?.Message);
        GameState terminal = stolen.State;

        Assert.Equal(3, terminal.Players.Single(player => player.Side == PlayerSide.Runner).AgendaPoints);
        Assert.Equal(GameOutcomeKind.Won, terminal.Outcome.Kind);
        Assert.Equal(PlayerSide.Runner, terminal.Outcome.Winner);
        Assert.Equal(GameWinReason.AgendaPoints, terminal.Outcome.Reason);
        Assert.Equal(TurnPhase.GameOver, terminal.Turn.Phase);
        Assert.Null(terminal.RunState);
        Assert.Null(terminal.PendingDecision);
        Assert.Empty(terminal.ResolutionStack);

        // Event order: CardMoved -> AgendaStolen -> RunEnded -> GameWon.
        var kinds = stolen.Events.Select(value => value.Kind).ToArray();
        int stolenIndex = Array.IndexOf(kinds, EngineEventKinds.AgendaStolen);
        int endedIndex = Array.IndexOf(kinds, EngineEventKinds.RunEnded);
        int wonIndex = Array.IndexOf(kinds, EngineEventKinds.GameWon);
        Assert.True(stolenIndex >= 0 && stolenIndex < endedIndex && endedIndex < wonIndex);
        RunEndedEventPayload ended = Assert.IsType<RunEndedEventPayload>(
            stolen.Events[endedIndex].Payload);
        Assert.Equal(RunEndReason.BreachCompleted, ended.Reason);
        GameWonEventPayload won = Assert.IsType<GameWonEventPayload>(
            stolen.Events[wonIndex].Payload);
        Assert.Equal(PlayerSide.Runner, won.Winner);
        Assert.Equal(GameWinReason.AgendaPoints, won.Reason);

        ZoneState runnerScore = terminal.Zones.Single(zone => zone.Kind == ZoneKind.RunnerScore && zone.ServerId is null);
        Assert.Equal(runnerScore.Id, terminal.GetCard(agenda).ZoneId);
    }

    [Fact]
    public void PassingTheMidAccessWindowCannotSkipTheAgendaSteal()
    {
        GameState fixture = CreateBreachState(
            remoteRoot: [false],
            remoteRootDefinitions: [FixtureAgenda]);
        ServerId remoteId = fixture.Servers.Single(server => server.Kind == ServerKind.Remote).Id;
        ZoneState root = fixture.Zones.Single(zone => zone.Id == fixture.GetServer(remoteId).RootZoneId);
        CardInstanceId agenda = root.CardIds[0];

        ApplyResult breach = PassTwice(BeginRun(fixture, remoteId));
        Assert.True(breach.Accepted, breach.Rejection?.Message);

        ApplyResult chosen = ApplyAction(breach.State, Assert.Single(breach.State.PendingDecision!.LegalActions));
        Assert.True(chosen.Accepted, chosen.Rejection?.Message);
        GameState decisionState = chosen.State;
        PendingDecision decision = decisionState.PendingDecision!;
        Assert.Equal(RunTimingStep.BreachAccessAbility, decisionState.RunState!.Step);
        LegalAction pass = Assert.Single(decision.LegalActions);
        Assert.Equal(ActionKind.NoAccessAction, pass.Kind);
        string before = EngineJson.Serialize(decisionState);

        ApplyResult rejected = Apply(decisionState, new ActionSubmission(
            decision.Owner,
            decision.Id,
            decision.Revision,
            new ActionId("action:forged-steal")));

        Assert.Equal(ApplyRejectionCode.UnknownAction, rejected.Rejection?.Code);
        Assert.Same(decisionState, rejected.State);
        Assert.Equal(before, EngineJson.Serialize(rejected.State));
        Assert.Contains(agenda, rejected.State.Zones.Single(zone => zone.Id == root.Id).CardIds);
        Assert.Equal(0, rejected.State.Players.Single(player => player.Side == PlayerSide.Runner).AgendaPoints);
        Assert.Equal(RunTimingStep.BreachAccessAbility, rejected.State.RunState!.Step);

        // CR 7.2.3: the only legal action is the pass, and passing it does
        // not skip the mandatory steal — the agenda is stolen automatically.
        ApplyResult accepted = ApplyAction(decisionState, pass);
        Assert.True(accepted.Accepted, accepted.Rejection?.Message);
        Assert.Single(accepted.Events, value => value.Kind == EngineEventKinds.AgendaStolen);
        Assert.Equal(2, accepted.State.Players.Single(player => player.Side == PlayerSide.Runner).AgendaPoints);
    }

    [Fact]
    public void ArchivesAgendaBreachAccessStillForcesSteal()
    {
        GameState fixture = CreateBreachState(discard: [false], discardDefinitions: [FixtureAgenda]);
        ServerId archivesId = fixture.Servers.Single(server => server.Kind == ServerKind.Archives).Id;

        ApplyResult breach = PassTwice(BeginRun(fixture, archivesId));
        Assert.True(breach.Accepted, breach.Rejection?.Message);

        PendingDecision selection = breach.State.PendingDecision!;
        LegalAction onlyCandidate = Assert.Single(selection.LegalActions);
        CardInstanceId agenda = Assert.IsType<BreachCandidateParameters>(onlyCandidate.Parameters).CardId!.Value;

        ApplyResult chosen = ApplyAction(breach.State, onlyCandidate);
        Assert.True(chosen.Accepted, chosen.Rejection?.Message);
        // Archives' breach start has already made every CorpDiscard card
        // faceup authoritatively (CR 7.5.2); candidate selection does not
        // perform a separate visibility flip.
        Assert.True(chosen.State.GetCard(agenda).FaceUp);

        // CR 7.2.2/7.1.5: the mid-access window offers the pass only.
        PendingDecision accessDecision = chosen.State.PendingDecision!;
        LegalAction pass = Assert.Single(accessDecision.LegalActions);
        Assert.Equal(ActionKind.NoAccessAction, pass.Kind);

        // CR 7.2.3: passing it steals the still-accessed agenda automatically.
        ApplyResult stolen = ApplyAction(chosen.State, pass);
        Assert.True(stolen.Accepted, stolen.Rejection?.Message);
        GameState terminal = stolen.State;

        ZoneState runnerScore = terminal.Zones.Single(zone => zone.Kind == ZoneKind.RunnerScore && zone.ServerId is null);
        CardInstanceState scoredCard = terminal.GetCard(agenda);
        Assert.Equal(runnerScore.Id, scoredCard.ZoneId);
        Assert.True(scoredCard.FaceUp);
        ZoneState discardAfter = terminal.Zones.Single(zone => zone.Kind == ZoneKind.CorpDiscard && zone.ServerId is null);
        Assert.DoesNotContain(agenda, discardAfter.CardIds);
        Assert.Equal(2, terminal.Players.Single(player => player.Side == PlayerSide.Runner).AgendaPoints);

        // Last candidate accessed: the run completes normally, game ongoing.
        Assert.Null(terminal.RunState);
        Assert.Equal(GameOutcomeKind.Ongoing, terminal.Outcome.Kind);
        RunEndedEventPayload ended = Assert.IsType<RunEndedEventPayload>(stolen.Events[^1].Payload);
        Assert.Equal(RunEndReason.BreachCompleted, ended.Reason);
    }

    [Fact]
    public void RemoteBreachFacedownAssetVisibleToBothViewersOnlyDuringTheBreach()
    {
        GameState fixture = CreateBreachState(remoteRoot: [false]);
        ServerId remoteId = fixture.Servers.Single(server => server.Kind == ServerKind.Remote).Id;
        ZoneState root = fixture.Zones.Single(zone => zone.Id == fixture.GetServer(remoteId).RootZoneId);
        CardInstanceId asset = root.CardIds[0];

        ApplyResult breach = PassTwice(BeginRun(fixture, remoteId));
        Assert.True(breach.Accepted, breach.Rejection?.Message);
        Assert.Equal(RunTimingStep.BreachCandidateSelection, breach.State.RunState!.Step);

        // Before selection the facedown asset's identity is unknown to the
        // Runner (its server root slot is anonymous) and known to the owning
        // Corp; the candidate offer only names the card id in the action.
        string runnerBefore = EngineJson.Serialize(BuildObservation(breach.State, PlayerSide.Runner));
        Assert.DoesNotContain(FixturePlain.Value, runnerBefore);
        Assert.Contains(asset.Value, EngineJson.Serialize(BuildObservation(breach.State, PlayerSide.Corp)));

        ApplyResult chosen = ApplyAction(breach.State, Assert.Single(breach.State.PendingDecision!.LegalActions));
        Assert.True(chosen.Accepted, chosen.Rejection?.Message);
        Assert.False(chosen.State.GetCard(asset).FaceUp);

        // CR 26.03 7.3.1a: during the breach the chosen card is visible to
        // both viewers in the run view even though it is facedown in state.
        string runnerMid = EngineJson.Serialize(BuildObservation(chosen.State, PlayerSide.Runner));
        string corpMid = EngineJson.Serialize(BuildObservation(chosen.State, PlayerSide.Corp));
        Assert.Contains(asset.Value, runnerMid);
        Assert.Contains(FixturePlain.Value, runnerMid);
        Assert.Contains(asset.Value, corpMid);
        Assert.Contains(FixturePlain.Value, corpMid);

        ApplyResult noAction = ApplyKind(chosen.State, ActionKind.NoAccessAction);
        Assert.True(noAction.Accepted, noAction.Rejection?.Message);
        GameState terminal = noAction.State;
        Assert.Null(terminal.RunState);

        // After the run the card is facedown again: hidden from the Runner's
        // server view, still visible to the owning Corp.
        Assert.False(terminal.GetCard(asset).FaceUp);
        string runnerAfter = EngineJson.Serialize(BuildObservation(terminal, PlayerSide.Runner));
        string corpAfter = EngineJson.Serialize(BuildObservation(terminal, PlayerSide.Corp));
        Assert.DoesNotContain(asset.Value, runnerAfter);
        Assert.DoesNotContain(FixturePlain.Value, runnerAfter);
        Assert.Contains(asset.Value, corpAfter);
        Assert.Contains(FixturePlain.Value, corpAfter);
    }

    [Fact]
    public void HqBreachFacedownHandCardVisibleToBothViewersOnlyDuringTheBreach()
    {
        GameState fixture = CreateBreachState(hand: [false, false]);
        ServerId headquarters = fixture.Servers.Single(server => server.Kind == ServerKind.Headquarters).Id;
        ZoneState hand = fixture.Zones.Single(zone => zone.Kind == ZoneKind.CorpHand && zone.ServerId is null);

        ApplyResult breach = PassTwice(BeginRun(fixture, headquarters));
        Assert.True(breach.Accepted, breach.Rejection?.Message);
        Assert.Equal(RunTimingStep.BreachCandidateSelection, breach.State.RunState!.Step);

        LegalAction token = Assert.Single(breach.State.PendingDecision!.LegalActions);
        ApplyResult chosen = ApplyAction(breach.State, token);
        Assert.True(chosen.Accepted, chosen.Rejection?.Message);
        BreachCandidateChosenEventPayload chosenPayload = Assert.IsType<BreachCandidateChosenEventPayload>(
            chosen.Events.Single(value => value.Kind == EngineEventKinds.BreachCandidateChosen).Payload);
        CardInstanceId drawn = chosenPayload.CardId;
        CardInstanceId undrawn = hand.CardIds.Single(candidate => candidate != drawn);

        // CR 26.03 7.3.1a: the drawn hand card stays facedown in state.
        Assert.False(chosen.State.GetCard(drawn).FaceUp);

        // Both viewers see the drawn card in the run view; the Runner never
        // sees the undrawn hand card.
        string runnerMid = EngineJson.Serialize(BuildObservation(chosen.State, PlayerSide.Runner));
        string corpMid = EngineJson.Serialize(BuildObservation(chosen.State, PlayerSide.Corp));
        Assert.Contains(drawn.Value, runnerMid);
        Assert.Contains(FixturePlain.Value, runnerMid);
        Assert.Contains(drawn.Value, corpMid);
        Assert.Contains(FixturePlain.Value, corpMid);
        Assert.DoesNotContain(undrawn.Value, runnerMid);

        ApplyResult noAction = ApplyKind(chosen.State, ActionKind.NoAccessAction);
        Assert.True(noAction.Accepted, noAction.Rejection?.Message);
        GameState terminal = noAction.State;
        Assert.Null(terminal.RunState);

        // After the run the drawn card is facedown again in the hand:
        // invisible to the Runner, still listed in the Corp's private view.
        Assert.False(terminal.GetCard(drawn).FaceUp);
        string runnerAfter = EngineJson.Serialize(BuildObservation(terminal, PlayerSide.Runner));
        string corpAfter = EngineJson.Serialize(BuildObservation(terminal, PlayerSide.Corp));
        Assert.DoesNotContain(drawn.Value, runnerAfter);
        Assert.DoesNotContain(FixturePlain.Value, runnerAfter);
        Assert.Contains(drawn.Value, corpAfter);
        Assert.Contains(undrawn.Value, corpAfter);
    }

    [Fact]
    public void RndBreachRevealsTopCardOnlyToRunnerAndOnlyDuringTheBreach()
    {
        GameState fixture = CreateBreachState(deck: [false, false]);
        ZoneState deck = fixture.Zones.Single(zone => zone.Kind == ZoneKind.CorpDeck && zone.ServerId is null);
        CardInstanceId top = deck.CardIds[0];
        CardInstanceId second = deck.CardIds[1];
        ServerId rnd = fixture.Servers.Single(server => server.Kind == ServerKind.RAndD).Id;

        ApplyResult breach = PassTwice(BeginRun(fixture, rnd));
        Assert.True(breach.Accepted, breach.Rejection?.Message);
        Assert.Equal(RunTimingStep.BreachCandidateSelection, breach.State.RunState!.Step);

        LegalAction token = Assert.Single(breach.State.PendingDecision!.LegalActions);
        ApplyResult chosen = ApplyAction(breach.State, token);
        Assert.True(chosen.Accepted, chosen.Rejection?.Message);
        BreachCandidateChosenEventPayload chosenPayload = Assert.IsType<BreachCandidateChosenEventPayload>(
            chosen.Events.Single(value => value.Kind == EngineEventKinds.BreachCandidateChosen).Payload);
        Assert.Equal(top, chosenPayload.CardId);

        // CR 26.03 7.3.1a: the top deck card stays facedown in state.
        Assert.False(chosen.State.GetCard(top).FaceUp);

        // CR 7.1.2: R&D access does not grant the Corp permission to look.
        string runnerMid = EngineJson.Serialize(BuildObservation(chosen.State, PlayerSide.Runner));
        string corpMid = EngineJson.Serialize(BuildObservation(chosen.State, PlayerSide.Corp));
        Assert.Contains(top.Value, runnerMid);
        Assert.Contains(FixturePlain.Value, runnerMid);
        Assert.DoesNotContain(top.Value, corpMid);
        Assert.DoesNotContain(FixturePlain.Value, corpMid);
        Assert.DoesNotContain(second.Value, corpMid);
        Assert.DoesNotContain(second.Value, runnerMid);

        ApplyResult noAction = ApplyKind(chosen.State, ActionKind.NoAccessAction);
        Assert.True(noAction.Accepted, noAction.Rejection?.Message);
        GameState terminal = noAction.State;
        Assert.Null(terminal.RunState);

        // After the run the top card is facedown again in the deck:
        // invisible to either player.
        Assert.False(terminal.GetCard(top).FaceUp);
        string runnerAfter = EngineJson.Serialize(BuildObservation(terminal, PlayerSide.Runner));
        string corpAfter = EngineJson.Serialize(BuildObservation(terminal, PlayerSide.Corp));
        Assert.DoesNotContain(top.Value, runnerAfter);
        Assert.DoesNotContain(FixturePlain.Value, runnerAfter);
        Assert.DoesNotContain(top.Value, corpAfter);
        Assert.DoesNotContain(second.Value, corpAfter);
    }

    [Fact]
    public void ArchivesBreachDiscardVisibilityIsAuthoritativeAndPersistent()
    {
        GameState fixture = CreateBreachState(discard: [false, true]);
        ZoneState discard = fixture.Zones.Single(zone => zone.Kind == ZoneKind.CorpDiscard && zone.ServerId is null);
        CardInstanceId facedown = discard.CardIds[0];
        CardInstanceId faceup = discard.CardIds[1];
        ServerId archives = fixture.Servers.Single(server => server.Kind == ServerKind.Archives).Id;

        ApplyResult breach = PassTwice(BeginRun(fixture, archives));
        Assert.True(breach.Accepted, breach.Rejection?.Message);
        Assert.Equal(RunTimingStep.BreachCandidateSelection, breach.State.RunState!.Step);

        // CR 26.03 7.5.2: the breach turns every CorpDiscard card faceup
        // authoritatively before candidate selection.
        Assert.True(breach.State.GetCard(facedown).FaceUp);
        string runnerMid = EngineJson.Serialize(BuildObservation(breach.State, PlayerSide.Runner));
        string corpMid = EngineJson.Serialize(BuildObservation(breach.State, PlayerSide.Corp));
        Assert.Contains(facedown.Value, runnerMid);
        Assert.Contains(facedown.Value, corpMid);
        Assert.Contains(faceup.Value, runnerMid);
        Assert.Contains(faceup.Value, corpMid);

        PendingDecision firstDecision = breach.State.PendingDecision!;
        LegalAction chooseFacedown = Assert.Single(firstDecision.LegalActions, candidate =>
            candidate.Kind == ActionKind.ChooseBreachCandidate
            && Assert.IsType<BreachCandidateParameters>(candidate.Parameters).CardId == facedown);
        ApplyResult chosenFacedown = ApplyAction(breach.State, chooseFacedown);
        Assert.True(chosenFacedown.Accepted, chosenFacedown.Rejection?.Message);
        Assert.True(chosenFacedown.State.GetCard(facedown).FaceUp);
        ApplyResult noAction = ApplyKind(chosenFacedown.State, ActionKind.NoAccessAction);
        Assert.True(noAction.Accepted, noAction.Rejection?.Message);

        PendingDecision secondDecision = noAction.State.PendingDecision!;
        LegalAction chooseFaceup = Assert.Single(secondDecision.LegalActions, candidate =>
            candidate.Kind == ActionKind.ChooseBreachCandidate
            && Assert.IsType<BreachCandidateParameters>(candidate.Parameters).CardId == faceup);
        ApplyResult chosenFaceup = ApplyAction(noAction.State, chooseFaceup);
        Assert.True(chosenFaceup.Accepted, chosenFaceup.Rejection?.Message);
        ApplyResult finalNoAction = ApplyKind(chosenFaceup.State, ActionKind.NoAccessAction);
        Assert.True(finalNoAction.Accepted, finalNoAction.Rejection?.Message);

        GameState terminal = finalNoAction.State;
        Assert.Null(terminal.RunState);

        // The authoritative flip persists after the run, so both cards stay
        // public to both viewers.
        Assert.True(terminal.GetCard(facedown).FaceUp);
        string runnerAfter = EngineJson.Serialize(BuildObservation(terminal, PlayerSide.Runner));
        string corpAfter = EngineJson.Serialize(BuildObservation(terminal, PlayerSide.Corp));
        string corpPublicAfter = EngineJson.Serialize(BuildObservation(terminal, PlayerSide.Corp).PublicState);
        Assert.Contains(facedown.Value, runnerAfter);
        Assert.Contains(facedown.Value, corpPublicAfter);
        Assert.Contains(facedown.Value, corpAfter);
        Assert.Contains(faceup.Value, runnerAfter);
        Assert.Contains(faceup.Value, corpAfter);
    }

    private static GameState WithFormalRunnerFacts(GameState state)
    {
        TurnFactsState facts = new(
            1,
            PlayerSide.Runner,
            state.Turn.TurnNumber,
            clicksAllotted: 4,
            clicksLost: 0,
            turnBegan: true,
            mandatoryDrawPerformed: false,
            actionPhaseEnded: false,
            discardPhaseEnded: false);
        return new GameState(
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            state.Players,
            state.Cards,
            state.Zones,
            state.Servers,
            new TurnState(
                PlayerSide.Runner,
                TurnPhase.Runner,
                state.Turn.TurnNumber,
                TurnStep.RunnerAction,
                new TurnHistoryState(1, null, null, facts, null)),
            state.Outcome,
            state.RandomState,
            state.Allocator,
            state.SchemaVersion,
            state.DecisionRevision,
            state.EventSequence,
            state.ResolutionStack,
            state.PendingDecision,
            state.ActionSequence,
            state.InitialSeed,
            state.RunState,
            state.RulesState,
            state.BehaviorRegistryVersion,
            state.BehaviorCatalogFingerprint,
            state.RuntimeCardCatalogVersion,
            state.RuntimeCardCatalogFingerprint);
    }

    private static GameState AddRunEventSource(GameState state, CardDefinitionId definitionId)
    {
        ZoneState grip = state.GetZone(ZoneKind.RunnerGrip);
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
        return result;
    }

    private static GameState CreateBreachState(
        IReadOnlyList<bool>? hand = null,
        IReadOnlyList<bool>? deck = null,
        IReadOnlyList<bool>? discard = null,
        IReadOnlyList<bool>? remoteRoot = null,
        IReadOnlyList<CardDefinitionId>? remoteRootDefinitions = null,
        IReadOnlyList<CardDefinitionId>? discardDefinitions = null,
        IReadOnlyList<bool>? hqRoot = null,
        IReadOnlyList<CardDefinitionId>? hqRootDefinitions = null,
        IReadOnlyList<bool>? rndRoot = null,
        IReadOnlyList<CardDefinitionId>? rndRootDefinitions = null,
        IReadOnlyList<bool>? archivesRoot = null,
        IReadOnlyList<CardDefinitionId>? archivesRootDefinitions = null,
        int runnerCredits = 4,
        int runnerAgendaPoints = 0)
    {
        GameState baseState = GameState.CreateNew(99);
        var allocator = new StableIdAllocator(baseState.Allocator);
        var cards = new List<CardInstanceState>();
        var zones = baseState.Zones.ToList();
        var servers = baseState.Servers.ToList();

        if (remoteRoot is not null)
        {
            ServerId remoteId = allocator.NextServerId();
            ZoneId iceZoneId = allocator.NextZoneId();
            ZoneId rootZoneId = allocator.NextZoneId();
            var rootIds = new List<CardInstanceId>();
            for (int index = 0; index < remoteRoot.Count; index++)
            {
                CardInstanceId cardId = allocator.NextCardInstanceId();
                rootIds.Add(cardId);
                cards.Add(new CardInstanceState(
                    cardId,
                    remoteRootDefinitions is not null && index < remoteRootDefinitions.Count
                        ? remoteRootDefinitions[index]
                        : FixturePlain,
                    rootZoneId,
                    PlayerSide.Corp,
                    faceUp: remoteRoot[index]));
            }

            servers.Add(new ServerState(remoteId, ServerKind.Remote, iceZoneId, rootZoneId));
            zones.Add(new ZoneState(iceZoneId, ZoneKind.ServerIce, PlayerSide.Corp, remoteId));
            zones.Add(new ZoneState(rootZoneId, ZoneKind.ServerRoot, PlayerSide.Corp, remoteId, rootIds));
        }

        AddCards(ZoneKind.CorpHand, hand, null);
        AddCards(ZoneKind.CorpDeck, deck, null);
        AddCards(ZoneKind.CorpDiscard, discard, discardDefinitions);
        AddServerRootCards(servers.Single(server => server.Kind == ServerKind.Headquarters).RootZoneId, hqRoot, hqRootDefinitions);
        AddServerRootCards(servers.Single(server => server.Kind == ServerKind.RAndD).RootZoneId, rndRoot, rndRootDefinitions);
        AddServerRootCards(servers.Single(server => server.Kind == ServerKind.Archives).RootZoneId, archivesRoot, archivesRootDefinitions);

        var players = baseState.Players.Select(player => new PlayerState(
            player.Id,
            player.Side,
            player.Side == PlayerSide.Runner ? runnerCredits : player.Credits,
            player.Side == PlayerSide.Runner ? 4 : 3,
            player.Side == PlayerSide.Runner ? runnerAgendaPoints : player.AgendaPoints)).ToArray();
        return new GameState(
            baseState.SchemaVersion,
            baseState.GameId,
            baseState.RulesVersion,
            baseState.CardDataVersion,
            players,
            cards,
            zones,
            servers,
            new TurnState(PlayerSide.Runner, TurnPhase.Runner, baseState.Turn.TurnNumber),
            new GameOutcome(),
            baseState.RandomState,
            allocator.State,
            baseState.DecisionRevision,
            baseState.EventSequence,
            [],
            null,
            baseState.ActionSequence,
            baseState.InitialSeed);

        void AddCards(ZoneKind kind, IReadOnlyList<bool>? faceUps, IReadOnlyList<CardDefinitionId>? definitions)
        {
            if (faceUps is null)
            {
                return;
            }

            ZoneState zone = zones.Single(candidate => candidate.Kind == kind && candidate.ServerId is null);
            var ids = new List<CardInstanceId>();
            for (int index = 0; index < faceUps.Count; index++)
            {
                CardInstanceId cardId = allocator.NextCardInstanceId();
                ids.Add(cardId);
                cards.Add(new CardInstanceState(
                    cardId,
                    definitions is not null && index < definitions.Count ? definitions[index] : FixturePlain,
                    zone.Id,
                    PlayerSide.Corp,
                    faceUp: faceUps[index]));
            }

            zones[zones.IndexOf(zone)] = new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, ids);
        }

        void AddServerRootCards(ZoneId rootZoneId, IReadOnlyList<bool>? faceUps, IReadOnlyList<CardDefinitionId>? definitions)
        {
            if (faceUps is null)
            {
                return;
            }

            ZoneState zone = zones.Single(candidate => candidate.Id == rootZoneId);
            var ids = new List<CardInstanceId>();
            for (int index = 0; index < faceUps.Count; index++)
            {
                CardInstanceId cardId = allocator.NextCardInstanceId();
                ids.Add(cardId);
                cards.Add(new CardInstanceState(
                    cardId,
                    definitions is not null && index < definitions.Count ? definitions[index] : FixturePlain,
                    zone.Id,
                    PlayerSide.Corp,
                    faceUp: faceUps[index]));
            }

            zones[zones.IndexOf(zone)] = new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, ids);
        }
    }

    private static GameState BeginRun(
        GameState state,
        ServerId serverId,
        PlayerId? initiatingPlayer = null,
        CardInstanceId? initiatingSource = null) =>
        GameEngine.BeginRun(
            state,
            serverId,
            FixtureBehaviorCatalog.Catalog,
            initiatingPlayer,
            initiatingSource);

    private static GameEngine.RunStartResult BeginRunWithEvents(
        GameState state,
        ServerId serverId,
        PlayerId? initiatingPlayer = null,
        CardInstanceId? initiatingSource = null) =>
        GameEngine.BeginRunWithEvents(
            state,
            serverId,
            FixtureBehaviorCatalog.Catalog,
            initiatingPlayer,
            initiatingSource);

    private static ApplyResult Apply(GameState state, ActionSubmission submission) =>
        GameEngine.Apply(state, submission, FixtureBehaviorCatalog.Catalog);

    private static Observation BuildObservation(GameState state, PlayerSide viewer) =>
        ObservationBuilder.Build(state, viewer, FixtureBehaviorCatalog.Catalog);

    private static string[] RelevantKinds(params ApplyResult[] results) => results
        .SelectMany(result => result.Events)
        .Where(value => value.Kind is
            EngineEventKinds.RunSuccessful
            or EngineEventKinds.BreachStarted
            or EngineEventKinds.BreachCandidateChosen
            or EngineEventKinds.CardAccessed
            or EngineEventKinds.RunEnded)
        .Select(value => value.Kind)
        .ToArray();
}

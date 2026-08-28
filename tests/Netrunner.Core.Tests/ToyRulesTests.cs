using System.Text.Json;
using Xunit;

namespace Netrunner.Core.Tests;

public sealed class ToyRulesTests
{
    [Fact]
    public void SetupIsDeterministicAndYieldsCorpDecisionAfterMandatoryDraw()
    {
        GameState first = CreateState();
        GameState second = CreateState();

        Assert.Equal(EngineJson.Serialize(first), EngineJson.Serialize(second));
        Assert.Equal(DeterministicHash.ForState(first), DeterministicHash.ForState(second));
        Assert.Equal(PlayerSide.Corp, first.Turn.ActiveSide);
        Assert.Equal(TurnPhase.Corp, first.Turn.Phase);
        Assert.Equal(1, first.Turn.TurnNumber);
        PendingDecision decision = Assert.IsType<PendingDecision>(first.PendingDecision);
        Assert.Equal(DecisionKind.ToyTurn, decision.Kind);
        Assert.Equal(PlayerSide.Corp, first.GetPlayer(decision.Owner).Side);
        Assert.Contains(decision.LegalActions, action => action.Kind == ActionKind.GainCredit);
        Assert.Contains(decision.LegalActions, action => action.Kind == ActionKind.DrawCard);
        Assert.Contains(first.ResolutionStack, frame => frame is ToyTurnFrame { Stage: ToyTurnStage.AwaitingAction });
        Assert.Equal(3, first.GetZone(ZoneKind.CorpHand).CardIds.Count);
    }

    [Fact]
    public void GainCreditConsumesOneClickAndEmitsTypedEvents()
    {
        GameState state = CreateState();
        ApplyResult result = ApplyKind(state, ActionKind.GainCredit);

        Assert.True(result.Accepted, result.Rejection?.Message);
        PlayerState corp = result.State.Players.Single(player => player.Side == PlayerSide.Corp);
        Assert.Equal(6, corp.Credits);
        Assert.Equal(2, corp.Clicks);
        Assert.Contains(result.Events, @event => @event.Kind == EngineEventKinds.CreditsAdjusted);
        Assert.Contains(result.Events, @event => @event.Kind == EngineEventKinds.ClickSpent);
        StateInvariantValidator.Validate(result.State);
    }

    [Fact]
    public void DrawMovesCardAndKeepsCardZoneReferencesConsistent()
    {
        GameState state = CreateState();
        int handBefore = state.GetZone(ZoneKind.CorpHand).CardIds.Count;
        int deckBefore = state.GetZone(ZoneKind.CorpDeck).CardIds.Count;
        ApplyResult result = ApplyKind(state, ActionKind.DrawCard);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(handBefore + 1, result.State.GetZone(ZoneKind.CorpHand).CardIds.Count);
        Assert.Equal(deckBefore - 1, result.State.GetZone(ZoneKind.CorpDeck).CardIds.Count);
        Assert.Contains(result.Events, @event => @event.Kind == EngineEventKinds.CardMoved);
        Assert.Contains(result.Events, @event => @event.Kind == EngineEventKinds.CardDrawn);
        StateInvariantValidator.Validate(result.State);
    }

    [Fact]
    public void InstallMovesCorpAgendaToDeterministicRemoteAndChargesFixedCost()
    {
        GameState state = CreateState();
        CardInstanceId agenda = state.GetZone(ZoneKind.CorpHand).CardIds[0];
        ApplyResult result = ApplyKind(state, ActionKind.InstallCard, agenda);

        Assert.True(result.Accepted, result.Rejection?.Message);
        CardInstanceState installed = result.State.GetCard(agenda);
        ZoneState remoteRoot = result.State.Servers
            .Where(server => server.Kind == ServerKind.Remote)
            .Select(server => result.State.GetZone(server.RootZoneId))
            .Single();
        Assert.Equal(remoteRoot.Id, installed.ZoneId);
        Assert.Contains(agenda, remoteRoot.CardIds);
        Assert.Equal(3, result.State.Players.Single(player => player.Side == PlayerSide.Corp).Credits);
        Assert.Equal(2, result.State.Players.Single(player => player.Side == PlayerSide.Corp).Clicks);
        Assert.Contains(result.Events, @event =>
            @event.Payload is ClickSpentEventPayload { Side: PlayerSide.Corp, ClicksRemaining: 2 });
    }

    [Fact]
    public void RunnerInstallMovesProgramToRigAndChargesFixedCost()
    {
        GameState state = CreateState();
        state = ApplyKind(state, ActionKind.EndTurn).State;
        CardInstanceId program = state.GetZone(ZoneKind.RunnerGrip).CardIds[0];
        ApplyResult result = ApplyKind(state, ActionKind.InstallCard, program);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(
            result.State.GetZone(ZoneKind.RunnerRig).Id,
            result.State.GetCard(program).ZoneId);
        Assert.Equal(3, result.State.Players.Single(player => player.Side == PlayerSide.Runner).Credits);
        Assert.Equal(3, result.State.Players.Single(player => player.Side == PlayerSide.Runner).Clicks);
        Assert.Contains(result.Events, @event =>
            @event.Payload is ClickSpentEventPayload { Side: PlayerSide.Runner, ClicksRemaining: 3 });
        StateInvariantValidator.Validate(result.State);
    }

    [Fact]
    public void CorpCanAdvanceAndScoreWithoutAnExtraScoreClick()
    {
        GameState state = CreateState();
        CardInstanceId agenda = state.GetZone(ZoneKind.CorpHand).CardIds[0];
        state = ApplyKind(state, ActionKind.InstallCard, agenda).State;
        state = ApplyKind(state, ActionKind.AdvanceAgenda, agenda).State;
        state = ApplyKind(state, ActionKind.AdvanceAgenda, agenda).State;
        Assert.Equal(0, state.GetPlayer(state.Players.Single(player => player.Side == PlayerSide.Corp).Id).Clicks);
        ApplyResult firstEnd = ApplyKind(state, ActionKind.EndTurn);
        Assert.True(firstEnd.Accepted, firstEnd.Rejection?.Message);
        state = firstEnd.State;
        ApplyResult secondEnd = ApplyKind(state, ActionKind.EndTurn);
        Assert.True(secondEnd.Accepted, secondEnd.Rejection?.Message);
        state = secondEnd.State;
        Assert.Equal(PlayerSide.Corp, state.Turn.ActiveSide);
        Assert.True(state.PendingDecision is not null, EngineJson.Serialize(state));
        Assert.Contains(state.PendingDecision!.LegalActions, action => action.Kind == ActionKind.AdvanceAgenda);
        state = ApplyKind(state, ActionKind.AdvanceAgenda, agenda).State;
        Assert.Equal(2, state.GetPlayer(state.Players.Single(player => player.Side == PlayerSide.Corp).Id).Clicks);
        Assert.Contains(state.PendingDecision!.LegalActions, action => action.Kind == ActionKind.ScoreAgenda);
        ApplyResult result = ApplyKind(state, ActionKind.ScoreAgenda, agenda);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(GameOutcomeKind.Won, result.State.Outcome.Kind);
        Assert.Equal(PlayerSide.Corp, result.State.Outcome.Winner);
        Assert.Equal(GameWinReason.AgendaPoints, result.State.Outcome.Reason);
        Assert.Equal(TurnPhase.GameOver, result.State.Turn.Phase);
        Assert.Null(result.State.PendingDecision);
        Assert.Empty(result.State.ResolutionStack);
        Assert.True(result.State.GetCard(agenda).FaceUp);
        Assert.Equal(PlayerSide.Corp, result.State.GetCard(agenda).OwnerSide);
        Assert.All(result.State.Players, player => Assert.Equal(0, player.Clicks));
        Assert.Contains(result.Events, @event => @event.Kind == EngineEventKinds.AgendaScored);
        Assert.Contains(result.Events, @event => @event.Kind == EngineEventKinds.GameWon);
    }

    [Fact]
    public void RunnerCanStealInstalledAgendaForAgendaPointWin()
    {
        GameState state = CreateState();
        CardInstanceId agenda = state.GetZone(ZoneKind.CorpHand).CardIds[0];
        state = ApplyKind(state, ActionKind.InstallCard, agenda).State;
        state = ApplyKind(state, ActionKind.EndTurn).State;
        ApplyResult result = ApplyKind(state, ActionKind.StealAgenda, agenda);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(GameOutcomeKind.Won, result.State.Outcome.Kind);
        Assert.Equal(PlayerSide.Runner, result.State.Outcome.Winner);
        Assert.Equal(GameWinReason.AgendaPoints, result.State.Outcome.Reason);
        Assert.Equal(result.State.GetZone(ZoneKind.RunnerScore).Id, result.State.GetCard(agenda).ZoneId);
        Assert.True(result.State.GetCard(agenda).FaceUp);
        Assert.Equal(PlayerSide.Corp, result.State.GetCard(agenda).OwnerSide);
        Assert.Contains(result.Events, @event =>
            @event.Payload is ClickSpentEventPayload { Side: PlayerSide.Runner, ClicksRemaining: 3 });
        Assert.Contains(result.Events, @event => @event.Kind == EngineEventKinds.AgendaStolen);
    }

    [Fact]
    public void EmptyCorpDrawAtTurnStartProducesRunnerDeckOutAndClearsResolution()
    {
        GameState state = CreateState(cardCount: 3);
        state = ApplyKind(state, ActionKind.EndTurn).State;
        ApplyResult result = ApplyKind(state, ActionKind.EndTurn);

        // The Runner's turn ends; the next Corp turn-start draw is the terminal step.
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(GameOutcomeKind.Won, result.State.Outcome.Kind);
        Assert.Equal(PlayerSide.Runner, result.State.Outcome.Winner);
        Assert.Equal(GameWinReason.DeckOut, result.State.Outcome.Reason);
        Assert.Null(result.State.PendingDecision);
        Assert.Empty(result.State.ResolutionStack);
    }

    [Fact]
    public void FailedCorpClickDrawOnEmptyDeckProducesRunnerDeckOut()
    {
        GameState state = CreateState(cardCount: 3);
        PendingDecision decision = state.PendingDecision!;
        Assert.Contains(decision.LegalActions, action => action.Kind == ActionKind.DrawCard);

        ApplyResult result = ApplyKind(state, ActionKind.DrawCard);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(GameOutcomeKind.Won, result.State.Outcome.Kind);
        Assert.Equal(PlayerSide.Runner, result.State.Outcome.Winner);
        Assert.Equal(GameWinReason.DeckOut, result.State.Outcome.Reason);
        Assert.Null(result.State.PendingDecision);
        Assert.Empty(result.State.ResolutionStack);
    }

    [Fact]
    public void CompletedAgendaCanBeScoredAtZeroClicksWithoutASecondClickEvent()
    {
        GameState state = CreateState();
        CardInstanceId agenda = state.GetZone(ZoneKind.CorpHand).CardIds[0];
        state = ApplyKind(state, ActionKind.InstallCard, agenda).State;
        state = ApplyKind(state, ActionKind.AdvanceAgenda, agenda).State;
        state = ApplyKind(state, ActionKind.AdvanceAgenda, agenda).State;
        state = ApplyKind(state, ActionKind.EndTurn).State;
        state = ApplyKind(state, ActionKind.EndTurn).State;
        state = ApplyKind(state, ActionKind.AdvanceAgenda, agenda).State;
        state = ApplyKind(state, ActionKind.GainCredit).State;
        state = ApplyKind(state, ActionKind.GainCredit).State;

        Assert.Equal(0, state.Players.Single(player => player.Side == PlayerSide.Corp).Clicks);
        Assert.Contains(state.PendingDecision!.LegalActions, action => action.Kind == ActionKind.ScoreAgenda);
        string scoreJson = EngineJson.Serialize(
            state.PendingDecision.LegalActions.Single(action => action.Kind == ActionKind.ScoreAgenda));
        Assert.Contains("\"card\"", scoreJson, StringComparison.Ordinal);

        ApplyResult result = ApplyKind(state, ActionKind.ScoreAgenda, agenda);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(GameOutcomeKind.Won, result.State.Outcome.Kind);
        Assert.DoesNotContain(result.Events, @event => @event.Kind == EngineEventKinds.ClickSpent);
        Assert.Contains(result.Events, @event => @event.Kind == EngineEventKinds.AgendaScored);
    }

    [Fact]
    public void EmptyRunnerStackOmitsDrawAndOtherRunnerActionRemainsNonterminal()
    {
        GameState state = CreateState(cardCount: 3);
        state = ApplyKind(state, ActionKind.EndTurn).State;
        Assert.Contains(state.PendingDecision!.LegalActions, action => action.Kind == ActionKind.DrawCard);
        state = ApplyKind(state, ActionKind.DrawCard).State;

        Assert.Empty(state.GetZone(ZoneKind.RunnerStack).CardIds);
        Assert.DoesNotContain(state.PendingDecision!.LegalActions, action => action.Kind == ActionKind.DrawCard);
        ApplyResult result = ApplyKind(state, ActionKind.GainCredit);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(GameOutcomeKind.Ongoing, result.State.Outcome.Kind);
        Assert.Equal(PlayerSide.Runner, result.State.Turn.ActiveSide);
    }

    [Fact]
    public void OccupiedCorpRemoteOmitsSecondInstallAndRejectsTamperedActionSet()
    {
        GameState state = CreateState();
        CardInstanceId firstAgenda = state.GetZone(ZoneKind.CorpHand).CardIds[0];
        CardInstanceId secondAgenda = state.GetZone(ZoneKind.CorpHand).CardIds[1];
        state = ApplyKind(state, ActionKind.InstallCard, firstAgenda).State;

        Assert.DoesNotContain(state.PendingDecision!.LegalActions, action => action.Kind == ActionKind.InstallCard);
        LegalAction forged = new(
            new ActionId("action:forged-install"),
            ActionKind.InstallCard,
            "forged description",
            new CardChoiceParameters(secondAgenda));
        GameState tampered = ReplacePending(state, state.PendingDecision.LegalActions.Append(forged));
        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(tampered);
        Assert.Contains(violations, violation => violation.Code == "decision.extra-toy-action");

        ApplyResult result = GameEngine.Apply(
            tampered,
            new ActionSubmission(
                tampered.PendingDecision!.Owner,
                tampered.PendingDecision.Id,
                tampered.PendingDecision.Revision,
                forged.Id));
        Assert.False(result.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, result.Rejection?.Code);
        Assert.Same(tampered, result.State);
    }

    [Fact]
    public void UnknownToyActionIsRejectedWithoutChangingState()
    {
        GameState state = CreateState();
        string before = EngineJson.Serialize(state);
        PendingDecision decision = state.PendingDecision!;
        ApplyResult result = GameEngine.Apply(
            state,
            new ActionSubmission(
                decision.Owner,
                decision.Id,
                decision.Revision,
                new ActionId("action:not-offered")));

        Assert.False(result.Accepted);
        Assert.Equal(ApplyRejectionCode.UnknownAction, result.Rejection?.Code);
        Assert.Same(state, result.State);
        Assert.Empty(result.Events);
        Assert.Equal(before, EngineJson.Serialize(state));
    }

    [Fact]
    public void SavingAndReloadingPendingToyTurnPreservesIdenticalCompletion()
    {
        GameState state = CreateState();
        CardInstanceId agenda = state.GetZone(ZoneKind.CorpHand).CardIds[0];
        GameState prompt = ApplyKind(state, ActionKind.InstallCard, agenda).State;
        string json = EngineJson.Serialize(prompt);
        GameState restored = EngineJson.DeserializeState(json);
        Assert.Equal(json, EngineJson.Serialize(restored));

        ApplyResult expected = ApplyKind(prompt, ActionKind.AdvanceAgenda, agenda);
        ApplyResult actual = ApplyKind(restored, ActionKind.AdvanceAgenda, agenda);
        Assert.True(expected.Accepted);
        Assert.True(actual.Accepted);
        Assert.Equal(EngineJson.Serialize(expected.State), EngineJson.Serialize(actual.State));
        Assert.Equal(DeterministicHash.ForEvents(expected.Events), DeterministicHash.ForEvents(actual.Events));
    }

    [Fact]
    public void ToyPendingDecisionIsAnExactStateDerivedActionBijection()
    {
        GameState state = CreateState();
        PendingDecision decision = state.PendingDecision!;
        foreach (ActionKind omittedKind in new[] { ActionKind.GainCredit, ActionKind.DrawCard, ActionKind.InstallCard })
        {
            LegalAction omitted = decision.LegalActions.First(action => action.Kind == omittedKind);
            GameState tampered = ReplacePending(state, decision.LegalActions.Where(action => action != omitted));
            Assert.Contains(
                StateInvariantValidator.Collect(tampered),
                violation => violation.Code == "decision.missing-toy-action");
        }

        CardInstanceId agenda = state.GetZone(ZoneKind.CorpHand).CardIds[0];
        state = ApplyKind(state, ActionKind.InstallCard, agenda).State;
        state = ApplyKind(state, ActionKind.AdvanceAgenda, agenda).State;
        state = ApplyKind(state, ActionKind.AdvanceAgenda, agenda).State;
        ApplyResult end = ApplyKind(state, ActionKind.EndTurn);
        Assert.True(end.Accepted, end.Rejection?.Message);
        end = ApplyKind(end.State, ActionKind.EndTurn);
        Assert.True(end.Accepted, end.Rejection?.Message);
        state = ApplyKind(end.State, ActionKind.AdvanceAgenda, agenda).State;
        LegalAction score = state.PendingDecision!.LegalActions.Single(action => action.Kind == ActionKind.ScoreAgenda);
        GameState missingScore = ReplacePending(
            state,
            state.PendingDecision.LegalActions.Where(action => action != score));
        Assert.Contains(
            StateInvariantValidator.Collect(missingScore),
            violation => violation.Code == "decision.missing-toy-action");

        LegalAction duplicateScore = new(
            new ActionId("action:duplicate-score"),
            ActionKind.ScoreAgenda,
            "A different description is irrelevant",
            new CardChoiceParameters(agenda));
        GameState duplicate = ReplacePending(state, state.PendingDecision.LegalActions.Append(duplicateScore));
        Assert.Contains(
            StateInvariantValidator.Collect(duplicate),
            violation => violation.Code == "decision.duplicate-toy-action");
    }

    [Fact]
    public void CorpWithNoCreditsDoesNotReceiveAnUnresolvableAdvanceAction()
    {
        GameState state = CreateState();
        CardInstanceId agenda = state.GetZone(ZoneKind.CorpHand).CardIds[0];
        state = ApplyKind(state, ActionKind.InstallCard, agenda).State;
        state = ApplyKind(state, ActionKind.AdvanceAgenda, agenda).State;
        state = ApplyKind(state, ActionKind.AdvanceAgenda, agenda).State;
        PlayerState corp = state.Players.Single(player => player.Side == PlayerSide.Corp);
        GameState noCredits = ReplacePlayer(
            state,
            new PlayerState(corp.Id, corp.Side, credits: 0, clicks: corp.Clicks, agendaPoints: corp.AgendaPoints));
        noCredits = new GameState(
            noCredits.SchemaVersion,
            noCredits.GameId,
            noCredits.RulesVersion,
            noCredits.CardDataVersion,
            noCredits.Players,
            noCredits.Cards,
            noCredits.Zones,
            noCredits.Servers,
            noCredits.Turn,
            noCredits.Outcome,
            noCredits.RandomState,
            noCredits.Allocator,
            noCredits.DecisionRevision,
            noCredits.EventSequence,
            [],
            null,
            noCredits.ActionSequence,
            noCredits.InitialSeed);
        noCredits = GameEngine.BeginResolution(
            noCredits,
            new ToyTurnFrame(PlayerSide.Corp, ToyTurnStage.AwaitingAction));

        Assert.DoesNotContain(noCredits.PendingDecision!.LegalActions, action => action.Kind == ActionKind.AdvanceAgenda);
        Assert.DoesNotContain(noCredits.PendingDecision.LegalActions, action => action.Kind == ActionKind.InstallCard);
        StateInvariantValidator.Validate(noCredits);
    }

    [Fact]
    public void SameSeedProducesIdenticalStateAndEvidenceAtEveryToyStep()
    {
        GameState first = CreateState();
        GameState second = CreateState();
        CardInstanceId firstAgenda = first.GetZone(ZoneKind.CorpHand).CardIds[0];
        CardInstanceId secondAgenda = second.GetZone(ZoneKind.CorpHand).CardIds[0];
        ActionKind[] kinds =
        [
            ActionKind.InstallCard,
            ActionKind.AdvanceAgenda,
            ActionKind.AdvanceAgenda,
            ActionKind.EndTurn,
            ActionKind.EndTurn,
            ActionKind.AdvanceAgenda,
            ActionKind.ScoreAgenda,
        ];

        foreach (ActionKind kind in kinds)
        {
            CardInstanceId? firstTarget = kind is ActionKind.InstallCard or ActionKind.AdvanceAgenda or ActionKind.ScoreAgenda
                ? firstAgenda
                : null;
            CardInstanceId? secondTarget = kind is ActionKind.InstallCard or ActionKind.AdvanceAgenda or ActionKind.ScoreAgenda
                ? secondAgenda
                : null;
            ApplyResult firstResult = ApplyKind(first, kind, firstTarget);
            ApplyResult secondResult = ApplyKind(second, kind, secondTarget);
            Assert.True(firstResult.Accepted, firstResult.Rejection?.Message);
            Assert.True(secondResult.Accepted, secondResult.Rejection?.Message);
            Assert.Equal(DeterministicHash.ForState(firstResult.State), DeterministicHash.ForState(secondResult.State));
            Assert.Equal(DeterministicHash.ForEvents(firstResult.Events), DeterministicHash.ForEvents(secondResult.Events));
            Assert.Equal(firstResult.AcceptedAction!.ResultingStateHash, secondResult.AcceptedAction!.ResultingStateHash);
            Assert.Equal(firstResult.AcceptedAction.ResultingEventBatchHash, secondResult.AcceptedAction.ResultingEventBatchHash);
            first = firstResult.State;
            second = secondResult.State;
        }

        Assert.Equal(EngineJson.Serialize(first), EngineJson.Serialize(second));
    }

    [Fact]
    public void UnknownFixtureDefinitionAndWrongSideRecipeFailClosed()
    {
        ToyDeckRecipe corp = new("corp", PlayerSide.Corp, [new CardDefinitionId("fixture:unknown")]);
        ToyDeckRecipe runner = new("runner", PlayerSide.Runner, [ToyCardDefinitionIds.RunnerProgram]);
        Assert.Throws<ArgumentException>(() => ToyGameSetup.Create(1, corp, runner));

        ToyDeckRecipe wrongSide = new("wrong", PlayerSide.Runner, [ToyCardDefinitionIds.CorpAgenda3]);
        Assert.Throws<ArgumentException>(() => ToyGameSetup.Create(1, wrongSide, runner));
    }

    [Fact]
    public void ToyFrameAndEventJsonRoundTripWithStrictDiscriminators()
    {
        GameState state = CreateState();
        string json = EngineJson.Serialize(state);
        GameState restored = EngineJson.DeserializeState(json);
        Assert.IsType<ToyTurnFrame>(restored.ResolutionStack.Single());
        Assert.IsType<NoActionParameters>(restored.PendingDecision!.LegalActions.First(action => action.Kind == ActionKind.GainCredit).Parameters);

        EngineEvent @event = new(
            1,
            1,
            EngineEventKinds.GameWon,
            new GameWonEventPayload(PlayerSide.Runner, GameWinReason.DeckOut));
        Assert.IsType<GameWonEventPayload>(EngineJson.Deserialize<EngineEvent>(EngineJson.Serialize(@event)).Payload);
        Assert.Throws<JsonException>(() => EngineJson.Deserialize<EngineEvent>(EngineJson.Serialize(@event).Replace("\"game-won\"", "\"unknown\"", StringComparison.Ordinal)));

        string missingStage = json.Replace("\"stage\":\"awaitingAction\",", string.Empty, StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => EngineJson.DeserializeState(missingStage));
    }

    private static GameState CreateState(int cardCount = 6)
    {
        ToyDeckRecipe corp = new(
            "corp-fixture",
            PlayerSide.Corp,
            Enumerable.Repeat(ToyCardDefinitionIds.CorpAgenda3, cardCount).ToArray());
        ToyDeckRecipe runner = new(
            "runner-fixture",
            PlayerSide.Runner,
            Enumerable.Repeat(ToyCardDefinitionIds.RunnerProgram, cardCount).ToArray());
        return ToyGameSetup.Create(1234, corp, runner);
    }

    private static ApplyResult ApplyKind(GameState state, ActionKind kind, CardInstanceId? cardId = null)
    {
        PendingDecision decision = state.PendingDecision
            ?? throw new InvalidOperationException("Expected a pending toy decision.");
        LegalAction action = decision.LegalActions.Single(candidate =>
            candidate.Kind == kind
            && (cardId is null
                || candidate.Parameters is CardChoiceParameters { CardId: var offered } && offered == cardId.Value));
        return GameEngine.Apply(
            state,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id));
    }

    private static GameState ReplacePending(GameState state, IEnumerable<LegalAction> actions)
    {
        PendingDecision? pending = state.PendingDecision;
        if (pending is null)
        {
            throw new InvalidOperationException("Expected a pending decision.");
        }

        return new GameState(
            state.SchemaVersion,
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            state.Players,
            state.Cards,
            state.Zones,
            state.Servers,
            state.Turn,
            state.Outcome,
            state.RandomState,
            state.Allocator,
            state.DecisionRevision,
            state.EventSequence,
            state.ResolutionStack,
            new PendingDecision(pending.Id, pending.Revision, pending.Owner, pending.Kind, pending.Prompt, actions.ToArray()),
            state.ActionSequence,
            state.InitialSeed);
    }

    private static GameState ReplacePlayer(GameState state, PlayerState replacement)
    {
        return new GameState(
            state.SchemaVersion,
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            state.Players.Select(player => player.Id == replacement.Id ? replacement : player).ToArray(),
            state.Cards,
            state.Zones,
            state.Servers,
            state.Turn,
            state.Outcome,
            state.RandomState,
            state.Allocator,
            state.DecisionRevision,
            state.EventSequence,
            state.ResolutionStack,
            state.PendingDecision,
            state.ActionSequence,
            state.InitialSeed);
    }
}

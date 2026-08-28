using System.Text.Json;
using Xunit;

namespace Netrunner.Core.Tests;

public sealed class ResolutionEngineTests
{
    [Fact]
    public void NestedNumberPromptRequiresTwoMaterializedSubmissionsAndAppliesAtomically()
    {
        (GameState initial, GameState prompt) = CreateNestedPrompt(creditsPerChoice: 3, minimum: 1, maximum: 3);
        PendingDecision outerDecision = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        PlayerId corpId = initial.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        LegalAction outerAction = outerDecision.LegalActions.Single(action =>
            action.Parameters is NumberChoiceParameters { Value: 2 });

        ApplyResult outerResult = GameEngine.Apply(
            prompt,
            new ActionSubmission(outerDecision.Owner, outerDecision.Id, outerDecision.Revision, outerAction.Id));

        Assert.True(outerResult.Accepted, outerResult.Rejection?.Message);
        Assert.NotSame(prompt, outerResult.State);
        GameState innerPrompt = outerResult.State;
        PendingDecision innerDecision = Assert.IsType<PendingDecision>(innerPrompt.PendingDecision);
        Assert.NotEqual(outerDecision.Id, innerDecision.Id);
        Assert.True(innerDecision.Revision > outerDecision.Revision);
        Assert.Equal(2, innerPrompt.ResolutionStack.Count);
        ChooseNumberThenAdjustCreditsFrame parent = Assert.IsType<ChooseNumberThenAdjustCreditsFrame>(innerPrompt.ResolutionStack[0]);
        Assert.Equal(NestedNumberStage.InnerPrompt, parent.Stage);
        Assert.Equal(2, parent.ChosenOuterValue);
        ChooseNumberFrame child = Assert.IsType<ChooseNumberFrame>(innerPrompt.ResolutionStack[1]);
        Assert.Equal(ResolutionContinuation.Complete, child.Continuation);
        Assert.Equal(outerDecision.Owner, innerDecision.Owner);

        LegalAction innerAction = innerDecision.LegalActions.Single(action =>
            action.Parameters is NumberChoiceParameters { Value: 3 });
        ApplyResult result = GameEngine.Apply(
            innerPrompt,
            new ActionSubmission(innerDecision.Owner, innerDecision.Id, innerDecision.Revision, innerAction.Id));

        Assert.True(result.Accepted);
        Assert.NotSame(innerPrompt, result.State);
        Assert.Equal(23, result.State.GetPlayer(corpId).Credits);
        Assert.Null(result.State.PendingDecision);
        Assert.Empty(result.State.ResolutionStack);
        Assert.Single(result.Events);
        Assert.Equal(1L, result.Events[0].Sequence);
        Assert.Equal(1L, result.State.EventSequence);
        Assert.Equal(5, prompt.GetPlayer(corpId).Credits);
        Assert.NotNull(prompt.PendingDecision);
    }

    [Fact]
    public void InnerPromptRoundTripsAndCompletesIdenticallyAfterSaveLoad()
    {
        (_, GameState outerPrompt) = CreateNestedPrompt(creditsPerChoice: 2, minimum: 1, maximum: 3);
        PendingDecision outerDecision = Assert.IsType<PendingDecision>(outerPrompt.PendingDecision);
        LegalAction outerAction = outerDecision.LegalActions.Single(action =>
            action.Parameters is NumberChoiceParameters { Value: 2 });
        ApplyResult outerResult = GameEngine.Apply(
            outerPrompt,
            new ActionSubmission(outerDecision.Owner, outerDecision.Id, outerDecision.Revision, outerAction.Id));
        Assert.True(outerResult.Accepted);

        GameState innerPrompt = outerResult.State;
        PendingDecision innerDecision = Assert.IsType<PendingDecision>(innerPrompt.PendingDecision);
        Assert.NotEqual(outerDecision.Id, innerDecision.Id);
        Assert.True(innerDecision.Revision > outerDecision.Revision);
        Assert.Contains("\"stage\":\"innerPrompt\"", EngineJson.Serialize(innerPrompt), StringComparison.Ordinal);
        Assert.Contains("\"chosenOuterValue\":2", EngineJson.Serialize(innerPrompt), StringComparison.Ordinal);

        string json = EngineJson.Serialize(innerPrompt);
        GameState restored = EngineJson.DeserializeState(json);

        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(2, restored.ResolutionStack.Count);
        ChooseNumberThenAdjustCreditsFrame restoredParent = Assert.IsType<ChooseNumberThenAdjustCreditsFrame>(restored.ResolutionStack[0]);
        Assert.Equal(NestedNumberStage.InnerPrompt, restoredParent.Stage);
        Assert.Equal(2, restoredParent.ChosenOuterValue);
        Assert.IsType<ChooseNumberFrame>(restored.ResolutionStack[1]);

        PendingDecision decision = Assert.IsType<PendingDecision>(restored.PendingDecision);
        LegalAction selected = decision.LegalActions.Single(action =>
            action.Parameters is NumberChoiceParameters { Value: 3 });
        var submission = new ActionSubmission(decision.Owner, decision.Id, decision.Revision, selected.Id);

        ApplyResult restoredResult = GameEngine.Apply(restored, submission);
        GameState originalResult = GameEngine.Apply(innerPrompt, submission).State;

        Assert.True(restoredResult.Accepted);
        Assert.Equal(EngineJson.Serialize(originalResult), EngineJson.Serialize(restoredResult.State));
        Assert.Equal(EngineJson.Serialize(originalResult.Snapshot), EngineJson.Serialize(restoredResult.State.Snapshot));
    }

    [Fact]
    public void CardChoiceMaterializesTypedCardActionsAndCompletes()
    {
        GameState state = CreateCardState(out CardInstanceId firstCard, out CardInstanceId secondCard);
        PlayerId corpId = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        GameState prompt = GameEngine.BeginResolution(
            state,
            new ChooseCardFrame(corpId, [firstCard, secondCard]));
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);

        Assert.All(
            decision.LegalActions,
            action => Assert.IsType<CardChoiceParameters>(action.Parameters));
        LegalAction selected = decision.LegalActions.Single(action =>
            action.Parameters is CardChoiceParameters { CardId: var cardId } && cardId == secondCard);

        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, selected.Id));

        Assert.True(result.Accepted);
        Assert.Null(result.State.PendingDecision);
        Assert.Empty(result.State.ResolutionStack);
        Assert.Empty(result.Events);
        Assert.Equal(prompt.Cards, result.State.Cards);
    }

    [Fact]
    public void StaleDecisionIdIsRejectedWithoutChangingTheOriginalState()
    {
        (_, GameState prompt) = CreateNestedPrompt();
        string before = EngineJson.Serialize(prompt);
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        LegalAction action = decision.LegalActions[0];

        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(
                decision.Owner,
                new DecisionId("decision:stale"),
                decision.Revision,
                action.Id));

        AssertRejected(result, ApplyRejectionCode.StaleDecisionId, prompt, before);
    }

    [Fact]
    public void StaleRevisionIsRejectedWithoutChangingTheOriginalState()
    {
        (_, GameState prompt) = CreateNestedPrompt();
        string before = EngineJson.Serialize(prompt);
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);

        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(
                decision.Owner,
                decision.Id,
                decision.Revision + 1,
                decision.LegalActions[0].Id));

        AssertRejected(result, ApplyRejectionCode.StaleRevision, prompt, before);
    }

    [Fact]
    public void WrongOwnerIsRejectedAsOutOfTurn()
    {
        (_, GameState prompt) = CreateNestedPrompt();
        string before = EngineJson.Serialize(prompt);
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        PlayerId wrongOwner = prompt.Players.Single(player => player.Id != decision.Owner).Id;

        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(wrongOwner, decision.Id, decision.Revision, decision.LegalActions[0].Id));

        AssertRejected(result, ApplyRejectionCode.WrongOwner, prompt, before);
    }

    [Fact]
    public void NonActiveRunnerMayOwnAndSubmitAResponseDecision()
    {
        GameState initial = GameState.CreateNew(29);
        PlayerId corpId = initial.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        PlayerId runnerId = initial.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameState prompt = GameEngine.BeginResolution(initial, new ChooseNumberFrame(runnerId, 1, 1));
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);

        Assert.Equal(PlayerSide.Corp, prompt.Turn.ActiveSide);
        Assert.Equal(runnerId, decision.Owner);

        ApplyResult corpResult = GameEngine.Apply(
            prompt,
            new ActionSubmission(corpId, decision.Id, decision.Revision, decision.LegalActions[0].Id));
        AssertRejected(corpResult, ApplyRejectionCode.WrongOwner, prompt, EngineJson.Serialize(prompt));

        ApplyResult runnerResult = GameEngine.Apply(
            prompt,
            new ActionSubmission(runnerId, decision.Id, decision.Revision, decision.LegalActions[0].Id));
        Assert.True(runnerResult.Accepted);
        Assert.Null(runnerResult.State.PendingDecision);
    }

    [Fact]
    public void NumberDecisionMustContainExactlyOneActionForEveryOfferedValue()
    {
        (_, GameState prompt) = CreateNestedPrompt(minimum: 1, maximum: 3);
        PendingDecision decision = prompt.PendingDecision!;

        LegalAction duplicate = new(
            new ActionId("action:duplicate-choice"),
            ActionKind.ChooseNumber,
            "Choose 1 again",
            new NumberChoiceParameters(1));
        GameState duplicateState = ReplacePending(prompt, decision.LegalActions.Append(duplicate));
        IReadOnlyList<StateInvariantViolation> duplicateViolations = StateInvariantValidator.Collect(duplicateState);
        Assert.Contains(duplicateViolations, violation => violation.Code == "decision.duplicate-choice");
        Assert.Contains(duplicateViolations, violation => violation.Code == "decision.action-count-mismatch");

        GameState missingState = ReplacePending(
            prompt,
            decision.LegalActions.Where(action => action.Parameters is not NumberChoiceParameters { Value: 2 }));
        IReadOnlyList<StateInvariantViolation> missingViolations = StateInvariantValidator.Collect(missingState);
        Assert.Contains(missingViolations, violation => violation.Code == "decision.missing-choice");
        Assert.Contains(missingViolations, violation => violation.Code == "decision.action-count-mismatch");

        LegalAction extra = new(
            new ActionId("action:extra-choice"),
            ActionKind.ChooseNumber,
            "Choose 99",
            new NumberChoiceParameters(99));
        GameState extraState = ReplacePending(prompt, decision.LegalActions.Append(extra));
        IReadOnlyList<StateInvariantViolation> extraViolations = StateInvariantValidator.Collect(extraState);
        Assert.Contains(extraViolations, violation => violation.Code == "decision.action-out-of-range");
        Assert.Contains(extraViolations, violation => violation.Code == "decision.action-count-mismatch");
    }

    [Fact]
    public void CardDecisionMustContainExactlyOneTypedActionForEveryOption()
    {
        GameState initial = CreateCardState(out CardInstanceId firstCard, out CardInstanceId secondCard);
        PlayerId corpId = initial.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        GameState prompt = GameEngine.BeginResolution(initial, new ChooseCardFrame(corpId, [firstCard, secondCard]));
        PendingDecision decision = prompt.PendingDecision!;

        GameState missingState = ReplacePending(
            prompt,
            decision.LegalActions.Where(action => action.Parameters is not CardChoiceParameters { CardId: var cardId } || cardId != secondCard));
        IReadOnlyList<StateInvariantViolation> missingViolations = StateInvariantValidator.Collect(missingState);
        Assert.Contains(missingViolations, violation => violation.Code == "decision.missing-card-choice");
        Assert.Contains(missingViolations, violation => violation.Code == "decision.action-count-mismatch");

        LegalAction duplicate = new(
            new ActionId("action:duplicate-card-choice"),
            ActionKind.ChooseCard,
            "Choose first card again",
            new CardChoiceParameters(firstCard));
        GameState duplicateState = ReplacePending(prompt, decision.LegalActions.Append(duplicate));
        IReadOnlyList<StateInvariantViolation> duplicateViolations = StateInvariantValidator.Collect(duplicateState);
        Assert.Contains(duplicateViolations, violation => violation.Code == "decision.duplicate-card-choice");
        Assert.Contains(duplicateViolations, violation => violation.Code == "decision.action-count-mismatch");

        LegalAction extra = new(
            new ActionId("action:extra-card-choice"),
            ActionKind.ChooseCard,
            "Choose an unoffered card",
            new CardChoiceParameters(new CardInstanceId("card:extra")));
        GameState extraState = ReplacePending(prompt, decision.LegalActions.Append(extra));
        IReadOnlyList<StateInvariantViolation> extraViolations = StateInvariantValidator.Collect(extraState);
        Assert.Contains(extraViolations, violation => violation.Code == "decision.card-action-not-offered");
        Assert.Contains(extraViolations, violation => violation.Code == "decision.action-count-mismatch");
    }

    [Fact]
    public void LegacyCompatibilityActionsAreRejectedInAuthoritativeDecisions()
    {
        (_, GameState prompt) = CreateNestedPrompt(minimum: 1, maximum: 1);
        PendingDecision decision = prompt.PendingDecision!;
        GameState invalid = ReplacePending(
            prompt,
            [new LegalAction(new ActionId("action:legacy"), "legacy-draw", "Draw")]);

        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(invalid);

        Assert.Contains(violations, violation => violation.Code == "decision.invalid-action-kind");
        Assert.Contains(violations, violation => violation.Code == "decision.action-kind-mismatch");
        Assert.Equal(DecisionKind.ChooseNumber, decision.Kind);
    }

    [Fact]
    public void MalformedHugeNumberRangeFailsClosedWithoutExpandingDiagnostics()
    {
        GameState initial = GameState.CreateNew(30);
        PlayerId corpId = initial.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        GameState invalid = new(
            initial.GameId,
            initial.RulesVersion,
            initial.CardDataVersion,
            initial.Players,
            initial.Cards,
            initial.Zones,
            initial.Servers,
            initial.Turn,
            initial.Outcome,
            initial.RandomState,
            initial.Allocator,
            decisionRevision: 1,
            resolutionStack: [new ChooseNumberFrame(corpId, int.MinValue, int.MaxValue)],
            pendingDecision: new PendingDecision(
                new DecisionId("decision:huge-range"),
                1,
                corpId,
                DecisionKind.ChooseNumber,
                "Choose a number",
                [new LegalAction(
                    new ActionId("action:huge-range"),
                    ActionKind.ChooseNumber,
                    "Choose 0",
                    new NumberChoiceParameters(0))]));

        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(invalid);

        Assert.Contains(violations, violation => violation.Code == "resolution.range-too-large");
    }

    [Fact]
    public void UnknownActionIsRejectedWithoutChangingTheOriginalState()
    {
        (_, GameState prompt) = CreateNestedPrompt();
        string before = EngineJson.Serialize(prompt);
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);

        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(
                decision.Owner,
                decision.Id,
                decision.Revision,
                new ActionId("action:not-offered")));

        AssertRejected(result, ApplyRejectionCode.UnknownAction, prompt, before);
    }

    [Fact]
    public void TerminalAndNoPendingStatesHaveTypedRejections()
    {
        GameState ongoing = GameState.CreateNew(24);
        ApplyResult noPending = GameEngine.Apply(
            ongoing,
            new ActionSubmission(
                ongoing.Players[0].Id,
                new DecisionId("decision:none"),
                1,
                new ActionId("action:none")));

        Assert.Equal(ApplyRejectionCode.NoPendingDecision, noPending.Rejection?.Code);
        Assert.Same(ongoing, noPending.State);
        Assert.Empty(noPending.Events);

        GameState terminal = new(
            ongoing.GameId,
            ongoing.RulesVersion,
            ongoing.CardDataVersion,
            ongoing.Players,
            ongoing.Cards,
            ongoing.Zones,
            ongoing.Servers,
            new TurnState(PlayerSide.Corp, TurnPhase.GameOver),
            new GameOutcome(GameOutcomeKind.Won, PlayerSide.Corp, GameWinReason.AgendaPoints),
            ongoing.RandomState,
            ongoing.Allocator);
        ApplyResult terminalResult = GameEngine.Apply(
            terminal,
            new ActionSubmission(
                terminal.Players[0].Id,
                new DecisionId("decision:none"),
                1,
                new ActionId("action:none")));

        Assert.Equal(ApplyRejectionCode.TerminalState, terminalResult.Rejection?.Code);
        Assert.Same(terminal, terminalResult.State);
        Assert.Empty(terminalResult.Events);
    }

    [Fact]
    public void InvalidInputStateIsRejectedBeforeResolution()
    {
        GameState valid = GameState.CreateNew(25);
        ZoneState runnerRig = valid.Zones.Single(zone => zone.Kind == ZoneKind.RunnerRig);
        GameState invalid = new(
            valid.GameId,
            valid.RulesVersion,
            valid.CardDataVersion,
            valid.Players,
            valid.Cards,
            valid.Zones.Where(zone => zone != runnerRig),
            valid.Servers,
            valid.Turn,
            valid.Outcome,
            valid.RandomState,
            valid.Allocator);

        ApplyResult result = GameEngine.Apply(
            invalid,
            new ActionSubmission(
                valid.Players[0].Id,
                new DecisionId("decision:none"),
                1,
                new ActionId("action:none")));

        Assert.Equal(ApplyRejectionCode.InvalidInputState, result.Rejection?.Code);
        Assert.Same(invalid, result.State);
        Assert.Empty(result.Events);
    }

    [Fact]
    public void ResolutionFailureRollsBackCreditsRngAllocatorAndOutput()
    {
        (GameState initial, GameState outerPrompt) = CreateNestedPrompt(
            creditsPerChoice: int.MaxValue,
            minimum: 2,
            maximum: 2);
        PendingDecision outerDecision = Assert.IsType<PendingDecision>(outerPrompt.PendingDecision);
        LegalAction outerAction = outerDecision.LegalActions[0];
        ApplyResult outerResult = GameEngine.Apply(
            outerPrompt,
            new ActionSubmission(outerDecision.Owner, outerDecision.Id, outerDecision.Revision, outerAction.Id));
        Assert.True(outerResult.Accepted);

        GameState innerPrompt = outerResult.State;
        PendingDecision innerDecision = Assert.IsType<PendingDecision>(innerPrompt.PendingDecision);
        LegalAction innerAction = innerDecision.LegalActions[0];
        string before = EngineJson.Serialize(innerPrompt);
        ulong randomBefore = innerPrompt.RandomState;
        IdAllocatorState allocatorBefore = innerPrompt.Allocator;
        long revisionBefore = innerPrompt.DecisionRevision;
        long eventSequenceBefore = innerPrompt.EventSequence;

        ApplyResult result = GameEngine.Apply(
            innerPrompt,
            new ActionSubmission(innerDecision.Owner, innerDecision.Id, innerDecision.Revision, innerAction.Id));

        Assert.False(result.Accepted);
        Assert.Equal(ApplyRejectionCode.ResolutionFailure, result.Rejection?.Code);
        Assert.Same(innerPrompt, result.State);
        Assert.Empty(result.Events);
        Assert.Equal(before, EngineJson.Serialize(innerPrompt));
        Assert.Equal(randomBefore, innerPrompt.RandomState);
        Assert.Equal(allocatorBefore, innerPrompt.Allocator);
        Assert.Equal(revisionBefore, innerPrompt.DecisionRevision);
        Assert.Equal(eventSequenceBefore, innerPrompt.EventSequence);
        Assert.Equal(5, innerPrompt.GetPlayer(initial.Players[0].Id).Credits);
    }

    [Fact]
    public void ResolutionStepBudgetStopsNestedExpansion()
    {
        GameState initial = GameState.CreateNew(26);
        PlayerId corpId = initial.Players.Single(player => player.Side == PlayerSide.Corp).Id;

        GameState outerPrompt = GameEngine.BeginResolution(
            initial,
            new ChooseNumberThenAdjustCreditsFrame(corpId, corpId, 1, 2, 1),
            maxResolutionSteps: 1);

        Assert.NotNull(outerPrompt.PendingDecision);
        Assert.Single(outerPrompt.ResolutionStack);

        PendingDecision outerDecision = outerPrompt.PendingDecision!;
        ApplyResult innerResult = GameEngine.Apply(
            outerPrompt,
            new ActionSubmission(
                outerDecision.Owner,
                outerDecision.Id,
                outerDecision.Revision,
                outerDecision.LegalActions[0].Id));

        Assert.True(innerResult.Accepted);
        Assert.NotNull(innerResult.State.PendingDecision);
        Assert.Equal(2, innerResult.State.ResolutionStack.Count);

        Assert.Throws<ResolutionStepBudgetExceededException>(() =>
            GameEngine.BeginResolution(
                initial,
                new ChooseNumberThenAdjustCreditsFrame(corpId, corpId, 1, 2, 1),
                maxResolutionSteps: 0));

        Assert.Null(initial.PendingDecision);
        Assert.Empty(initial.ResolutionStack);
    }

    [Fact]
    public void UnknownFrameTypeAndBehaviorFailClosed()
    {
        (_, GameState prompt) = CreateNestedPrompt();
        string json = EngineJson.Serialize(prompt);

        string unknownType = json.Replace(
            "\"$type\":\"choose-number-then-adjust-credits\"",
            "\"$type\":\"unregistered-frame\"",
            StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => EngineJson.DeserializeState(unknownType));

        string unknownBehavior = json.Replace(
            "\"behaviorId\":\"fixture.choose-number-then-adjust-credits\"",
            "\"behaviorId\":\"fixture.unknown\"",
            StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => EngineJson.DeserializeState(unknownBehavior));

        string unknownVersion = json.Replace(
            "\"version\":1",
            "\"version\":2",
            StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => EngineJson.DeserializeState(unknownVersion));
    }

    [Fact]
    public void PendingDecisionAndResolutionStackAreDeeplyImmutable()
    {
        GameState initial = GameState.CreateNew(27);
        PlayerId corpId = initial.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        var sourceStack = new List<ResolutionFrame>
        {
            new ChooseNumberFrame(corpId, 1, 1),
        };
        var sourceActions = new List<LegalAction>
        {
            new(
                new ActionId("action:1"),
                ActionKind.ChooseNumber,
                "Choose 1",
                new NumberChoiceParameters(1)),
        };
        var pending = new PendingDecision(
            new DecisionId("decision:1"),
            1,
            corpId,
            DecisionKind.ChooseNumber,
            "Choose a number",
            sourceActions);
        GameState state = new(
            initial.GameId,
            initial.RulesVersion,
            initial.CardDataVersion,
            initial.Players,
            initial.Cards,
            initial.Zones,
            initial.Servers,
            initial.Turn,
            initial.Outcome,
            initial.RandomState,
            new IdAllocatorState(2),
            decisionRevision: 1,
            resolutionStack: sourceStack,
            pendingDecision: pending);

        sourceStack.Clear();
        sourceActions.Clear();
        Assert.Single(state.ResolutionStack);
        Assert.Single(state.PendingDecision!.LegalActions);
        Assert.Throws<NotSupportedException>(() =>
            Assert.IsAssignableFrom<IList<ResolutionFrame>>(state.ResolutionStack).Clear());
        Assert.Throws<NotSupportedException>(() =>
            Assert.IsAssignableFrom<IList<LegalAction>>(state.PendingDecision.LegalActions).Clear());
    }

    [Fact]
    public void ValidatorRejectsMalformedPendingDecisionAndImpossibleFrames()
    {
        GameState initial = GameState.CreateNew(28);
        PlayerId corpId = initial.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        var duplicateAction = new LegalAction(
            new ActionId("action:100"),
            ActionKind.ChooseNumber,
            "Choose 1",
            new NumberChoiceParameters(1));
        var pending = new PendingDecision(
            new DecisionId("decision:99"),
            1,
            corpId,
            DecisionKind.ChooseNumber,
            "Choose a number",
            [duplicateAction, duplicateAction]);
        GameState invalid = new(
            initial.GameId,
            initial.RulesVersion,
            initial.CardDataVersion,
            initial.Players,
            initial.Cards,
            initial.Zones,
            initial.Servers,
            initial.Turn,
            initial.Outcome,
            initial.RandomState,
            new IdAllocatorState(100),
            decisionRevision: 1,
            resolutionStack: [new ChooseNumberFrame(corpId, 1, 1)],
            pendingDecision: pending);

        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(invalid);

        Assert.Contains(violations, violation => violation.Code == "decision.duplicate-action");
        Assert.Contains(violations, violation => violation.Code == "allocator.behind");
        Assert.Contains(
            StateInvariantValidator.Collect(new GameState(
                initial.GameId,
                initial.RulesVersion,
                initial.CardDataVersion,
                initial.Players,
                initial.Cards,
                initial.Zones,
                initial.Servers,
                initial.Turn,
                initial.Outcome,
                initial.RandomState,
                initial.Allocator,
                resolutionStack: [new ChooseNumberFrame(
                    corpId,
                    1,
                    1,
                    ResolutionContinuation.Complete,
                    adjustmentTarget: corpId,
                    creditsPerChoice: 1)])),
            violation => violation.Code == "resolution.unused-continuation-arguments");
    }

    private static (GameState Initial, GameState Prompt) CreateNestedPrompt(
        int creditsPerChoice = 2,
        int minimum = 1,
        int maximum = 3)
    {
        GameState initial = GameState.CreateNew(1234);
        PlayerId corpId = initial.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        GameState prompt = GameEngine.BeginResolution(
            initial,
            new ChooseNumberThenAdjustCreditsFrame(
                corpId,
                corpId,
                minimum,
                maximum,
                creditsPerChoice));
        return (initial, prompt);
    }

    private static GameState CreateCardState(
        out CardInstanceId firstCard,
        out CardInstanceId secondCard)
    {
        GameState initial = GameState.CreateNew(1235);
        var allocator = new StableIdAllocator(initial.Allocator);
        firstCard = allocator.NextCardInstanceId();
        secondCard = allocator.NextCardInstanceId();
        ZoneState deck = initial.Zones.Single(zone => zone.Kind == ZoneKind.CorpDeck);
        var cards = new[]
        {
            new CardInstanceState(firstCard, new CardDefinitionId("fixture:first"), deck.Id, PlayerSide.Corp),
            new CardInstanceState(secondCard, new CardDefinitionId("fixture:second"), deck.Id, PlayerSide.Corp),
        };
        ZoneState populatedDeck = new(deck.Id, deck.Kind, deck.OwnerSide, deck.ServerId, [firstCard, secondCard]);
        return new GameState(
            initial.GameId,
            initial.RulesVersion,
            initial.CardDataVersion,
            initial.Players,
            cards,
            initial.Zones.Select(zone => zone.Id == deck.Id ? populatedDeck : zone),
            initial.Servers,
            initial.Turn,
            initial.Outcome,
            initial.RandomState,
            allocator.State);
    }

    private static GameState ReplacePending(GameState state, IEnumerable<LegalAction> legalActions)
    {
        PendingDecision pending = state.PendingDecision
            ?? throw new InvalidOperationException("The test state must contain a pending decision.");
        return new GameState(
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
            decisionRevision: state.DecisionRevision,
            eventSequence: state.EventSequence,
            resolutionStack: state.ResolutionStack,
            pendingDecision: new PendingDecision(
                pending.Id,
                pending.Revision,
                pending.Owner,
                pending.Kind,
                pending.Prompt,
                legalActions.ToArray()));
    }

    private static void AssertRejected(
        ApplyResult result,
        ApplyRejectionCode expectedCode,
        GameState original,
        string before)
    {
        Assert.False(result.Accepted);
        Assert.Equal(expectedCode, result.Rejection?.Code);
        Assert.Same(original, result.State);
        Assert.Empty(result.Events);
        Assert.Equal(before, EngineJson.Serialize(original));
    }
}

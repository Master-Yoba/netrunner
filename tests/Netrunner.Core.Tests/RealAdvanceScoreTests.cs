using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>Focused coverage for the real Corp advance and score actions.</summary>
public sealed class RealAdvanceScoreTests
{
    [Fact]
    public void CorpTurnMaterializesRemoteAgendasInPhysicalRootOrderAndAllowsOveradvancement()
    {
        Fixture fixture = CreateFixture(
            agendaCounters: [3, 0],
            agendaOrder: [0, 1],
            credits: 3);
        GameState state = OpenCorpAction(fixture);

        AdvanceCardParameters[] actions = state.PendingDecision!
            .LegalActions
            .Where(action => action.Kind == ActionKind.AdvanceCard)
            .Select(action => Assert.IsType<AdvanceCardParameters>(action.Parameters))
            .ToArray();

        Assert.DoesNotContain(state.PendingDecision.LegalActions, action => action.Kind == ActionKind.ScoreAgenda);
        Assert.Equal(
            [fixture.AgendaIds[0], fixture.AgendaIds[1]],
            actions.Select(action => action.CardId));
        Assert.All(actions, action => Assert.Equal(1, action.CreditCost));
    }

    [Fact]
    public void AdvanceProjectionExcludesWrongContextResourcesTopologyAndCardFacts()
    {
        Fixture fixture = CreateFixture(agendaCounters: [0], credits: 3);
        GameState valid = OpenCorpAction(fixture);
        Assert.Single(GameEngine.MaterializeAdvanceCardParameters(valid, fixture.RuntimeCatalog));
        // EL-FACEUP-AGENDA: an installed agenda turned faceup (BANGUN) stays unrezzed and can still be advanced.
        Assert.Single(GameEngine.MaterializeAdvanceCardParameters(ReplaceTarget(valid, faceUp: true), fixture.RuntimeCatalog));

        var contexts = new (string Name, GameState State)[]
        {
            (
                "runner-side",
                Rebuild(
                    valid,
                    turn: new TurnState(PlayerSide.Runner, TurnPhase.Runner, valid.Turn.TurnNumber),
                    resolutionStack: [new TurnFrame(PlayerSide.Runner, valid.Turn.TurnNumber, TurnFrameStage.AwaitingAction)])),
            (
                "wrong-phase",
                Rebuild(
                    valid,
                    turn: new TurnState(PlayerSide.Corp, TurnPhase.Runner, valid.Turn.TurnNumber),
                    resolutionStack: [new TurnFrame(PlayerSide.Corp, valid.Turn.TurnNumber, TurnFrameStage.AwaitingAction)])),
            ("zero-click", ReplaceCorp(valid, clicks: 0)),
            ("zero-credit", ReplaceCorp(valid, credits: 0)),
            ("transient-play-area", AddTransientPlayArea(valid, PlayerSide.Corp)),
            ("rezzed", ReplaceTarget(valid, rezzed: true)),
            ("wrong-topology", ReplaceTarget(valid, zoneId: valid.GetZone(ZoneKind.CorpDeck).Id)),
        };

        foreach ((string name, GameState state) in contexts)
        {
            Assert.True(
                GameEngine.MaterializeAdvanceCardParameters(state, fixture.RuntimeCatalog).Count == 0,
                $"Context '{name}' unexpectedly materialized a real advance action.");
        }

        Fixture nonAgendaFixture = CreateFixture(
            agendaCounters: [0],
            credits: 3,
            addProtectedRootCard: true);
        GameState nonAgendaState = OpenCorpAction(nonAgendaFixture);
        IReadOnlyList<AdvanceCardParameters> nonAgendaActions =
            GameEngine.MaterializeAdvanceCardParameters(nonAgendaState, nonAgendaFixture.RuntimeCatalog);
        Assert.Single(nonAgendaActions);
        Assert.Equal(nonAgendaFixture.AgendaIds[0], nonAgendaActions[0].CardId);
    }

    [Fact]
    public void AdvanceAndScoreProjectionsPreservePhysicalServerAndRootCardOrderWithoutAllocating()
    {
        Fixture fixture = CreatePhysicalOrderFixture();
        GameState actionState = OpenCorpAction(fixture);
        CardInstanceId[] expected =
        [
            new("card:real-alpha-agenda"),
            new("card:real-zulu-2"),
            new("card:real-zulu-1"),
        ];

        ulong actionAllocatorPosition = actionState.Allocator.NextSequence;
        IReadOnlyList<AdvanceCardParameters> advances =
            GameEngine.MaterializeAdvanceCardParameters(actionState, fixture.RuntimeCatalog);
        Assert.Equal(expected, advances.Select(action => action.CardId));
        Assert.Equal(actionAllocatorPosition, actionState.Allocator.NextSequence);

        GameState timingState = OpenCorpTiming(fixture);
        ulong scoreAllocatorPosition = timingState.Allocator.NextSequence;
        IReadOnlyList<ScoreAgendaParameters> scores =
            GameEngine.MaterializeScoreAgendaParameters(timingState, fixture.RuntimeCatalog);
        Assert.Equal(expected, scores.Select(action => action.CardId));
        Assert.Equal(scoreAllocatorPosition, timingState.Allocator.NextSequence);
    }

    [Fact]
    public void AdvanceConsumesClickAndCreditThenEmitsTypedEvidenceInOrder()
    {
        Fixture fixture = CreateFixture(agendaCounters: [0], credits: 3);
        GameState state = OpenCorpAction(fixture);
        LegalAction action = Assert.Single(
            state.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.AdvanceCard);

        PlayerState before = state.GetPlayer(fixture.CorpId);
        ApplyResult result = Apply(fixture, state, action);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(
            [
                EngineEventKinds.ClickSpent,
                EngineEventKinds.CreditsAdjusted,
                EngineEventKinds.AgendaAdvanced,
                EngineEventKinds.TurnTimingWindowOpened,
            ],
            result.Events.Select(@event => @event.Kind));
        PlayerState after = result.State.GetPlayer(fixture.CorpId);
        Assert.Equal(before.Clicks - 1, after.Clicks);
        Assert.Equal(before.Credits - 1, after.Credits);
        Assert.Equal(1, result.State.GetCard(fixture.AgendaIds[0]).AdvancementCounters);
        AgendaAdvancedEventPayload advanced = Assert.IsType<AgendaAdvancedEventPayload>(result.Events[2].Payload);
        Assert.Equal(fixture.Requirement, advanced.Requirement);
    }

    [Fact]
    public void AdvancingAnIntMaxValueAgendaRejectsAtomicallyWithoutThrowing()
    {
        Fixture fixture = CreateFixture(agendaCounters: [int.MaxValue], credits: 3);
        GameState state = OpenCorpAction(fixture);
        LegalAction action = Assert.Single(
            state.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.AdvanceCard);
        string before = EngineJson.Serialize(state);
        ApplyResult? result = null;

        Exception? exception = Record.Exception(() => result = Apply(fixture, state, action));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.False(result!.Accepted);
        Assert.Equal(ApplyRejectionCode.ResolutionFailure, result.Rejection?.Code);
        Assert.Same(state, result.State);
        Assert.Empty(result.Events);
        Assert.Equal(before, EngineJson.Serialize(result.State));
    }

    [Fact]
    public void ScoreIsOnlyOfferedInCorpScorePriorityWindowAndRestartsWindowAfterNonterminalScore()
    {
        Fixture fixture = CreateFixture(
            agendaCounters: [2],
            agendaPoints: 0,
            credits: 0);
        GameState state = OpenCorpTiming(fixture);
        LegalAction score = Assert.Single(
            state.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.ScoreAgenda);
        ScoreAgendaParameters payload = Assert.IsType<ScoreAgendaParameters>(score.Parameters);
        Assert.Equal(fixture.AgendaIds[0], payload.CardId);
        Assert.Equal(fixture.AgendaPoints, payload.AgendaPoints);
        Assert.Equal(fixture.Requirement, payload.AdvancementRequirement);

        PlayerState before = state.GetPlayer(fixture.CorpId);

        ApplyResult result = Apply(fixture, state, score);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(
            [EngineEventKinds.CardMoved, EngineEventKinds.AgendaScored, EngineEventKinds.TurnTimingWindowOpened],
            result.Events.Select(@event => @event.Kind));
        Assert.Equal(fixture.AgendaPoints, result.State.GetPlayer(fixture.CorpId).AgendaPoints);
        Assert.Equal(before.Clicks, result.State.GetPlayer(fixture.CorpId).Clicks);
        Assert.Equal(before.Credits, result.State.GetPlayer(fixture.CorpId).Credits);
        AgendaScoredEventPayload scored = Assert.IsType<AgendaScoredEventPayload>(result.Events[1].Payload);
        Assert.Equal(PlayerSide.Corp, scored.Winner);
        Assert.Equal(fixture.AgendaIds[0], scored.AgendaId);
        Assert.Equal(fixture.AgendaPoints, scored.AgendaPoints);
        Assert.Equal(
            [new AgendaTurnChange(
                fixture.AgendaIds[0],
                PlayerSide.Corp,
                fixture.AgendaPoints,
                AgendaTurnChangeKind.Score)],
            result.State.Turn.ThisTurnFacts!.AgendaTurnChanges);
        Assert.Equal(TurnFrameStage.ActionPriority, Assert.IsType<TurnFrame>(result.State.ResolutionStack.Single()).Stage);
        Assert.Equal(PlayerSide.Corp, Assert.IsType<TurnFrame>(result.State.ResolutionStack.Single()).PriorityHolder);
        Assert.Equal(DecisionKind.TurnTiming, result.State.PendingDecision!.Kind);
        Assert.Contains(result.State.PendingDecision.LegalActions, candidate => candidate.Kind == ActionKind.PassPriority);
        Assert.DoesNotContain(result.State.PendingDecision.LegalActions, candidate => candidate.Kind == ActionKind.ScoreAgenda);
        Assert.Equal(result.State.GetZone(ZoneKind.CorpScore).Id, result.State.GetCard(fixture.AgendaIds[0]).ZoneId);
        Assert.True(result.State.GetCard(fixture.AgendaIds[0]).FaceUp);
        Assert.False(result.State.GetCard(fixture.AgendaIds[0]).Rezzed);
        Assert.Equal(0, result.State.GetCard(fixture.AgendaIds[0]).AdvancementCounters);
    }

    [Fact]
    public void ScoreProjectionRequiresCorpPaidRezAndScoreWindowAndEligibleInstalledAgenda()
    {
        Fixture fixture = CreateFixture(agendaCounters: [2], credits: 0);
        GameState valid = OpenCorpTiming(fixture);
        Assert.Single(GameEngine.MaterializeScoreAgendaParameters(valid, fixture.RuntimeCatalog));
        // EL-FACEUP-AGENDA: a faceup unrezzed agenda (BANGUN) can still be scored.
        Assert.Single(GameEngine.MaterializeScoreAgendaParameters(ReplaceTarget(valid, faceUp: true), fixture.RuntimeCatalog));
        Assert.Single(
            GameEngine.MaterializeScoreAgendaParameters(
                ReplaceCorp(valid, clicks: 0, credits: 0),
                fixture.RuntimeCatalog));

        var contexts = new (string Name, GameState State)[]
        {
            (
                "runner-side",
                Rebuild(
                    valid,
                    turn: new TurnState(PlayerSide.Runner, TurnPhase.Runner, valid.Turn.TurnNumber),
                    resolutionStack:
                    [PriorityFrame(
                        valid,
                        PlayerSide.Runner,
                        TurnFrameStage.ActionPriority,
                        TurnPriorityWindowKind.PaidRezAndScore,
                        PlayerSide.Runner)])),
            (
                "wrong-phase",
                Rebuild(
                    valid,
                    turn: new TurnState(PlayerSide.Corp, TurnPhase.Runner, valid.Turn.TurnNumber),
                    resolutionStack:
                    [PriorityFrame(
                        valid,
                        PlayerSide.Corp,
                        TurnFrameStage.ActionPriority,
                        TurnPriorityWindowKind.PaidRezAndScore,
                        PlayerSide.Corp)])),
            (
                "wrong-holder",
                Rebuild(
                    valid,
                    resolutionStack:
                    [PriorityFrame(
                        valid,
                        PlayerSide.Corp,
                        TurnFrameStage.ActionPriority,
                        TurnPriorityWindowKind.PaidRezAndScore,
                        PlayerSide.Runner)])),
            (
                "wrong-window",
                Rebuild(
                    valid,
                    resolutionStack:
                    [PriorityFrame(
                        valid,
                        PlayerSide.Corp,
                        TurnFrameStage.ActionPriority,
                        TurnPriorityWindowKind.PaidAndRez,
                        PlayerSide.Corp)])),
            (
                "wrong-stage",
                Rebuild(
                    valid,
                    resolutionStack:
                    [PriorityFrame(
                        valid,
                        PlayerSide.Corp,
                        TurnFrameStage.BeforeTurnPriority,
                        TurnPriorityWindowKind.PaidRezAndScore,
                        PlayerSide.Corp)])),
            ("rezzed", ReplaceTarget(valid, rezzed: true)),
            ("below-requirement", ReplaceTarget(valid, advancementCounters: 1)),
            ("wrong-topology", ReplaceTarget(valid, zoneId: valid.GetZone(ZoneKind.CorpDeck).Id)),
            ("runner-owned", ReplaceTarget(valid, ownerSide: PlayerSide.Runner)),
        };

        foreach ((string name, GameState state) in contexts)
        {
            Assert.True(
                GameEngine.MaterializeScoreAgendaParameters(state, fixture.RuntimeCatalog).Count == 0,
                $"Context '{name}' unexpectedly materialized a real score action.");
        }

        Fixture nonAgendaFixture = CreateFixture(
            agendaCounters: [2],
            credits: 0,
            addProtectedRootCard: true);
        GameState nonAgendaState = OpenCorpTiming(nonAgendaFixture);
        IReadOnlyList<ScoreAgendaParameters> nonAgendaActions =
            GameEngine.MaterializeScoreAgendaParameters(nonAgendaState, nonAgendaFixture.RuntimeCatalog);
        Assert.Single(nonAgendaActions);
        Assert.Equal(nonAgendaFixture.AgendaIds[0], nonAgendaActions[0].CardId);
    }

    [Fact]
    public void ScoreDecisionInWrongTimingOwnerOrWindowIsRejectedBeforeMutation()
    {
        Fixture fixture = CreateFixture(agendaCounters: [2], credits: 0);
        GameState source = OpenCorpTiming(fixture);
        LegalAction score = Assert.Single(source.PendingDecision!.LegalActions, action => action.Kind == ActionKind.ScoreAgenda);

        GameState wrongHolder = Rebuild(
            source,
            resolutionStack:
            [PriorityFrame(
                source,
                PlayerSide.Corp,
                TurnFrameStage.ActionPriority,
                TurnPriorityWindowKind.PaidRezAndScore,
                PlayerSide.Runner)]);
        AssertValidatorRejects(
            wrongHolder,
            fixture.RuntimeCatalog,
            "resolution.pending-owner-mismatch",
            score.Id);

        GameState wrongWindow = Rebuild(
            source,
            resolutionStack:
            [PriorityFrame(
                source,
                PlayerSide.Corp,
                TurnFrameStage.ActionPriority,
                TurnPriorityWindowKind.PaidAndRez,
                PlayerSide.Corp)]);
        AssertValidatorRejects(
            wrongWindow,
            fixture.RuntimeCatalog,
            "decision.extra-score-action",
            score.Id);

        GameState wrongStage = Rebuild(
            source,
            resolutionStack:
            [PriorityFrame(
                source,
                PlayerSide.Corp,
                TurnFrameStage.BeforeTurnPriority,
                TurnPriorityWindowKind.PaidRezAndScore,
                PlayerSide.Corp)]);
        AssertValidatorRejects(
            wrongStage,
            fixture.RuntimeCatalog,
            "decision.extra-score-action",
            score.Id);
    }

    [Fact]
    public void ScoreAtSevenEmitsGameWonLastAndRemovesEmptyRemote()
    {
        Fixture fixture = CreateFixture(
            agendaCounters: [2],
            agendaPoints: 6,
            credits: 0);
        GameState state = OpenCorpTiming(fixture);
        LegalAction score = Assert.Single(
            state.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.ScoreAgenda);

        ApplyResult result = Apply(fixture, state, score);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(
            [EngineEventKinds.CardMoved, EngineEventKinds.AgendaScored, EngineEventKinds.GameWon],
            result.Events.Select(@event => @event.Kind));
        Assert.Equal(GameOutcomeKind.Won, result.State.Outcome.Kind);
        Assert.Equal(PlayerSide.Corp, result.State.Outcome.Winner);
        Assert.Equal(RealGameRules.AgendaPointsToWin, result.State.GetPlayer(fixture.CorpId).AgendaPoints);
        Assert.Equal(
            [new AgendaTurnChange(
                fixture.AgendaIds[0],
                PlayerSide.Corp,
                fixture.AgendaPoints,
                AgendaTurnChangeKind.Score)],
            result.State.Turn.ThisTurnFacts!.AgendaTurnChanges);
        Assert.DoesNotContain(result.State.Servers, server => server.Id == fixture.RemoteId);
        Assert.DoesNotContain(result.State.Zones, zone => zone.Id == fixture.RootZoneId || zone.Id == fixture.IceZoneId);
    }

    [Fact]
    public void PendingScoreRoundTripsThroughJsonAndHash()
    {
        Fixture fixture = CreateFixture(agendaCounters: [2], credits: 0);
        GameState state = OpenCorpTiming(fixture);
        string json = EngineJson.Serialize(state);
        GameState restored = EngineJson.DeserializeState(json, null, fixture.RuntimeCatalog);

        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(state), DeterministicHash.ForState(restored));
        ScoreAgendaParameters original = Assert.IsType<ScoreAgendaParameters>(
            Assert.Single(state.PendingDecision!.LegalActions, action => action.Kind == ActionKind.ScoreAgenda).Parameters);
        ScoreAgendaParameters roundTripped = Assert.IsType<ScoreAgendaParameters>(
            Assert.Single(restored.PendingDecision!.LegalActions, action => action.Kind == ActionKind.ScoreAgenda).Parameters);
        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void ForgedAdvanceDecisionSetsAreDiagnosedAndRejectedAtomically()
    {
        Fixture fixture = CreateFixture(agendaCounters: [0, 4], credits: 3);
        GameState source = OpenCorpAction(fixture);
        PendingDecision pending = source.PendingDecision!;
        LegalAction[] issued = pending.LegalActions
            .Where(action => action.Kind == ActionKind.AdvanceCard)
            .ToArray();
        Assert.Equal(2, issued.Length);

        LegalAction first = issued[0];
        AdvanceCardParameters firstParameters = Assert.IsType<AdvanceCardParameters>(first.Parameters);
        ActionId extraId = new("action:real-advance-extra");
        var variants = new (string Code, GameState State, ActionId ActionId)[]
        {
            (
                "decision.missing-advance-action",
                ReplacePending(source, pending.LegalActions.Where(action => action.Id != first.Id)),
                issued[1].Id),
            (
                "decision.extra-advance-action",
                ReplacePending(
                    source,
                    pending.LegalActions.Append(new LegalAction(
                        extraId,
                        ActionKind.AdvanceCard,
                        "Advance unknown agenda",
                        new AdvanceCardParameters(new CardInstanceId("card:real-advance-unknown"), 1)))),
                extraId),
            (
                "decision.duplicate-advance-action",
                ReplacePending(
                    source,
                    pending.LegalActions.Append(new LegalAction(
                        extraId,
                        ActionKind.AdvanceCard,
                        "Duplicate advance",
                        new AdvanceCardParameters(firstParameters.CardId, 1)))),
                extraId),
            (
                "decision.action-parameters-mismatch",
                ReplacePending(
                    source,
                    pending.LegalActions.Select(action => action.Id == first.Id
                        ? new LegalAction(action.Id, ActionKind.AdvanceCard, action.Description, new NoActionParameters())
                        : action)),
                first.Id),
            (
                "decision.advance-cost-mismatch",
                ReplacePending(
                    source,
                    pending.LegalActions.Select(action => action.Id == first.Id
                        ? new LegalAction(
                            action.Id,
                            ActionKind.AdvanceCard,
                            action.Description,
                            new AdvanceCardParameters(firstParameters.CardId, 2))
                        : action)),
                first.Id),
        };

        foreach ((string code, GameState state, ActionId actionId) in variants)
        {
            AssertValidatorRejects(state, fixture.RuntimeCatalog, code, actionId);
        }

        AssertValidatorRejects(
            ReplaceCorp(source, credits: 0),
            fixture.RuntimeCatalog,
            "decision.extra-advance-action",
            first.Id);
        AssertValidatorRejects(
            ReplaceTarget(source, zoneId: source.GetZone(ZoneKind.CorpDeck).Id),
            fixture.RuntimeCatalog,
            "decision.extra-advance-action",
            first.Id);
        AssertValidatorRejects(
            ReplaceTarget(source, definitionId: new CardDefinitionId("test:real-missing-definition")),
            fixture.RuntimeCatalog,
            "decision.extra-advance-action",
            first.Id);
    }

    [Fact]
    public void ForgedScoreDecisionSetsAndFrozenPrintedValuesAreDiagnosedAtomically()
    {
        Fixture fixture = CreateFixture(agendaCounters: [2, 2], credits: 0);
        GameState source = OpenCorpTiming(fixture);
        PendingDecision pending = source.PendingDecision!;
        LegalAction[] issued = pending.LegalActions
            .Where(action => action.Kind == ActionKind.ScoreAgenda)
            .ToArray();
        Assert.Equal(2, issued.Length);

        LegalAction first = issued[0];
        ScoreAgendaParameters firstParameters = Assert.IsType<ScoreAgendaParameters>(first.Parameters);
        ActionId extraId = new("action:real-score-extra");
        var variants = new (string Code, GameState State, ActionId ActionId)[]
        {
            (
                "decision.missing-score-action",
                ReplacePending(source, pending.LegalActions.Where(action => action.Id != first.Id)),
                issued[1].Id),
            (
                "decision.extra-score-action",
                ReplacePending(
                    source,
                    pending.LegalActions.Append(new LegalAction(
                        extraId,
                        ActionKind.ScoreAgenda,
                        "Score unknown agenda",
                        new ScoreAgendaParameters(new CardInstanceId("card:real-score-unknown"), 1, 2)))),
                extraId),
            (
                "decision.duplicate-score-action",
                ReplacePending(
                    source,
                    pending.LegalActions.Append(new LegalAction(
                        extraId,
                        ActionKind.ScoreAgenda,
                        "Duplicate score",
                        new ScoreAgendaParameters(
                            firstParameters.CardId,
                            firstParameters.AgendaPoints,
                            firstParameters.AdvancementRequirement)))),
                extraId),
            (
                "decision.action-parameters-mismatch",
                ReplacePending(
                    source,
                    pending.LegalActions.Select(action => action.Id == first.Id
                        ? new LegalAction(action.Id, ActionKind.ScoreAgenda, action.Description, new NoActionParameters())
                        : action)),
                first.Id),
            (
                "decision.extra-score-action",
                ReplacePending(
                    source,
                    pending.LegalActions.Select(action => action.Id == first.Id
                        ? new LegalAction(
                            action.Id,
                            ActionKind.ScoreAgenda,
                            action.Description,
                            new ScoreAgendaParameters(
                                firstParameters.CardId,
                                firstParameters.AgendaPoints + 1,
                                firstParameters.AdvancementRequirement))
                        : action)),
                first.Id),
            (
                "decision.extra-score-action",
                ReplacePending(
                    source,
                    pending.LegalActions.Select(action => action.Id == first.Id
                        ? new LegalAction(
                            action.Id,
                            ActionKind.ScoreAgenda,
                            action.Description,
                            new ScoreAgendaParameters(
                                firstParameters.CardId,
                                firstParameters.AgendaPoints,
                                firstParameters.AdvancementRequirement + 1))
                        : action)),
                first.Id),
        };

        foreach ((string code, GameState state, ActionId actionId) in variants)
        {
            AssertValidatorRejects(state, fixture.RuntimeCatalog, code, actionId);
        }

        AssertValidatorRejects(
            ReplaceTarget(source, advancementCounters: 1),
            fixture.RuntimeCatalog,
            "decision.extra-score-action",
            first.Id);
        AssertValidatorRejects(
            ReplaceTarget(source, zoneId: source.GetZone(ZoneKind.CorpDeck).Id),
            fixture.RuntimeCatalog,
            "decision.extra-score-action",
            first.Id);
        AssertValidatorRejects(
            ReplaceTarget(source, definitionId: new CardDefinitionId("test:real-missing-definition")),
            fixture.RuntimeCatalog,
            "decision.extra-score-action",
            first.Id);
    }

    [Fact]
    public void ForgedPendingAdvanceAndScoreSetsAreRejectedAtomicallyWithoutValidatorThrows()
    {
        Fixture advanceFixture = CreateFixture(agendaCounters: [0], credits: 3);
        GameState advanceState = OpenCorpAction(advanceFixture);
        LegalAction advance = Assert.Single(advanceState.PendingDecision!.LegalActions, action => action.Kind == ActionKind.AdvanceCard);
        LegalAction alteredAdvance = new(
            advance.Id,
            ActionKind.AdvanceCard,
            advance.Description,
            new AdvanceCardParameters(advanceFixture.AgendaIds[0], 2));
        GameState forgedAdvance = ReplacePending(advanceState, advanceState.PendingDecision!.LegalActions
            .Select(action => action.Id == advance.Id ? alteredAdvance : action));
        AssertValidatorRejects(forgedAdvance, advanceFixture.RuntimeCatalog, "decision.advance-cost-mismatch");

        Fixture scoreFixture = CreateFixture(agendaCounters: [2], credits: 0);
        GameState scoreState = OpenCorpTiming(scoreFixture);
        LegalAction score = Assert.Single(scoreState.PendingDecision!.LegalActions, action => action.Kind == ActionKind.ScoreAgenda);
        ScoreAgendaParameters scorePayload = Assert.IsType<ScoreAgendaParameters>(score.Parameters);
        LegalAction alteredScore = new(
            score.Id,
            ActionKind.ScoreAgenda,
            score.Description,
            new ScoreAgendaParameters(scorePayload.CardId, scorePayload.AgendaPoints + 1, scorePayload.AdvancementRequirement));
        GameState forgedScore = ReplacePending(scoreState, scoreState.PendingDecision!.LegalActions
            .Select(action => action.Id == score.Id ? alteredScore : action));
        AssertValidatorRejects(forgedScore, scoreFixture.RuntimeCatalog, "decision.extra-score-action");
    }

    [Fact]
    public void ScoreRetainsRemoteWhenIceRemains()
    {
        Fixture fixture = CreateFixture(agendaCounters: [2], credits: 0, addIce: true);
        GameState state = OpenCorpTiming(fixture);
        LegalAction score = Assert.Single(state.PendingDecision!.LegalActions, action => action.Kind == ActionKind.ScoreAgenda);

        ApplyResult result = Apply(fixture, state, score);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Contains(result.State.Servers, server => server.Id == fixture.RemoteId);
        Assert.Contains(result.State.Zones, zone => zone.Id == fixture.RootZoneId);
        Assert.Contains(result.State.Zones, zone => zone.Id == fixture.IceZoneId);
    }

    [Fact]
    public void ScoreRetainsRemoteWhenAnotherRootCardRemains()
    {
        Fixture fixture = CreateFixture(agendaCounters: [2], credits: 0, addProtectedRootCard: true);
        GameState state = OpenCorpTiming(fixture);
        LegalAction score = Assert.Single(state.PendingDecision!.LegalActions, action => action.Kind == ActionKind.ScoreAgenda);

        ApplyResult result = Apply(fixture, state, score);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Contains(result.State.Servers, server => server.Id == fixture.RemoteId);
        Assert.Contains(result.State.Zones, zone => zone.Id == fixture.RootZoneId);
        Assert.Contains(result.State.Zones, zone => zone.Id == fixture.IceZoneId);
    }

    private static void AssertValidatorRejects(
        GameState state,
        IRuntimeCardCatalog runtimeCatalog,
        string code,
        ActionId? submittedActionId = null)
    {
        IReadOnlyList<StateInvariantViolation>? violations = null;
        Exception? exception = Record.Exception(() => violations = StateInvariantValidator.Collect(state, null, runtimeCatalog));
        Assert.Null(exception);
        Assert.Contains(violations!, violation => violation.Code == code);

        string before = EngineJson.Serialize(state);
        PendingDecision pending = state.PendingDecision!;
        ApplyResult rejected = GameEngine.Apply(
            state,
            new ActionSubmission(
                pending.Owner,
                pending.Id,
                pending.Revision,
                submittedActionId ?? pending.LegalActions[0].Id),
            FixtureBehaviorCatalog.Catalog,
            runtimeCatalog);
        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Same(state, rejected.State);
        Assert.Empty(rejected.Events);
        Assert.Equal(before, EngineJson.Serialize(rejected.State));
    }

    private static ApplyResult Apply(Fixture fixture, GameState state, LegalAction action)
    {
        PendingDecision pending = state.PendingDecision!;
        return GameEngine.Apply(
            state,
            new ActionSubmission(pending.Owner, pending.Id, pending.Revision, action.Id),
            FixtureBehaviorCatalog.Catalog,
            fixture.RuntimeCatalog);
    }

    private static GameState OpenCorpAction(Fixture fixture)
    {
        GameState state = BeginCorp(fixture);
        while (state.PendingDecision?.Kind != DecisionKind.TurnAction)
        {
            PendingDecision pending = state.PendingDecision!;
            LegalAction pass = Assert.Single(pending.LegalActions, action => action.Kind == ActionKind.PassPriority);
            ApplyResult result = Apply(fixture, state, pass);
            Assert.True(result.Accepted, result.Rejection?.Message);
            state = result.State;
        }

        return state;
    }

    private static GameState OpenCorpTiming(Fixture fixture) => BeginCorp(fixture);

    private static GameState BeginCorp(Fixture fixture)
    {
        GameState state = GameEngine.BeginTurn(
            fixture.State,
            PlayerSide.Corp,
            FixtureBehaviorCatalog.Catalog,
            fixture.RuntimeCatalog);
        while (state.PendingDecision?.Kind != DecisionKind.TurnTiming)
        {
            throw new Xunit.Sdk.XunitException("Corp turn did not yield its timing window.");
        }

        // BeginTurn first yields the BeforeTurnPriority window. Pass through
        // both priority seats; the resulting prompt is the action-priority
        // window where score actions are materialized.
        for (int index = 0; index < 2; index++)
        {
            PendingDecision pending = state.PendingDecision!;
            LegalAction pass = Assert.Single(pending.LegalActions, action => action.Kind == ActionKind.PassPriority);
            ApplyResult result = Apply(fixture, state, pass);
            Assert.True(result.Accepted, result.Rejection?.Message);
            state = result.State;
        }

        Assert.Equal(DecisionKind.TurnTiming, state.PendingDecision!.Kind);
        Assert.Equal(TurnFrameStage.ActionPriority, Assert.IsType<TurnFrame>(state.ResolutionStack.Single()).Stage);
        return state;
    }

    private static GameState ReplacePending(GameState state, IEnumerable<LegalAction> actions) => new(
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
        new PendingDecision(
            state.PendingDecision!.Id,
            state.PendingDecision.Revision,
            state.PendingDecision.Owner,
            state.PendingDecision.Kind,
            state.PendingDecision.Prompt,
            actions.ToArray()),
        state.ActionSequence,
        state.InitialSeed,
        state.RunState,
        state.RulesState,
        state.BehaviorRegistryVersion,
        state.BehaviorCatalogFingerprint,
        state.RuntimeCardCatalogVersion,
        state.RuntimeCardCatalogFingerprint);

    private static GameState ReplaceCorp(
        GameState state,
        int? credits = null,
        int? clicks = null)
    {
        PlayerState corp = state.GetPlayer(state.Players.Single(player => player.Side == PlayerSide.Corp).Id);
        IReadOnlyList<PlayerState> players = state.Players
            .Select(player => player.Id == corp.Id
                ? new PlayerState(
                    player.Id,
                    player.Side,
                    credits ?? player.Credits,
                    clicks ?? player.Clicks,
                    player.AgendaPoints)
                : player)
            .ToArray();
        return Rebuild(state, players: players);
    }

    private static GameState ReplaceTarget(
        GameState state,
        ZoneId? zoneId = null,
        CardDefinitionId? definitionId = null,
        PlayerSide? ownerSide = null,
        bool? faceUp = null,
        bool? rezzed = null,
        int? advancementCounters = null)
    {
        ServerState remote = state.Servers.Single(server => server.Kind == ServerKind.Remote);
        ZoneState root = state.GetZone(remote.RootZoneId);
        CardInstanceState card = state.GetCard(root.CardIds[0]);
        CardInstanceState replacement = new(
            card.Id,
            definitionId ?? card.DefinitionId,
            zoneId ?? card.ZoneId,
            ownerSide ?? card.OwnerSide,
            faceUp ?? card.FaceUp,
            rezzed ?? card.Rezzed,
            advancementCounters ?? card.AdvancementCounters);
        return Rebuild(
            state,
            cards: state.Cards
                .Select(candidate => candidate.Id == replacement.Id ? replacement : candidate)
                .ToArray());
    }

    private static GameState AddTransientPlayArea(GameState state, PlayerSide side)
    {
        ZoneKind kind = side == PlayerSide.Corp ? ZoneKind.CorpPlayArea : ZoneKind.RunnerPlayArea;
        ZoneId zoneId = new($"zone:real-{side.ToString().ToLowerInvariant()}-play-area-forged");
        CardInstanceId cardId = new($"card:real-{side.ToString().ToLowerInvariant()}-play-area-forged");
        return Rebuild(
            state,
            zones: state.Zones.Append(new ZoneState(zoneId, kind, side, cardIds: [cardId])).ToArray());
    }

    private static TurnFrame PriorityFrame(
        GameState state,
        PlayerSide activeSide,
        TurnFrameStage stage,
        TurnPriorityWindowKind window,
        PlayerSide holder) =>
        new(
            1,
            ResolutionBehaviorIds.Turn,
            activeSide,
            state.Turn.TurnNumber,
            stage,
            window,
            holder);

    private static GameState Rebuild(
        GameState source,
        IReadOnlyList<PlayerState>? players = null,
        IReadOnlyList<CardInstanceState>? cards = null,
        IReadOnlyList<ZoneState>? zones = null,
        IReadOnlyList<ServerState>? servers = null,
        TurnState? turn = null,
        IdAllocatorState? allocator = null,
        IReadOnlyList<ResolutionFrame>? resolutionStack = null) => new(
        source.SchemaVersion,
        source.GameId,
        source.RulesVersion,
        source.CardDataVersion,
        players ?? source.Players,
        cards ?? source.Cards,
        zones ?? source.Zones,
        servers ?? source.Servers,
        turn ?? source.Turn,
        source.Outcome,
        source.RandomState,
        allocator ?? source.Allocator,
        source.DecisionRevision,
        source.EventSequence,
        resolutionStack ?? source.ResolutionStack,
        source.PendingDecision,
        source.ActionSequence,
        source.InitialSeed,
        source.RunState,
        source.RulesState,
        source.BehaviorRegistryVersion,
        source.BehaviorCatalogFingerprint,
        source.RuntimeCardCatalogVersion,
        source.RuntimeCardCatalogFingerprint);

    private static Fixture CreatePhysicalOrderFixture()
    {
        CardDefinitionId agendaDefinition = new("test:real-physical-agenda");
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:real-physical-order")
            .Add(new CardRuntimeSpec(
                agendaDefinition,
                PlayerSide.Corp,
                RuntimeCardClass.Agenda,
                agendaPoints: 1,
                advancementRequirement: 2))
            .Build();

        GameState baseState = GameState.CreateNew(
            449,
            behaviorRegistryVersion: FixtureBehaviorCatalog.Catalog.Version,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);
        ServerId zuluId = new("server:real-zulu");
        ServerId alphaId = new("server:real-alpha");
        ZoneId zuluIceId = new("zone:real-zulu-ice");
        ZoneId zuluRootId = new("zone:real-zulu-root");
        ZoneId alphaIceId = new("zone:real-alpha-ice");
        ZoneId alphaRootId = new("zone:real-alpha-root");
        var servers = baseState.Servers
            .Append(new ServerState(zuluId, ServerKind.Remote, zuluIceId, zuluRootId))
            .Append(new ServerState(alphaId, ServerKind.Remote, alphaIceId, alphaRootId))
            .ToArray();
        var zones = baseState.Zones
            .Append(new ZoneState(zuluIceId, ZoneKind.ServerIce, PlayerSide.Corp, zuluId))
            .Append(new ZoneState(zuluRootId, ZoneKind.ServerRoot, PlayerSide.Corp, zuluId))
            .Append(new ZoneState(alphaIceId, ZoneKind.ServerIce, PlayerSide.Corp, alphaId))
            .Append(new ZoneState(alphaRootId, ZoneKind.ServerRoot, PlayerSide.Corp, alphaId))
            .ToList();

        CardInstanceId deckCardId = new("card:real-physical-deck");
        CardInstanceId zuluSecondId = new("card:real-zulu-2");
        CardInstanceId zuluFirstId = new("card:real-zulu-1");
        CardInstanceId alphaIdCard = new("card:real-alpha-agenda");
        var cards = new List<CardInstanceState>
        {
            new(deckCardId, new CardDefinitionId("test:real-physical-deck"), baseState.GetZone(ZoneKind.CorpDeck).Id, PlayerSide.Corp),
            new(zuluSecondId, agendaDefinition, zuluRootId, PlayerSide.Corp, faceUp: false, advancementCounters: 3),
            new(zuluFirstId, agendaDefinition, zuluRootId, PlayerSide.Corp, faceUp: false, advancementCounters: 2),
            new(alphaIdCard, agendaDefinition, alphaRootId, PlayerSide.Corp, faceUp: false, advancementCounters: 2),
        };
        int deckIndex = zones.FindIndex(zone => zone.Kind == ZoneKind.CorpDeck && zone.ServerId is null);
        zones[deckIndex] = new ZoneState(
            zones[deckIndex].Id,
            zones[deckIndex].Kind,
            zones[deckIndex].OwnerSide,
            zones[deckIndex].ServerId,
            [deckCardId]);
        int zuluRootIndex = zones.FindIndex(zone => zone.Id == zuluRootId);
        zones[zuluRootIndex] = new ZoneState(
            zuluRootId,
            ZoneKind.ServerRoot,
            PlayerSide.Corp,
            zuluId,
            // Deliberately reverse the lexical card IDs: this list is the
            // physical root order the action projection must preserve.
            [zuluSecondId, zuluFirstId]);
        int alphaRootIndex = zones.FindIndex(zone => zone.Id == alphaRootId);
        zones[alphaRootIndex] = new ZoneState(
            alphaRootId,
            ZoneKind.ServerRoot,
            PlayerSide.Corp,
            alphaId,
            [alphaIdCard]);

        GameState state = new(
            baseState.SchemaVersion,
            baseState.GameId,
            baseState.RulesVersion,
            baseState.CardDataVersion,
            baseState.Players
                .Select(player => player.Side == PlayerSide.Corp
                    ? new PlayerState(player.Id, player.Side, credits: 3, clicks: 3)
                    : new PlayerState(player.Id, player.Side, player.Credits, player.Clicks, player.AgendaPoints))
                .ToArray(),
            cards,
            zones,
            servers,
            new TurnState(PlayerSide.Corp, TurnPhase.Corp, 1),
            new GameOutcome(),
            baseState.RandomState,
            baseState.Allocator,
            baseState.DecisionRevision,
            baseState.EventSequence,
            [],
            null,
            baseState.ActionSequence,
            baseState.InitialSeed,
            null,
            null,
            baseState.BehaviorRegistryVersion,
            baseState.BehaviorCatalogFingerprint,
            runtimeCatalog.Version,
            runtimeCatalog.Fingerprint);
        return new Fixture(
            state,
            runtimeCatalog,
            state.Players.Single(player => player.Side == PlayerSide.Corp).Id,
            zuluId,
            zuluRootId,
            zuluIceId,
            [zuluSecondId, zuluFirstId],
            [
                new RemoteFixture(alphaId, alphaRootId, alphaIceId, [alphaIdCard]),
                new RemoteFixture(zuluId, zuluRootId, zuluIceId, [zuluSecondId, zuluFirstId]),
            ],
            1,
            2);
    }

    private static Fixture CreateFixture(
        IReadOnlyList<int> agendaCounters,
        int agendaPoints = 0,
        int credits = 3,
        IReadOnlyList<int>? agendaOrder = null,
        bool addIce = false,
        bool addProtectedRootCard = false)
    {
        CardDefinitionId agendaDefinition = new("test:real-agenda");
        CardDefinitionId assetDefinition = new("test:real-asset");
        CardDefinitionId iceDefinition = new("test:real-ice");
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:real-advance-score")
            .Add(new CardRuntimeSpec(
                agendaDefinition,
                PlayerSide.Corp,
                RuntimeCardClass.Agenda,
                agendaPoints: 1,
                advancementRequirement: 2))
            .Add(new CardRuntimeSpec(
                assetDefinition,
                PlayerSide.Corp,
                RuntimeCardClass.Asset,
                rezCost: 0))
            .Add(new CardRuntimeSpec(
                iceDefinition,
                PlayerSide.Corp,
                RuntimeCardClass.Ice,
                rezCost: 0))
            .Build();

        GameState baseState = GameState.CreateNew(
            401,
            behaviorRegistryVersion: FixtureBehaviorCatalog.Catalog.Version,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);
        var allocator = new StableIdAllocator(baseState.Allocator);
        ServerId remoteId = allocator.NextServerId();
        ZoneId iceZoneId = allocator.NextZoneId();
        ZoneId rootZoneId = allocator.NextZoneId();
        var servers = baseState.Servers.Append(new ServerState(remoteId, ServerKind.Remote, iceZoneId, rootZoneId)).ToArray();
        var zones = baseState.Zones
            .Append(new ZoneState(iceZoneId, ZoneKind.ServerIce, PlayerSide.Corp, remoteId))
            .Append(new ZoneState(rootZoneId, ZoneKind.ServerRoot, PlayerSide.Corp, remoteId))
            .ToList();
        var cards = new List<CardInstanceState>();
        ZoneState deck = baseState.GetZone(ZoneKind.CorpDeck);
        CardInstanceId deckCardId = allocator.NextCardInstanceId();
        cards.Add(new CardInstanceState(deckCardId, new CardDefinitionId("test:corp-deck"), deck.Id, PlayerSide.Corp));
        zones[zones.FindIndex(zone => zone.Id == deck.Id)] = new ZoneState(
            deck.Id,
            deck.Kind,
            deck.OwnerSide,
            deck.ServerId,
            [deckCardId]);

        var agendaIds = new List<CardInstanceId>();
        List<CardInstanceId> rootCards = [];
        IReadOnlyList<int> order = agendaOrder ?? Enumerable.Range(0, agendaCounters.Count).ToArray();
        foreach (int index in order)
        {
            CardInstanceId cardId = allocator.NextCardInstanceId();
            agendaIds.Add(cardId);
            cards.Add(new CardInstanceState(
                cardId,
                agendaDefinition,
                rootZoneId,
                PlayerSide.Corp,
                faceUp: false,
                advancementCounters: agendaCounters[index]));
            rootCards.Add(cardId);
        }

        if (addProtectedRootCard)
        {
            CardInstanceId assetId = allocator.NextCardInstanceId();
            cards.Add(new CardInstanceState(assetId, assetDefinition, rootZoneId, PlayerSide.Corp));
            rootCards.Add(assetId);
        }

        if (addIce)
        {
            CardInstanceId iceId = allocator.NextCardInstanceId();
            cards.Add(new CardInstanceState(iceId, iceDefinition, iceZoneId, PlayerSide.Corp));
            zones[zones.FindIndex(zone => zone.Id == iceZoneId)] = new ZoneState(
                iceZoneId,
                ZoneKind.ServerIce,
                PlayerSide.Corp,
                remoteId,
                [iceId]);
        }

        zones[zones.FindIndex(zone => zone.Id == rootZoneId)] = new ZoneState(
            rootZoneId,
            ZoneKind.ServerRoot,
            PlayerSide.Corp,
            remoteId,
            rootCards);
        PlayerState[] players = baseState.Players
            .Select(player => player.Side == PlayerSide.Corp
                ? new PlayerState(player.Id, player.Side, credits, 3, agendaPoints)
                : new PlayerState(player.Id, player.Side, player.Credits, 4, player.AgendaPoints))
            .ToArray();
        GameState state = new(
            baseState.SchemaVersion,
            baseState.GameId,
            baseState.RulesVersion,
            baseState.CardDataVersion,
            players,
            cards,
            zones,
            servers,
            new TurnState(PlayerSide.Corp, TurnPhase.Corp, 1),
            new GameOutcome(),
            baseState.RandomState,
            allocator.State,
            baseState.DecisionRevision,
            baseState.EventSequence,
            [],
            null,
            baseState.ActionSequence,
            baseState.InitialSeed,
            null,
            null,
            baseState.BehaviorRegistryVersion,
            baseState.BehaviorCatalogFingerprint,
            runtimeCatalog.Version,
            runtimeCatalog.Fingerprint);
        return new Fixture(
            state,
            runtimeCatalog,
            state.Players.Single(player => player.Side == PlayerSide.Corp).Id,
            remoteId,
            rootZoneId,
            iceZoneId,
            agendaIds,
            [new RemoteFixture(remoteId, rootZoneId, iceZoneId, agendaIds)],
            1,
            2);
    }

    private sealed record RemoteFixture(
        ServerId ServerId,
        ZoneId RootZoneId,
        ZoneId IceZoneId,
        IReadOnlyList<CardInstanceId> AgendaIds);

    private sealed record Fixture(
        GameState State,
        RuntimeCardCatalog RuntimeCatalog,
        PlayerId CorpId,
        ServerId RemoteId,
        ZoneId RootZoneId,
        ZoneId IceZoneId,
        IReadOnlyList<CardInstanceId> AgendaIds,
        IReadOnlyList<RemoteFixture> Remotes,
        int AgendaPoints,
        int Requirement);
}

using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>Contract coverage for the pure ENG-413a1 turn-facts ledger.</summary>
public sealed class TurnFactsContractTests
{
    [Fact]
    public void PureTransitionsPreserveOrderingUniquenessAndImmutability()
    {
        CardInstanceId accessedCard = new("card:facts-accessed");
        CardInstanceId sourceCard = new("card:facts-source");
        ServerId firstServer = new("server:facts-first");
        ServerId retiredServer = new("server:facts-retired");

        TurnFactsState initial = Facts();
        TurnFactsState attempted = initial.MarkRunAttempted(firstServer);
        TurnFactsState duplicateAttempt = attempted.MarkRunAttempted(firstServer);
        TurnFactsState successful = attempted
            .MarkRunAttempted(retiredServer)
            .MarkSuccessfulRun(retiredServer)
            .MarkSuccessfulRun(retiredServer);
        TurnFactsState recorded = successful
            .RecordAccess(accessedCard)
            .RecordAccess(accessedCard)
            .RecordTrash(accessedCard)
            .RecordTrash(accessedCard)
            .RecordAgendaChange(new AgendaTurnChange(
                accessedCard,
                PlayerSide.Corp,
                2,
                AgendaTurnChangeKind.Score))
            .RecordTagChange(new TagTurnChange(
                new PlayerId("player:facts-runner"),
                -1))
            .ConsumeFirstX(sourceCard, "first:facts", 2)
            .ConsumeFirstX(sourceCard, "first:facts", 3);

        Assert.Empty(initial.AttemptedRunServerIds);
        Assert.Equal([firstServer], attempted.AttemptedRunServerIds);
        Assert.Same(attempted, duplicateAttempt);
        Assert.Equal([firstServer, retiredServer], successful.AttemptedRunServerIds);
        Assert.Equal([retiredServer], successful.SuccessfulRunServerIds);
        Assert.True(successful.SuccessfulRun);
        Assert.Equal([accessedCard, accessedCard], recorded.AccessedCardOccurrenceIds);
        Assert.Equal([accessedCard, accessedCard], recorded.TrashedCardOccurrenceIds);
        Assert.Single(recorded.AgendaTurnChanges);
        Assert.Single(recorded.TagTurnChanges);
        Assert.Single(recorded.FirstXUseKeys);
        Assert.Equal(5, recorded.GetFirstXCount(sourceCard, "first:facts"));
        Assert.Equal(0, recorded.GetFirstXCount(sourceCard, "first:missing"));
    }

    [Fact]
    public void HistoryTransitionsReplaceOnlyCurrentFactsAndCarryAllTypedLedgers()
    {
        CardInstanceId card = new("card:facts-history");
        ServerId server = new("server:facts-history");
        TurnFactsState facts = Facts()
            .MarkRunAttempted(server)
            .RecordAccess(card)
            .RecordTrash(card)
            .RecordAgendaChange(card, PlayerSide.Corp, 1, AgendaTurnChangeKind.Score)
            .RecordTagChange(new PlayerId("player:facts-runner"), 1)
            .ConsumeFirstX(card, "first:history");
        TurnHistoryState history = new(1, null, null, facts, null);

        TurnHistoryState next = history
            .MarkSuccessfulRun(PlayerSide.Runner, server)
            .RecordAccess(PlayerSide.Runner, card);

        Assert.Empty(history.RunnerThisTurn!.SuccessfulRunServerIds);
        Assert.Equal([server], next.RunnerThisTurn!.SuccessfulRunServerIds);
        Assert.Equal([card, card], next.RunnerThisTurn.AccessedCardOccurrenceIds);
        Assert.Equal([card], next.RunnerThisTurn.TrashedCardOccurrenceIds);
        Assert.Equal(1, next.GetFirstXCount(PlayerSide.Runner, card, "first:history"));
        Assert.NotSame(history.RunnerThisTurn, next.RunnerThisTurn);
    }

    [Fact]
    public void StartingNextTurnMovesEveryTypedLedgerToLastTurnAndResetsCurrentFacts()
    {
        CardInstanceId card = new("card:facts-boundary");
        ServerId server = new("server:facts-boundary");
        TurnFactsState priorFacts = Facts()
            .MarkSuccessfulRun(server)
            .RecordAccess(card)
            .RecordTrash(card)
            .RecordAgendaChange(card, PlayerSide.Runner, 2, AgendaTurnChangeKind.Steal)
            .RecordTagChange(new PlayerId("player:facts-boundary-runner"), 1)
            .ConsumeFirstX(card, "first:boundary", 3);
        TurnHistoryState priorHistory = new(1, null, null, priorFacts, null);

        TurnHistoryState nextHistory = priorHistory.Start(PlayerSide.Runner, 2, 4);
        TurnFactsState current = nextHistory.RunnerThisTurn!;
        TurnFactsState last = nextHistory.RunnerLastTurn!;

        Assert.Equal(priorFacts.AttemptedRunServerIds, last.AttemptedRunServerIds);
        Assert.Equal(priorFacts.SuccessfulRunServerIds, last.SuccessfulRunServerIds);
        Assert.Equal(priorFacts.AccessedCardOccurrenceIds, last.AccessedCardOccurrenceIds);
        Assert.Equal(priorFacts.TrashedCardOccurrenceIds, last.TrashedCardOccurrenceIds);
        Assert.Equal(priorFacts.AgendaTurnChanges, last.AgendaTurnChanges);
        Assert.Equal(priorFacts.TagTurnChanges, last.TagTurnChanges);
        Assert.Equal(priorFacts.FirstXUseKeys, last.FirstXUseKeys);
        Assert.False(current.SuccessfulRun);
        Assert.Empty(current.AttemptedRunServerIds);
        Assert.Empty(current.SuccessfulRunServerIds);
        Assert.Empty(current.AccessedCardOccurrenceIds);
        Assert.Empty(current.TrashedCardOccurrenceIds);
        Assert.Empty(current.AgendaTurnChanges);
        Assert.Empty(current.TagTurnChanges);
        Assert.Empty(current.FirstXUseKeys);
        Assert.Equal(2, current.TurnNumber);
        Assert.NotSame(priorHistory.RunnerThisTurn, last);
        Assert.Null(priorHistory.RunnerLastTurn);
        Assert.Equal(priorFacts.SuccessfulRunServerIds, priorHistory.RunnerThisTurn!.SuccessfulRunServerIds);
        Assert.Equal(priorFacts.FirstXUseKeys, priorHistory.RunnerThisTurn.FirstXUseKeys);
    }

    [Fact]
    public void ConstructorAndCopyDeepFreezeTypedListsAndNestedRecords()
    {
        CardInstanceId card = new("card:facts-copy");
        ServerId server = new("server:facts-copy");
        CardInstanceId source = new("card:facts-copy-source");
        var attempted = new List<ServerId> { server };
        var successful = new List<ServerId> { server };
        var accesses = new List<CardInstanceId> { card };
        var trashes = new List<CardInstanceId> { card };
        var agendas = new List<AgendaTurnChange>
        {
            new(card, PlayerSide.Corp, 2, AgendaTurnChangeKind.Score),
        };
        var tags = new List<TagTurnChange>
        {
            new(new PlayerId("player:facts-runner"), 1),
        };
        var firstX = new List<FirstXUseKey>
        {
            new(source, "first:copy", 2),
        };
        TurnFactsState facts = new(
            1,
            PlayerSide.Runner,
            1,
            4,
            0,
            true,
            false,
            false,
            false,
            successfulRun: true,
            attemptedRunServerIds: attempted,
            successfulRunServerIds: successful,
            accessedCardOccurrenceIds: accesses,
            trashedCardOccurrenceIds: trashes,
            agendaTurnChanges: agendas,
            tagTurnChanges: tags,
            firstXUseKeys: firstX);

        attempted.Add(new ServerId("server:facts-late"));
        successful.Clear();
        accesses.Add(new CardInstanceId("card:facts-late"));
        agendas.Clear();
        tags.Clear();
        firstX.Clear();

        TurnFactsState copy = facts.Copy();
        Assert.Equal([server], facts.AttemptedRunServerIds);
        Assert.Equal([server], facts.SuccessfulRunServerIds);
        Assert.Equal([card], facts.AccessedCardOccurrenceIds);
        Assert.Single(facts.AgendaTurnChanges);
        Assert.Single(facts.TagTurnChanges);
        Assert.Single(facts.FirstXUseKeys);
        Assert.NotSame(facts.AttemptedRunServerIds, copy.AttemptedRunServerIds);
        Assert.NotSame(facts.AgendaTurnChanges, copy.AgendaTurnChanges);
        Assert.NotSame(facts.AgendaTurnChanges[0], copy.AgendaTurnChanges[0]);
        Assert.NotSame(facts.TagTurnChanges[0], copy.TagTurnChanges[0]);
        Assert.NotSame(facts.FirstXUseKeys[0], copy.FirstXUseKeys[0]);

        TurnHistoryState history = new(1, null, null, facts, null);
        TurnHistoryState historyCopy = history.Copy();
        Assert.NotSame(history.RunnerThisTurn, historyCopy.RunnerThisTurn);
        Assert.NotSame(history.RunnerThisTurn!.AgendaTurnChanges, historyCopy.RunnerThisTurn!.AgendaTurnChanges);
        Assert.NotSame(history.RunnerThisTurn.AgendaTurnChanges[0], historyCopy.RunnerThisTurn.AgendaTurnChanges[0]);
    }

    [Fact]
    public void TypedFactsRoundTripWithCanonicalJsonAndLegacySuccessfulRunShape()
    {
        CardInstanceId card = new("card:facts-json");
        TurnFactsState facts = Facts()
            .MarkSuccessfulRun(new ServerId("server:facts-json"))
            .RecordAccess(card)
            .RecordTrash(card)
            .RecordAgendaChange(card, PlayerSide.Runner, 1, AgendaTurnChangeKind.Steal)
            .RecordTagChange(new PlayerId("player:facts-runner"), 1)
            .ConsumeFirstX(card, "first:json", 4);

        string json = EngineJson.Serialize(facts);
        TurnFactsState restored = EngineJson.Deserialize<TurnFactsState>(json);

        Assert.Contains("attemptedRunServerIds", json, StringComparison.Ordinal);
        Assert.Contains("agendaTurnChanges", json, StringComparison.Ordinal);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(facts.AttemptedRunServerIds, restored.AttemptedRunServerIds);
        Assert.Equal(facts.SuccessfulRunServerIds, restored.SuccessfulRunServerIds);
        Assert.Equal(facts.AccessedCardOccurrenceIds, restored.AccessedCardOccurrenceIds);
        Assert.Equal(facts.TrashedCardOccurrenceIds, restored.TrashedCardOccurrenceIds);
        Assert.Equal(facts.AgendaTurnChanges, restored.AgendaTurnChanges);
        Assert.Equal(facts.TagTurnChanges, restored.TagTurnChanges);
        Assert.Equal(facts.FirstXUseKeys, restored.FirstXUseKeys);

        TurnFactsState legacy = new(
            1,
            PlayerSide.Runner,
            1,
            4,
            0,
            true,
            false,
            false,
            false,
            successfulRun: true);
        TurnFactsState restoredLegacy = EngineJson.Deserialize<TurnFactsState>(EngineJson.Serialize(legacy));
        Assert.True(restoredLegacy.SuccessfulRun);
        Assert.Empty(restoredLegacy.SuccessfulRunServerIds);
    }

    [Fact]
    public void FirstXConsumptionRejectsNonpositiveAmountsAndCheckedOverflow()
    {
        CardInstanceId source = new("card:facts-overflow-source");
        TurnFactsState facts = Facts();

        Assert.Throws<ArgumentOutOfRangeException>(() => facts.ConsumeFirstX(source, "first:zero", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => facts.ConsumeFirstX(source, "first:negative", -1));

        TurnFactsState maxed = Facts(firstXUseKeys: [new FirstXUseKey(source, "first:max", int.MaxValue)]);
        Assert.Throws<OverflowException>(() => maxed.ConsumeFirstX(source, "first:max"));
    }

    [Fact]
    public void InvariantsRejectForgedFactReferencesDuplicatesAndInconsistentRunLedger()
    {
        GameState seed = GameState.CreateNew(4131);
        ServerId knownServer = seed.Servers[0].Id;
        ServerId retiredServer = new("server:retired-history");
        CardInstanceId unknownCard = new("card:missing-facts");
        CardInstanceId unknownSource = new("card:missing-source");
        PlayerId corp = seed.Players.Single(player => player.Side == PlayerSide.Corp).Id;

        TurnFactsState invalid = new(
            1,
            PlayerSide.Runner,
            1,
            4,
            0,
            true,
            false,
            false,
            false,
            successfulRun: false,
            attemptedRunServerIds: [knownServer, knownServer],
            successfulRunServerIds: [retiredServer, retiredServer],
            accessedCardOccurrenceIds: [unknownCard, unknownCard],
            trashedCardOccurrenceIds: [unknownCard],
            agendaTurnChanges: [new AgendaTurnChange(unknownCard, PlayerSide.Corp, 1, AgendaTurnChangeKind.Score)],
            tagTurnChanges: [new TagTurnChange(corp, 1)],
            firstXUseKeys:
            [
                new FirstXUseKey(unknownSource, "first:duplicate", 1),
                new FirstXUseKey(unknownSource, "first:duplicate", 2),
            ]);
        GameState forged = WithFacts(seed, invalid);

        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(forged);
        Assert.Contains(violations, violation => violation.Code == "turn.duplicate-attempted-run-server");
        Assert.Contains(violations, violation => violation.Code == "turn.duplicate-successful-run-server");
        Assert.Contains(violations, violation => violation.Code == "turn.successful-run-not-attempted");
        Assert.Contains(violations, violation => violation.Code == "turn.successful-run-flag-mismatch");
        Assert.Contains(violations, violation => violation.Code == "turn.unknown-card-occurrence");
        Assert.Contains(violations, violation => violation.Code == "turn.unknown-agenda-card");
        Assert.Contains(violations, violation => violation.Code == "turn.tag-target-not-runner");
        Assert.Contains(violations, violation => violation.Code == "turn.unknown-first-x-source");
        Assert.Contains(violations, violation => violation.Code == "turn.duplicate-first-x-key");

        TurnFactsState retiredHistory = Facts()
            .MarkSuccessfulRun(retiredServer);
        IReadOnlyList<StateInvariantViolation> validHistoricalViolations = StateInvariantValidator.Collect(
            WithFacts(seed, retiredHistory));
        Assert.DoesNotContain(validHistoricalViolations, violation =>
            violation.Code is "turn.unknown-run-server" or "turn.successful-run-not-attempted");
    }

    private static TurnFactsState Facts(
        IReadOnlyList<FirstXUseKey>? firstXUseKeys = null) => new(
        1,
        PlayerSide.Runner,
        1,
        4,
        0,
        true,
        false,
        false,
        false,
        firstXUseKeys: firstXUseKeys);

    private static GameState WithFacts(TurnFactsState facts)
    {
        GameState seed = GameState.CreateNew(4131);
        return WithFacts(seed, facts);
    }

    private static GameState WithFacts(GameState seed, TurnFactsState facts)
    {
        return new GameState(
            seed.GameId,
            seed.RulesVersion,
            seed.CardDataVersion,
            players: seed.Players,
            cards: seed.Cards,
            zones: seed.Zones,
            servers: seed.Servers,
            turn: new TurnState(
                PlayerSide.Runner,
                TurnPhase.Runner,
                1,
                TurnStep.RunnerAction,
                new TurnHistoryState(1, null, null, facts, null)),
            outcome: seed.Outcome,
            randomState: seed.RandomState,
            allocator: seed.Allocator,
            schemaVersion: seed.SchemaVersion,
            decisionRevision: seed.DecisionRevision,
            eventSequence: seed.EventSequence,
            resolutionStack: seed.ResolutionStack,
            pendingDecision: seed.PendingDecision,
            actionSequence: seed.ActionSequence,
            initialSeed: seed.InitialSeed,
            runState: seed.RunState,
            rulesState: seed.RulesState,
            behaviorRegistryVersion: seed.BehaviorRegistryVersion,
            behaviorCatalogFingerprint: seed.BehaviorCatalogFingerprint,
            runtimeCardCatalogVersion: seed.RuntimeCardCatalogVersion,
            runtimeCardCatalogFingerprint: seed.RuntimeCardCatalogFingerprint);
    }
}

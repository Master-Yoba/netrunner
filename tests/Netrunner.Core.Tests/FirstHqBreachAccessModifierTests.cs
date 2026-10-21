using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// ENG-416 coverage for the first-HQ-breach access declaration.  These tests
/// exercise the typed declaration, the real BeginBreach checkpoint, the
/// occurrence/FirstX ledgers, and the observation boundary together so a
/// source cannot be re-read after the access snapshot has been committed.
/// </summary>
public sealed class FirstHqBreachAccessModifierTests
{
    private static readonly CardDefinitionId SourceDefinition = new("test:first-hq-source");
    private static readonly CardDefinitionId SecondSourceDefinition = new("test:first-hq-source-two");
    private static readonly CardDefinitionId HiddenDefinition = new("test:first-hq-hidden");
    private static readonly CardInstanceId SourceCard = new("card:first-hq-source");
    private static readonly CardInstanceId SecondSourceCard = new("card:first-hq-source-two");

    [Fact]
    public void DeclarationValidatesRoundTripsRejectsDuplicateReasonsAndChangesFingerprint()
    {
        BehaviorFirstHqBreachAccessModifier modifier = new(1, "first-hq-test");
        CardBehaviorDefinition plain = new(SourceDefinition, PlayerSide.Runner);
        CardBehaviorDefinition declared = new(
            SourceDefinition,
            PlayerSide.Runner,
            firstHqBreachAccessModifiers: [modifier]);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BehaviorFirstHqBreachAccessModifier(0, "first-hq-test"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BehaviorFirstHqBreachAccessModifier(-1, "first-hq-test"));
        Assert.Throws<ArgumentException>(() =>
            new BehaviorFirstHqBreachAccessModifier(1, "not stable"));
        Assert.Throws<ArgumentException>(() => new CardBehaviorDefinition(
            SourceDefinition,
            PlayerSide.Runner,
            firstHqBreachAccessModifiers:
            [
                modifier,
                new BehaviorFirstHqBreachAccessModifier(2, "first-hq-test"),
            ]));

        string json = EngineJson.Serialize(declared);
        CardBehaviorDefinition restored = EngineJson.Deserialize<CardBehaviorDefinition>(json);
        Assert.Contains("firstHqBreachAccessModifiers", json, StringComparison.Ordinal);
        Assert.Equal(declared.FirstHqBreachAccessModifiers, restored.FirstHqBreachAccessModifiers);

        string legacyJson = EngineJson.Serialize(plain);
        string emptyJson = EngineJson.Serialize(new CardBehaviorDefinition(
            SourceDefinition,
            PlayerSide.Runner,
            firstHqBreachAccessModifiers: []));
        Assert.Equal(legacyJson, emptyJson);
        Assert.DoesNotContain("firstHqBreachAccessModifiers", legacyJson, StringComparison.Ordinal);

        BehaviorCatalog baseline = new BehaviorCatalogBuilder("test:first-hq-fingerprint")
            .Add(plain)
            .Build();
        BehaviorCatalog changed = new BehaviorCatalogBuilder("test:first-hq-fingerprint")
            .Add(declared)
            .Build();
        Assert.NotEqual(baseline.Fingerprint, changed.Fingerprint);
    }

    [Fact]
    public void LegacyModifierSourceKindDefaultsAndRoundTripsWithoutChangingJson()
    {
        RunBreachAccessModifier legacy = new(
            SourceCard,
            new ServerId("server:first-hq-legacy"),
            1,
            "legacy-access");

        string json = EngineJson.Serialize(legacy);
        Assert.DoesNotContain("sourceKind", json, StringComparison.Ordinal);

        RunBreachAccessModifier restored = EngineJson.Deserialize<RunBreachAccessModifier>(json);
        Assert.Equal(RunBreachAccessModifierSourceKind.InstalledRunSource, restored.SourceKind);
        Assert.Equal(json, EngineJson.Serialize(restored));
    }

    [Fact]
    public void FirstHqBreachSnapshotsActiveSourceAddsAccessAndConsumesFirstX()
    {
        Fixture fixture = CreateFixture(
            includeSource: true,
            handCards: [
                new CardInstanceId("card:first-hq-hand-one"),
                new CardInstanceId("card:first-hq-hand-two"),
                new CardInstanceId("card:first-hq-hand-three"),
            ]);
        BreachCursor breach = BeginBreach(fixture, ServerKind.Headquarters);

        BreachStartedEventPayload started = Assert.IsType<BreachStartedEventPayload>(
            breach.Events.Single(value => value.Kind == EngineEventKinds.BreachStarted).Payload);
        Assert.Equal(fixture.Headquarters, started.ServerId);
        Assert.Equal(0, started.CandidateCount);
        Assert.Equal(2, started.RandomAccessLimit);
        Assert.Equal(2, breach.State.RunState!.RandomAccessLimit);

        RunBreachAccessModifier snapshot = Assert.Single(breach.State.RunState.BreachAccessModifiers!);
        Assert.Equal(SourceCard, snapshot.SourceCardId);
        Assert.Equal(fixture.Headquarters, snapshot.AttackedServerId);
        Assert.Equal(1, snapshot.Delta);
        Assert.Equal("first-hq-test", snapshot.ReasonKey);
        Assert.Equal(RunBreachAccessModifierSourceKind.FirstHeadquartersBreach, snapshot.SourceKind);

        TurnFactsState facts = Assert.IsType<TurnFactsState>(breach.State.Turn.ThisTurnFacts);
        Assert.Equal([fixture.Headquarters], facts.HqBreachOccurrenceServerIds);
        Assert.Equal(1, facts.GetFirstXCount(SourceCard, "first-hq-test"));
        Assert.Empty(StateInvariantValidator.Collect(breach.State, fixture.Catalog));
    }

    [Fact]
    public void MultipleSourcesHaveDeterministicOrderAndHiddenPoolCapsAccess()
    {
        Fixture fixture = CreateFixture(
            includeSource: true,
            includeSecondSource: true,
            firstDelta: 2,
            secondDelta: 3,
            handCards: [
                new CardInstanceId("card:first-hq-hand-one"),
                new CardInstanceId("card:first-hq-hand-two"),
            ]);
        BreachCursor left = BeginBreach(fixture, ServerKind.Headquarters);
        BreachCursor right = BeginBreach(CreateFixture(
            includeSource: true,
            includeSecondSource: true,
            firstDelta: 2,
            secondDelta: 3,
            handCards: [
                new CardInstanceId("card:first-hq-hand-one"),
                new CardInstanceId("card:first-hq-hand-two"),
            ]), ServerKind.Headquarters);

        RunBreachAccessModifier[] modifiers = left.State.RunState!.BreachAccessModifiers!.ToArray();
        Assert.Equal([SourceCard, SecondSourceCard], modifiers.Select(value => value.SourceCardId).ToArray());
        Assert.Equal([2, 3], modifiers.Select(value => value.Delta).ToArray());
        Assert.All(modifiers, value =>
            Assert.Equal(RunBreachAccessModifierSourceKind.FirstHeadquartersBreach, value.SourceKind));
        Assert.Equal(2, left.State.RunState.RandomAccessLimit);
        Assert.Equal(EngineJson.Serialize(left.State), EngineJson.Serialize(right.State));
        Assert.Equal(DeterministicHash.ForState(left.State), DeterministicHash.ForState(right.State));
    }

    [Fact]
    public void SecondHqBreachInTheSameFormalTurnHasNoModifier()
    {
        Fixture fixture = CreateFixture(
            includeSource: true,
            handCards: [
                new CardInstanceId("card:first-hq-hand-one"),
                new CardInstanceId("card:first-hq-hand-two"),
            ]);
        BreachCursor first = BeginBreach(fixture, ServerKind.Headquarters);
        GameState afterFirst = FinishBreach(first.State, fixture.Catalog);
        Assert.Null(afterFirst.RunState);
        Assert.Equal(1, afterFirst.Turn.ThisTurnFacts!.GetFirstXCount(SourceCard, "first-hq-test"));
        Assert.Equal([fixture.Headquarters], afterFirst.Turn.ThisTurnFacts.HqBreachOccurrenceServerIds);

        BreachCursor second = BeginBreach(fixture with { State = afterFirst }, ServerKind.Headquarters);
        Assert.Equal(1, second.State.RunState!.RandomAccessLimit);
        Assert.DoesNotContain(
            second.State.RunState.BreachAccessModifiers ?? [],
            value => value.SourceKind == RunBreachAccessModifierSourceKind.FirstHeadquartersBreach);
        Assert.Equal(1, second.State.Turn.ThisTurnFacts!.GetFirstXCount(SourceCard, "first-hq-test"));
        Assert.Equal(
            [fixture.Headquarters, fixture.Headquarters],
            second.State.Turn.ThisTurnFacts.HqBreachOccurrenceServerIds);
    }

    [Fact]
    public void FirstHqWithoutSourceCannotActivateSourceAddedLaterInTheTurn()
    {
        Fixture fixture = CreateFixture(
            includeSource: false,
            handCards: [
                new CardInstanceId("card:first-hq-hand-one"),
                new CardInstanceId("card:first-hq-hand-two"),
            ]);
        BreachCursor first = BeginBreach(fixture, ServerKind.Headquarters);
        Assert.Equal(1, first.State.RunState!.RandomAccessLimit);
        Assert.Null(first.State.RunState.BreachAccessModifiers);
        Assert.Empty(first.State.Turn.ThisTurnFacts!.FirstXUseKeys);
        GameState afterFirst = FinishBreach(first.State, fixture.Catalog);

        GameState lateSource = AddCard(
            afterFirst,
            SourceCard,
            SourceDefinition,
            ZoneKind.RunnerRig,
            PlayerSide.Runner,
            faceUp: true,
            fixture.Catalog);
        BreachCursor second = BeginBreach(fixture with { State = lateSource }, ServerKind.Headquarters);
        Assert.Equal(1, second.State.RunState!.RandomAccessLimit);
        Assert.DoesNotContain(
            second.State.RunState.BreachAccessModifiers ?? [],
            value => value.SourceKind == RunBreachAccessModifierSourceKind.FirstHeadquartersBreach);
        Assert.Empty(second.State.Turn.ThisTurnFacts!.FirstXUseKeys);
    }

    [Fact]
    public void NonHqBreachDoesNotConsumeFirstHqSource()
    {
        Fixture fixture = CreateFixture(
            includeSource: true,
            deckCards: [
                new CardInstanceId("card:first-hq-deck-one"),
                new CardInstanceId("card:first-hq-deck-two"),
            ]);
        BreachCursor breach = BeginBreach(fixture, ServerKind.RAndD);

        Assert.Equal(1, breach.State.RunState!.RandomAccessLimit);
        Assert.DoesNotContain(
            breach.State.RunState.BreachAccessModifiers ?? [],
            value => value.SourceKind == RunBreachAccessModifierSourceKind.FirstHeadquartersBreach);
        Assert.Empty(breach.State.Turn.ThisTurnFacts!.HqBreachOccurrenceServerIds);
        Assert.Empty(breach.State.Turn.ThisTurnFacts.FirstXUseKeys);
    }

    [Fact]
    public void StartingNextFormalTurnResetsFirstHqWindow()
    {
        Fixture fixture = CreateFixture(
            includeSource: true,
            handCards: [
                new CardInstanceId("card:first-hq-hand-one"),
                new CardInstanceId("card:first-hq-hand-two"),
            ]);
        GameState afterFirst = FinishBreach(
            BeginBreach(fixture, ServerKind.Headquarters).State,
            fixture.Catalog);
        TurnHistoryState history = afterFirst.Turn.History!;
        TurnHistoryState nextHistory = history
            .Start(PlayerSide.Runner, 2, 4)
            .MarkTurnBegan(PlayerSide.Runner);
        GameState nextTurn = ReplaceTurn(
            afterFirst,
            new TurnState(
                PlayerSide.Runner,
                TurnPhase.Runner,
                2,
                TurnStep.RunnerAction,
                nextHistory));

        BreachCursor breach = BeginBreach(fixture with { State = nextTurn }, ServerKind.Headquarters);
        RunBreachAccessModifier modifier = Assert.Single(
            breach.State.RunState!.BreachAccessModifiers!,
            value => value.SourceKind == RunBreachAccessModifierSourceKind.FirstHeadquartersBreach);
        Assert.Equal(2, breach.State.RunState.RandomAccessLimit);
        Assert.Equal(1, breach.State.Turn.ThisTurnFacts!.GetFirstXCount(SourceCard, "first-hq-test"));
        Assert.Equal(1, breach.State.Turn.History!.RunnerLastTurn!.GetFirstXCount(SourceCard, "first-hq-test"));
        Assert.Equal(1, modifier.Delta);
    }

    [Fact]
    public void SourceMovedAfterBreachSnapshotDoesNotRemoveModifier()
    {
        Fixture fixture = CreateFixture(
            includeSource: true,
            handCards: [new CardInstanceId("card:first-hq-hand-one")]);
        BreachCursor breach = BeginBreach(fixture, ServerKind.Headquarters);
        RunBreachAccessModifier snapshot = Assert.Single(breach.State.RunState!.BreachAccessModifiers!);
        GameState moved = MoveCard(breach.State, SourceCard, ZoneKind.RunnerHeap, fixture.Catalog);

        Assert.Equal(ZoneKind.RunnerHeap, moved.GetZone(moved.GetCard(SourceCard).ZoneId).Kind);
        Assert.Contains(moved.RunState!.BreachAccessModifiers!, value => value == snapshot);
        Assert.Empty(StateInvariantValidator.Collect(moved, fixture.Catalog));

        GameState finished = FinishBreach(moved, fixture.Catalog);
        Assert.Null(finished.RunState);
        Assert.Equal(1, finished.Turn.ThisTurnFacts!.GetFirstXCount(SourceCard, "first-hq-test"));
    }

    [Fact]
    public void MovingHiddenAgendaOutOfHandKeepsRemainingAccessCursorValid()
    {
        Fixture fixture = CreateFixture(
            includeSource: true,
            hiddenIsAgenda: true,
            handCards:
            [
                new CardInstanceId("card:first-hq-hand-one"),
                new CardInstanceId("card:first-hq-hand-two"),
                new CardInstanceId("card:first-hq-hand-three"),
            ]);
        BreachCursor breach = BeginBreach(fixture, ServerKind.Headquarters);
        Assert.Equal(2, breach.State.RunState!.RandomAccessLimit);

        LegalAction hiddenToken = Assert.Single(breach.State.PendingDecision!.LegalActions);
        ApplyResult chosen = Apply(breach.State, hiddenToken, fixture.Catalog);
        Assert.True(chosen.Accepted, chosen.Rejection?.Message);
        Assert.Equal(1, chosen.State.RunState!.RandomAccessLimit);

        ApplyResult stolen = Apply(
            chosen.State,
            Assert.Single(chosen.State.PendingDecision!.LegalActions),
            fixture.Catalog);
        Assert.True(stolen.Accepted, stolen.Rejection?.Message);
        Assert.Equal(RunTimingStep.BreachCandidateSelection, stolen.State.RunState!.Step);
        Assert.Equal(1, stolen.State.RunState.RandomAccessLimit);
        CardInstanceId accessed = chosen.State.RunState!.ChosenAccessCards[^1];
        Assert.Equal(
            stolen.State.GetZone(ZoneKind.RunnerScore).Id,
            stolen.State.GetCard(accessed).ZoneId);
        Assert.DoesNotContain(
            StateInvariantValidator.Collect(stolen.State, fixture.Catalog),
            violation => violation.Code == "run.random-access-limit-mismatch");

        RunState stolenRun = stolen.State.RunState!;
        RunState forgedMoved = CopyRun(
            stolenRun,
            randomAccessLimit: stolenRun.RandomAccessLimit + 2);
        Assert.Contains(
            StateInvariantValidator.Collect(ReplaceRun(stolen.State, forgedMoved), fixture.Catalog),
            violation => violation.Code == "run.random-access-limit-mismatch");
    }

    [Fact]
    public void MovingRootAgendaOutOfRootDoesNotConsumeHiddenAccess()
    {
        Fixture fixture = CreateFixture(
            includeSource: true,
            hiddenIsAgenda: true,
            handCards:
            [
                new CardInstanceId("card:first-hq-hand-one"),
                new CardInstanceId("card:first-hq-hand-two"),
                new CardInstanceId("card:first-hq-hand-three"),
            ],
            hqRootCards: [new CardInstanceId("card:first-hq-root-one")]);
        BreachCursor breach = BeginBreach(fixture, ServerKind.Headquarters);
        Assert.Equal(2, breach.State.RunState!.RandomAccessLimit);

        CardInstanceId rootCard = new("card:first-hq-root-one");
        LegalAction rootCandidate = Assert.Single(
            breach.State.PendingDecision!.LegalActions,
            action => (action.Parameters as BreachCandidateParameters)?.CardId == rootCard);
        ApplyResult chosen = Apply(breach.State, rootCandidate, fixture.Catalog);
        Assert.True(chosen.Accepted, chosen.Rejection?.Message);
        Assert.Equal(2, chosen.State.RunState!.RandomAccessLimit);

        ApplyResult stolen = Apply(
            chosen.State,
            Assert.Single(chosen.State.PendingDecision!.LegalActions),
            fixture.Catalog);
        Assert.True(stolen.Accepted, stolen.Rejection?.Message);
        Assert.Equal(2, stolen.State.RunState!.RandomAccessLimit);
        Assert.Equal(
            stolen.State.GetZone(ZoneKind.RunnerScore).Id,
            stolen.State.GetCard(rootCard).ZoneId);
        Assert.DoesNotContain(
            StateInvariantValidator.Collect(stolen.State, fixture.Catalog),
            violation => violation.Code == "run.random-access-limit-mismatch");
    }

    [Fact]
    public void MovingTheLastHiddenAgendaOutOfASmallPoolLeavesZeroCursorValid()
    {
        Fixture fixture = CreateFixture(
            includeSource: true,
            hiddenIsAgenda: true,
            handCards: [new CardInstanceId("card:first-hq-hand-one")],
            hqRootCards: [new CardInstanceId("card:first-hq-root-one")]);
        BreachCursor breach = BeginBreach(fixture, ServerKind.Headquarters);
        Assert.Equal(1, breach.State.RunState!.RandomAccessLimit);

        LegalAction hiddenToken = Assert.Single(
            breach.State.PendingDecision!.LegalActions,
            action => Assert.IsType<BreachCandidateParameters>(action.Parameters).Source
                == BreachHiddenCandidateSource.CorpHand);
        ApplyResult chosen = Apply(breach.State, hiddenToken, fixture.Catalog);
        Assert.True(chosen.Accepted, chosen.Rejection?.Message);
        Assert.Equal(0, chosen.State.RunState!.RandomAccessLimit);

        ApplyResult stolen = Apply(
            chosen.State,
            Assert.Single(chosen.State.PendingDecision!.LegalActions),
            fixture.Catalog);
        Assert.True(stolen.Accepted, stolen.Rejection?.Message);
        Assert.Equal(RunTimingStep.BreachCandidateSelection, stolen.State.RunState!.Step);
        Assert.Equal(0, stolen.State.RunState.RandomAccessLimit);
        Assert.DoesNotContain(
            StateInvariantValidator.Collect(stolen.State, fixture.Catalog),
            violation => violation.Code == "run.random-access-limit-mismatch");
    }

    [Fact]
    public void ForgedSourceKindDeclarationLedgerAndAccessQuoteAreRejectedByInvariants()
    {
        Fixture fixture = CreateFixture(
            includeSource: true,
            handCards: [new CardInstanceId("card:first-hq-hand-one")]);
        BreachCursor breach = BeginBreach(fixture, ServerKind.Headquarters);
        RunState valid = breach.State.RunState!;
        RunBreachAccessModifier declared = Assert.Single(valid.BreachAccessModifiers!);

        RunState forgedKind = CopyRun(valid, modifiers:
        [
            new RunBreachAccessModifier(
                declared.SourceCardId,
                declared.AttackedServerId,
                declared.Delta,
                declared.ReasonKey,
                RunBreachAccessModifierSourceKind.InstalledRunSource),
        ]);
        Assert.Contains(
            StateInvariantValidator.Collect(ReplaceRun(breach.State, forgedKind), fixture.Catalog),
            value => value.Code == "run.breach-modifier-no-source");

        RunState forgedDeclaration = CopyRun(valid, modifiers:
        [
            new RunBreachAccessModifier(
                declared.SourceCardId,
                declared.AttackedServerId,
                declared.Delta,
                "first-hq-forged",
                RunBreachAccessModifierSourceKind.FirstHeadquartersBreach),
        ]);
        Assert.Contains(
            StateInvariantValidator.Collect(ReplaceRun(breach.State, forgedDeclaration), fixture.Catalog),
            value => value.Code == "run.breach-modifier-undeclared");

        TurnFactsState facts = breach.State.Turn.ThisTurnFacts!;
        TurnFactsState forgedLedger = new(
            facts.Version,
            facts.Side,
            facts.TurnNumber,
            facts.ClicksAllotted,
            facts.ClicksLost,
            facts.TurnBegan,
            facts.MandatoryDrawPerformed,
            facts.ActionPhaseEnded,
            facts.DiscardPhaseEnded,
            facts.InstalledCardIds,
            facts.SuccessfulRun,
            facts.UsedAbilityKeys,
            facts.AttemptedRunServerIds,
            facts.SuccessfulRunServerIds,
            facts.AccessedCardOccurrenceIds,
            facts.TrashedCardOccurrenceIds,
            facts.AgendaTurnChanges,
            facts.TagTurnChanges,
            facts.FirstXUseKeys,
            facts.ProgramInstallOccurrenceIds,
            []);
        Assert.Contains(
            StateInvariantValidator.Collect(ReplaceFacts(breach.State, forgedLedger), fixture.Catalog),
            value => value.Code == "run.breach-modifier-first-hq-occurrence");

        TurnFactsState forgedFirstXLedger = new(
            facts.Version,
            facts.Side,
            facts.TurnNumber,
            facts.ClicksAllotted,
            facts.ClicksLost,
            facts.TurnBegan,
            facts.MandatoryDrawPerformed,
            facts.ActionPhaseEnded,
            facts.DiscardPhaseEnded,
            facts.InstalledCardIds,
            facts.SuccessfulRun,
            facts.UsedAbilityKeys,
            facts.AttemptedRunServerIds,
            facts.SuccessfulRunServerIds,
            facts.AccessedCardOccurrenceIds,
            facts.TrashedCardOccurrenceIds,
            facts.AgendaTurnChanges,
            facts.TagTurnChanges,
            [],
            facts.ProgramInstallOccurrenceIds,
            facts.HqBreachOccurrenceServerIds);
        Assert.Contains(
            StateInvariantValidator.Collect(ReplaceFacts(breach.State, forgedFirstXLedger), fixture.Catalog),
            value => value.Code == "run.breach-modifier-first-hq-unconsumed");

        RunState forgedQuote = CopyRun(valid, randomAccessLimit: valid.RandomAccessLimit + 1);
        Assert.Contains(
            StateInvariantValidator.Collect(ReplaceRun(breach.State, forgedQuote), fixture.Catalog),
            value => value.Code == "run.random-access-limit-mismatch");
    }

    [Fact]
    public void ObservationCarriesRawModifierTermsWithoutLeakingHiddenHand()
    {
        CardInstanceId hidden = new("card:first-hq-hidden-observation");
        Fixture fixture = CreateFixture(includeSource: true, handCards: [hidden]);
        BreachCursor breach = BeginBreach(fixture, ServerKind.Headquarters);

        Observation runner = ObservationBuilder.Build(breach.State, PlayerSide.Runner, fixture.Catalog);
        Dictionary<string, object?> runnerRun = Assert.IsType<Dictionary<string, object?>>(runner.PublicState["run"]);
        IReadOnlyList<object?> modifiers = Assert.IsAssignableFrom<IReadOnlyList<object?>>(runnerRun["breachAccessModifiers"]);
        Dictionary<string, object?> modifier = Assert.IsType<Dictionary<string, object?>>(Assert.Single(modifiers));
        Assert.Equal(SourceCard.Value, modifier["sourceCardId"]);
        Assert.Equal(fixture.Headquarters.Value, modifier["attackedServerId"]);
        Assert.Equal(1, modifier["delta"]);
        Assert.Equal("first-hq-test", modifier["reasonKey"]);
        Assert.Equal(RunBreachAccessModifierSourceKind.FirstHeadquartersBreach, modifier["sourceKind"]);

        string runnerJson = EngineJson.Serialize(runner);
        Assert.DoesNotContain(hidden.Value, runnerJson, StringComparison.Ordinal);
        Assert.DoesNotContain(HiddenDefinition.Value, runnerJson, StringComparison.Ordinal);
        string corpJson = EngineJson.Serialize(ObservationBuilder.Build(breach.State, PlayerSide.Corp, fixture.Catalog));
        Assert.Contains(hidden.Value, corpJson, StringComparison.Ordinal);
    }

    [Fact]
    public void MidBreachRoundTripPreservesModifierFactsAndStateHash()
    {
        Fixture fixture = CreateFixture(
            includeSource: true,
            handCards: [
                new CardInstanceId("card:first-hq-hand-one"),
                new CardInstanceId("card:first-hq-hand-two"),
            ]);
        BreachCursor original = BeginBreach(fixture, ServerKind.Headquarters);
        string json = EngineJson.Serialize(original.State);
        GameState restored = EngineJson.DeserializeState(json, fixture.Catalog);

        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(original.State), DeterministicHash.ForState(restored));
        Assert.Equal(
            original.State.Turn.ThisTurnFacts!.HqBreachOccurrenceServerIds,
            restored.Turn.ThisTurnFacts!.HqBreachOccurrenceServerIds);
        Assert.Equal(
            original.State.RunState!.BreachAccessModifiers,
            restored.RunState!.BreachAccessModifiers);

        LegalAction action = Assert.Single(original.State.PendingDecision!.LegalActions);
        ApplyResult left = Apply(original.State, action, fixture.Catalog);
        ApplyResult right = Apply(restored, action, fixture.Catalog);
        Assert.True(left.Accepted, left.Rejection?.Message);
        Assert.True(right.Accepted, right.Rejection?.Message);
        Assert.Equal(EngineJson.Serialize(left.State), EngineJson.Serialize(right.State));
        Assert.Equal(DeterministicHash.ForEvents(left.Events), DeterministicHash.ForEvents(right.Events));
    }

    private static BehaviorCatalog CreateCatalog(
        bool includeSecondSource,
        int firstDelta,
        int secondDelta,
        bool hiddenIsAgenda = false)
    {
        BehaviorCatalogBuilder builder = new("test:first-hq-catalog");
        builder.Add(new CardBehaviorDefinition(
            SourceDefinition,
            PlayerSide.Runner,
            firstHqBreachAccessModifiers: [new BehaviorFirstHqBreachAccessModifier(firstDelta, "first-hq-test")]));
        if (includeSecondSource)
        {
            builder.Add(new CardBehaviorDefinition(
                SecondSourceDefinition,
                PlayerSide.Runner,
                firstHqBreachAccessModifiers:
                [new BehaviorFirstHqBreachAccessModifier(secondDelta, "second-first-hq-test")]));
        }

        if (hiddenIsAgenda)
        {
            builder.AddAgenda(new AgendaBehaviorSpec(HiddenDefinition, agendaPoints: 2));
        }

        return builder.Build();
    }

    private static Fixture CreateFixture(
        bool includeSource,
        bool includeSecondSource = false,
        int firstDelta = 1,
        int secondDelta = 1,
        bool hiddenIsAgenda = false,
        IReadOnlyList<CardInstanceId>? handCards = null,
        IReadOnlyList<CardInstanceId>? deckCards = null,
        IReadOnlyList<CardInstanceId>? hqRootCards = null)
    {
        BehaviorCatalog catalog = CreateCatalog(includeSecondSource, firstDelta, secondDelta, hiddenIsAgenda);
        GameState baseline = GameState.CreateNew(
            seed: 41601,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint);
        ZoneState rig = baseline.GetZone(ZoneKind.RunnerRig);
        ZoneState hand = baseline.GetZone(ZoneKind.CorpHand);
        ZoneState deck = baseline.GetZone(ZoneKind.CorpDeck);
        ServerState headquarters = baseline.Servers.Single(server => server.Kind == ServerKind.Headquarters);
        ZoneState hqRoot = baseline.GetZone(headquarters.RootZoneId);
        List<CardInstanceState> cards = [];
        List<CardInstanceId> rigCards = [];
        if (includeSource)
        {
            cards.Add(new CardInstanceState(SourceCard, SourceDefinition, rig.Id, PlayerSide.Runner, faceUp: true));
            rigCards.Add(SourceCard);
        }

        if (includeSecondSource)
        {
            cards.Add(new CardInstanceState(SecondSourceCard, SecondSourceDefinition, rig.Id, PlayerSide.Runner, faceUp: true));
            rigCards.Add(SecondSourceCard);
        }

        CardInstanceId[] handIds = handCards?.ToArray() ?? [];
        foreach (CardInstanceId cardId in handIds)
        {
            cards.Add(new CardInstanceState(cardId, HiddenDefinition, hand.Id, PlayerSide.Corp));
        }

        CardInstanceId[] deckIds = deckCards?.ToArray() ?? [];
        foreach (CardInstanceId cardId in deckIds)
        {
            cards.Add(new CardInstanceState(cardId, HiddenDefinition, deck.Id, PlayerSide.Corp));
        }

        CardInstanceId[] hqRootIds = hqRootCards?.ToArray() ?? [];
        foreach (CardInstanceId cardId in hqRootIds)
        {
            cards.Add(new CardInstanceState(cardId, HiddenDefinition, hqRoot.Id, PlayerSide.Corp));
        }

        ZoneState[] zones = baseline.Zones.Select(zone => zone.Id == rig.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, rigCards)
                : zone.Id == hand.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, handIds)
                    : zone.Id == deck.Id
                        ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, deckIds)
                        : zone.Id == hqRoot.Id
                            ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, hqRootIds)
                            : zone)
            .ToArray();
        PlayerState[] players = baseline.Players
            .Select(player => player.Side == PlayerSide.Runner
                ? new PlayerState(player.Id, player.Side, credits: 10, clicks: 4, player.AgendaPoints)
                : player)
            .ToArray();
        TurnFactsState facts = new(
            1,
            PlayerSide.Runner,
            baseline.Turn.TurnNumber,
            clicksAllotted: 4,
            clicksLost: 0,
            turnBegan: true,
            mandatoryDrawPerformed: false,
            actionPhaseEnded: false,
            discardPhaseEnded: false);
        TurnHistoryState history = new(1, null, null, facts, null);
        GameState state = new(
            schemaVersion: baseline.SchemaVersion,
            gameId: baseline.GameId,
            rulesVersion: baseline.RulesVersion,
            cardDataVersion: baseline.CardDataVersion,
            players: players,
            cards: cards,
            zones: zones,
            servers: baseline.Servers,
            turn: new TurnState(
                PlayerSide.Runner,
                TurnPhase.Runner,
                baseline.Turn.TurnNumber,
                TurnStep.RunnerAction,
                history),
            outcome: baseline.Outcome,
            randomState: baseline.RandomState,
            allocator: baseline.Allocator,
            decisionRevision: baseline.DecisionRevision,
            eventSequence: baseline.EventSequence,
            resolutionStack: [],
            pendingDecision: null,
            actionSequence: baseline.ActionSequence,
            initialSeed: baseline.InitialSeed,
            runState: null,
            rulesState: baseline.RulesState,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint);
        StateInvariantValidator.Validate(state, catalog);
        return new Fixture(
            state,
            catalog,
            baseline.Servers.Single(server => server.Kind == ServerKind.Headquarters).Id,
            baseline.Servers.Single(server => server.Kind == ServerKind.RAndD).Id);
    }

    private static BreachCursor BeginBreach(Fixture fixture, ServerKind serverKind)
    {
        ServerId serverId = serverKind == ServerKind.Headquarters ? fixture.Headquarters : fixture.RAndD;
        GameEngine.RunStartResult start = GameEngine.BeginRunWithEvents(fixture.State, serverId, fixture.Catalog);
        List<EngineEvent> events = start.Events.ToList();
        GameState cursor = start.State;
        for (int pass = 0; pass < 2; pass++)
        {
            ApplyResult result = Apply(cursor, Assert.Single(
                cursor.PendingDecision!.LegalActions,
                value => value.Kind == ActionKind.PassPriority), fixture.Catalog);
            Assert.True(result.Accepted, result.Rejection?.Message);
            events.AddRange(result.Events);
            cursor = result.State;
        }

        Assert.Equal(RunTimingStep.BreachCandidateSelection, cursor.RunState!.Step);
        Assert.Contains(events, value => value.Kind == EngineEventKinds.BreachStarted);
        return new BreachCursor(cursor, events);
    }

    private static GameState FinishBreach(GameState state, BehaviorCatalog catalog)
    {
        GameState cursor = state;
        for (int step = 0; cursor.RunState is not null; step++)
        {
            Assert.True(step < 32, "The breach did not reach run cleanup.");
            PendingDecision decision = cursor.PendingDecision
                ?? throw new InvalidOperationException("An active breach must yield a decision.");
            LegalAction action = cursor.RunState.Step == RunTimingStep.BreachCandidateSelection
                ? Assert.Single(decision.LegalActions)
                : Assert.Single(decision.LegalActions, value => value.Kind == ActionKind.NoAccessAction);
            ApplyResult result = Apply(cursor, action, catalog);
            Assert.True(result.Accepted, result.Rejection?.Message);
            cursor = result.State;
        }

        return cursor;
    }

    private static ApplyResult Apply(GameState state, LegalAction action, BehaviorCatalog catalog) => GameEngine.Apply(
        state,
        new ActionSubmission(
            state.PendingDecision!.Owner,
            state.PendingDecision.Id,
            state.PendingDecision.Revision,
            action.Id),
        catalog);

    private static GameState AddCard(
        GameState source,
        CardInstanceId cardId,
        CardDefinitionId definitionId,
        ZoneKind destinationKind,
        PlayerSide ownerSide,
        bool faceUp,
        BehaviorCatalog catalog)
    {
        ZoneState destination = source.GetZone(destinationKind);
        CardInstanceState card = new(cardId, definitionId, destination.Id, ownerSide, faceUp);
        GameState next = new(
            gameId: source.GameId,
            rulesVersion: source.RulesVersion,
            cardDataVersion: source.CardDataVersion,
            players: source.Players,
            cards: source.Cards.Append(card).ToArray(),
            zones: source.Zones.Select(zone => zone.Id == destination.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(cardId).ToArray())
                : zone).ToArray(),
            servers: source.Servers,
            turn: source.Turn,
            outcome: source.Outcome,
            randomState: source.RandomState,
            allocator: source.Allocator,
            schemaVersion: source.SchemaVersion,
            decisionRevision: source.DecisionRevision,
            eventSequence: source.EventSequence,
            resolutionStack: source.ResolutionStack,
            pendingDecision: source.PendingDecision,
            actionSequence: source.ActionSequence,
            initialSeed: source.InitialSeed,
            runState: source.RunState,
            rulesState: source.RulesState,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint,
            runtimeCardCatalogVersion: source.RuntimeCardCatalogVersion,
            runtimeCardCatalogFingerprint: source.RuntimeCardCatalogFingerprint);
        StateInvariantValidator.Validate(next, catalog);
        return next;
    }

    private static GameState MoveCard(GameState source, CardInstanceId cardId, ZoneKind destinationKind, BehaviorCatalog catalog)
    {
        CardInstanceState card = source.GetCard(cardId);
        ZoneState from = source.GetZone(card.ZoneId);
        ZoneState to = source.GetZone(destinationKind);
        bool faceUp = destinationKind is ZoneKind.RunnerRig
            or ZoneKind.RunnerPlayArea
            or ZoneKind.RunnerHeap
            or ZoneKind.CorpDiscard;
        CardInstanceState moved = new(
            card.Id,
            card.DefinitionId,
            to.Id,
            card.OwnerSide,
            faceUp,
            card.Rezzed && faceUp,
            card.AdvancementCounters);
        return ReplaceCardsAndZones(
            source,
            source.Cards.Select(value => value.Id == cardId ? moved : value).ToArray(),
            source.Zones.Select(zone => zone.Id == from.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Where(id => id != cardId).ToArray())
                    : zone.Id == to.Id
                        ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(cardId).ToArray())
                        : zone)
                .ToArray(),
            catalog);
    }

    private static GameState ReplaceFacts(GameState source, TurnFactsState facts)
    {
        TurnHistoryState history = source.Turn.History!;
        return ReplaceTurn(
            source,
            new TurnState(
                source.Turn.ActiveSide,
                source.Turn.Phase,
                source.Turn.TurnNumber,
                source.Turn.Step,
                new TurnHistoryState(history.Version, history.CorpThisTurn, history.CorpLastTurn, facts, history.RunnerLastTurn)));
    }

    private static GameState ReplaceTurn(GameState source, TurnState turn) => new(
        gameId: source.GameId,
        rulesVersion: source.RulesVersion,
        cardDataVersion: source.CardDataVersion,
        players: source.Players,
        cards: source.Cards,
        zones: source.Zones,
        servers: source.Servers,
        turn: turn,
        outcome: source.Outcome,
        randomState: source.RandomState,
        allocator: source.Allocator,
        schemaVersion: source.SchemaVersion,
        decisionRevision: source.DecisionRevision,
        eventSequence: source.EventSequence,
        resolutionStack: source.ResolutionStack,
        pendingDecision: source.PendingDecision,
        actionSequence: source.ActionSequence,
        initialSeed: source.InitialSeed,
        runState: source.RunState,
        rulesState: source.RulesState,
        behaviorRegistryVersion: source.BehaviorRegistryVersion,
        behaviorCatalogFingerprint: source.BehaviorCatalogFingerprint,
        runtimeCardCatalogVersion: source.RuntimeCardCatalogVersion,
        runtimeCardCatalogFingerprint: source.RuntimeCardCatalogFingerprint);

    private static GameState ReplaceRun(GameState source, RunState run) => new(
        gameId: source.GameId,
        rulesVersion: source.RulesVersion,
        cardDataVersion: source.CardDataVersion,
        players: source.Players,
        cards: source.Cards,
        zones: source.Zones,
        servers: source.Servers,
        turn: source.Turn,
        outcome: source.Outcome,
        randomState: source.RandomState,
        allocator: source.Allocator,
        schemaVersion: source.SchemaVersion,
        decisionRevision: source.DecisionRevision,
        eventSequence: source.EventSequence,
        resolutionStack: source.ResolutionStack,
        pendingDecision: source.PendingDecision,
        actionSequence: source.ActionSequence,
        initialSeed: source.InitialSeed,
        runState: run,
        rulesState: source.RulesState,
        behaviorRegistryVersion: source.BehaviorRegistryVersion,
        behaviorCatalogFingerprint: source.BehaviorCatalogFingerprint,
        runtimeCardCatalogVersion: source.RuntimeCardCatalogVersion,
        runtimeCardCatalogFingerprint: source.RuntimeCardCatalogFingerprint);

    private static GameState ReplaceCardsAndZones(
        GameState source,
        IReadOnlyList<CardInstanceState> cards,
        IReadOnlyList<ZoneState> zones,
        BehaviorCatalog catalog) => new(
        gameId: source.GameId,
        rulesVersion: source.RulesVersion,
        cardDataVersion: source.CardDataVersion,
        players: source.Players,
        cards: cards,
        zones: zones,
        servers: source.Servers,
        turn: source.Turn,
        outcome: source.Outcome,
        randomState: source.RandomState,
        allocator: source.Allocator,
        schemaVersion: source.SchemaVersion,
        decisionRevision: source.DecisionRevision,
        eventSequence: source.EventSequence,
        resolutionStack: source.ResolutionStack,
        pendingDecision: source.PendingDecision,
        actionSequence: source.ActionSequence,
        initialSeed: source.InitialSeed,
        runState: source.RunState,
        rulesState: source.RulesState,
        behaviorRegistryVersion: catalog.Version,
        behaviorCatalogFingerprint: catalog.Fingerprint,
        runtimeCardCatalogVersion: source.RuntimeCardCatalogVersion,
        runtimeCardCatalogFingerprint: source.RuntimeCardCatalogFingerprint);

    private static RunState CopyRun(
        RunState source,
        IReadOnlyList<RunBreachAccessModifier>? modifiers = null,
        int? randomAccessLimit = null) => new(
        source.Version,
        source.Id,
        source.AttackedServer,
        source.InitiatingSide,
        source.InitiatingPlayer,
        source.InitiatingSource,
        source.Phase,
        source.Step,
        source.CurrentIceIndex,
        source.CurrentIceId,
        source.Successful,
        source.Unsuccessful,
        source.EndReason,
        source.Priority,
        source.Encounter,
        source.BreachCandidates,
        randomAccessLimit ?? source.RandomAccessLimit,
        source.ChosenAccessCards,
        source.AccessedCards,
        source.AccessState,
        source.RunCredits,
        source.ActiveRunEventId,
        source.LingeringEffects,
        modifiers ?? source.BreachAccessModifiers,
        source.RezCostModifiers,
        source.RunRestrictions,
        source.ResolvedSubroutineLedger,
        source.PersistentContext);

    private sealed record Fixture(
        GameState State,
        BehaviorCatalog Catalog,
        ServerId Headquarters,
        ServerId RAndD);

    private sealed record BreachCursor(GameState State, IReadOnlyList<EngineEvent> Events);
}

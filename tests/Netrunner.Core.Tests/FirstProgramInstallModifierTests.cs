using Xunit;

namespace Netrunner.Core.Tests;

public sealed class FirstProgramInstallModifierTests
{
    private static readonly CardDefinitionId SourceDefinition = new("test:first-program-source");
    private static readonly CardDefinitionId SecondSourceDefinition = new("test:first-program-source-two");
    private static readonly CardDefinitionId ProgramDefinition = new("test:first-program-target");
    private static readonly CardDefinitionId ExistingProgramDefinition = new("test:first-program-existing");
    private static readonly CardInstanceId SourceCard = new("card:first-program-source");
    private static readonly CardInstanceId SecondSourceCard = new("card:first-program-source-two");
    private static readonly CardInstanceId ProgramCard = new("card:first-program-target");
    private static readonly CardInstanceId ExistingProgramCard = new("card:first-program-existing");

    [Fact]
    public void DeclarationValidatesRoundTripsAndChangesCatalogFingerprint()
    {
        BehaviorFirstProgramInstallCostModifier modifier = new(-1, "first-program-test");
        CardBehaviorDefinition plain = new(SourceDefinition, PlayerSide.Runner);
        CardBehaviorDefinition declared = new(
            SourceDefinition,
            PlayerSide.Runner,
            firstProgramInstallCostModifiers: [modifier]);

        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorFirstProgramInstallCostModifier(0, "first-program-test"));
        Assert.Throws<ArgumentException>(() => new BehaviorFirstProgramInstallCostModifier(-1, "not stable"));
        Assert.Throws<ArgumentException>(() => new CardBehaviorDefinition(
            SourceDefinition,
            PlayerSide.Runner,
            firstProgramInstallCostModifiers: [modifier, modifier]));

        string json = EngineJson.Serialize(declared);
        CardBehaviorDefinition restored = EngineJson.Deserialize<CardBehaviorDefinition>(json);
        Assert.Contains("firstProgramInstallCostModifiers", json, StringComparison.Ordinal);
        Assert.Equal(declared.FirstProgramInstallCostModifiers, restored.FirstProgramInstallCostModifiers);

        string legacyJson = EngineJson.Serialize(plain);
        string emptyJson = EngineJson.Serialize(new CardBehaviorDefinition(
            SourceDefinition,
            PlayerSide.Runner,
            firstProgramInstallCostModifiers: []));
        Assert.Equal(legacyJson, emptyJson);
        Assert.DoesNotContain("firstProgramInstallCostModifiers", legacyJson, StringComparison.Ordinal);

        BehaviorCatalog baseline = new BehaviorCatalogBuilder("test:first-program-fingerprint")
            .Add(plain)
            .Build();
        BehaviorCatalog changed = new BehaviorCatalogBuilder("test:first-program-fingerprint")
            .Add(declared)
            .Build();
        Assert.NotEqual(baseline.Fingerprint, changed.Fingerprint);
    }

    [Fact]
    public void FirstProgramQuoteIsAppliedAndConsumedByDirectInstall()
    {
        (GameState state, BehaviorCatalog behaviorCatalog, RuntimeCardCatalog runtimeCatalog) = CreateFixture();

        CostQuote quote = Assert.IsType<CostQuote>(GameEngine.ProjectCostQuote(
            state,
            behaviorCatalog,
            ProgramCard,
            BehaviorCostModifierKind.RunnerInstallCreditCost,
            baseCost: 5,
            runtimeCardCatalog: runtimeCatalog));
        Assert.Equal(5, quote.BaseCost);
        Assert.Equal(4, quote.EffectiveCost);
        Assert.Equal(SourceDefinition, quote.Adjustments.Single().SourceDefinitionId);
        Assert.Equal(SourceCard, quote.Adjustments.Single().SourceCardId);

        GameState actionState = GameEngine.BeginResolution(
            state,
            new TurnFrame(PlayerSide.Runner, 1, TurnFrameStage.AwaitingAction),
            behaviorCatalog,
            runtimeCatalog);
        PendingDecision decision = Assert.IsType<PendingDecision>(actionState.PendingDecision);
        InstallCardParameters install = Assert.IsType<InstallCardParameters>(
            Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.InstallCard).Parameters);
        Assert.Equal(4, install.CreditCost);
        Assert.Equal(quote, install.CostQuote);

        ApplyResult result = GameEngine.Apply(
            actionState,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, decision.LegalActions.Single(action => action.Kind == ActionKind.InstallCard).Id),
            behaviorCatalog,
            runtimeCatalog);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(1, result.State.GetPlayer(PlayerSide.Runner).Credits);
        Assert.Equal(ZoneKind.RunnerRig, result.State.GetZone(result.State.GetCard(ProgramCard).ZoneId).Kind);
        Assert.Equal([ProgramCard], result.State.Turn.ThisTurnFacts!.ProgramInstallOccurrenceIds);
        Assert.Equal(1, result.State.Turn.ThisTurnFacts.GetFirstXCount(SourceCard, "first-program-test"));
        Assert.Empty(StateInvariantValidator.Collect(result.State, behaviorCatalog, runtimeCatalog));
    }

    [Fact]
    public void MultipleActiveSourcesStackInDeclarationOrderAndClampAtZero()
    {
        (GameState state, BehaviorCatalog behaviorCatalog, RuntimeCardCatalog runtimeCatalog) = CreateFixture(
            includeSecondSource: true,
            firstSourceDelta: -4,
            secondSourceDelta: -3);

        CostQuote quote = Assert.IsType<CostQuote>(GameEngine.ProjectCostQuote(
            state,
            behaviorCatalog,
            ProgramCard,
            BehaviorCostModifierKind.RunnerInstallCreditCost,
            5,
            runtimeCardCatalog: runtimeCatalog));

        Assert.Equal(0, quote.EffectiveCost);
        Assert.Equal(
            [SourceCard, SecondSourceCard],
            quote.Adjustments.Select(adjustment => adjustment.SourceCardId).ToArray());
        Assert.Equal([-4, -3], quote.Adjustments.Select(adjustment => adjustment.Delta).ToArray());

        ApplyResult installed = InstallDirect(state, behaviorCatalog, runtimeCatalog);
        Assert.True(installed.Accepted, installed.Rejection?.Message);
        Assert.Equal(5, installed.State.GetPlayer(PlayerSide.Runner).Credits);
        Assert.Equal([ProgramCard], installed.State.Turn.ThisTurnFacts!.ProgramInstallOccurrenceIds);
        Assert.Equal(1, installed.State.Turn.ThisTurnFacts.GetFirstXCount(SourceCard, "first-program-test"));
        Assert.Equal(1, installed.State.Turn.ThisTurnFacts.GetFirstXCount(SecondSourceCard, "second-first-program-test"));
    }

    [Fact]
    public void NonProgramInstallDoesNotConsumeFirstProgramSources()
    {
        (GameState state, BehaviorCatalog behaviorCatalog, RuntimeCardCatalog runtimeCardCatalog) = CreateFixture(
            targetClass: RuntimeCardClass.Resource);

        GameState actionState = GameEngine.BeginResolution(
            state,
            new TurnFrame(PlayerSide.Runner, 1, TurnFrameStage.AwaitingAction),
            behaviorCatalog,
            runtimeCardCatalog);
        PendingDecision decision = Assert.IsType<PendingDecision>(actionState.PendingDecision);
        LegalAction installAction = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.InstallCard);
        Assert.Null(Assert.IsType<InstallCardParameters>(installAction.Parameters).CostQuote);

        ApplyResult result = GameEngine.Apply(
            actionState,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, installAction.Id),
            behaviorCatalog,
            runtimeCardCatalog);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Empty(result.State.Turn.ThisTurnFacts!.ProgramInstallOccurrenceIds);
        Assert.Equal(0, result.State.Turn.ThisTurnFacts.GetFirstXCount(SourceCard, "first-program-test"));
    }

    [Fact]
    public void NoSourceStillRecordsOccurrenceAndLateSourceCannotActivate()
    {
        // The compiled declaration remains in the catalog even when this
        // fixture omits the physical source card. This isolates a late source
        // entering the Rig from a missing behavior definition.
        (GameState state, BehaviorCatalog behaviorCatalog, RuntimeCardCatalog runtimeCardCatalog) = CreateFixture(
            includeSourceCard: false);
        ApplyResult installed = InstallDirect(state, behaviorCatalog, runtimeCardCatalog);
        Assert.True(installed.Accepted, installed.Rejection?.Message);
        Assert.Equal([ProgramCard], installed.State.Turn.ThisTurnFacts!.ProgramInstallOccurrenceIds);

        GameState lateSource = AddCard(
            installed.State,
            SecondSourceCard,
            SourceDefinition,
            ZoneKind.RunnerRig,
            faceUp: true,
            runtimeCardCatalog);
        GameState withLateTarget = AddCard(
            lateSource,
            new CardInstanceId("card:first-program-late-target"),
            ProgramDefinition,
            ZoneKind.RunnerGrip,
            faceUp: false,
            runtimeCardCatalog);

        Assert.Null(GameEngine.ProjectCostQuote(
            withLateTarget,
            behaviorCatalog,
            new CardInstanceId("card:first-program-late-target"),
            BehaviorCostModifierKind.RunnerInstallCreditCost,
            5,
            runtimeCardCatalog));
    }

    [Fact]
    public void InactiveOrMovedSourcesAreExcludedFromProjection()
    {
        (GameState inactive, BehaviorCatalog behaviorCatalog, RuntimeCardCatalog runtimeCatalog) = CreateFixture(
            sourceFaceUp: false);
        Assert.Null(GameEngine.ProjectCostQuote(
            inactive,
            behaviorCatalog,
            ProgramCard,
            BehaviorCostModifierKind.RunnerInstallCreditCost,
            5,
            runtimeCatalog));

        GameState moved = MoveCard(inactive, SourceCard, ZoneKind.RunnerHeap);
        Assert.Null(GameEngine.ProjectCostQuote(
            moved,
            behaviorCatalog,
            ProgramCard,
            BehaviorCostModifierKind.RunnerInstallCreditCost,
            5,
            runtimeCatalog));
    }

    [Fact]
    public void FormalCorpTurnFactsCanProjectAnOffTurnRunnerProgramInstall()
    {
        (GameState state, BehaviorCatalog behaviorCatalog, RuntimeCardCatalog runtimeCatalog) = CreateFixture(
            activeSide: PlayerSide.Corp,
            includeOffTurnTrigger: true);

        PlayerId corp = state.GetPlayer(PlayerSide.Corp).Id;
        GameState prompt = GameEngine.BeginGameplayOccurrenceForTesting(
            state,
            new GameplayOccurrenceEffect(OccurrenceKind.CardAccessed, corp, SourceCard),
            behaviorCatalog,
            runtimeCatalog);
        PendingDecision search = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        LegalAction selectedTarget = Assert.Single(
            search.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters choice
                && choice.CardIds.SequenceEqual([ProgramCard]));
        ApplyResult selected = GameEngine.Apply(
            prompt,
            new ActionSubmission(search.Owner, search.Id, search.Revision, selectedTarget.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(selected.Accepted, selected.Rejection?.Message);
        PendingDecision mode = Assert.IsType<PendingDecision>(selected.State.PendingDecision);
        LegalAction install = Assert.Single(
            mode.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters choice
                && choice.OptionKeys.SequenceEqual(["install"]));
        ApplyResult result = GameEngine.Apply(
            selected.State,
            new ActionSubmission(mode.Owner, mode.Id, mode.Revision, install.Id),
            behaviorCatalog,
            runtimeCatalog);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(1, result.State.GetPlayer(PlayerSide.Runner).Credits);
        Assert.Equal(ZoneKind.RunnerRig, result.State.GetZone(result.State.GetCard(ProgramCard).ZoneId).Kind);
        Assert.Equal([ProgramCard], result.State.Turn.History!.CorpThisTurn!.ProgramInstallOccurrenceIds);
        Assert.Equal(1, result.State.Turn.History.CorpThisTurn.GetFirstXCount(SourceCard, "first-program-test"));
    }

    [Fact]
    public void StartingNextFormalRunnerTurnResetsFirstProgramWindow()
    {
        (GameState state, BehaviorCatalog behaviorCatalog, RuntimeCardCatalog runtimeCatalog) = CreateFixture();
        TurnHistoryState history = state.Turn.History!;
        TurnHistoryState nextHistory = history
            .ConsumeFirstX(PlayerSide.Runner, SourceCard, "first-program-test")
            .Start(PlayerSide.Runner, 2, 4)
            .MarkTurnBegan(PlayerSide.Runner);
        GameState nextTurn = ReplaceTurn(
            state,
            new TurnState(
                PlayerSide.Runner,
                TurnPhase.Runner,
                2,
                TurnStep.RunnerAction,
                nextHistory));

        CostQuote quote = Assert.IsType<CostQuote>(GameEngine.ProjectCostQuote(
            nextTurn,
            behaviorCatalog,
            ProgramCard,
            BehaviorCostModifierKind.RunnerInstallCreditCost,
            5,
            runtimeCatalog));
        Assert.Equal(4, quote.EffectiveCost);
        Assert.Equal(0, nextTurn.Turn.History!.GetFirstXCount(
            PlayerSide.Runner,
            SourceCard,
            "first-program-test"));
        Assert.Equal(1, nextTurn.Turn.History.RunnerLastTurn!.GetFirstXCount(
            SourceCard,
            "first-program-test"));
    }

    [Fact]
    public void PendingInstallQuoteRoundTripsWithStableStateHashAndObservation()
    {
        (GameState state, BehaviorCatalog behaviorCatalog, RuntimeCardCatalog runtimeCatalog) = CreateFixture();
        GameState actionState = GameEngine.BeginResolution(
            state,
            new TurnFrame(PlayerSide.Runner, 1, TurnFrameStage.AwaitingAction),
            behaviorCatalog,
            runtimeCatalog);
        string json = EngineJson.Serialize(actionState);
        GameState restored = EngineJson.DeserializeState(json, behaviorCatalog, runtimeCatalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(actionState), DeterministicHash.ForState(restored));

        Observation observation = ObservationBuilder.Build(actionState, PlayerSide.Runner, behaviorCatalog);
        InstallCardParameters install = Assert.IsType<InstallCardParameters>(
            Assert.Single(observation.LegalActions, action => action.Kind == ActionKind.InstallCard).Parameters);
        Assert.Equal(4, install.CostQuote!.EffectiveCost);
        Assert.Equal(SourceCard, install.CostQuote.Adjustments.Single().SourceCardId);
        Assert.Equal(0, ((Dictionary<string, object?>)((Dictionary<string, object?>)observation.PublicState["turnFacts"]!)["runner"]!)["thisTurn"] is Dictionary<string, object?> facts
            ? (int)facts["programInstallOccurrenceCount"]!
            : -1);
    }

    [Fact]
    public void ForgedQuoteAndStaleSourceStateRejectAtomically()
    {
        (GameState state, BehaviorCatalog behaviorCatalog, RuntimeCardCatalog runtimeCatalog) = CreateFixture();
        GameState actionState = GameEngine.BeginResolution(
            state,
            new TurnFrame(PlayerSide.Runner, 1, TurnFrameStage.AwaitingAction),
            behaviorCatalog,
            runtimeCatalog);
        PendingDecision decision = actionState.PendingDecision!;
        LegalAction issued = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.InstallCard);
        InstallCardParameters original = Assert.IsType<InstallCardParameters>(issued.Parameters);
        CostQuote forgedQuote = new(
            5,
            [new CostAdjustment(
                BehaviorCostModifierKind.RunnerInstallCreditCost,
                -1,
                "forged-first-program-term",
                SourceDefinition,
                SourceCard)]);
        InstallCardParameters forgedParameters = new(
            original.CardId,
            original.DestinationKind,
            original.ServerId,
            original.CreateNewRemote,
            forgedQuote.EffectiveCost,
            original.CandidateReplacementCardIds,
            original.RequiredReplacementCardIds,
            original.SelectedReplacementCardIds,
            original.FinalizeSelection,
            forgedQuote);
        PendingDecision forgedDecision = new(
            decision.Id,
            decision.Revision,
            decision.Owner,
            decision.Kind,
            decision.Prompt,
            [new LegalAction(issued.Id, issued.Kind, issued.Description, forgedParameters)]);
        GameState forgedState = ReplacePending(actionState, forgedDecision);
        string hash = DeterministicHash.ForState(forgedState);

        ApplyResult forgedResult = GameEngine.Apply(
            forgedState,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, issued.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.False(forgedResult.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, forgedResult.Rejection?.Code);
        Assert.Equal(hash, DeterministicHash.ForState(forgedResult.State));

        GameState movedSource = MoveCard(actionState, SourceCard, ZoneKind.RunnerHeap);
        ApplyResult staleResult = GameEngine.Apply(
            movedSource,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, issued.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.False(staleResult.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, staleResult.Rejection?.Code);
        Assert.Equal(DeterministicHash.ForState(movedSource), DeterministicHash.ForState(staleResult.State));
    }

    [Fact]
    public void ProgramInstallOccurrencesRequireRunnerProgramsWithRuntimeCatalog()
    {
        (GameState nonProgramState, BehaviorCatalog behaviorCatalog, RuntimeCardCatalog runtimeCatalog) =
            CreateFixture(targetClass: RuntimeCardClass.Resource);
        GameState forgedNonProgram = WithProgramOccurrences(nonProgramState, ProgramCard);
        Assert.Contains(
            StateInvariantValidator.Collect(forgedNonProgram, behaviorCatalog, runtimeCatalog),
            violation => violation.Code == "turn.program-install-class-invalid");

        GameState state = AddCard(
            nonProgramState,
            new CardInstanceId("card:first-program-corp-occurrence"),
            SourceDefinition,
            ZoneKind.CorpDiscard,
            faceUp: true,
            runtimeCatalog,
            ownerSide: PlayerSide.Corp);
        GameState forgedNonRunner = WithProgramOccurrences(
            state,
            new CardInstanceId("card:first-program-corp-occurrence"));
        Assert.Contains(
            StateInvariantValidator.Collect(forgedNonRunner, behaviorCatalog, runtimeCatalog),
            violation => violation.Code == "turn.program-install-owner-mismatch");
    }

    [Fact]
    public void MultiStepProgramInstallCarriesQuoteAndConsumesSourcesAtCommit()
    {
        (GameState state, BehaviorCatalog behaviorCatalog, RuntimeCardCatalog runtimeCatalog) = CreateFixture(
            includeExistingProgram: true,
            runnerCredits: 4);
        GameState actionState = GameEngine.BeginResolution(
            state,
            new TurnFrame(PlayerSide.Runner, 1, TurnFrameStage.AwaitingAction),
            behaviorCatalog,
            runtimeCatalog);
        PendingDecision turnDecision = actionState.PendingDecision!;
        LegalAction open = Assert.Single(turnDecision.LegalActions, action => action.Kind == ActionKind.InstallCard);
        InstallCardParameters initial = Assert.IsType<InstallCardParameters>(open.Parameters);
        Assert.Equal(4, initial.CreditCost);
        Assert.NotNull(initial.CostQuote);

        ApplyResult opened = GameEngine.Apply(
            actionState,
            new ActionSubmission(turnDecision.Owner, turnDecision.Id, turnDecision.Revision, open.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        PendingDecision choice = opened.State.PendingDecision!;
        LegalAction select = Assert.Single(choice.LegalActions, action =>
            !Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);
        InstallCardParameters selected = Assert.IsType<InstallCardParameters>(select.Parameters);
        Assert.Equal(4, selected.CreditCost);
        Assert.Equal(SourceCard, selected.CostQuote!.Adjustments.Single().SourceCardId);

        ApplyResult selectedResult = GameEngine.Apply(
            opened.State,
            new ActionSubmission(choice.Owner, choice.Id, choice.Revision, select.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(selectedResult.Accepted, selectedResult.Rejection?.Message);
        PendingDecision finalDecision = selectedResult.State.PendingDecision!;
        LegalAction finalize = Assert.Single(finalDecision.LegalActions, action =>
            Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);
        InstallCardParameters finalParameters = Assert.IsType<InstallCardParameters>(finalize.Parameters);
        Assert.Equal(selected.CostQuote, finalParameters.CostQuote);

        ApplyResult result = GameEngine.Apply(
            selectedResult.State,
            new ActionSubmission(finalDecision.Owner, finalDecision.Id, finalDecision.Revision, finalize.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal([ProgramCard], result.State.Turn.ThisTurnFacts!.ProgramInstallOccurrenceIds);
        Assert.Equal(1, result.State.Turn.ThisTurnFacts.GetFirstXCount(SourceCard, "first-program-test"));
        Assert.Equal(0, result.State.GetPlayer(PlayerSide.Runner).Credits);
    }

    private static (GameState State, BehaviorCatalog BehaviorCatalog, RuntimeCardCatalog RuntimeCatalog) CreateFixture(
        bool includeSourceCard = true,
        bool includeSecondSource = false,
        bool sourceFaceUp = true,
        int firstSourceDelta = -1,
        int secondSourceDelta = -2,
        RuntimeCardClass targetClass = RuntimeCardClass.Program,
        bool includeExistingProgram = false,
        PlayerSide activeSide = PlayerSide.Runner,
        int runnerCredits = 5,
        bool includeOffTurnTrigger = false)
    {
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:first-program")
            .Add(new CardRuntimeSpec(SourceDefinition, PlayerSide.Runner, RuntimeCardClass.Hardware, installCost: 0))
            .Add(new CardRuntimeSpec(SecondSourceDefinition, PlayerSide.Runner, RuntimeCardClass.Hardware, installCost: 0))
            .Add(new CardRuntimeSpec(ProgramDefinition, PlayerSide.Runner, targetClass, installCost: 5, memoryUnits: targetClass == RuntimeCardClass.Program ? 1 : 0))
            .Add(new CardRuntimeSpec(ExistingProgramDefinition, PlayerSide.Runner, RuntimeCardClass.Program, installCost: 1, memoryUnits: 4))
            .Build();
        var catalogBuilder = new BehaviorCatalogBuilder("test:first-program-catalog");
        BehaviorCardFilter programFilter = new(BehaviorCardType.Program, ownerSide: PlayerSide.Runner);
        BehaviorChoiceSpec offTurnInstallChoice = new(
            "off-turn-install-choice",
            BehaviorChooser.Controller,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            [
                new BehaviorChoiceOption(
                    "install",
                    [new BehaviorInstruction(
                        BehaviorInstructionKind.InstallCard,
                        target: new BehaviorTarget(BehaviorTargetKind.ChosenCard),
                        sourceZone: BehaviorZone.Stack,
                        destinationZone: BehaviorZone.Rig)]),
                new BehaviorChoiceOption("skip", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
            ]);
        BehaviorTrigger offTurnInstallTrigger = new(
            "off-turn-install",
            OccurrenceKind.CardAccessed,
            TriggerMode.Mandatory,
            BehaviorTriggerScope.Any,
            subjectCardFilter: null,
            instructions:
            [
                new BehaviorInstruction(
                    BehaviorInstructionKind.Search,
                    sourceZone: BehaviorZone.Stack,
                    filter: programFilter,
                    target: BehaviorTarget.CardInZone(BehaviorZone.Stack, programFilter)),
                new BehaviorInstruction(
                    BehaviorInstructionKind.ShuffleIntoDeck,
                    target: new BehaviorTarget(BehaviorTargetKind.ChosenCard),
                    sourceZone: BehaviorZone.Stack,
                    destinationZone: BehaviorZone.Stack),
                new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: offTurnInstallChoice),
            ]);
        catalogBuilder.Add(new CardBehaviorDefinition(
            SourceDefinition,
            PlayerSide.Runner,
            firstProgramInstallCostModifiers:
            [new BehaviorFirstProgramInstallCostModifier(firstSourceDelta, "first-program-test")],
            triggers: includeOffTurnTrigger ? [offTurnInstallTrigger] : null));
        if (includeSecondSource)
        {
            catalogBuilder.Add(new CardBehaviorDefinition(
                SecondSourceDefinition,
                PlayerSide.Runner,
                firstProgramInstallCostModifiers:
                [new BehaviorFirstProgramInstallCostModifier(secondSourceDelta, "second-first-program-test")]));
        }
        BehaviorCatalog behaviorCatalog = catalogBuilder.Build();
        GameState baseline = GameState.CreateNew(
            seed: 418,
            behaviorRegistryVersion: behaviorCatalog.BehaviorRegistryVersion,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);
        ZoneState rig = baseline.GetZone(ZoneKind.RunnerRig);
        ZoneState grip = baseline.GetZone(ZoneKind.RunnerGrip);
        ZoneState stack = baseline.GetZone(ZoneKind.RunnerStack);
        var cards = new List<CardInstanceState>();
        if (includeSourceCard)
        {
            cards.Add(new CardInstanceState(SourceCard, SourceDefinition, rig.Id, PlayerSide.Runner, faceUp: sourceFaceUp));
        }
        if (includeSecondSource)
        {
            cards.Add(new CardInstanceState(SecondSourceCard, SecondSourceDefinition, rig.Id, PlayerSide.Runner, faceUp: true));
        }
        ZoneState programSource = includeOffTurnTrigger ? stack : grip;
        CardInstanceState program = new(ProgramCard, ProgramDefinition, programSource.Id, PlayerSide.Runner);
        cards.Add(program);
        if (includeExistingProgram)
        {
            cards.Add(new CardInstanceState(ExistingProgramCard, ExistingProgramDefinition, rig.Id, PlayerSide.Runner, faceUp: true));
        }
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == rig.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, cards
                    .Where(card => card.ZoneId == rig.Id)
                    .Select(card => card.Id)
                    .ToArray())
                : zone.Id == grip.Id && !includeOffTurnTrigger
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [ProgramCard])
                    : zone.Id == stack.Id && includeOffTurnTrigger
                        ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [ProgramCard])
                    : zone)
            .ToArray();
        PlayerState[] players = baseline.Players
            .Select(player => player.Side == PlayerSide.Runner
                ? new PlayerState(player.Id, player.Side, credits: runnerCredits, clicks: 4, player.AgendaPoints)
                : player)
            .ToArray();
        PlayerSide factSide = activeSide;
        TurnFactsState facts = new(
            1,
            factSide,
            1,
            factSide == PlayerSide.Corp ? 3 : 4,
            0,
            true,
            factSide == PlayerSide.Corp,
            false,
            false);
        TurnHistoryState history = factSide == PlayerSide.Corp
            ? new TurnHistoryState(1, facts, null, null, null)
            : new TurnHistoryState(1, null, null, facts, null);
        GameState state = new(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            players,
            cards,
            zones,
            baseline.Servers,
            new TurnState(
                factSide,
                factSide == PlayerSide.Corp ? TurnPhase.Corp : TurnPhase.Runner,
                1,
                factSide == PlayerSide.Corp ? TurnStep.CorpAction : TurnStep.RunnerAction,
                history),
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            behaviorRegistryVersion: behaviorCatalog.BehaviorRegistryVersion,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);
        StateInvariantValidator.Validate(state, behaviorCatalog, runtimeCatalog);
        return (state, behaviorCatalog, runtimeCatalog);
    }

    private static ApplyResult InstallDirect(
        GameState state,
        BehaviorCatalog behaviorCatalog,
        RuntimeCardCatalog runtimeCardCatalog)
    {
        GameState actionState = GameEngine.BeginResolution(
            state,
            new TurnFrame(PlayerSide.Runner, state.Turn.TurnNumber, TurnFrameStage.AwaitingAction),
            behaviorCatalog,
            runtimeCardCatalog);
        PendingDecision decision = actionState.PendingDecision!;
        LegalAction install = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.InstallCard);
        return GameEngine.Apply(
            actionState,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, install.Id),
            behaviorCatalog,
            runtimeCardCatalog);
    }

    private static GameState AddCard(
        GameState source,
        CardInstanceId cardId,
        CardDefinitionId definitionId,
        ZoneKind destinationKind,
        bool faceUp,
        RuntimeCardCatalog runtimeCardCatalog,
        PlayerSide ownerSide = PlayerSide.Runner)
    {
        ZoneState destination = source.GetZone(destinationKind);
        CardInstanceState card = new(cardId, definitionId, destination.Id, ownerSide, faceUp: faceUp);
        GameState next = new(
            source.SchemaVersion,
            source.GameId,
            source.RulesVersion,
            source.CardDataVersion,
            source.Players,
            source.Cards.Append(card).ToArray(),
            source.Zones.Select(zone => zone.Id == destination.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(cardId).ToArray())
                : zone).ToArray(),
            source.Servers,
            source.Turn,
            source.Outcome,
            source.RandomState,
            source.Allocator,
            source.DecisionRevision,
            source.EventSequence,
            source.ResolutionStack,
            source.PendingDecision,
            source.ActionSequence,
            source.InitialSeed,
            source.RunState,
            source.RulesState,
            source.BehaviorRegistryVersion,
            source.BehaviorCatalogFingerprint,
            runtimeCardCatalog.Version,
            runtimeCardCatalog.Fingerprint);
        StateInvariantValidator.Validate(next);
        return next;
    }

    private static GameState WithProgramOccurrences(GameState source, params CardInstanceId[] occurrenceIds)
    {
        TurnHistoryState history = source.Turn.History
            ?? throw new InvalidOperationException("The occurrence fixture requires formal turn facts.");
        TurnFactsState current = history.ThisTurn(source.Turn.ActiveSide)
            ?? throw new InvalidOperationException("The occurrence fixture requires current-turn facts.");
        TurnFactsState forged = new(
            current.Version,
            current.Side,
            current.TurnNumber,
            current.ClicksAllotted,
            current.ClicksLost,
            current.TurnBegan,
            current.MandatoryDrawPerformed,
            current.ActionPhaseEnded,
            current.DiscardPhaseEnded,
            installedCardIds: current.InstalledCardIds,
            successfulRun: current.SuccessfulRun,
            usedAbilityKeys: current.UsedAbilityKeys,
            attemptedRunServerIds: current.AttemptedRunServerIds,
            successfulRunServerIds: current.SuccessfulRunServerIds,
            accessedCardOccurrenceIds: current.AccessedCardOccurrenceIds,
            trashedCardOccurrenceIds: current.TrashedCardOccurrenceIds,
            agendaTurnChanges: current.AgendaTurnChanges,
            tagTurnChanges: current.TagTurnChanges,
            firstXUseKeys: current.FirstXUseKeys,
            programInstallOccurrenceIds: occurrenceIds);
        TurnHistoryState forgedHistory = source.Turn.ActiveSide == PlayerSide.Corp
            ? new(history.Version, forged, history.CorpLastTurn, history.RunnerThisTurn, history.RunnerLastTurn)
            : new(history.Version, history.CorpThisTurn, history.CorpLastTurn, forged, history.RunnerLastTurn);
        return ReplaceTurn(
            source,
            new TurnState(
                source.Turn.ActiveSide,
                source.Turn.Phase,
                source.Turn.TurnNumber,
                source.Turn.Step,
                forgedHistory));
    }

    private static GameState MoveCard(GameState source, CardInstanceId cardId, ZoneKind destinationKind)
    {
        CardInstanceState card = source.GetCard(cardId);
        ZoneState sourceZone = source.GetZone(card.ZoneId);
        ZoneState destination = source.GetZone(destinationKind);
        CardInstanceState moved = new(
            card.Id,
            card.DefinitionId,
            destination.Id,
            card.OwnerSide,
            faceUp: destinationKind == ZoneKind.RunnerRig && card.FaceUp,
            rezzed: card.Rezzed,
            advancementCounters: card.AdvancementCounters);
        GameState next = new(
            source.SchemaVersion,
            source.GameId,
            source.RulesVersion,
            source.CardDataVersion,
            source.Players,
            source.Cards.Select(candidate => candidate.Id == cardId ? moved : candidate).ToArray(),
            source.Zones.Select(zone => zone.Id == sourceZone.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Where(id => id != cardId).ToArray())
                    : zone.Id == destination.Id
                        ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(cardId).ToArray())
                        : zone)
                .ToArray(),
            source.Servers,
            source.Turn,
            source.Outcome,
            source.RandomState,
            source.Allocator,
            source.DecisionRevision,
            source.EventSequence,
            source.ResolutionStack,
            source.PendingDecision,
            source.ActionSequence,
            source.InitialSeed,
            source.RunState,
            source.RulesState,
            source.BehaviorRegistryVersion,
            source.BehaviorCatalogFingerprint,
            source.RuntimeCardCatalogVersion,
            source.RuntimeCardCatalogFingerprint);
        return next;
    }

    private static GameState ReplaceTurn(GameState source, TurnState turn) => new(
        source.SchemaVersion,
        source.GameId,
        source.RulesVersion,
        source.CardDataVersion,
        source.Players,
        source.Cards,
        source.Zones,
        source.Servers,
        turn,
        source.Outcome,
        source.RandomState,
        source.Allocator,
        source.DecisionRevision,
        source.EventSequence,
        source.ResolutionStack,
        source.PendingDecision,
        source.ActionSequence,
        source.InitialSeed,
        source.RunState,
        source.RulesState,
        source.BehaviorRegistryVersion,
        source.BehaviorCatalogFingerprint,
        source.RuntimeCardCatalogVersion,
        source.RuntimeCardCatalogFingerprint);

    private static GameState ReplacePending(GameState source, PendingDecision pending) => new(
        source.SchemaVersion,
        source.GameId,
        source.RulesVersion,
        source.CardDataVersion,
        source.Players,
        source.Cards,
        source.Zones,
        source.Servers,
        source.Turn,
        source.Outcome,
        source.RandomState,
        source.Allocator,
        source.DecisionRevision,
        source.EventSequence,
        source.ResolutionStack,
        pending,
        source.ActionSequence,
        source.InitialSeed,
        source.RunState,
        source.RulesState,
        source.BehaviorRegistryVersion,
        source.BehaviorCatalogFingerprint,
        source.RuntimeCardCatalogVersion,
        source.RuntimeCardCatalogFingerprint);
}

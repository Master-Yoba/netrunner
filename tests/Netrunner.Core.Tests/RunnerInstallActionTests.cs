using Xunit;

namespace Netrunner.Core.Tests;

public sealed class RunnerInstallActionTests
{
    [Fact]
    public void RunnerHardwareInstallChargesOneClickAndPrintedCostThenReturnsToPriority()
    {
        CardDefinitionId definitionId = new("test:runner-hardware");
        CardInstanceId cardId = new("card:runner-hardware");
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:runner-install")
            .Add(new CardRuntimeSpec(
                definitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Hardware,
                installCost: 2))
            .Build();
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder(EngineCompatibility.BehaviorRegistryVersion).Build();
        GameState state = CreateState(cardId, definitionId, runnerCredits: 5, runtimeCatalog);

        state = OpenRunnerAction(state, behaviorCatalog, runtimeCatalog);
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction action = Assert.Single(decision.LegalActions, candidate => candidate.Kind == ActionKind.InstallCard);
        InstallCardParameters parameters = Assert.IsType<InstallCardParameters>(action.Parameters);
        Assert.Equal(cardId, parameters.CardId);
        Assert.Equal(InstallDestinationKind.RunnerRig, parameters.DestinationKind);
        Assert.Equal(2, parameters.CreditCost);
        Assert.True(parameters.FinalizeSelection);

        string serialized = EngineJson.Serialize(state);
        state = EngineJson.DeserializeState(serialized, behaviorCatalog, runtimeCatalog);
        Assert.Equal(serialized, EngineJson.Serialize(state));
        decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        action = Assert.Single(decision.LegalActions, candidate => candidate.Kind == ActionKind.InstallCard);

        ApplyResult result = GameEngine.Apply(
            state,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id),
            behaviorCatalog,
            runtimeCatalog);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.NotNull(result.State.PendingDecision);
        Assert.Equal(DecisionKind.TurnTiming, result.State.PendingDecision!.Kind);
        Assert.Equal(TurnFrameStage.ActionPriority, Assert.IsType<TurnFrame>(result.State.ResolutionStack[^1]).Stage);
        Assert.Equal(3, result.State.GetPlayer(decision.Owner).Clicks);
        Assert.Equal(3, result.State.GetPlayer(decision.Owner).Credits);
        CardInstanceState installed = result.State.GetCard(cardId);
        Assert.Equal(ZoneKind.RunnerRig, result.State.GetZone(installed.ZoneId).Kind);
        Assert.True(installed.FaceUp);
        Assert.Contains(cardId, result.State.Turn.ThisTurnFacts!.InstalledCardIds);
        Assert.Single(result.Events, @event => @event.Kind == EngineEventKinds.ClickSpent);
        CardInstalledEventPayload installedEvent = Assert.IsType<CardInstalledEventPayload>(
            result.Events.Single(@event => @event.Kind == EngineEventKinds.CardInstalled).Payload);
        Assert.Equal(2, installedEvent.Cost);
    }

    [Fact]
    public void RunnerProgramInstallRegistersRuntimeMemoryAndReturnsToPriority()
    {
        CardDefinitionId definitionId = new("test:runner-program");
        CardInstanceId cardId = new("card:runner-program");
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:runner-program-install")
            .Add(new CardRuntimeSpec(
                definitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 2,
                memoryUnits: 3))
            .Build();
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder(EngineCompatibility.BehaviorRegistryVersion).Build();
        GameState state = CreateState(
            cardId,
            definitionId,
            runnerCredits: 5,
            runtimeCatalog,
            initializeRulesState: true);

        state = OpenRunnerAction(state, behaviorCatalog, runtimeCatalog);
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction action = Assert.Single(decision.LegalActions, candidate => candidate.Kind == ActionKind.InstallCard);
        InstallCardParameters parameters = Assert.IsType<InstallCardParameters>(action.Parameters);
        Assert.Equal(cardId, parameters.CardId);
        Assert.Equal(InstallDestinationKind.RunnerRig, parameters.DestinationKind);
        Assert.Equal(2, parameters.CreditCost);
        Assert.Empty(parameters.CandidateReplacementCardIds);
        Assert.True(parameters.FinalizeSelection);

        ApplyResult result = GameEngine.Apply(
            state,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id),
            behaviorCatalog,
            runtimeCatalog);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(3, result.State.GetPlayer(decision.Owner).Clicks);
        Assert.Equal(3, result.State.GetPlayer(decision.Owner).Credits);
        CardInstanceState installed = result.State.GetCard(cardId);
        Assert.Equal(ZoneKind.RunnerRig, result.State.GetZone(installed.ZoneId).Kind);
        Assert.True(installed.FaceUp);
        PlayerRulesState runnerRules = Assert.Single(
            result.State.RulesState!.Players,
            rules => rules.PlayerId == decision.Owner);
        Assert.Equal(4, runnerRules.BaseMemory);
        Assert.Equal(3, runnerRules.UsedMemory);
        ProgramMemoryState registration = Assert.Single(result.State.RulesState.Programs);
        Assert.Equal(cardId, registration.CardId);
        Assert.Equal(decision.Owner, registration.Owner);
        Assert.Equal(3, registration.MemoryCost);
        Assert.Equal(1, result.Events.Count(@event => @event.Kind == EngineEventKinds.ClickSpent));
        MemoryAdjustedEventPayload memoryEvent = Assert.IsType<MemoryAdjustedEventPayload>(
            result.Events.Single(@event => @event.Kind == EngineEventKinds.MemoryAdjusted).Payload);
        Assert.Equal(3, memoryEvent.UsedMemory);
        Assert.Equal(DecisionKind.TurnTiming, result.State.PendingDecision!.Kind);
        Assert.Equal(TurnFrameStage.ActionPriority, Assert.IsType<TurnFrame>(result.State.ResolutionStack[^1]).Stage);
    }

    [Fact]
    public void RunnerUniqueProgramInstallOffersAndRequiresActiveConflictReplacement()
    {
        CardDefinitionId incomingDefinitionId = new("test:runner-unique-incoming");
        CardInstanceId incomingCardId = new("card:runner-unique-incoming");
        CardDefinitionId existingDefinitionId = new("test:runner-unique-existing");
        CardInstanceId existingCardId = new("card:runner-unique-existing");
        const string uniquenessKey = "runner-unique-program";
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:runner-unique-program")
            .Add(new CardRuntimeSpec(
                incomingDefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 2,
                memoryUnits: 1,
                unique: true,
                uniquenessKey: uniquenessKey))
            .Add(new CardRuntimeSpec(
                existingDefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 1,
                memoryUnits: 1,
                unique: true,
                uniquenessKey: uniquenessKey))
            .Build();
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder(EngineCompatibility.BehaviorRegistryVersion).Build();
        GameState state = CreateStateWithPreExistingPrograms(
            incomingCardId,
            incomingDefinitionId,
            runnerCredits: 5,
            runtimeCatalog,
            initializeRulesState: true,
            (existingCardId, existingDefinitionId));

        state = OpenRunnerAction(state, behaviorCatalog, runtimeCatalog);
        PendingDecision turnDecision = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction install = Assert.Single(turnDecision.LegalActions, action => action.Kind == ActionKind.InstallCard);
        InstallCardParameters issued = Assert.IsType<InstallCardParameters>(install.Parameters);
        Assert.Equal([existingCardId], issued.CandidateReplacementCardIds);
        Assert.Equal([existingCardId], issued.RequiredReplacementCardIds);
        Assert.False(issued.FinalizeSelection);
        int clicksBeforeSelection = state.GetPlayer(turnDecision.Owner).Clicks;
        int creditsBeforeSelection = state.GetPlayer(turnDecision.Owner).Credits;

        ApplyResult opened = GameEngine.Apply(
            state,
            new ActionSubmission(turnDecision.Owner, turnDecision.Id, turnDecision.Revision, install.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        Assert.Empty(opened.Events);
        Assert.Equal(clicksBeforeSelection, opened.State.GetPlayer(turnDecision.Owner).Clicks);
        Assert.Equal(creditsBeforeSelection, opened.State.GetPlayer(turnDecision.Owner).Credits);
        Assert.Empty(StateInvariantValidator.Collect(opened.State, behaviorCatalog, runtimeCatalog));

        string json = EngineJson.Serialize(opened.State);
        GameState restored = EngineJson.DeserializeState(json, behaviorCatalog, runtimeCatalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(opened.State), DeterministicHash.ForState(restored));
        PendingDecision choice = Assert.IsType<PendingDecision>(restored.PendingDecision);
        LegalAction select = Assert.Single(
            choice.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters)
                .SelectedReplacementCardIds.SequenceEqual([existingCardId]));
        ApplyResult selected = GameEngine.Apply(
            restored,
            new ActionSubmission(choice.Owner, choice.Id, choice.Revision, select.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(selected.Accepted, selected.Rejection?.Message);
        Assert.Equal(clicksBeforeSelection, selected.State.GetPlayer(turnDecision.Owner).Clicks);
        Assert.Equal(creditsBeforeSelection, selected.State.GetPlayer(turnDecision.Owner).Credits);
        PendingDecision finalDecision = Assert.IsType<PendingDecision>(selected.State.PendingDecision);
        LegalAction finalize = Assert.Single(
            finalDecision.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);
        InstallCardParameters finalParameters = Assert.IsType<InstallCardParameters>(finalize.Parameters);
        Assert.Equal([existingCardId], finalParameters.SelectedReplacementCardIds);
        Assert.Equal([existingCardId], finalParameters.RequiredReplacementCardIds);
        Assert.Equal(2, finalParameters.CreditCost);

        ApplyResult result = GameEngine.Apply(
            selected.State,
            new ActionSubmission(finalDecision.Owner, finalDecision.Id, finalDecision.Revision, finalize.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(ZoneKind.RunnerHeap, result.State.GetZone(result.State.GetCard(existingCardId).ZoneId).Kind);
        Assert.Equal(ZoneKind.RunnerRig, result.State.GetZone(result.State.GetCard(incomingCardId).ZoneId).Kind);
        Assert.True(result.State.GetCard(incomingCardId).FaceUp);
        Assert.Equal(creditsBeforeSelection - 2, result.State.GetPlayer(turnDecision.Owner).Credits);
        Assert.Equal(clicksBeforeSelection - 1, result.State.GetPlayer(turnDecision.Owner).Clicks);
        Assert.Equal([incomingCardId], result.State.Turn.ThisTurnFacts!.InstalledCardIds);
        Assert.Single(result.State.RulesState!.Programs, program => program.CardId == incomingCardId);
        Assert.DoesNotContain(result.State.RulesState.Programs, program => program.CardId == existingCardId);
        Assert.Equal(
            [
                EngineEventKinds.CardMoved,
                EngineEventKinds.CreditsAdjusted,
                EngineEventKinds.ClickSpent,
                EngineEventKinds.CardMoved,
                EngineEventKinds.MemoryAdjusted,
                EngineEventKinds.CardInstalled,
                EngineEventKinds.TurnTimingWindowOpened,
            ],
            result.Events.Select(value => value.Kind));
        CardInstalledEventPayload installed = Assert.IsType<CardInstalledEventPayload>(
            result.Events.Single(value => value.Kind == EngineEventKinds.CardInstalled).Payload);
        Assert.Equal(2, installed.Cost);
        Assert.Empty(StateInvariantValidator.Collect(result.State, behaviorCatalog, runtimeCatalog));
    }

    [Fact]
    public void RunnerConsoleHardwareInstallOffersAndRequiresEveryActiveConsoleReplacement()
    {
        CardDefinitionId incomingDefinitionId = new("test:runner-console-incoming");
        CardInstanceId incomingCardId = new("card:runner-console-incoming");
        CardDefinitionId firstDefinitionId = new("test:runner-console-first");
        CardInstanceId firstCardId = new("card:runner-console-first");
        CardDefinitionId secondDefinitionId = new("test:runner-console-second");
        CardInstanceId secondCardId = new("card:runner-console-second");
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:runner-console-hardware")
            .Add(new CardRuntimeSpec(
                incomingDefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Hardware,
                installCost: 2,
                console: true,
                consoleKey: "console-incoming",
                subtypes: ["console"]))
            .Add(new CardRuntimeSpec(
                firstDefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Hardware,
                installCost: 1,
                console: true,
                consoleKey: "console-first",
                subtypes: ["console"]))
            .Add(new CardRuntimeSpec(
                secondDefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Hardware,
                installCost: 1,
                console: true,
                consoleKey: "console-second",
                subtypes: ["console"]))
            .Build();
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder(EngineCompatibility.BehaviorRegistryVersion).Build();
        GameState state = CreateStateWithPreExistingRigCards(
            incomingCardId,
            incomingDefinitionId,
            runnerCredits: 5,
            runtimeCatalog,
            (firstCardId, firstDefinitionId),
            (secondCardId, secondDefinitionId));

        state = OpenRunnerAction(state, behaviorCatalog, runtimeCatalog);
        PendingDecision turnDecision = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction install = Assert.Single(turnDecision.LegalActions, action => action.Kind == ActionKind.InstallCard);
        InstallCardParameters issued = Assert.IsType<InstallCardParameters>(install.Parameters);
        Assert.Equal([firstCardId, secondCardId], issued.CandidateReplacementCardIds);
        Assert.Equal([firstCardId, secondCardId], issued.RequiredReplacementCardIds);
        Assert.False(issued.FinalizeSelection);

        ApplyResult opened = GameEngine.Apply(
            state,
            new ActionSubmission(turnDecision.Owner, turnDecision.Id, turnDecision.Revision, install.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        Assert.Empty(opened.Events);
        InstallFrame frame = Assert.IsType<InstallFrame>(opened.State.ResolutionStack[^1]);
        Assert.Equal([firstCardId, secondCardId], frame.CandidateReplacementCardIds);
        Assert.Equal([firstCardId, secondCardId], frame.RequiredReplacementCardIds);
        Assert.Empty(StateInvariantValidator.Collect(opened.State, behaviorCatalog, runtimeCatalog));

        PendingDecision choice = Assert.IsType<PendingDecision>(opened.State.PendingDecision);
        LegalAction selectFirst = Assert.Single(
            choice.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters)
                .SelectedReplacementCardIds.SequenceEqual([firstCardId]));
        ApplyResult selectedFirst = GameEngine.Apply(
            opened.State,
            new ActionSubmission(choice.Owner, choice.Id, choice.Revision, selectFirst.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(selectedFirst.Accepted, selectedFirst.Rejection?.Message);
        PendingDecision secondChoice = Assert.IsType<PendingDecision>(selectedFirst.State.PendingDecision);
        LegalAction selectSecond = Assert.Single(
            secondChoice.LegalActions,
            action => !Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection
                && Assert.IsType<InstallCardParameters>(action.Parameters)
                    .SelectedReplacementCardIds.SequenceEqual([firstCardId, secondCardId]));
        ApplyResult selected = GameEngine.Apply(
            selectedFirst.State,
            new ActionSubmission(secondChoice.Owner, secondChoice.Id, secondChoice.Revision, selectSecond.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(selected.Accepted, selected.Rejection?.Message);
        PendingDecision finalDecision = Assert.IsType<PendingDecision>(selected.State.PendingDecision);
        LegalAction finalize = Assert.Single(
            finalDecision.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);
        InstallCardParameters finalParameters = Assert.IsType<InstallCardParameters>(finalize.Parameters);
        Assert.Equal([firstCardId, secondCardId], finalParameters.SelectedReplacementCardIds);
        Assert.Equal(2, finalParameters.CreditCost);

        ApplyResult result = GameEngine.Apply(
            selected.State,
            new ActionSubmission(finalDecision.Owner, finalDecision.Id, finalDecision.Revision, finalize.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(ZoneKind.RunnerHeap, result.State.GetZone(result.State.GetCard(firstCardId).ZoneId).Kind);
        Assert.Equal(ZoneKind.RunnerHeap, result.State.GetZone(result.State.GetCard(secondCardId).ZoneId).Kind);
        Assert.Equal(ZoneKind.RunnerRig, result.State.GetZone(result.State.GetCard(incomingCardId).ZoneId).Kind);
        Assert.True(result.State.GetCard(incomingCardId).FaceUp);
        Assert.Equal(3, result.State.GetPlayer(turnDecision.Owner).Credits);
        Assert.Equal(3, result.State.GetPlayer(turnDecision.Owner).Clicks);
        Assert.Equal([incomingCardId], result.State.Turn.ThisTurnFacts!.InstalledCardIds);
        Assert.Equal(
            [
                EngineEventKinds.CardMoved,
                EngineEventKinds.CardMoved,
                EngineEventKinds.CreditsAdjusted,
                EngineEventKinds.ClickSpent,
                EngineEventKinds.CardMoved,
                EngineEventKinds.CardInstalled,
                EngineEventKinds.TurnTimingWindowOpened,
            ],
            result.Events.Select(value => value.Kind));
        Assert.Equal(3, result.Events.Count(value => value.Kind == EngineEventKinds.CardMoved));
        CardInstalledEventPayload installed = Assert.IsType<CardInstalledEventPayload>(
            result.Events.Single(value => value.Kind == EngineEventKinds.CardInstalled).Payload);
        Assert.Equal(2, installed.Cost);
        Assert.Empty(StateInvariantValidator.Collect(result.State, behaviorCatalog, runtimeCatalog));
    }

    [Fact]
    public void RunnerUniqueHardwareInstallFailsClosedWhenActiveRigMetadataIsUnknown()
    {
        CardDefinitionId incomingDefinitionId = new("test:runner-unique-hardware-unknown-incoming");
        CardInstanceId incomingCardId = new("card:runner-unique-hardware-unknown-incoming");
        CardDefinitionId unknownDefinitionId = new("test:runner-unique-hardware-unknown-existing");
        CardInstanceId unknownCardId = new("card:runner-unique-hardware-unknown-existing");
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:runner-unique-hardware-unknown")
            .Add(new CardRuntimeSpec(
                incomingDefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Hardware,
                installCost: 1,
                unique: true,
                uniquenessKey: "runner-unique-hardware-unknown"))
            .Build();
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder(EngineCompatibility.BehaviorRegistryVersion).Build();
        GameState state = CreateStateWithPreExistingRigCards(
            incomingCardId,
            incomingDefinitionId,
            runnerCredits: 5,
            runtimeCatalog,
            (unknownCardId, unknownDefinitionId));

        state = OpenRunnerAction(state, behaviorCatalog, runtimeCatalog);

        Assert.DoesNotContain(state.PendingDecision!.LegalActions, action => action.Kind == ActionKind.InstallCard);
    }

    [Fact]
    public void RunnerProgramInstallInitializesMemoryForPreExistingRigPrograms()
    {
        CardDefinitionId existingDefinitionId = new("test:runner-existing-program");
        CardInstanceId existingCardId = new("card:runner-existing-program");
        CardDefinitionId incomingDefinitionId = new("test:runner-incoming-program");
        CardInstanceId incomingCardId = new("card:runner-incoming-program");
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:runner-program-existing")
            .Add(new CardRuntimeSpec(
                existingDefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 1,
                memoryUnits: 1))
            .Add(new CardRuntimeSpec(
                incomingDefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 2,
                memoryUnits: 2))
            .Build();
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder(EngineCompatibility.BehaviorRegistryVersion).Build();
        GameState state = CreateStateWithPreExistingProgram(
            incomingCardId,
            incomingDefinitionId,
            existingCardId,
            existingDefinitionId,
            runnerCredits: 5,
            runtimeCatalog);
        Assert.Null(state.RulesState);

        state = OpenRunnerAction(state, behaviorCatalog, runtimeCatalog);
        PendingDecision turnDecision = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction issued = Assert.Single(
            turnDecision.LegalActions,
            candidate => candidate.Kind == ActionKind.InstallCard);
        InstallCardParameters issuedParameters = Assert.IsType<InstallCardParameters>(issued.Parameters);
        Assert.Contains(existingCardId, issuedParameters.CandidateReplacementCardIds);

        ApplyResult selected = GameEngine.Apply(
            state,
            new ActionSubmission(turnDecision.Owner, turnDecision.Id, turnDecision.Revision, issued.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(selected.Accepted, selected.Rejection?.Message);
        InstallFrame materialized = Assert.IsType<InstallFrame>(selected.State.ResolutionStack[^1]);
        Assert.Equal(selected.State.GetZone(ZoneKind.RunnerRig).Id, materialized.DestinationZoneId);
        GameState snapshotRoundTrip = GameState.FromSnapshot(
            selected.State.CreateSnapshot(behaviorCatalog, runtimeCatalog),
            behaviorCatalog,
            runtimeCatalog);
        Assert.Equal(
            materialized.DestinationZoneId,
            Assert.IsType<InstallFrame>(snapshotRoundTrip.ResolutionStack[^1]).DestinationZoneId);
        PendingDecision choiceDecision = Assert.IsType<PendingDecision>(selected.State.PendingDecision);
        LegalAction finalize = Assert.Single(
            choiceDecision.LegalActions,
            candidate => candidate.Kind == ActionKind.InstallCard
                && Assert.IsType<InstallCardParameters>(candidate.Parameters).FinalizeSelection);

        ApplyResult result = GameEngine.Apply(
            selected.State,
            new ActionSubmission(choiceDecision.Owner, choiceDecision.Id, choiceDecision.Revision, finalize.Id),
            behaviorCatalog,
            runtimeCatalog);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(3, result.State.GetPlayer(turnDecision.Owner).Clicks);
        Assert.Equal(3, result.State.GetPlayer(turnDecision.Owner).Credits);
        Assert.Equal(ZoneKind.RunnerRig, result.State.GetZone(result.State.GetCard(incomingCardId).ZoneId).Kind);
        Assert.True(result.State.GetCard(incomingCardId).FaceUp);
        Assert.Equal(2, result.State.RulesState!.Programs.Count);
        Assert.Equal(3, result.State.RulesState.Players.Single(rules => rules.PlayerId == turnDecision.Owner).UsedMemory);
        Assert.Equal(1, result.State.RulesState.Programs.Single(program => program.CardId == existingCardId).MemoryCost);
        Assert.Equal(2, result.State.RulesState.Programs.Single(program => program.CardId == incomingCardId).MemoryCost);
    }

    [Fact]
    public void RunnerOverMemoryProgramInstallUsesLinearSelectionAndPaysOnlyAtFinalization()
    {
        CardDefinitionId incomingDefinitionId = new("test:runner-over-memory-incoming");
        CardInstanceId incomingCardId = new("card:runner-over-memory-incoming");
        CardDefinitionId firstDefinitionId = new("test:runner-over-memory-first");
        CardInstanceId firstCardId = new("card:runner-over-memory-first");
        CardDefinitionId secondDefinitionId = new("test:runner-over-memory-second");
        CardInstanceId secondCardId = new("card:runner-over-memory-second");
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:runner-over-memory")
            .Add(new CardRuntimeSpec(
                incomingDefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 2,
                memoryUnits: 2))
            .Add(new CardRuntimeSpec(
                firstDefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 1,
                memoryUnits: 2))
            .Add(new CardRuntimeSpec(
                secondDefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 1,
                memoryUnits: 2))
            .Build();
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder(EngineCompatibility.BehaviorRegistryVersion).Build();
        GameState state = CreateStateWithPreExistingPrograms(
            incomingCardId,
            incomingDefinitionId,
            runnerCredits: 5,
            runtimeCatalog,
            true,
            (firstCardId, firstDefinitionId),
            (secondCardId, secondDefinitionId));

        state = OpenRunnerAction(state, behaviorCatalog, runtimeCatalog);
        PendingDecision turnDecision = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction issued = Assert.Single(
            turnDecision.LegalActions,
            action => action.Kind == ActionKind.InstallCard);
        InstallCardParameters issuedParameters = Assert.IsType<InstallCardParameters>(issued.Parameters);
        Assert.Equal(2, issuedParameters.CandidateReplacementCardIds.Count);
        Assert.False(issuedParameters.FinalizeSelection);
        Assert.Empty(issuedParameters.SelectedReplacementCardIds);

        int clicksBeforeSelection = state.GetPlayer(turnDecision.Owner).Clicks;
        int creditsBeforeSelection = state.GetPlayer(turnDecision.Owner).Credits;
        ZoneId gripId = state.GetZone(ZoneKind.RunnerGrip).Id;
        ZoneId rigId = state.GetZone(ZoneKind.RunnerRig).Id;
        string pendingJson = EngineJson.Serialize(state);
        string pendingHash = DeterministicHash.ForState(state);

        ApplyResult opened = GameEngine.Apply(
            state,
            new ActionSubmission(turnDecision.Owner, turnDecision.Id, turnDecision.Revision, issued.Id),
            behaviorCatalog,
            runtimeCatalog);

        Assert.True(opened.Accepted, opened.Rejection?.Message);
        Assert.Equal(clicksBeforeSelection, opened.State.GetPlayer(turnDecision.Owner).Clicks);
        Assert.Equal(creditsBeforeSelection, opened.State.GetPlayer(turnDecision.Owner).Credits);
        Assert.Equal(gripId, opened.State.GetCard(incomingCardId).ZoneId);
        Assert.Equal(rigId, opened.State.GetCard(firstCardId).ZoneId);
        Assert.Equal(rigId, opened.State.GetCard(secondCardId).ZoneId);
        InstallFrame frame = Assert.IsType<InstallFrame>(opened.State.ResolutionStack[^1]);
        Assert.Equal(rigId, frame.DestinationZoneId);
        PendingDecision choice = Assert.IsType<PendingDecision>(opened.State.PendingDecision);
        Assert.Equal(DecisionKind.InstallChoice, choice.Kind);
        Assert.Equal(2, choice.LegalActions.Count);
        Assert.DoesNotContain(
            choice.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);

        GameState roundTrip = EngineJson.DeserializeState(
            EngineJson.Serialize(opened.State),
            behaviorCatalog,
            runtimeCatalog);
        Assert.Equal(EngineJson.Serialize(opened.State), EngineJson.Serialize(roundTrip));
        Assert.Equal(pendingJson, EngineJson.Serialize(state));
        Assert.Equal(pendingHash, DeterministicHash.ForState(state));
        Assert.Equal(DeterministicHash.ForState(opened.State), DeterministicHash.ForState(roundTrip));
        Assert.Empty(ObservationBuilder.Build(opened.State, PlayerSide.Corp, behaviorCatalog).LegalActions);
        Assert.Equal(
            choice.LegalActions.Count,
            ObservationBuilder.Build(opened.State, PlayerSide.Runner, behaviorCatalog).LegalActions.Count);

        LegalAction selectFirst = Assert.Single(
            choice.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters)
                .SelectedReplacementCardIds.SequenceEqual([firstCardId]));
        ApplyResult selected = GameEngine.Apply(
            opened.State,
            new ActionSubmission(choice.Owner, choice.Id, choice.Revision, selectFirst.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(selected.Accepted, selected.Rejection?.Message);
        Assert.Equal(clicksBeforeSelection, selected.State.GetPlayer(turnDecision.Owner).Clicks);
        Assert.Equal(creditsBeforeSelection, selected.State.GetPlayer(turnDecision.Owner).Credits);
        Assert.Equal(gripId, selected.State.GetCard(incomingCardId).ZoneId);
        Assert.Equal(rigId, selected.State.GetCard(firstCardId).ZoneId);
        Assert.Equal(rigId, selected.State.GetCard(secondCardId).ZoneId);
        Assert.Contains(
            selected.State.PendingDecision!.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);
        Assert.Equal(2, selected.State.PendingDecision.LegalActions.Count);
        Assert.Contains(
            selected.State.PendingDecision.LegalActions,
            action => !Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);

        // Selecting the remaining candidate stays linear and exercises the
        // multi-trash reducer path without materializing every subset.
        LegalAction selectSecond = Assert.Single(
            selected.State.PendingDecision.LegalActions,
            action => !Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);
        ApplyResult selectedBoth = GameEngine.Apply(
            selected.State,
            new ActionSubmission(
                selected.State.PendingDecision.Owner,
                selected.State.PendingDecision.Id,
                selected.State.PendingDecision.Revision,
                selectSecond.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(selectedBoth.Accepted, selectedBoth.Rejection?.Message);
        PendingDecision bothDecision = Assert.IsType<PendingDecision>(selectedBoth.State.PendingDecision);
        LegalAction finalizeBoth = Assert.Single(
            bothDecision.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);
        ApplyResult bothResult = GameEngine.Apply(
            selectedBoth.State,
            new ActionSubmission(bothDecision.Owner, bothDecision.Id, bothDecision.Revision, finalizeBoth.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(bothResult.Accepted, bothResult.Rejection?.Message);
        Assert.Equal(ZoneKind.RunnerHeap, bothResult.State.GetZone(bothResult.State.GetCard(firstCardId).ZoneId).Kind);
        Assert.Equal(ZoneKind.RunnerHeap, bothResult.State.GetZone(bothResult.State.GetCard(secondCardId).ZoneId).Kind);
        Assert.Single(bothResult.State.RulesState!.Programs);
        Assert.DoesNotContain(
            bothResult.State.GetZone(ZoneKind.RunnerRig).CardIds,
            cardId => cardId == firstCardId || cardId == secondCardId);

        ApplyResult stale = GameEngine.Apply(
            selected.State,
            new ActionSubmission(choice.Owner, choice.Id, choice.Revision, selectFirst.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.False(stale.Accepted);
        Assert.Equal(ApplyRejectionCode.StaleDecisionId, stale.Rejection?.Code);
        Assert.Equal(DeterministicHash.ForState(selected.State), DeterministicHash.ForState(stale.State));

        PendingDecision selectedDecision = Assert.IsType<PendingDecision>(selected.State.PendingDecision);
        LegalAction finalize = Assert.Single(
            selectedDecision.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);
        ApplyResult result = GameEngine.Apply(
            selected.State,
            new ActionSubmission(selectedDecision.Owner, selectedDecision.Id, selectedDecision.Revision, finalize.Id),
            behaviorCatalog,
            runtimeCatalog);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(clicksBeforeSelection - 1, result.State.GetPlayer(turnDecision.Owner).Clicks);
        Assert.Equal(creditsBeforeSelection - 2, result.State.GetPlayer(turnDecision.Owner).Credits);
        Assert.Equal(ZoneKind.RunnerHeap, result.State.GetZone(result.State.GetCard(firstCardId).ZoneId).Kind);
        Assert.Equal(ZoneKind.RunnerRig, result.State.GetZone(result.State.GetCard(incomingCardId).ZoneId).Kind);
        Assert.True(result.State.GetCard(incomingCardId).FaceUp);
        Assert.DoesNotContain(firstCardId, result.State.RulesState!.Programs.Select(program => program.CardId));
        Assert.Contains(incomingCardId, result.State.RulesState.Programs.Select(program => program.CardId));
        Assert.Equal(4, result.State.RulesState.Players.Single(player => player.PlayerId == turnDecision.Owner).UsedMemory);
        Assert.Single(result.Events, @event => @event.Kind == EngineEventKinds.ClickSpent);
        Assert.Single(result.Events, @event => @event.Kind == EngineEventKinds.CardInstalled);
        Assert.Equal(1, result.Events.Count(@event => @event.Kind == EngineEventKinds.CreditsAdjusted));
        Assert.Equal(DecisionKind.TurnTiming, result.State.PendingDecision!.Kind);
    }

    [Fact]
    public void RunnerIncomingStaticMemoryModifierEnablesFullMemoryFitAndCountsAfterActivation()
    {
        CardDefinitionId incomingDefinitionId = new("test:runner-static-incoming");
        CardInstanceId incomingCardId = new("card:runner-static-incoming");
        CardDefinitionId existingDefinitionId = new("test:runner-static-existing");
        CardInstanceId existingCardId = new("card:runner-static-existing");
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder(EngineCompatibility.BehaviorRegistryVersion)
            .Add(new CardBehaviorDefinition(
                incomingDefinitionId,
                PlayerSide.Runner,
                staticModifiers: [new BehaviorStaticModifier(BehaviorStaticModifierKind.MemoryUnits, 1)]))
            .Build();
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:runner-static-incoming")
            .Add(new CardRuntimeSpec(
                incomingDefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 2,
                memoryUnits: 1))
            .Add(new CardRuntimeSpec(
                existingDefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 1,
                memoryUnits: 4))
            .Build();
        GameState state = CreateStateWithPreExistingPrograms(
            incomingCardId,
            incomingDefinitionId,
            runnerCredits: 5,
            runtimeCatalog,
            true,
            (existingCardId, existingDefinitionId));

        state = OpenRunnerAction(state, behaviorCatalog, runtimeCatalog);
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction install = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.InstallCard);
        InstallCardParameters issued = Assert.IsType<InstallCardParameters>(install.Parameters);
        Assert.Contains(existingCardId, issued.CandidateReplacementCardIds);
        ApplyResult result = GameEngine.Apply(
            state,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, install.Id),
            behaviorCatalog,
            runtimeCatalog);

        Assert.True(result.Accepted, result.Rejection?.Message);
        PendingDecision choice = Assert.IsType<PendingDecision>(result.State.PendingDecision);
        LegalAction finalize = Assert.Single(
            choice.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);
        Assert.Empty(Assert.IsType<InstallCardParameters>(finalize.Parameters).SelectedReplacementCardIds);

        result = GameEngine.Apply(
            result.State,
            new ActionSubmission(choice.Owner, choice.Id, choice.Revision, finalize.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(1, BehaviorStaticModifierProjection.Sum(
            behaviorCatalog,
            result.State.Cards,
            result.State.Zones,
            PlayerSide.Runner,
            BehaviorStaticModifierKind.MemoryUnits));
        Assert.Equal(4, result.State.RulesState!.Players.Single(player => player.PlayerId == decision.Owner).BaseMemory);
        Assert.Equal(5, result.State.RulesState.Players.Single(player => player.PlayerId == decision.Owner).UsedMemory);
        Assert.Equal(ZoneKind.RunnerRig, result.State.GetZone(result.State.GetCard(incomingCardId).ZoneId).Kind);
    }

    [Fact]
    public void RunnerTrashingActiveStaticMemoryProgramRemovesItsModifierBeforeFitCheck()
    {
        CardDefinitionId incomingDefinitionId = new("test:runner-static-incoming");
        CardInstanceId incomingCardId = new("card:runner-static-incoming");
        CardDefinitionId modifierDefinitionId = new("test:runner-static-program");
        CardInstanceId modifierCardId = new("card:runner-static-program");
        CardDefinitionId regularDefinitionId = new("test:runner-static-regular");
        CardInstanceId regularCardId = new("card:runner-static-regular");
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:runner-static-program")
            .Add(new CardRuntimeSpec(
                incomingDefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 2,
                memoryUnits: 2))
            .Add(new CardRuntimeSpec(
                modifierDefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 1,
                memoryUnits: 1))
            .Add(new CardRuntimeSpec(
                regularDefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 1,
                memoryUnits: 3))
            .Build();
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder(EngineCompatibility.BehaviorRegistryVersion)
            .Add(new CardBehaviorDefinition(
                modifierDefinitionId,
                PlayerSide.Runner,
                staticModifiers: [new BehaviorStaticModifier(BehaviorStaticModifierKind.MemoryUnits, 1)]))
            .Build();
        GameState state = CreateStateWithPreExistingPrograms(
            incomingCardId,
            incomingDefinitionId,
            runnerCredits: 5,
            runtimeCatalog,
            true,
            (modifierCardId, modifierDefinitionId),
            (regularCardId, regularDefinitionId));

        state = OpenRunnerAction(state, behaviorCatalog, runtimeCatalog);
        PendingDecision turnDecision = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction issued = Assert.Single(turnDecision.LegalActions, action => action.Kind == ActionKind.InstallCard);
        ApplyResult opened = GameEngine.Apply(
            state,
            new ActionSubmission(turnDecision.Owner, turnDecision.Id, turnDecision.Revision, issued.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        PendingDecision choice = Assert.IsType<PendingDecision>(opened.State.PendingDecision);
        LegalAction selectModifier = Assert.Single(
            choice.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters)
                .SelectedReplacementCardIds.SequenceEqual([modifierCardId]));
        LegalAction selectRegular = Assert.Single(
            choice.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters)
                .SelectedReplacementCardIds.SequenceEqual([regularCardId]));

        ApplyResult selected = GameEngine.Apply(
            opened.State,
            new ActionSubmission(choice.Owner, choice.Id, choice.Revision, selectModifier.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(selected.Accepted, selected.Rejection?.Message);
        Assert.Equal(1, BehaviorStaticModifierProjection.Sum(
            behaviorCatalog,
            selected.State.Cards,
            selected.State.Zones,
            PlayerSide.Runner,
            BehaviorStaticModifierKind.MemoryUnits));
        Assert.DoesNotContain(
            selected.State.PendingDecision!.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);

        // The regular three-MU candidate is independently sufficient while
        // retaining the active +1 modifier. Reuse the immutable first-choice
        // state so this branch is not accidentally selecting [modifier,
        // regular] from the already-selected decision.
        ApplyResult selectedRegular = GameEngine.Apply(
            opened.State,
            new ActionSubmission(
                choice.Owner,
                choice.Id,
                choice.Revision,
                selectRegular.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(selectedRegular.Accepted, selectedRegular.Rejection?.Message);
        PendingDecision finalDecision = Assert.IsType<PendingDecision>(selectedRegular.State.PendingDecision);
        LegalAction finalize = Assert.Single(
            finalDecision.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);
        ApplyResult result = GameEngine.Apply(
            selectedRegular.State,
            new ActionSubmission(finalDecision.Owner, finalDecision.Id, finalDecision.Revision, finalize.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(1, BehaviorStaticModifierProjection.Sum(
            behaviorCatalog,
            result.State.Cards,
            result.State.Zones,
            PlayerSide.Runner,
            BehaviorStaticModifierKind.MemoryUnits));
        Assert.Equal(3, result.State.RulesState!.Players.Single(player => player.PlayerId == finalDecision.Owner).UsedMemory);
        Assert.Equal(ZoneKind.RunnerRig, result.State.GetZone(result.State.GetCard(modifierCardId).ZoneId).Kind);
        Assert.Equal(ZoneKind.RunnerHeap, result.State.GetZone(result.State.GetCard(regularCardId).ZoneId).Kind);
    }

    [Fact]
    public void RunnerInstallRejectsStaleDestinationAndInsufficientResourcesWithoutMutation()
    {
        CardDefinitionId incomingDefinitionId = new("test:runner-stale-install-incoming");
        CardInstanceId incomingCardId = new("card:runner-stale-install-incoming");
        CardDefinitionId existingDefinitionId = new("test:runner-stale-install-existing");
        CardInstanceId existingCardId = new("card:runner-stale-install-existing");
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:runner-stale-install")
            .Add(new CardRuntimeSpec(
                incomingDefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 2,
                memoryUnits: 2))
            .Add(new CardRuntimeSpec(
                existingDefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 1,
                memoryUnits: 2))
            .Build();
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder(EngineCompatibility.BehaviorRegistryVersion).Build();
        GameState state = CreateStateWithPreExistingPrograms(
            incomingCardId,
            incomingDefinitionId,
            runnerCredits: 5,
            runtimeCatalog,
            true,
            (existingCardId, existingDefinitionId));
        state = OpenRunnerAction(state, behaviorCatalog, runtimeCatalog);
        PendingDecision turnDecision = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction install = Assert.Single(turnDecision.LegalActions, action => action.Kind == ActionKind.InstallCard);
        ApplyResult opened = GameEngine.Apply(
            state,
            new ActionSubmission(turnDecision.Owner, turnDecision.Id, turnDecision.Revision, install.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        PendingDecision choice = Assert.IsType<PendingDecision>(opened.State.PendingDecision);
        LegalAction select = Assert.Single(choice.LegalActions, action =>
            !Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);

        ZoneState oldRig = opened.State.GetZone(ZoneKind.RunnerRig);
        ZoneId replacementRigId = new("zone:runner-rig-rebuilt");
        ZoneState[] staleZones = opened.State.Zones
            .Where(zone => zone.Id != oldRig.Id)
            .Append(new ZoneState(
                replacementRigId,
                ZoneKind.RunnerRig,
                PlayerSide.Runner,
                cardIds: oldRig.CardIds))
            .ToArray();
        CardInstanceState[] staleCards = opened.State.Cards
            .Select(card => card.ZoneId == oldRig.Id
                ? new CardInstanceState(
                    card.Id,
                    card.DefinitionId,
                    replacementRigId,
                    card.OwnerSide,
                    card.FaceUp,
                    card.Rezzed,
                    card.AdvancementCounters)
                : card)
            .ToArray();
        GameState staleTopology = RebuildState(opened.State, staleCards, staleZones, opened.State.Players);
        ApplyResult staleDestination = GameEngine.Apply(
            staleTopology,
            new ActionSubmission(choice.Owner, choice.Id, choice.Revision, select.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.False(staleDestination.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, staleDestination.Rejection?.Code);
        Assert.Equal(DeterministicHash.ForState(staleTopology), DeterministicHash.ForState(staleDestination.State));

        ApplyResult selected = GameEngine.Apply(
            opened.State,
            new ActionSubmission(choice.Owner, choice.Id, choice.Revision, select.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(selected.Accepted, selected.Rejection?.Message);
        PendingDecision selectedDecision = Assert.IsType<PendingDecision>(selected.State.PendingDecision);
        LegalAction finalize = Assert.Single(
            selectedDecision.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);
        PlayerState runner = selected.State.GetPlayer(selectedDecision.Owner);
        GameState unaffordable = RebuildState(
            selected.State,
            selected.State.Cards,
            selected.State.Zones,
            selected.State.Players.Select(player => player.Id == runner.Id
                ? new PlayerState(player.Id, player.Side, credits: 0, clicks: 0, player.AgendaPoints)
                : player).ToArray());
        ApplyResult rejected = GameEngine.Apply(
            unaffordable,
            new ActionSubmission(selectedDecision.Owner, selectedDecision.Id, selectedDecision.Revision, finalize.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Contains("decision.install-materialization-failed", rejected.Rejection?.Message, StringComparison.Ordinal);
        Assert.Equal(DeterministicHash.ForState(unaffordable), DeterministicHash.ForState(rejected.State));
    }

    [Fact]
    public void RunnerProgramInstallIsNotIssuedWhenNullRulesStateHasUnknownRigCard()
    {
        CardDefinitionId unknownDefinitionId = new("test:runner-unknown-rig-card");
        CardInstanceId unknownCardId = new("card:runner-unknown-rig-card");
        CardDefinitionId incomingDefinitionId = new("test:runner-program-with-unknown-rig");
        CardInstanceId incomingCardId = new("card:runner-program-with-unknown-rig");
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:runner-unknown-rig")
            .Add(new CardRuntimeSpec(
                incomingDefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 2,
                memoryUnits: 1))
            .Build();
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder(EngineCompatibility.BehaviorRegistryVersion).Build();
        GameState state = CreateStateWithPreExistingProgram(
            incomingCardId,
            incomingDefinitionId,
            unknownCardId,
            unknownDefinitionId,
            runnerCredits: 5,
            runtimeCatalog);

        state = OpenRunnerAction(state, behaviorCatalog, runtimeCatalog);

        Assert.DoesNotContain(state.PendingDecision!.LegalActions, action => action.Kind == ActionKind.InstallCard);
    }

    [Fact]
    public void RunnerProgramInstallIsNotIssuedWhenRulesStateOmitsUnknownRigCard()
    {
        CardDefinitionId unknownDefinitionId = new("test:runner-unknown-rig-card-with-rules");
        CardInstanceId unknownCardId = new("card:runner-unknown-rig-card-with-rules");
        CardDefinitionId incomingDefinitionId = new("test:runner-program-with-unknown-rig-with-rules");
        CardInstanceId incomingCardId = new("card:runner-program-with-unknown-rig-with-rules");
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:runner-unknown-rig-with-rules")
            .Add(new CardRuntimeSpec(
                incomingDefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 2,
                memoryUnits: 1))
            .Build();
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder(EngineCompatibility.BehaviorRegistryVersion).Build();
        GameState baseline = CreateState(
            incomingCardId,
            incomingDefinitionId,
            runnerCredits: 5,
            runtimeCatalog,
            initializeRulesState: true);
        ZoneState rig = baseline.GetZone(ZoneKind.RunnerRig);
        CardInstanceState unknown = new(
            unknownCardId,
            unknownDefinitionId,
            rig.Id,
            PlayerSide.Runner,
            faceUp: true);
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == rig.Id
                ? new ZoneState(
                    zone.Id,
                    zone.Kind,
                    zone.OwnerSide,
                    zone.ServerId,
                    zone.CardIds.Append(unknownCardId).ToArray())
                : zone)
            .ToArray();
        GameState state = RebuildState(
            baseline,
            baseline.Cards.Append(unknown).ToArray(),
            zones,
            baseline.Players);

        Assert.NotNull(state.RulesState);
        Assert.Empty(state.RulesState!.Programs);

        state = OpenRunnerAction(state, behaviorCatalog, runtimeCatalog);

        Assert.DoesNotContain(state.PendingDecision!.LegalActions, action => action.Kind == ActionKind.InstallCard);
    }

    [Fact]
    public void RunnerInstallIsNotIssuedWhenPrintedCostIsUnaffordable()
    {
        CardDefinitionId definitionId = new("test:runner-resource");
        CardInstanceId cardId = new("card:runner-resource");
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:runner-install-poor")
            .Add(new CardRuntimeSpec(
                definitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Resource,
                installCost: 2))
            .Build();
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder(EngineCompatibility.BehaviorRegistryVersion).Build();
        GameState state = OpenRunnerAction(
            CreateState(cardId, definitionId, runnerCredits: 1, runtimeCatalog),
            behaviorCatalog,
            runtimeCatalog);

        Assert.DoesNotContain(state.PendingDecision!.LegalActions, action => action.Kind == ActionKind.InstallCard);
    }

    [Fact]
    public void InstallChoiceInvariantRejectsOmittedContinuation()
    {
        (GameState state, BehaviorCatalog behaviorCatalog, RuntimeCardCatalog runtimeCatalog) = OpenInstallChoiceAfterFirstSelection();
        PendingDecision choice = state.PendingDecision!;
        LegalAction finalize = Assert.Single(choice.LegalActions, action => Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);

        GameState forged = WithPendingDecision(state, ReissueDecision(choice, [finalize]));
        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(
            forged,
            behaviorCatalog,
            runtimeCatalog);

        Assert.Contains(violations, violation => violation.Code == "decision.missing-install-continuation");
    }

    [Fact]
    public void InstallChoiceInvariantRejectsSemanticDuplicateUnderFreshActionId()
    {
        (GameState state, BehaviorCatalog behaviorCatalog, RuntimeCardCatalog runtimeCatalog) = OpenInstallChoiceAfterFirstSelection();
        PendingDecision choice = state.PendingDecision!;
        LegalAction incremental = Assert.Single(choice.LegalActions, action => !Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);
        LegalAction duplicate = new(
            new ActionId("action:install-continuation-duplicate"),
            incremental.Kind,
            incremental.Description,
            incremental.Parameters);

        GameState forged = WithPendingDecision(
            state,
            ReissueDecision(choice, [.. choice.LegalActions, duplicate]));
        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(
            forged,
            behaviorCatalog,
            runtimeCatalog);

        Assert.Contains(violations, violation => violation.Code == "decision.duplicate-install-continuation");
    }

    [Fact]
    public void InstallChoiceInvariantRejectsExtraStaleContinuation()
    {
        (GameState state, BehaviorCatalog behaviorCatalog, RuntimeCardCatalog runtimeCatalog) = OpenInstallChoiceAfterFirstSelection();
        PendingDecision choice = state.PendingDecision!;
        InstallFrame frame = Assert.IsType<InstallFrame>(state.ResolutionStack[^1]);
        InstallCardParameters finalize = Assert.IsType<InstallCardParameters>(
            Assert.Single(choice.LegalActions, action => Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection).Parameters);
        InstallCardParameters stale = new(
            finalize.CardId,
            finalize.DestinationKind,
            finalize.ServerId,
            finalize.CreateNewRemote,
            checked(finalize.CreditCost + 1),
            frame.CandidateReplacementCardIds,
            frame.RequiredReplacementCardIds,
            frame.SelectedReplacementCardIds,
            finalizeSelection: true);
        LegalAction extra = new(
            new ActionId("action:install-continuation-stale"),
            ActionKind.InstallCard,
            "Install selected card",
            stale);

        GameState forged = WithPendingDecision(
            state,
            ReissueDecision(choice, [.. choice.LegalActions, extra]));
        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(
            forged,
            behaviorCatalog,
            runtimeCatalog);

        Assert.Contains(violations, violation => violation.Code == "decision.extra-install-continuation");
    }

    [Fact]
    public void InstallChoiceInvariantAcceptsExactSemanticSetRegardlessOfActionIds()
    {
        (GameState state, BehaviorCatalog behaviorCatalog, RuntimeCardCatalog runtimeCatalog) = OpenInstallChoiceAfterFirstSelection();
        PendingDecision choice = state.PendingDecision!;
        PendingDecision reissued = ReissueDecision(
            choice,
            choice.LegalActions
                .Select((action, index) => new LegalAction(
                    new ActionId($"action:install-continuation-valid-{index}"),
                    action.Kind,
                    action.Description,
                    action.Parameters))
                .ToArray());
        GameState exact = WithPendingDecision(state, reissued);
        ulong allocatorBefore = exact.Allocator.NextSequence;
        string hashBefore = DeterministicHash.ForState(exact);

        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(
            exact,
            behaviorCatalog,
            runtimeCatalog);

        Assert.Empty(violations);
        Assert.Equal(allocatorBefore, exact.Allocator.NextSequence);
        Assert.Equal(hashBefore, DeterministicHash.ForState(exact));
    }

    [Fact]
    public void InvariantRejectsForgedCorpInstallFrameAndPendingChoice()
    {
        CardDefinitionId definitionId = new("test:runner-forged-install");
        CardInstanceId cardId = new("card:runner-forged-install");
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:runner-forged-install")
            .Add(new CardRuntimeSpec(
                definitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 1,
                memoryUnits: 1))
            .Build();
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder(EngineCompatibility.BehaviorRegistryVersion).Build();
        GameState source = CreateState(cardId, definitionId, runnerCredits: 5, runtimeCatalog);
        PlayerId corp = source.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        ServerState headquarters = source.Servers.Single(server => server.Kind == ServerKind.Headquarters);
        ZoneId runnerGripId = source.GetZone(ZoneKind.RunnerGrip).Id;

        TurnFactsState corpFacts = new(
            1,
            PlayerSide.Corp,
            source.Turn.TurnNumber,
            clicksAllotted: 3,
            clicksLost: 0,
            turnBegan: true,
            mandatoryDrawPerformed: true,
            actionPhaseEnded: false,
            discardPhaseEnded: false);
        TurnState corpTurn = new(
            PlayerSide.Corp,
            TurnPhase.Corp,
            source.Turn.TurnNumber,
            TurnStep.CorpAction,
            new TurnHistoryState(1, corpFacts, null, null, null));
        InstallFrame forgedFrame = new(
            corp,
            cardId,
            definitionId,
            RuntimeCardClass.Agenda,
            runnerGripId,
            ZoneKind.CorpHand,
            InstallDestinationKind.ServerRoot,
            headquarters.Id,
            createNewRemote: false,
            installCost: 0,
            candidateReplacementCardIds: [],
            requiredReplacementCardIds: [],
            selectedReplacementCardIds: [],
            destinationZoneId: headquarters.RootZoneId);
        TurnFrame forgedParent = new(
            PlayerSide.Corp,
            source.Turn.TurnNumber,
            TurnFrameStage.AwaitingAction);
        PendingDecision forgedDecision = new(
            new DecisionId("decision:forged-corp-install"),
            revision: 1,
            corp,
            DecisionKind.InstallChoice,
            "Choose cards to trash before installing",
            [new LegalAction(
                new ActionId("action:forged-corp-install"),
                ActionKind.InstallCard,
                "Install selected card",
                new InstallCardParameters(
                    cardId,
                    InstallDestinationKind.ServerRoot,
                    headquarters.Id,
                    createNewRemote: false,
                    creditCost: 0,
                    candidateReplacementCardIds: [],
                    requiredReplacementCardIds: [],
                    selectedReplacementCardIds: [],
                    finalizeSelection: true))]);
        GameState forged = new(
            source.GameId,
            source.RulesVersion,
            source.CardDataVersion,
            source.Players,
            source.Cards,
            source.Zones,
            source.Servers,
            corpTurn,
            source.Outcome,
            source.RandomState,
            source.Allocator,
            decisionRevision: 1,
            resolutionStack: [forgedParent, forgedFrame],
            pendingDecision: forgedDecision,
            initialSeed: source.InitialSeed,
            behaviorRegistryVersion: source.BehaviorRegistryVersion,
            behaviorCatalogFingerprint: source.BehaviorCatalogFingerprint,
            runtimeCardCatalogVersion: source.RuntimeCardCatalogVersion,
            runtimeCardCatalogFingerprint: source.RuntimeCardCatalogFingerprint);

        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(
            forged,
            behaviorCatalog,
            runtimeCatalog);
        StateInvariantViolation ownerViolation = Assert.Single(
            violations,
            violation => violation.Code == "resolution.install-owner-side");
        Assert.Contains("Runner-only", ownerViolation.Message, StringComparison.Ordinal);
        Assert.Contains(violations, violation => violation.Code == "resolution.install-parent-side");
        Assert.Contains(violations, violation => violation.Code == "resolution.install-source-mismatch");
        Assert.Contains(violations, violation => violation.Code == "resolution.install-destination-kind");
        Assert.Contains(violations, violation => violation.Code == "resolution.install-destination-mismatch");

        ApplyResult rejected = GameEngine.Apply(
            forged,
            new ActionSubmission(
                forgedDecision.Owner,
                forgedDecision.Id,
                forgedDecision.Revision,
                forgedDecision.LegalActions[0].Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Contains("Runner-only", rejected.Rejection?.Message, StringComparison.Ordinal);
    }

    private static GameState OpenRunnerAction(
        GameState state,
        BehaviorCatalog behaviorCatalog,
        RuntimeCardCatalog runtimeCatalog)
    {
        state = GameEngine.BeginTurn(state, PlayerSide.Runner, behaviorCatalog, runtimeCatalog);
        for (int index = 0; index < 4; index++)
        {
            PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction pass = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.PassPriority);
            ApplyResult result = GameEngine.Apply(
                state,
                new ActionSubmission(decision.Owner, decision.Id, decision.Revision, pass.Id),
                behaviorCatalog,
                runtimeCatalog);
            Assert.True(result.Accepted, result.Rejection?.Message);
            state = result.State;
        }

        Assert.Equal(DecisionKind.TurnAction, state.PendingDecision!.Kind);
        return state;
    }

    private static GameState CreateState(
        CardInstanceId cardId,
        CardDefinitionId definitionId,
        int runnerCredits,
        RuntimeCardCatalog runtimeCatalog,
        bool initializeRulesState = false)
    {
        GameState baseline = GameState.CreateNew(
            seed: 405,
            behaviorRegistryVersion: EngineCompatibility.BehaviorRegistryVersion,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);
        ZoneState grip = baseline.GetZone(ZoneKind.RunnerGrip);
        var card = new CardInstanceState(cardId, definitionId, grip.Id, PlayerSide.Runner);
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == grip.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(cardId).ToArray())
                : zone)
            .ToArray();
        PlayerState runner = baseline.Players.Single(player => player.Side == PlayerSide.Runner);
        PlayerState corp = baseline.Players.Single(player => player.Side == PlayerSide.Corp);
        PlayerState[] players =
        [
            corp,
            new PlayerState(runner.Id, runner.Side, runnerCredits, runner.Clicks, runner.AgendaPoints),
        ];
        return new GameState(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            players,
            [card],
            zones,
            baseline.Servers,
            new TurnState(PlayerSide.Runner, TurnPhase.Runner, 1),
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            rulesState: initializeRulesState ? RulesState.CreateDefault(players) : null,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);
    }

    private static GameState RebuildState(
        GameState source,
        IReadOnlyList<CardInstanceState> cards,
        IReadOnlyList<ZoneState> zones,
        IReadOnlyList<PlayerState> players) =>
        new(
            source.SchemaVersion,
            source.GameId,
            source.RulesVersion,
            source.CardDataVersion,
            players,
            cards,
            zones,
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

    private static GameState CreateStateWithPreExistingProgram(
        CardInstanceId incomingCardId,
        CardDefinitionId incomingDefinitionId,
        CardInstanceId existingCardId,
        CardDefinitionId existingDefinitionId,
        int runnerCredits,
        RuntimeCardCatalog runtimeCatalog)
    {
        return CreateStateWithPreExistingPrograms(
            incomingCardId,
            incomingDefinitionId,
            runnerCredits,
            runtimeCatalog,
            false,
            (existingCardId, existingDefinitionId));
    }

    private static GameState CreateStateWithPreExistingRigCards(
        CardInstanceId incomingCardId,
        CardDefinitionId incomingDefinitionId,
        int runnerCredits,
        RuntimeCardCatalog runtimeCatalog,
        params (CardInstanceId CardId, CardDefinitionId DefinitionId)[] existingCards)
    {
        GameState baseline = CreateState(
            incomingCardId,
            incomingDefinitionId,
            runnerCredits,
            runtimeCatalog);
        ZoneState rig = baseline.GetZone(ZoneKind.RunnerRig);
        CardInstanceState[] installed = existingCards
            .Select(card => new CardInstanceState(
                card.CardId,
                card.DefinitionId,
                rig.Id,
                PlayerSide.Runner,
                faceUp: true))
            .ToArray();
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == rig.Id
                ? new ZoneState(
                    zone.Id,
                    zone.Kind,
                    zone.OwnerSide,
                    zone.ServerId,
                    zone.CardIds.Concat(installed.Select(card => card.Id)).ToArray())
                : zone)
            .ToArray();
        return new GameState(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            baseline.Players,
            baseline.Cards.Concat(installed).ToArray(),
            zones,
            baseline.Servers,
            baseline.Turn,
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);
    }

    private static GameState CreateStateWithPreExistingPrograms(
        CardInstanceId incomingCardId,
        CardDefinitionId incomingDefinitionId,
        int runnerCredits,
        RuntimeCardCatalog runtimeCatalog,
        bool initializeRulesState,
        params (CardInstanceId CardId, CardDefinitionId DefinitionId)[] existingPrograms)
    {
        GameState baseline = CreateState(
            incomingCardId,
            incomingDefinitionId,
            runnerCredits,
            runtimeCatalog);
        ZoneState rig = baseline.GetZone(ZoneKind.RunnerRig);
        CardInstanceState[] existing = existingPrograms
            .Select(program => new CardInstanceState(
                program.CardId,
                program.DefinitionId,
                rig.Id,
                PlayerSide.Runner,
                faceUp: true))
            .ToArray();
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == rig.Id
                ? new ZoneState(
                    zone.Id,
                    zone.Kind,
                    zone.OwnerSide,
                    zone.ServerId,
                    zone.CardIds.Concat(existing.Select(card => card.Id)).ToArray())
                : zone)
            .ToArray();
        if (!initializeRulesState)
        {
            return new GameState(
                baseline.SchemaVersion,
                baseline.GameId,
                baseline.RulesVersion,
                baseline.CardDataVersion,
                baseline.Players,
                baseline.Cards.Concat(existing).ToArray(),
                zones,
                baseline.Servers,
                baseline.Turn,
                baseline.Outcome,
                baseline.RandomState,
                baseline.Allocator,
                runtimeCardCatalogVersion: runtimeCatalog.Version,
                runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);
        }

        PlayerState runner = baseline.Players.Single(player => player.Side == PlayerSide.Runner);
        PlayerState corp = baseline.Players.Single(player => player.Side == PlayerSide.Corp);
        RulesState defaults = RulesState.CreateDefault(baseline.Players);
        ProgramMemoryState[] registrations = existing
            .Select(card => new ProgramMemoryState(
                card.Id,
                runner.Id,
                runtimeCatalog.GetSpec(card.DefinitionId).MemoryUnits))
            .ToArray();
        int usedMemory = registrations.Sum(program => program.MemoryCost);
        PlayerRulesState[] rulesPlayers = defaults.Players
            .Select(player => player.PlayerId == runner.Id
                ? new PlayerRulesState(
                    player.PlayerId,
                    player.BaseHandSize,
                    player.CoreDamage,
                    player.Tags,
                    player.BaseMemory,
                    usedMemory)
                : player)
            .ToArray();
        return new GameState(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            [corp, new PlayerState(runner.Id, runner.Side, runner.Credits, runner.Clicks, runner.AgendaPoints)],
            baseline.Cards.Concat(existing).ToArray(),
            zones,
            baseline.Servers,
            baseline.Turn,
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint,
            rulesState: new RulesState(
                defaults.Version,
                rulesPlayers,
                registrations,
                defaults.Counters,
                defaults.LingeringEffects,
                defaults.Triggers,
                defaults.Interrupts));
    }

    private static (GameState State, BehaviorCatalog BehaviorCatalog, RuntimeCardCatalog RuntimeCatalog) OpenInstallChoiceAfterFirstSelection()
    {
        CardDefinitionId incomingDefinitionId = new("test:runner-parity-incoming");
        CardInstanceId incomingCardId = new("card:runner-parity-incoming");
        CardDefinitionId firstDefinitionId = new("test:runner-parity-first");
        CardInstanceId firstCardId = new("card:runner-parity-first");
        CardDefinitionId secondDefinitionId = new("test:runner-parity-second");
        CardInstanceId secondCardId = new("card:runner-parity-second");
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:runner-parity")
            .Add(new CardRuntimeSpec(incomingDefinitionId, PlayerSide.Runner, RuntimeCardClass.Program, installCost: 2, memoryUnits: 2))
            .Add(new CardRuntimeSpec(firstDefinitionId, PlayerSide.Runner, RuntimeCardClass.Program, installCost: 1, memoryUnits: 2))
            .Add(new CardRuntimeSpec(secondDefinitionId, PlayerSide.Runner, RuntimeCardClass.Program, installCost: 1, memoryUnits: 2))
            .Build();
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder(EngineCompatibility.BehaviorRegistryVersion).Build();
        GameState state = CreateStateWithPreExistingPrograms(
            incomingCardId,
            incomingDefinitionId,
            runnerCredits: 5,
            runtimeCatalog,
            initializeRulesState: true,
            (firstCardId, firstDefinitionId),
            (secondCardId, secondDefinitionId));
        state = OpenRunnerAction(state, behaviorCatalog, runtimeCatalog);
        PendingDecision turnDecision = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction install = Assert.Single(turnDecision.LegalActions, action => action.Kind == ActionKind.InstallCard);
        ApplyResult opened = GameEngine.Apply(
            state,
            new ActionSubmission(turnDecision.Owner, turnDecision.Id, turnDecision.Revision, install.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        PendingDecision choice = Assert.IsType<PendingDecision>(opened.State.PendingDecision);
        LegalAction first = Assert.Single(
            choice.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters)
                .SelectedReplacementCardIds.SequenceEqual([firstCardId]));
        ApplyResult selected = GameEngine.Apply(
            opened.State,
            new ActionSubmission(choice.Owner, choice.Id, choice.Revision, first.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(selected.Accepted, selected.Rejection?.Message);
        return (selected.State, behaviorCatalog, runtimeCatalog);
    }

    private static PendingDecision ReissueDecision(
        PendingDecision source,
        IReadOnlyList<LegalAction> actions) =>
        new(source.Id, source.Revision, source.Owner, source.Kind, source.Prompt, actions);

    private static GameState WithPendingDecision(GameState source, PendingDecision pending) =>
        new(
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

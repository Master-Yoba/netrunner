using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// End-to-end evidence for the engine-owned InstallCard behavior effect.
/// These tests deliberately enter through a real Runner Event action so the
/// behavior install cannot bypass ordinary play, payment, or turn lifecycle
/// validation.
/// </summary>
public sealed class BehaviorInstallActionTests
{
    [Fact]
    public void BehaviorInstallMovesStackProgramPaysPrintedCostOnceAndReturnsToPriority()
    {
        Fixture fixture = CreateFixture();
        GameState state = OpenRunnerAction(fixture);
        int clicksBeforePlay = state.GetPlayer(fixture.RunnerId).Clicks;
        int creditsBeforePlay = state.GetPlayer(fixture.RunnerId).Credits;

        LegalAction play = Assert.Single(
            state.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.PlayCard);
        ApplyResult played = Apply(state, fixture, play);
        Assert.True(played.Accepted, played.Rejection?.Message);
        Assert.Equal(1, played.Events.Count(value => value.Kind == EngineEventKinds.ClickSpent));
        Assert.Equal(DecisionKind.BehaviorChoice, played.State.PendingDecision!.Kind);
        Assert.Equal(
            [typeof(TurnFrame), typeof(CardPlayFrame), typeof(BehaviorResolutionFrame)],
            played.State.ResolutionStack.Select(frame => frame.GetType()));

        LegalAction searchSelection = Assert.Single(
            played.State.PendingDecision.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters choice
                && choice.CardIds.SequenceEqual([fixture.IncomingProgramId]));
        ApplyResult selected = Apply(played.State, fixture, searchSelection);
        Assert.True(selected.Accepted, selected.Rejection?.Message);
        Assert.Equal(ZoneKind.RunnerStack, selected.State.ZoneOf(fixture.IncomingProgramId).Kind);
        Assert.Equal(DecisionKind.BehaviorChoice, selected.State.PendingDecision!.Kind);

        LegalAction install = Assert.Single(
            selected.State.PendingDecision.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters choice
                && choice.OptionKeys.SequenceEqual(["install"]));
        ApplyResult installed = Apply(selected.State, fixture, install);
        Assert.True(installed.Accepted, installed.Rejection?.Message);

        GameState result = installed.State;
        Assert.Equal(clicksBeforePlay - 1, result.GetPlayer(fixture.RunnerId).Clicks);
        Assert.Equal(creditsBeforePlay - fixture.IncomingInstallCost, result.GetPlayer(fixture.RunnerId).Credits);
        Assert.Equal(ZoneKind.RunnerRig, result.ZoneOf(fixture.IncomingProgramId).Kind);
        Assert.True(result.GetCard(fixture.IncomingProgramId).FaceUp);
        Assert.Contains(fixture.IncomingProgramId, result.Turn.ThisTurnFacts!.InstalledCardIds);
        Assert.Equal(TurnFrameStage.ActionPriority, Assert.IsType<TurnFrame>(result.ResolutionStack.Single()).Stage);
        Assert.Equal(DecisionKind.TurnTiming, result.PendingDecision!.Kind);
        Assert.Contains(fixture.IncomingProgramId, result.GetZone(ZoneKind.RunnerRig).CardIds);
        Assert.Empty(result.GetZone(ZoneKind.RunnerStack).CardIds);
        Assert.Contains(fixture.EventId, result.GetZone(ZoneKind.RunnerHeap).CardIds);

        PlayerRulesState runnerRules = result.RulesState!.Players.Single(value => value.PlayerId == fixture.RunnerId);
        Assert.Equal(fixture.IncomingMemoryUnits, runnerRules.UsedMemory);
        Assert.Single(result.RulesState.Programs, value => value.CardId == fixture.IncomingProgramId);
        MemoryAdjustedEventPayload memory = Assert.IsType<MemoryAdjustedEventPayload>(
            installed.Events.Single(value => value.Kind == EngineEventKinds.MemoryAdjusted).Payload);
        Assert.Equal(fixture.IncomingMemoryUnits, memory.UsedMemory);
        CardInstalledEventPayload cardInstalled = Assert.IsType<CardInstalledEventPayload>(
            installed.Events.Single(value => value.Kind == EngineEventKinds.CardInstalled).Payload);
        Assert.Equal(fixture.IncomingInstallCost, cardInstalled.Cost);
        Assert.DoesNotContain(installed.Events, value => value.Kind == EngineEventKinds.ClickSpent);
        Assert.Equal(1, installed.Events.Count(value => value.Kind == EngineEventKinds.CreditsAdjusted));
    }

    [Fact]
    public void BehaviorInstallOverMemoryUsesAuthoritativeIncrementalInstallChoiceAndRoundTrips()
    {
        CardInstanceId existingId = new("card:behavior-install-existing-program");
        CardDefinitionId existingDefinition = new("test:behavior-install-existing-program");
        Fixture fixture = CreateFixture(
            existingPrograms: [(existingId, existingDefinition, 4)],
            incomingMemoryUnits: 2);
        GameState state = OpenRunnerAction(fixture);
        LegalAction play = Assert.Single(
            state.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.PlayCard);
        GameState played = Apply(state, fixture, play).State;
        LegalAction selection = Assert.Single(
            played.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters choice
                && choice.CardIds.SequenceEqual([fixture.IncomingProgramId]));
        GameState disposition = Apply(played, fixture, selection).State;
        LegalAction install = Assert.Single(
            disposition.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters choice
                && choice.OptionKeys.SequenceEqual(["install"]));

        ApplyResult opened = Apply(disposition, fixture, install);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        Assert.Equal(
            [typeof(TurnFrame), typeof(CardPlayFrame), typeof(BehaviorResolutionFrame), typeof(InstallFrame)],
            opened.State.ResolutionStack.Select(frame => frame.GetType()));
        InstallFrame frame = Assert.IsType<InstallFrame>(opened.State.ResolutionStack[^1]);
        Assert.Equal(InstallInitiationKind.BehaviorEffect, frame.InitiationKind);
        Assert.Equal(BehaviorFrameIdentity.Compute(opened.State.ResolutionStack[^2]), frame.ParentFrameIdentity);
        Assert.Equal(ZoneKind.RunnerStack, frame.SourceZoneKind);
        Assert.Equal(fixture.IncomingProgramId, frame.CardId);
        Assert.Equal([existingId], frame.CandidateReplacementCardIds);
        Assert.Equal([existingDefinition], frame.CandidateReplacementDefinitionIds);
        Assert.Equal(state.GetPlayer(fixture.RunnerId).Clicks - 1, opened.State.GetPlayer(fixture.RunnerId).Clicks);
        Assert.Equal(state.GetPlayer(fixture.RunnerId).Credits, opened.State.GetPlayer(fixture.RunnerId).Credits);

        PendingDecision choice = Assert.IsType<PendingDecision>(opened.State.PendingDecision);
        Assert.Equal(DecisionKind.InstallChoice, choice.Kind);
        Assert.Single(choice.LegalActions);
        InstallCardParameters selectParameters = Assert.IsType<InstallCardParameters>(
            choice.LegalActions[0].Parameters);
        Assert.Equal([existingId], selectParameters.SelectedReplacementCardIds);
        Assert.False(selectParameters.FinalizeSelection);

        string json = EngineJson.Serialize(opened.State);
        GameState restored = EngineJson.DeserializeState(
            json,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(opened.State), DeterministicHash.ForState(restored));

        ApplyResult selectedReplacement = Apply(restored, fixture, choice.LegalActions[0]);
        Assert.True(selectedReplacement.Accepted, selectedReplacement.Rejection?.Message);
        Assert.Equal(DecisionKind.InstallChoice, selectedReplacement.State.PendingDecision!.Kind);
        LegalAction finalize = Assert.Single(
            selectedReplacement.State.PendingDecision.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);
        InstallCardParameters finalParameters = Assert.IsType<InstallCardParameters>(finalize.Parameters);
        Assert.Equal([existingId], finalParameters.SelectedReplacementCardIds);
        Assert.Equal(fixture.IncomingInstallCost, finalParameters.CreditCost);

        ApplyResult result = Apply(selectedReplacement.State, fixture, finalize);
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(ZoneKind.RunnerHeap, result.State.ZoneOf(existingId).Kind);
        Assert.Equal(ZoneKind.RunnerRig, result.State.ZoneOf(fixture.IncomingProgramId).Kind);
        Assert.Single(result.State.RulesState!.Programs, value => value.CardId == fixture.IncomingProgramId);
        Assert.Equal(
            fixture.IncomingMemoryUnits,
            result.State.RulesState.Players.Single(value => value.PlayerId == fixture.RunnerId).UsedMemory);
        Assert.Equal(state.GetPlayer(fixture.RunnerId).Credits - fixture.IncomingInstallCost, result.State.GetPlayer(fixture.RunnerId).Credits);
        Assert.Equal(state.GetPlayer(fixture.RunnerId).Clicks - 1, result.State.GetPlayer(fixture.RunnerId).Clicks);
        Assert.DoesNotContain(result.Events, value => value.Kind == EngineEventKinds.ClickSpent);
        Assert.Equal(1, result.Events.Count(value => value.Kind == EngineEventKinds.MemoryAdjusted));
        Assert.Equal(1, result.Events.Count(value => value.Kind == EngineEventKinds.CardInstalled));
    }

    [Fact]
    public void BehaviorInstallOptionRejectsAlternateSelectorAndLeavesOnlyGrip()
    {
        Fixture fixture = CreateFixture(
            installTargetOverride: new BehaviorTarget(
                BehaviorTargetKind.ChosenCard,
                selector: "alternate"));
        GameState turnAction = OpenRunnerAction(fixture);
        LegalAction play = Assert.Single(
            turnAction.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.PlayCard);
        GameState played = Apply(turnAction, fixture, play).State;
        // The first selection is still reachable; the disposition projection
        // must fail closed because the install instruction does not consume
        // the canonical `chosen` binding.
        LegalAction selection = Assert.Single(
            played.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters choice
                && choice.CardIds.SequenceEqual([fixture.IncomingProgramId]));
        GameState disposition = Apply(played, fixture, selection).State;
        Assert.Single(disposition.PendingDecision!.LegalActions);
        BehaviorChoiceParameters only = Assert.IsType<BehaviorChoiceParameters>(
            disposition.PendingDecision.LegalActions[0].Parameters);
        Assert.Equal(["grip"], only.OptionKeys);
        Assert.Empty(StateInvariantValidator.Collect(
            disposition,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog));
    }

    [Fact]
    public void BehaviorInstallOptionProjectionFailsClosedOnStaticModifierOverflow()
    {
        Fixture fixture = CreateFixture(
            existingPrograms:
            [(
                new CardInstanceId("card:behavior-install-overflow-program"),
                new CardDefinitionId("test:behavior-install-overflow-program"),
                1)],
            existingStaticModifierDelta: int.MaxValue);
        GameState turnAction = OpenRunnerAction(fixture);
        LegalAction play = Assert.Single(
            turnAction.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.PlayCard);
        GameState played = Apply(turnAction, fixture, play).State;
        LegalAction selection = Assert.Single(
            played.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters choice
                && choice.CardIds.SequenceEqual([fixture.IncomingProgramId]));
        GameState disposition = Apply(played, fixture, selection).State;

        Exception? exception = Record.Exception(() => StateInvariantValidator.Collect(
            disposition,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog));
        Assert.Null(exception);
        BehaviorChoiceParameters only = Assert.IsType<BehaviorChoiceParameters>(
            Assert.Single(disposition.PendingDecision!.LegalActions).Parameters);
        Assert.Equal(["grip"], only.OptionKeys);
    }

    [Fact]
    public void BehaviorInstallOptionIsGripOnlyWhenPrintedCostIsUnaffordable()
    {
        Fixture fixture = CreateFixture(runnerCredits: 2);
        GameState disposition = OpenBehaviorDispositionChoice(fixture);

        AssertOnlyGripOption(disposition);
    }

    [Fact]
    public void BehaviorInstallOptionIsGripOnlyWhenMemoryStillExceedsCapacityAfterReplacement()
    {
        CardInstanceId existingId = new("card:behavior-install-mu-existing");
        CardDefinitionId existingDefinition = new("test:behavior-install-mu-existing");
        Fixture fixture = CreateFixture(
            existingPrograms: [(existingId, existingDefinition, 1)],
            incomingMemoryUnits: 5);
        GameState disposition = OpenBehaviorDispositionChoice(fixture);

        AssertOnlyGripOption(disposition);
    }

    [Fact]
    public void BehaviorInstallOptionReplacesAnActiveUniqueConflictAfterSaveLoad()
    {
        CardInstanceId existingId = new("card:behavior-install-unique-existing");
        CardDefinitionId existingDefinition = new("test:behavior-install-unique-existing");
        const string uniquenessKey = "behavior-install-unique";
        Fixture fixture = CreateFixture(
            existingPrograms: [(existingId, existingDefinition, 1)],
            incomingUniquenessKey: uniquenessKey,
            existingUniquenessKey: uniquenessKey);
        GameState disposition = OpenBehaviorDispositionChoice(fixture);

        Assert.Equal(DecisionKind.BehaviorChoice, disposition.PendingDecision!.Kind);
        LegalAction[] options = disposition.PendingDecision.LegalActions.ToArray();
        Assert.Equal(2, options.Length);
        Assert.Contains(options, action =>
            action.Parameters is BehaviorChoiceParameters choice
            && choice.OptionKeys.SequenceEqual(["install"]));
        Assert.Contains(options, action =>
            action.Parameters is BehaviorChoiceParameters choice
            && choice.OptionKeys.SequenceEqual(["grip"]));

        int creditsBeforeInstall = disposition.GetPlayer(fixture.RunnerId).Credits;
        int clicksBeforeInstall = disposition.GetPlayer(fixture.RunnerId).Clicks;
        LegalAction install = Assert.Single(
            options,
            action => action.Parameters is BehaviorChoiceParameters choice
                && choice.OptionKeys.SequenceEqual(["install"]));
        ApplyResult opened = Apply(disposition, fixture, install);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        InstallFrame frame = Assert.IsType<InstallFrame>(opened.State.ResolutionStack[^1]);
        Assert.Equal([existingId], frame.CandidateReplacementCardIds);
        Assert.Equal([existingId], frame.RequiredReplacementCardIds);
        Assert.Equal([existingDefinition], frame.CandidateReplacementDefinitionIds);
        Assert.Equal(creditsBeforeInstall, opened.State.GetPlayer(fixture.RunnerId).Credits);
        Assert.Equal(clicksBeforeInstall, opened.State.GetPlayer(fixture.RunnerId).Clicks);

        string json = EngineJson.Serialize(opened.State);
        GameState restored = EngineJson.DeserializeState(
            json,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(opened.State), DeterministicHash.ForState(restored));

        PendingDecision choice = Assert.IsType<PendingDecision>(restored.PendingDecision);
        LegalAction select = Assert.Single(
            choice.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters)
                .SelectedReplacementCardIds.SequenceEqual([existingId]));
        ApplyResult selected = Apply(restored, fixture, select);
        Assert.True(selected.Accepted, selected.Rejection?.Message);
        PendingDecision finalDecision = Assert.IsType<PendingDecision>(selected.State.PendingDecision);
        LegalAction finalize = Assert.Single(
            finalDecision.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);
        InstallCardParameters finalParameters = Assert.IsType<InstallCardParameters>(finalize.Parameters);
        Assert.Equal([existingId], finalParameters.SelectedReplacementCardIds);
        Assert.Equal(fixture.IncomingInstallCost, finalParameters.CreditCost);

        ApplyResult result = Apply(selected.State, fixture, finalize);
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(ZoneKind.RunnerHeap, result.State.ZoneOf(existingId).Kind);
        Assert.Equal(ZoneKind.RunnerRig, result.State.ZoneOf(fixture.IncomingProgramId).Kind);
        Assert.True(result.State.GetCard(fixture.IncomingProgramId).FaceUp);
        Assert.Equal(
            creditsBeforeInstall - fixture.IncomingInstallCost,
            result.State.GetPlayer(fixture.RunnerId).Credits);
        Assert.Equal(clicksBeforeInstall, result.State.GetPlayer(fixture.RunnerId).Clicks);
        Assert.Equal([fixture.IncomingProgramId], result.State.Turn.ThisTurnFacts!.InstalledCardIds);
        Assert.Single(result.State.RulesState!.Programs, value => value.CardId == fixture.IncomingProgramId);
        Assert.DoesNotContain(result.State.RulesState.Programs, value => value.CardId == existingId);
        Assert.Equal(
            [
                EngineEventKinds.CardMoved,
                EngineEventKinds.CreditsAdjusted,
                EngineEventKinds.CardMoved,
                EngineEventKinds.MemoryAdjusted,
                EngineEventKinds.CardInstalled,
                EngineEventKinds.CardMoved,
                EngineEventKinds.CardPlayCompleted,
                EngineEventKinds.TurnTimingWindowOpened,
            ],
            result.Events.Select(value => value.Kind));
        CreditsAdjustedEventPayload credits = Assert.IsType<CreditsAdjustedEventPayload>(
            result.Events.Single(value => value.Kind == EngineEventKinds.CreditsAdjusted).Payload);
        Assert.Equal(-fixture.IncomingInstallCost, credits.Delta);
        Assert.Equal(creditsBeforeInstall - fixture.IncomingInstallCost, credits.CreditsAfter);
        CardInstalledEventPayload installed = Assert.IsType<CardInstalledEventPayload>(
            result.Events.Single(value => value.Kind == EngineEventKinds.CardInstalled).Payload);
        Assert.Equal(fixture.IncomingInstallCost, installed.Cost);
        Assert.Equal(1, result.Events.Count(value => value.Kind == EngineEventKinds.CreditsAdjusted));
        Assert.Equal(3, result.Events.Count(value => value.Kind == EngineEventKinds.CardMoved));
        Assert.Equal(1, result.Events.Count(value => value.Kind == EngineEventKinds.MemoryAdjusted));
        Assert.DoesNotContain(result.Events, value => value.Kind == EngineEventKinds.ClickSpent);
        Assert.Empty(StateInvariantValidator.Collect(result.State, fixture.BehaviorCatalog, fixture.RuntimeCatalog));
    }

    [Fact]
    public void BehaviorEffectInstallRequiresFrozenCandidateDefinitionSnapshot()
    {
        CardInstanceId existingId = new("card:behavior-install-definition-snapshot");
        CardDefinitionId existingDefinition = new("test:behavior-install-definition-snapshot");
        Fixture fixture = CreateFixture(
            existingPrograms: [(existingId, existingDefinition, 4)],
            incomingMemoryUnits: 2);
        GameState opened = OpenBehaviorInstallChoice(fixture);
        InstallFrame frame = Assert.IsType<InstallFrame>(opened.ResolutionStack[^1]);
        InstallFrame forged = CloneInstallFrame(frame, dropCandidateDefinitions: true);
        GameState stale = ReplaceTopFrame(opened, forged);
        string hash = DeterministicHash.ForState(stale);

        Exception? collectException = Record.Exception(() => StateInvariantValidator.Collect(
            stale,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog));
        Assert.Null(collectException);
        Assert.Contains(
            StateInvariantValidator.Collect(stale, fixture.BehaviorCatalog, fixture.RuntimeCatalog),
            violation => violation.Code == "resolution.install-candidate-definitions-missing");

        ApplyResult rejected = Apply(stale, fixture, stale.PendingDecision!.LegalActions[0]);
        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Same(stale, rejected.State);
        Assert.Equal(hash, DeterministicHash.ForState(rejected.State));
        Assert.Empty(rejected.Events);
    }

    [Fact]
    public void BehaviorEffectInstallTrustBoundaryRejectsForgedParentSourceCardAndBindingWithoutMutation()
    {
        CardInstanceId existingId = new("card:behavior-install-trust-existing");
        CardDefinitionId existingDefinition = new("test:behavior-install-trust-existing");
        Fixture fixture = CreateFixture(
            existingPrograms: [(existingId, existingDefinition, 4)],
            incomingMemoryUnits: 2);
        GameState opened = OpenBehaviorInstallChoice(fixture);
        InstallFrame frame = Assert.IsType<InstallFrame>(opened.ResolutionStack[^1]);
        BehaviorResolutionFrame behavior = Assert.IsType<BehaviorResolutionFrame>(opened.ResolutionStack[^2]);
        ZoneId gripId = opened.GetZone(ZoneKind.RunnerGrip).Id;
        var variants = new[]
        {
            ReplaceTopFrame(
                opened,
                CloneInstallFrame(frame, parentIdentity: "parent:behavior-install-forged")),
            ReplaceTopFrame(
                opened,
                CloneInstallFrame(frame, sourceZoneId: gripId)),
            ReplaceTopFrame(
                opened,
                CloneInstallFrame(frame, cardId: existingId)),
            ReplaceTopFrame(
                opened,
                CloneInstallFrame(
                    frame,
                    candidateDefinitions: [new CardDefinitionId("test:behavior-install-forged-definition")])),
            ReplaceResolutionFrame(
                opened,
                opened.ResolutionStack.Count - 2,
                behavior.With(bindings: [new BehaviorBinding("chosen", [existingId])])),
            ReplaceResolutionStack(
                opened,
                [
                    .. opened.ResolutionStack,
                    new TurnFrame(PlayerSide.Runner, opened.Turn.TurnNumber, TurnFrameStage.ActionPriority),
                ]),
        };

        foreach (GameState forged in variants)
        {
            string hash = DeterministicHash.ForState(forged);
            Exception? collectException = Record.Exception(() => StateInvariantValidator.Collect(
                forged,
                fixture.BehaviorCatalog,
                fixture.RuntimeCatalog));
            Assert.Null(collectException);
            ApplyResult rejected = Apply(forged, fixture, forged.PendingDecision!.LegalActions[0]);
            Assert.False(rejected.Accepted);
            Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
            Assert.Same(forged, rejected.State);
            Assert.Equal(hash, DeterministicHash.ForState(rejected.State));
            Assert.Empty(rejected.Events);
        }

        RuntimeCardCatalog changedCatalog = new RuntimeCardCatalogBuilder("runtime:behavior-install-trust-changed")
            .Add(new CardRuntimeSpec(
                new CardDefinitionId("test:behavior-install-event"),
                PlayerSide.Runner,
                RuntimeCardClass.Event,
                playCost: 0))
            .Add(new CardRuntimeSpec(
                new CardDefinitionId("test:behavior-install-program"),
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: fixture.IncomingInstallCost,
                memoryUnits: fixture.IncomingMemoryUnits))
            .Add(new CardRuntimeSpec(
                existingDefinition,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 1,
                memoryUnits: 4))
            .Build();
        string changedCatalogHash = DeterministicHash.ForState(opened);
        PendingDecision pending = opened.PendingDecision!;
        ApplyResult catalogRejected = GameEngine.Apply(
            opened,
            new ActionSubmission(
                pending.Owner,
                pending.Id,
                pending.Revision,
                pending.LegalActions[0].Id),
            fixture.BehaviorCatalog,
            changedCatalog);
        Assert.False(catalogRejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, catalogRejected.Rejection?.Code);
        Assert.Same(opened, catalogRejected.State);
        Assert.Equal(changedCatalogHash, DeterministicHash.ForState(catalogRejected.State));
        Assert.Empty(catalogRejected.Events);
    }

    [Fact]
    public void InstallFrameContractPreservesBasicByteShapeAndBehaviorProvenance()
    {
        CardDefinitionId definition = new("test:behavior-install-frame-contract");
        CardInstanceId card = new("card:behavior-install-frame-contract");
        ZoneId source = new("zone:behavior-install-frame-source");
        ZoneId destination = new("zone:behavior-install-frame-destination");
        PlayerId owner = new("player:behavior-install-frame-owner");
        InstallFrame basic = new(
            owner,
            card,
            definition,
            RuntimeCardClass.Program,
            source,
            ZoneKind.RunnerGrip,
            InstallDestinationKind.RunnerRig,
            serverId: null,
            createNewRemote: false,
            installCost: 2,
            destinationZoneId: destination,
            candidateReplacementDefinitionIds: []);
        string basicJson = EngineJson.Serialize(basic);
        Assert.DoesNotContain("initiationKind", basicJson, StringComparison.Ordinal);
        Assert.DoesNotContain("parentFrameIdentity", basicJson, StringComparison.Ordinal);
        InstallFrame basicRoundTrip = EngineJson.Deserialize<InstallFrame>(basicJson);
        Assert.Equal(InstallInitiationKind.BasicAction, basicRoundTrip.InitiationKind);
        Assert.Null(basicRoundTrip.ParentFrameIdentity);

        InstallFrame behavior = new(
            owner,
            card,
            definition,
            RuntimeCardClass.Program,
            new ZoneId("zone:behavior-install-frame-stack"),
            ZoneKind.RunnerStack,
            InstallDestinationKind.RunnerRig,
            serverId: null,
            createNewRemote: false,
            installCost: 2,
            destinationZoneId: destination,
            candidateReplacementDefinitionIds: [],
            initiationKind: InstallInitiationKind.BehaviorEffect,
            parentFrameIdentity: "parent:behavior-install-frame");
        string behaviorJson = EngineJson.Serialize(behavior);
        InstallFrame behaviorRoundTrip = EngineJson.Deserialize<InstallFrame>(behaviorJson);
        InstallFrame behaviorClone = ResolutionFrameCloner.Copy(behavior) as InstallFrame
            ?? throw new InvalidOperationException("Install frame clone changed type.");
        Assert.Equal(InstallInitiationKind.BehaviorEffect, behaviorRoundTrip.InitiationKind);
        Assert.Equal("parent:behavior-install-frame", behaviorRoundTrip.ParentFrameIdentity);
        Assert.Equal(behavior.InitiationKind, behaviorClone.InitiationKind);
        Assert.Equal(behavior.ParentFrameIdentity, behaviorClone.ParentFrameIdentity);
        Assert.Throws<ArgumentOutOfRangeException>(() => new InstallFrame(
            owner,
            card,
            definition,
            RuntimeCardClass.Program,
            source,
            ZoneKind.RunnerStack,
            InstallDestinationKind.RunnerRig,
            serverId: null,
            createNewRemote: false,
            installCost: 2,
            initiationKind: (InstallInitiationKind)99,
            parentFrameIdentity: "parent:behavior-install-frame"));
        Assert.Throws<ArgumentException>(() => new InstallFrame(
            owner,
            card,
            definition,
            RuntimeCardClass.Program,
            source,
            ZoneKind.RunnerGrip,
            InstallDestinationKind.RunnerRig,
            serverId: null,
            createNewRemote: false,
            installCost: 2,
            initiationKind: InstallInitiationKind.BasicAction,
            parentFrameIdentity: "parent:unexpected"));
        Assert.Throws<ArgumentException>(() => new InstallFrame(
            owner,
            card,
            definition,
            RuntimeCardClass.Program,
            source,
            ZoneKind.RunnerStack,
            InstallDestinationKind.RunnerRig,
            serverId: null,
            createNewRemote: false,
            installCost: 2,
            initiationKind: InstallInitiationKind.BehaviorEffect));

        GameState baseline = GameState.CreateNew(seed: 42);
        BehaviorCatalog emptyCatalog = new BehaviorCatalogBuilder(EngineCompatibility.BehaviorRegistryVersion).Build();
        Assert.Throws<ResolutionFailureException>(() => GameEngine.BeginResolution(
            baseline,
            behavior,
            emptyCatalog));
    }

    private static ApplyResult Apply(GameState state, Fixture fixture, LegalAction action) =>
        GameEngine.Apply(
            state,
            new ActionSubmission(
                state.PendingDecision!.Owner,
                state.PendingDecision.Id,
                state.PendingDecision.Revision,
                action.Id),
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);

    private static GameState OpenBehaviorInstallChoice(Fixture fixture)
    {
        GameState disposition = OpenBehaviorDispositionChoice(fixture);
        LegalAction install = Assert.Single(
            disposition.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters choice
                && choice.OptionKeys.SequenceEqual(["install"]));
        GameState opened = Apply(disposition, fixture, install).State;
        Assert.Equal(DecisionKind.InstallChoice, opened.PendingDecision!.Kind);
        return opened;
    }

    private static GameState OpenBehaviorDispositionChoice(Fixture fixture)
    {
        GameState turnAction = OpenRunnerAction(fixture);
        LegalAction play = Assert.Single(
            turnAction.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.PlayCard);
        GameState played = Apply(turnAction, fixture, play).State;
        LegalAction selection = Assert.Single(
            played.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters choice
                && choice.CardIds.SequenceEqual([fixture.IncomingProgramId]));
        return Apply(played, fixture, selection).State;
    }

    private static void AssertOnlyGripOption(GameState disposition)
    {
        Assert.Equal(DecisionKind.BehaviorChoice, disposition.PendingDecision!.Kind);
        LegalAction action = Assert.Single(disposition.PendingDecision.LegalActions);
        BehaviorChoiceParameters parameters = Assert.IsType<BehaviorChoiceParameters>(action.Parameters);
        Assert.Equal(["grip"], parameters.OptionKeys);
    }

    private static InstallFrame CloneInstallFrame(
        InstallFrame source,
        CardInstanceId? cardId = null,
        ZoneId? sourceZoneId = null,
        string? parentIdentity = null,
        IReadOnlyList<CardDefinitionId>? candidateDefinitions = null,
        bool dropCandidateDefinitions = false) =>
        new(
            source.Version,
            source.BehaviorId,
            source.Owner,
            cardId ?? source.CardId,
            source.DefinitionId,
            source.CardClass,
            sourceZoneId ?? source.SourceZoneId,
            source.SourceZoneKind,
            source.DestinationKind,
            source.ServerId,
            source.CreateNewRemote,
            source.InstallCost,
            source.CandidateReplacementCardIds,
            source.RequiredReplacementCardIds,
            source.SelectedReplacementCardIds,
            source.DestinationZoneId,
            dropCandidateDefinitions
                ? null
                : candidateDefinitions ?? source.CandidateReplacementDefinitionIds,
            source.InitiationKind,
            parentIdentity ?? source.ParentFrameIdentity);

    private static GameState ReplaceTopFrame(GameState state, ResolutionFrame frame) =>
        ReplaceResolutionFrame(state, state.ResolutionStack.Count - 1, frame);

    private static GameState ReplaceResolutionFrame(
        GameState state,
        int index,
        ResolutionFrame frame)
    {
        ResolutionFrame[] resolutionStack = state.ResolutionStack.ToArray();
        resolutionStack[index] = frame;
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
            resolutionStack,
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

    private static GameState ReplaceResolutionStack(
        GameState state,
        IReadOnlyList<ResolutionFrame> resolutionStack) =>
        new GameState(
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
            resolutionStack,
            state.PendingDecision,
            state.ActionSequence,
            state.InitialSeed,
            state.RunState,
            state.RulesState,
            state.BehaviorRegistryVersion,
            state.BehaviorCatalogFingerprint,
            state.RuntimeCardCatalogVersion,
            state.RuntimeCardCatalogFingerprint);

    private static GameState OpenRunnerAction(Fixture fixture)
    {
        GameState state = GameEngine.BeginTurn(
            fixture.State,
            PlayerSide.Runner,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        for (int index = 0; index < 4; index++)
        {
            PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction pass = Assert.Single(
                pending.LegalActions,
                action => action.Kind == ActionKind.PassPriority);
            ApplyResult result = Apply(state, fixture, pass);
            Assert.True(result.Accepted, result.Rejection?.Message);
            state = result.State;
        }

        Assert.Equal(DecisionKind.TurnAction, state.PendingDecision!.Kind);
        return state;
    }

    private static Fixture CreateFixture(
        int runnerCredits = 8,
        int runnerClicks = 4,
        int incomingInstallCost = 3,
        int incomingMemoryUnits = 1,
        IReadOnlyList<(CardInstanceId Id, CardDefinitionId DefinitionId, int MemoryUnits)>? existingPrograms = null,
        CardDefinitionId? eventDefinitionOverride = null,
        BehaviorTarget? selectionTargetOverride = null,
        BehaviorTarget? installTargetOverride = null,
        int? existingStaticModifierDelta = null,
        string? incomingUniquenessKey = null,
        string? existingUniquenessKey = null)
    {
        CardDefinitionId eventDefinition = eventDefinitionOverride ?? new("test:behavior-install-event");
        CardDefinitionId incomingDefinition = new("test:behavior-install-program");
        CardInstanceId eventId = new("card:behavior-install-event");
        CardInstanceId incomingId = new("card:behavior-install-program");
        BehaviorCardFilter runnerCards = new(ownerSide: PlayerSide.Runner);
        BehaviorTarget selectionTarget = selectionTargetOverride
            ?? BehaviorTarget.CardInZone(BehaviorZone.Stack, runnerCards);
        BehaviorTarget installTarget = installTargetOverride
            ?? new BehaviorTarget(BehaviorTargetKind.ChosenCard);
        BehaviorChoiceSpec disposition = new(
            "behavior-install-disposition",
            BehaviorChooser.Controller,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            [
                new BehaviorChoiceOption(
                    "install",
                    [new BehaviorInstruction(
                        BehaviorInstructionKind.InstallCard,
                        target: installTarget,
                        sourceZone: BehaviorZone.Stack,
                        destinationZone: BehaviorZone.Rig)]),
                new BehaviorChoiceOption(
                    "grip",
                    [new BehaviorInstruction(
                        BehaviorInstructionKind.AddToHand,
                        target: new BehaviorTarget(BehaviorTargetKind.ChosenCard),
                        sourceZone: BehaviorZone.Stack,
                        destinationZone: BehaviorZone.Grip)]),
            ]);
        CardBehaviorDefinition definition = new(
            eventDefinition,
            PlayerSide.Runner,
            instructions:
            [
                new BehaviorInstruction(
                    BehaviorInstructionKind.Search,
                    sourceZone: BehaviorZone.Stack,
                    filter: runnerCards,
                    target: selectionTarget),
                new BehaviorInstruction(
                    BehaviorInstructionKind.ShuffleIntoDeck,
                    target: new BehaviorTarget(BehaviorTargetKind.ChosenCard),
                    sourceZone: BehaviorZone.Stack,
                    destinationZone: BehaviorZone.Stack),
                new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: disposition),
            ]);
        BehaviorCatalogBuilder behaviorBuilder = new BehaviorCatalogBuilder("test:behavior-install-catalog")
            .Add(definition);

        RuntimeCardCatalogBuilder runtimeBuilder = new RuntimeCardCatalogBuilder("runtime:behavior-install-catalog")
            .Add(new CardRuntimeSpec(
                eventDefinition,
                PlayerSide.Runner,
                RuntimeCardClass.Event,
                playCost: 0))
            .Add(new CardRuntimeSpec(
                incomingDefinition,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: incomingInstallCost,
                memoryUnits: incomingMemoryUnits,
                unique: incomingUniquenessKey is not null,
                uniquenessKey: incomingUniquenessKey));
        foreach ((CardInstanceId id, CardDefinitionId definitionId, int memoryUnits) in existingPrograms ?? [])
        {
            runtimeBuilder.Add(new CardRuntimeSpec(
                definitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 1,
                memoryUnits: memoryUnits,
                unique: existingUniquenessKey is not null,
                uniquenessKey: existingUniquenessKey));
            if (existingStaticModifierDelta is { } staticDelta)
            {
                behaviorBuilder.Add(new CardBehaviorDefinition(
                    definitionId,
                    PlayerSide.Runner,
                    staticModifiers: [new BehaviorStaticModifier(
                        BehaviorStaticModifierKind.MemoryUnits,
                        staticDelta)]));
            }
        }

        BehaviorCatalog behaviors = behaviorBuilder.Build();
        RuntimeCardCatalog runtime = runtimeBuilder.Build();
        GameState baseline = GameState.CreateNew(
            seed: 42042,
            behaviorRegistryVersion: behaviors.Version,
            behaviorCatalogFingerprint: behaviors.Fingerprint,
            runtimeCardCatalogVersion: runtime.Version,
            runtimeCardCatalogFingerprint: runtime.Fingerprint);
        PlayerState corp = baseline.Players.Single(value => value.Side == PlayerSide.Corp);
        PlayerState runner = baseline.Players.Single(value => value.Side == PlayerSide.Runner);
        runner = new PlayerState(runner.Id, runner.Side, runnerCredits, runnerClicks, runner.AgendaPoints);
        ZoneState grip = baseline.GetZone(ZoneKind.RunnerGrip);
        ZoneState stack = baseline.GetZone(ZoneKind.RunnerStack);
        ZoneState rig = baseline.GetZone(ZoneKind.RunnerRig);
        CardInstanceState eventCard = new(eventId, eventDefinition, grip.Id, PlayerSide.Runner);
        CardInstanceState incomingCard = new(incomingId, incomingDefinition, stack.Id, PlayerSide.Runner);
        CardInstanceState[] existingCards = (existingPrograms ?? [])
            .Select(program => new CardInstanceState(
                program.Id,
                program.DefinitionId,
                rig.Id,
                PlayerSide.Runner,
                faceUp: true))
            .ToArray();
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == grip.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(eventId).ToArray())
                : zone.Id == stack.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(incomingId).ToArray())
                    : zone.Id == rig.Id
                        ? new ZoneState(
                            zone.Id,
                            zone.Kind,
                            zone.OwnerSide,
                            zone.ServerId,
                            zone.CardIds.Concat(existingCards.Select(card => card.Id)).ToArray())
                        : zone)
            .ToArray();
        CardInstanceState[] cards = [eventCard, incomingCard, .. existingCards];
        PlayerState[] players = [corp, runner];
        RulesState? rules = existingCards.Length == 0
            ? RulesState.CreateDefault(players)
            : BuildRules(players, existingCards, runtime);
        GameState state = new(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            players,
            cards,
            zones,
            baseline.Servers,
            new TurnState(PlayerSide.Runner, TurnPhase.Runner, 1),
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            rulesState: rules,
            behaviorRegistryVersion: behaviors.Version,
            behaviorCatalogFingerprint: behaviors.Fingerprint,
            runtimeCardCatalogVersion: runtime.Version,
            runtimeCardCatalogFingerprint: runtime.Fingerprint);
        return new Fixture(
            state,
            behaviors,
            runtime,
            runner.Id,
            eventId,
            incomingId,
            incomingInstallCost,
            incomingMemoryUnits);
    }

    private static RulesState BuildRules(
        IReadOnlyList<PlayerState> players,
        IReadOnlyList<CardInstanceState> existing,
        RuntimeCardCatalog runtime)
    {
        RulesState defaults = RulesState.CreateDefault(players);
        PlayerId runnerId = players.Single(value => value.Side == PlayerSide.Runner).Id;
        ProgramMemoryState[] programs = existing
            .Select(card => new ProgramMemoryState(
                card.Id,
                runnerId,
                runtime.GetSpec(card.DefinitionId).MemoryUnits))
            .ToArray();
        PlayerRulesState[] rulesPlayers = defaults.Players
            .Select(player => player.PlayerId == runnerId
                ? new PlayerRulesState(
                    player.PlayerId,
                    player.BaseHandSize,
                    player.CoreDamage,
                    player.Tags,
                    player.BaseMemory,
                    programs.Sum(value => value.MemoryCost))
                : player)
            .ToArray();
        return new RulesState(
            defaults.Version,
            rulesPlayers,
            programs,
            defaults.Counters,
            defaults.LingeringEffects,
            defaults.Triggers,
            defaults.Interrupts);
    }

    private sealed record Fixture(
        GameState State,
        BehaviorCatalog BehaviorCatalog,
        RuntimeCardCatalog RuntimeCatalog,
        PlayerId RunnerId,
        CardInstanceId EventId,
        CardInstanceId IncomingProgramId,
        int IncomingInstallCost,
        int IncomingMemoryUnits);
}

file static class BehaviorInstallTestStateExtensions
{
    public static ZoneState ZoneOf(this GameState state, CardInstanceId cardId) =>
        state.Zones.Single(zone => zone.Id == state.GetCard(cardId).ZoneId);
}

using Xunit;

namespace Netrunner.Core.Tests;

public sealed class CostModifierContractTests
{
    private static readonly CardDefinitionId DefinitionId = new("test:cost-modifier");
    private static readonly CardInstanceId SourceCardId = new("card:cost-modifier");
    private static readonly CardDefinitionId ExistingDefinitionId = new("test:cost-modifier-existing");
    private static readonly CardInstanceId ExistingCardId = new("card:cost-modifier-existing");

    [Fact]
    public void CostModifierDeclarationIsTypedImmutableAndRoundTrips()
    {
        BehaviorCostModifier modifier = new(
            BehaviorCostModifierKind.RunnerInstallCreditCost,
            -2,
            BehaviorCondition.SuccessfulRunThisTurn(),
            "successful-run-this-turn");
        CardBehaviorDefinition definition = new(
            DefinitionId,
            PlayerSide.Runner,
            costModifiers: [modifier]);

        string json = EngineJson.Serialize(definition);
        CardBehaviorDefinition restored = EngineJson.Deserialize<CardBehaviorDefinition>(json);

        Assert.Contains("\"costModifiers\"", json, StringComparison.Ordinal);
        Assert.Contains("\"runnerInstallCreditCost\"", json, StringComparison.Ordinal);
        Assert.Equal(definition.CostModifiers, restored.CostModifiers);
        Assert.Equal(modifier, restored.CostModifiers!.Single());
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorCostModifier(
            BehaviorCostModifierKind.RunnerInstallCreditCost,
            0,
            BehaviorCondition.SuccessfulRunThisTurn(),
            "reason"));
        Assert.Throws<ArgumentException>(() => new BehaviorCostModifier(
            BehaviorCostModifierKind.RunnerInstallCreditCost,
            -1,
            BehaviorCondition.InstalledThisTurn(),
            "reason"));
        Assert.Throws<ArgumentException>(() => new BehaviorCostModifier(
            BehaviorCostModifierKind.RunnerInstallCreditCost,
            -1,
            BehaviorCondition.SuccessfulRunThisTurn(),
            "not a stable reason"));
    }

    [Fact]
    public void CostModifierChangesStructuralCatalogEqualityAndFingerprint()
    {
        CardBehaviorDefinition unmodified = new(DefinitionId, PlayerSide.Runner);
        CardBehaviorDefinition modified = new(
            DefinitionId,
            PlayerSide.Runner,
            costModifiers:
            [
                new BehaviorCostModifier(
                    BehaviorCostModifierKind.IcebreakerBreakCreditCost,
                    -1,
                    BehaviorCondition.SuccessfulRunThisTurn(),
                    "successful-run-this-turn"),
            ]);

        BehaviorCatalog first = new BehaviorCatalogBuilder("test:cost-modifier-catalog")
            .Add(unmodified)
            .Build();
        BehaviorCatalog second = new BehaviorCatalogBuilder("test:cost-modifier-catalog")
            .Add(modified)
            .Build();

        Assert.NotEqual(first.Fingerprint, second.Fingerprint);
        Assert.Throws<InvalidOperationException>(() => new BehaviorCatalogBuilder("test:cost-modifier-conflict")
            .Add(unmodified)
            .Add(modified));

        // Passing an empty declaration is semantically the same as omitting
        // it, and must retain the historical definition JSON shape.
        string oldJson = EngineJson.Serialize(unmodified);
        string emptyJson = EngineJson.Serialize(new CardBehaviorDefinition(
            DefinitionId,
            PlayerSide.Runner,
            costModifiers: []));
        Assert.Equal(oldJson, emptyJson);
        Assert.DoesNotContain("costModifiers", oldJson, StringComparison.Ordinal);
    }

    [Fact]
    public void CostQuoteChecksTheOrderedSumClampsAtZeroAndRejectsOverflow()
    {
        CostAdjustment reduction = new(
            BehaviorCostModifierKind.RunnerInstallCreditCost,
            -10,
            "successful-run-this-turn",
            DefinitionId,
            SourceCardId);
        CostQuote quote = new(5, [reduction]);

        Assert.Equal(5, quote.BaseCost);
        Assert.Equal(0, quote.EffectiveCost);
        Assert.Same(quote.Adjustments, quote.Reasons);

        string json = EngineJson.Serialize(quote);
        CostQuote restored = EngineJson.Deserialize<CostQuote>(json);
        Assert.Equal(quote, restored);
        Assert.Throws<ArgumentException>(() => new CostQuote(
            5,
            4,
            [reduction]));
        Assert.Throws<OverflowException>(() => new CostQuote(
            int.MaxValue,
            [
                new CostAdjustment(
                    BehaviorCostModifierKind.RunnerInstallCreditCost,
                    1,
                    "overflow"),
            ]));
    }

    [Fact]
    public void UnmodifiedActionPayloadsOmitQuoteAndModifiedPayloadsExposeDetachedBreakdown()
    {
        InstallCardParameters unmodifiedInstall = new(
            SourceCardId,
            InstallDestinationKind.RunnerRig,
            serverId: null,
            createNewRemote: false,
            creditCost: 5);
        RunBreakParameters unmodifiedBreak = new(SourceCardId, quantity: 1);
        string unmodifiedInstallJson = EngineJson.Serialize(unmodifiedInstall);
        string unmodifiedBreakJson = EngineJson.Serialize(unmodifiedBreak);
        Assert.DoesNotContain("costQuote", unmodifiedInstallJson, StringComparison.Ordinal);
        Assert.DoesNotContain("costQuote", unmodifiedBreakJson, StringComparison.Ordinal);

        CostQuote quote = new(
            5,
            [
                new CostAdjustment(
                    BehaviorCostModifierKind.RunnerInstallCreditCost,
                    -2,
                    "successful-run-this-turn",
                    DefinitionId,
                    SourceCardId),
            ]);
        InstallCardParameters modifiedInstall = new(
            SourceCardId,
            InstallDestinationKind.RunnerRig,
            serverId: null,
            createNewRemote: false,
            creditCost: quote.EffectiveCost,
            costQuote: quote);
        string modifiedJson = EngineJson.Serialize(modifiedInstall);
        InstallCardParameters restoredInstall = EngineJson.Deserialize<InstallCardParameters>(modifiedJson);

        Assert.Contains("\"baseCost\":5", modifiedJson, StringComparison.Ordinal);
        Assert.Contains("\"effectiveCost\":3", modifiedJson, StringComparison.Ordinal);
        Assert.Contains("\"reasonKey\":\"successful-run-this-turn\"", modifiedJson, StringComparison.Ordinal);
        Assert.Equal(quote, restoredInstall.CostQuote);

        CostQuote breakQuote = new(
            2,
            [
                new CostAdjustment(
                    BehaviorCostModifierKind.IcebreakerBreakCreditCost,
                    -1,
                    "successful-run-this-turn",
                    DefinitionId,
                    SourceCardId),
            ]);
        RunBreakParameters modifiedBreak = new(
            SourceCardId,
            quantity: 1,
            costQuote: breakQuote);
        RunBreakParameters restoredBreak = EngineJson.Deserialize<RunBreakParameters>(
            EngineJson.Serialize(modifiedBreak));
        Assert.Equal(breakQuote, restoredBreak.CostQuote);
    }

    [Fact]
    public void SuccessfulRunProjectionRequiresFormalCurrentRunnerHistory()
    {
        BehaviorCostModifier modifier = new(
            BehaviorCostModifierKind.RunnerInstallCreditCost,
            -2,
            "successful-run-this-turn");
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder(
                EngineCompatibility.BehaviorRegistryVersion)
            .Add(new CardBehaviorDefinition(
                DefinitionId,
                PlayerSide.Runner,
                costModifiers: [modifier]))
            .Build();

        GameState missingHistory = CreateRunnerCardState(
            behaviorCatalog,
            runtimeCatalog: null,
            runnerCredits: 3,
            history: null);
        Assert.Null(GameEngine.ProjectCostQuote(
            missingHistory,
            behaviorCatalog,
            SourceCardId,
            BehaviorCostModifierKind.RunnerInstallCreditCost,
            5));

        GameState successfulHistory = CreateRunnerCardState(
            behaviorCatalog,
            runtimeCatalog: null,
            runnerCredits: 3,
            history: SuccessfulRunnerHistory());
        CostQuote quote = Assert.IsType<CostQuote>(GameEngine.ProjectCostQuote(
            successfulHistory,
            behaviorCatalog,
            SourceCardId,
            BehaviorCostModifierKind.RunnerInstallCreditCost,
            5));
        Assert.Equal(5, quote.BaseCost);
        Assert.Equal(3, quote.EffectiveCost);
        Assert.Equal(DefinitionId, quote.Adjustments.Single().SourceDefinitionId);
        Assert.Equal(SourceCardId, quote.Adjustments.Single().SourceCardId);
    }

    [Fact]
    public void InstallMaterializationUsesEffectiveCostForAffordability()
    {
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:cost-modifier")
            .Add(new CardRuntimeSpec(
                DefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Resource,
                installCost: 5))
            .Build();
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder(
                EngineCompatibility.BehaviorRegistryVersion)
            .Add(new CardBehaviorDefinition(
                DefinitionId,
                PlayerSide.Runner,
                costModifiers:
                [
                    new BehaviorCostModifier(
                        BehaviorCostModifierKind.RunnerInstallCreditCost,
                        -2,
                        "successful-run-this-turn"),
                ]))
            .Build();

        GameState beforeSuccess = CreateRunnerCardState(
            behaviorCatalog,
            runtimeCatalog,
            runnerCredits: 3,
            history: new TurnHistoryState());
        Assert.Empty(GameEngine.MaterializeRunnerInstallParameters(
            beforeSuccess,
            behaviorCatalog,
            runtimeCatalog));

        GameState afterSuccess = CreateRunnerCardState(
            behaviorCatalog,
            runtimeCatalog,
            runnerCredits: 3,
            history: SuccessfulRunnerHistory());
        InstallCardParameters action = Assert.Single(GameEngine.MaterializeRunnerInstallParameters(
            afterSuccess,
            behaviorCatalog,
            runtimeCatalog));
        Assert.Equal(3, action.CreditCost);
        Assert.Equal(5, action.CostQuote!.BaseCost);
        Assert.Equal(3, action.CostQuote.EffectiveCost);
    }

    [Fact]
    public void InstallChoiceFrameFreezesQuoteAcrossSaveLoadAndPaysEffectiveCost()
    {
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:cost-modifier-frame")
            .Add(new CardRuntimeSpec(
                DefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 5,
                memoryUnits: 1,
                unique: true,
                uniquenessKey: "cost-modifier-unique"))
            .Add(new CardRuntimeSpec(
                ExistingDefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 1,
                memoryUnits: 1,
                unique: true,
                uniquenessKey: "cost-modifier-unique"))
            .Build();
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder(
                EngineCompatibility.BehaviorRegistryVersion)
            .Add(new CardBehaviorDefinition(
                DefinitionId,
                PlayerSide.Runner,
                costModifiers:
                [
                    new BehaviorCostModifier(
                        BehaviorCostModifierKind.RunnerInstallCreditCost,
                        -2,
                        "successful-run-this-turn"),
                ]))
            .Build();
        GameState initial = CreateRunnerInstallReplacementState(
            behaviorCatalog,
            runtimeCatalog,
            runnerCredits: 3);
        GameState turnAction = GameEngine.BeginResolution(
            initial,
            new TurnFrame(
                PlayerSide.Runner,
                initial.Turn.TurnNumber,
                TurnFrameStage.AwaitingAction),
            behaviorCatalog,
            runtimeCatalog);
        LegalAction install = Assert.Single(
            turnAction.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.InstallCard);
        InstallCardParameters issued = Assert.IsType<InstallCardParameters>(install.Parameters);
        Assert.False(issued.FinalizeSelection);
        Assert.Equal(3, issued.CreditCost);
        Assert.Equal(5, issued.CostQuote!.BaseCost);
        Assert.Equal(3, issued.CostQuote.EffectiveCost);

        ApplyResult opened = GameEngine.Apply(
            turnAction,
            new ActionSubmission(
                turnAction.PendingDecision.Owner,
                turnAction.PendingDecision.Id,
                turnAction.PendingDecision.Revision,
                install.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        InstallFrame frame = Assert.IsType<InstallFrame>(opened.State.ResolutionStack[^1]);
        Assert.Equal(3, frame.InstallCost);
        Assert.Equal(issued.CostQuote, frame.CostQuote);
        string frameJson = EngineJson.Serialize(frame);
        Assert.Contains("\"baseCost\":5", frameJson, StringComparison.Ordinal);
        Assert.Contains("\"effectiveCost\":3", frameJson, StringComparison.Ordinal);
        InstallFrame restoredFrame = EngineJson.Deserialize<InstallFrame>(frameJson);
        Assert.Equal(frame.CostQuote, restoredFrame.CostQuote);

        string stateJson = EngineJson.Serialize(opened.State);
        GameState restored = GameState.FromSnapshot(
            opened.State.CreateSnapshot(behaviorCatalog, runtimeCatalog),
            behaviorCatalog,
            runtimeCatalog);
        Assert.Equal(stateJson, EngineJson.Serialize(restored));
        Assert.Equal(
            frame.CostQuote,
            Assert.IsType<InstallFrame>(restored.ResolutionStack[^1]).CostQuote);

        PendingDecision choice = Assert.IsType<PendingDecision>(restored.PendingDecision);
        LegalAction chooseExisting = Assert.Single(
            choice.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters)
                .SelectedReplacementCardIds.SequenceEqual([ExistingCardId]));
        ApplyResult selected = GameEngine.Apply(
            restored,
            new ActionSubmission(choice.Owner, choice.Id, choice.Revision, chooseExisting.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(selected.Accepted, selected.Rejection?.Message);
        PendingDecision finalDecision = Assert.IsType<PendingDecision>(selected.State.PendingDecision);
        LegalAction finalize = Assert.Single(
            finalDecision.LegalActions,
            action => action.Kind == ActionKind.InstallCard
                && Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);
        InstallCardParameters finalParameters = Assert.IsType<InstallCardParameters>(finalize.Parameters);
        Assert.Equal(3, finalParameters.CreditCost);
        Assert.Equal(issued.CostQuote, finalParameters.CostQuote);

        ApplyResult paid = GameEngine.Apply(
            selected.State,
            new ActionSubmission(finalDecision.Owner, finalDecision.Id, finalDecision.Revision, finalize.Id),
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(paid.Accepted, paid.Rejection?.Message);
        Assert.Equal(0, paid.State.GetPlayer(finalDecision.Owner).Credits);
        Assert.Equal(ZoneKind.RunnerRig, paid.State.GetZone(paid.State.GetCard(SourceCardId).ZoneId).Kind);
        Assert.Equal(ZoneKind.RunnerHeap, paid.State.GetZone(paid.State.GetCard(ExistingCardId).ZoneId).Kind);
    }

    [Fact]
    public void BreakMaterializationAndPaymentUseTheEffectiveQuoteOnlyAfterSuccess()
    {
        (GameState fixture, CardInstanceId breakerId, _) =
            EncounterBreakerTests.CreateRunStateWithBreaker(
                FixtureIceBehaviorSpecs.BreakerBarrier,
                iceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines);
        // Use the fixture's existing typed ICE registration and pin the
        // rebuilt catalog to the state. The modifier is the only changed
        // behavior registration.
        Assert.True(FixtureBehaviorCatalog.Catalog.TryGetIce(
            FixtureIceBehaviorSpecs.TwoSubroutines,
            out IceBehaviorSpec? iceSpec));
        Assert.True(FixtureBehaviorCatalog.Catalog.TryGetIcebreaker(
            FixtureIceBehaviorSpecs.BreakerBarrier,
            out IcebreakerBehaviorSpec? breakerSpec));
        IcebreakerBehaviorSpec modifiedBreaker = new(
            breakerSpec!.DefinitionId,
            breakerSpec.BaseStrength,
            breakerSpec.InterfaceType,
            2,
            breakerSpec.BreakLimit,
            breakerSpec.PumpCost,
            breakerSpec.PumpStrength);
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder(
                EngineCompatibility.BehaviorRegistryVersion)
            .AddIce(iceSpec!)
            .AddIcebreaker(modifiedBreaker)
            .Add(new CardBehaviorDefinition(
                FixtureIceBehaviorSpecs.BreakerBarrier,
                PlayerSide.Runner,
                costModifiers:
                [
                    new BehaviorCostModifier(
                        BehaviorCostModifierKind.IcebreakerBreakCreditCost,
                        -1,
                        "successful-run-this-turn"),
                ]))
            .Build();
        fixture = RebuildWithBehaviorCatalog(fixture, behaviorCatalog);

        // The pre-success branch has no formal current Runner history. Core
        // must therefore issue the historical unmodified break payload.
        GameState beforeSuccess = ReachEncounter(fixture, behaviorCatalog);
        RunBreakParameters baseParameters = Assert.IsType<RunBreakParameters>(
            Assert.Single(
                beforeSuccess.PendingDecision!.LegalActions,
                action => action.Kind == ActionKind.BreakSubroutines
                    && ((RunBreakParameters)action.Parameters).BreakerId == breakerId
                    && ((RunBreakParameters)action.Parameters).Quantity == 1
                    && ((RunBreakParameters)action.Parameters).SubroutineOrdinals!.SequenceEqual([0]))
            .Parameters);
        Assert.Null(baseParameters.CostQuote);

        // Establish the formal successful-run fact before starting this run;
        // no action payload is rewritten after materialization.
        TurnState successfulTurn = new(
            PlayerSide.Runner,
            TurnPhase.Runner,
            fixture.Turn.TurnNumber,
            TurnStep.RunnerAction,
            SuccessfulRunnerHistory(RunTimingTests.RemoteId(fixture)));
        GameState successfulFixture = Rebuild(fixture, successfulTurn, pending: null);
        GameState successful = ReachEncounter(successfulFixture, behaviorCatalog);

        LegalAction breakAction = Assert.Single(
            successful.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.BreakSubroutines
                && ((RunBreakParameters)action.Parameters).BreakerId == breakerId
                && ((RunBreakParameters)action.Parameters).Quantity == 1
                && ((RunBreakParameters)action.Parameters).SubroutineOrdinals!.SequenceEqual([0]));
        RunBreakParameters afterSuccess = Assert.IsType<RunBreakParameters>(breakAction.Parameters);
        Assert.Equal(2, afterSuccess.CostQuote!.BaseCost);
        Assert.Equal(1, afterSuccess.CostQuote.EffectiveCost);

        ApplyResult paid = ApplyCatalogResult(successful, behaviorCatalog, breakAction);
        Assert.True(paid.Accepted, paid.Rejection?.Message);
        Assert.Equal(4, paid.State.GetPlayer(
            paid.State.Players.Single(player => player.Side == PlayerSide.Runner).Id).Credits);
    }

    [Fact]
    public void BreakQuoteOffersEveryEffectiveCostWalletHostedSplitAndObservationDetachesBreakdown()
    {
        (GameState fixture, CardInstanceId breakerId, _) =
            EncounterBreakerTests.CreateRunStateWithBreaker(
                FixtureIceBehaviorSpecs.BreakerBarrier,
                iceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines);
        Assert.True(FixtureBehaviorCatalog.Catalog.TryGetIce(
            FixtureIceBehaviorSpecs.TwoSubroutines,
            out IceBehaviorSpec? iceSpec));
        Assert.True(FixtureBehaviorCatalog.Catalog.TryGetIcebreaker(
            FixtureIceBehaviorSpecs.BreakerBarrier,
            out IcebreakerBehaviorSpec? breakerSpec));
        CardDefinitionId eventDefinition = new("test:cost-modifier-run-event");
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder(
                EngineCompatibility.BehaviorRegistryVersion)
            .AddIce(iceSpec!)
            .AddIcebreaker(new IcebreakerBehaviorSpec(
                breakerSpec!.DefinitionId,
                breakerSpec.BaseStrength,
                breakerSpec.InterfaceType,
                2,
                breakerSpec.BreakLimit,
                breakerSpec.PumpCost,
                breakerSpec.PumpStrength))
            .Add(new CardBehaviorDefinition(
                FixtureIceBehaviorSpecs.BreakerBarrier,
                PlayerSide.Runner,
                costModifiers:
                [
                    new BehaviorCostModifier(
                        BehaviorCostModifierKind.IcebreakerBreakCreditCost,
                        -1,
                        "successful-run-this-turn"),
                ]))
            .AddRunEvent(new RunEventBehaviorSpec(eventDefinition, cost: 0, hostedCredits: 2))
            .Build();
        (fixture, CardInstanceId eventId) = AddGripCard(fixture, eventDefinition);
        fixture = RebuildWithBehaviorCatalog(fixture, behaviorCatalog);
        TurnState successfulTurn = new(
            PlayerSide.Runner,
            TurnPhase.Runner,
            fixture.Turn.TurnNumber,
            TurnStep.RunnerAction,
            SuccessfulRunnerHistory(RunTimingTests.RemoteId(fixture)));
        fixture = Rebuild(fixture, successfulTurn, pending: null);

        GameState prompt = GameEngine.BeginRun(
            fixture,
            RunTimingTests.RemoteId(fixture),
            behaviorCatalog,
            initiatingSource: eventId);
        GameState encounter = AdvanceToEncounter(prompt, behaviorCatalog);
        LegalAction walletOnly = Assert.Single(encounter.PendingDecision!.LegalActions, action =>
            action.Kind == ActionKind.BreakSubroutines
            && ((RunBreakParameters)action.Parameters).BreakerId == breakerId
            && ((RunBreakParameters)action.Parameters).Quantity == 1
                    && ((RunBreakParameters)action.Parameters).SubroutineOrdinals!.SequenceEqual([0])
            && ((RunBreakParameters)action.Parameters).HostedCredits == 0);
        LegalAction hostedOnly = Assert.Single(encounter.PendingDecision.LegalActions, action =>
            action.Kind == ActionKind.BreakSubroutines
            && ((RunBreakParameters)action.Parameters).BreakerId == breakerId
            && ((RunBreakParameters)action.Parameters).Quantity == 1
                    && ((RunBreakParameters)action.Parameters).SubroutineOrdinals!.SequenceEqual([0])
            && ((RunBreakParameters)action.Parameters).HostedCredits == 1);
        RunBreakParameters hostedParameters = Assert.IsType<RunBreakParameters>(hostedOnly.Parameters);
        Assert.Equal(2, hostedParameters.CostQuote!.BaseCost);
        Assert.Equal(1, hostedParameters.CostQuote.EffectiveCost);

        Observation observation = ObservationBuilder.Build(encounter, PlayerSide.Runner, behaviorCatalog);
        Assert.NotSame(encounter.PendingDecision.LegalActions, observation.LegalActions);
        LegalAction observedBreak = Assert.Single(
            observation.LegalActions,
            action => action.Kind == ActionKind.BreakSubroutines
                && ((RunBreakParameters)action.Parameters).BreakerId == breakerId
                && ((RunBreakParameters)action.Parameters).Quantity == 1
                    && ((RunBreakParameters)action.Parameters).SubroutineOrdinals!.SequenceEqual([0])
                && ((RunBreakParameters)action.Parameters).HostedCredits == 1);
        Assert.NotSame(hostedOnly, observedBreak);
        Assert.Equal(hostedParameters.CostQuote, ((RunBreakParameters)observedBreak.Parameters).CostQuote);
        string observationJson = EngineJson.Serialize(observation);
        Assert.Contains("\"baseCost\":2", observationJson, StringComparison.Ordinal);
        Assert.Contains("\"effectiveCost\":1", observationJson, StringComparison.Ordinal);
        Assert.Contains("\"reasonKey\":\"successful-run-this-turn\"", observationJson, StringComparison.Ordinal);
        Assert.Contains(breakerId.Value, observationJson, StringComparison.Ordinal);

        ApplyResult walletPaid = ApplyCatalogResult(encounter, behaviorCatalog, walletOnly);
        Assert.True(walletPaid.Accepted, walletPaid.Rejection?.Message);
        Assert.Equal(4, walletPaid.State.GetPlayer(
            walletPaid.State.Players.Single(player => player.Side == PlayerSide.Runner).Id).Credits);
        Assert.Equal(2, walletPaid.State.RunState!.RunCredits);

        // The remaining subroutine is offered again with both legal splits;
        // hosted-only payment consumes the run pool and does not touch wallet.
        LegalAction hostedOnlySecond = Assert.Single(walletPaid.State.PendingDecision!.LegalActions, action =>
            action.Kind == ActionKind.BreakSubroutines
            && ((RunBreakParameters)action.Parameters).BreakerId == breakerId
            && ((RunBreakParameters)action.Parameters).Quantity == 1
                    && ((RunBreakParameters)action.Parameters).SubroutineOrdinals!.SequenceEqual([1])
            && ((RunBreakParameters)action.Parameters).HostedCredits == 1);
        ApplyResult hostedPaid = ApplyCatalogResult(walletPaid.State, behaviorCatalog, hostedOnlySecond);
        Assert.True(hostedPaid.Accepted, hostedPaid.Rejection?.Message);
        Assert.Equal(4, hostedPaid.State.GetPlayer(
            hostedPaid.State.Players.Single(player => player.Side == PlayerSide.Runner).Id).Credits);
        Assert.Equal(1, hostedPaid.State.RunState!.RunCredits);
    }

    [Fact]
    public void ForgedCostQuotesAreRejectedAtomicallyBeforeInstallOrBreakPayment()
    {
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:cost-modifier-tamper")
            .Add(new CardRuntimeSpec(
                DefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 5,
                memoryUnits: 1,
                unique: true,
                uniquenessKey: "cost-modifier-tamper-unique"))
            .Add(new CardRuntimeSpec(
                ExistingDefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 1,
                memoryUnits: 1,
                unique: true,
                uniquenessKey: "cost-modifier-tamper-unique"))
            .Build();
        BehaviorCatalog installCatalog = new BehaviorCatalogBuilder(
                EngineCompatibility.BehaviorRegistryVersion)
            .Add(new CardBehaviorDefinition(
                DefinitionId,
                PlayerSide.Runner,
                costModifiers:
                [
                    new BehaviorCostModifier(
                        BehaviorCostModifierKind.RunnerInstallCreditCost,
                        -2,
                        "successful-run-this-turn"),
                ]))
            .Build();
        GameState turnAction = GameEngine.BeginResolution(
            CreateRunnerInstallReplacementState(installCatalog, runtimeCatalog, runnerCredits: 3),
            new TurnFrame(PlayerSide.Runner, 1, TurnFrameStage.AwaitingAction),
            installCatalog,
            runtimeCatalog);
        LegalAction install = Assert.Single(
            turnAction.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.InstallCard);
        ApplyResult opened = GameEngine.Apply(
            turnAction,
            new ActionSubmission(
                turnAction.PendingDecision.Owner,
                turnAction.PendingDecision.Id,
                turnAction.PendingDecision.Revision,
                install.Id),
            installCatalog,
            runtimeCatalog);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        PendingDecision installChoice = Assert.IsType<PendingDecision>(opened.State.PendingDecision);
        InstallCardParameters issued = Assert.IsType<InstallCardParameters>(
            Assert.Single(installChoice.LegalActions, action => action.Kind == ActionKind.InstallCard).Parameters);
        CostQuote staleInstallQuote = new(
            6,
            [new CostAdjustment(
                BehaviorCostModifierKind.RunnerInstallCreditCost,
                -2,
                "successful-run-this-turn",
                DefinitionId,
                SourceCardId)]);
        LegalAction forgedInstall = new(
            installChoice.LegalActions[0].Id,
            ActionKind.InstallCard,
            installChoice.LegalActions[0].Description,
            new InstallCardParameters(
                issued.CardId,
                issued.DestinationKind,
                issued.ServerId,
                issued.CreateNewRemote,
                staleInstallQuote.EffectiveCost,
                issued.CandidateReplacementCardIds,
                issued.RequiredReplacementCardIds,
                issued.SelectedReplacementCardIds,
                issued.FinalizeSelection,
                staleInstallQuote));
        GameState tamperedInstall = ReplacePending(
            opened.State,
            new PendingDecision(
                installChoice.Id,
                installChoice.Revision,
                installChoice.Owner,
                installChoice.Kind,
                installChoice.Prompt,
                [forgedInstall]));
        string installHash = DeterministicHash.ForState(tamperedInstall);
        ApplyResult installRejected = GameEngine.Apply(
            tamperedInstall,
            new ActionSubmission(
                installChoice.Owner,
                installChoice.Id,
                installChoice.Revision,
                forgedInstall.Id),
            installCatalog,
            runtimeCatalog);
        Assert.False(installRejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, installRejected.Rejection?.Code);
        Assert.Same(tamperedInstall, installRejected.State);
        Assert.Equal(installHash, DeterministicHash.ForState(installRejected.State));
        Assert.Empty(installRejected.Events);

        (GameState breakFixture, CardInstanceId breakerId, _) =
            EncounterBreakerTests.CreateRunStateWithBreaker(
                FixtureIceBehaviorSpecs.BreakerBarrier,
                iceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines);
        Assert.True(FixtureBehaviorCatalog.Catalog.TryGetIce(
            FixtureIceBehaviorSpecs.TwoSubroutines,
            out IceBehaviorSpec? iceSpec));
        Assert.True(FixtureBehaviorCatalog.Catalog.TryGetIcebreaker(
            FixtureIceBehaviorSpecs.BreakerBarrier,
            out IcebreakerBehaviorSpec? breakerSpec));
        BehaviorCatalog breakCatalog = new BehaviorCatalogBuilder(
                EngineCompatibility.BehaviorRegistryVersion)
            .AddIce(iceSpec!)
            .AddIcebreaker(new IcebreakerBehaviorSpec(
                breakerSpec!.DefinitionId,
                breakerSpec.BaseStrength,
                breakerSpec.InterfaceType,
                2,
                breakerSpec.BreakLimit,
                breakerSpec.PumpCost,
                breakerSpec.PumpStrength))
            .Add(new CardBehaviorDefinition(
                FixtureIceBehaviorSpecs.BreakerBarrier,
                PlayerSide.Runner,
                costModifiers:
                [
                    new BehaviorCostModifier(
                        BehaviorCostModifierKind.IcebreakerBreakCreditCost,
                        -1,
                        "successful-run-this-turn"),
                ]))
            .Build();
        breakFixture = RebuildWithBehaviorCatalog(breakFixture, breakCatalog);
        breakFixture = Rebuild(
            breakFixture,
            new TurnState(
                PlayerSide.Runner,
                TurnPhase.Runner,
                breakFixture.Turn.TurnNumber,
                TurnStep.RunnerAction,
                SuccessfulRunnerHistory(RunTimingTests.RemoteId(breakFixture))),
            pending: null);
        GameState encounter = ReachEncounter(breakFixture, breakCatalog);
        LegalAction breakAction = Assert.Single(
            encounter.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.BreakSubroutines
                && ((RunBreakParameters)action.Parameters).BreakerId == breakerId
                && ((RunBreakParameters)action.Parameters).Quantity == 1
                    && ((RunBreakParameters)action.Parameters).SubroutineOrdinals!.SequenceEqual([0]));
        RunBreakParameters issuedBreak = Assert.IsType<RunBreakParameters>(breakAction.Parameters);
        CostQuote staleBreakQuote = new(
            3,
            [new CostAdjustment(
                BehaviorCostModifierKind.IcebreakerBreakCreditCost,
                -1,
                "successful-run-this-turn",
                FixtureIceBehaviorSpecs.BreakerBarrier,
                breakerId)]);
        LegalAction forgedBreak = new(
            breakAction.Id,
            breakAction.Kind,
            breakAction.Description,
            new RunBreakParameters(
                issuedBreak.BreakerId,
                issuedBreak.Quantity,
                issuedBreak.HostedCredits,
                staleBreakQuote));
        PendingDecision breakDecision = encounter.PendingDecision;
        GameState tamperedBreak = ReplacePending(
            encounter,
            new PendingDecision(
                breakDecision.Id,
                breakDecision.Revision,
                breakDecision.Owner,
                breakDecision.Kind,
                breakDecision.Prompt,
                breakDecision.LegalActions
                    .Select(action => action.Id == breakAction.Id ? forgedBreak : action)
                    .ToArray()));
        string breakHash = DeterministicHash.ForState(tamperedBreak);
        ApplyResult breakRejected = GameEngine.Apply(
            tamperedBreak,
            new ActionSubmission(
                breakDecision.Owner,
                breakDecision.Id,
                breakDecision.Revision,
                forgedBreak.Id),
            breakCatalog);
        Assert.False(breakRejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, breakRejected.Rejection?.Code);
        Assert.Same(tamperedBreak, breakRejected.State);
        Assert.Equal(breakHash, DeterministicHash.ForState(breakRejected.State));
        Assert.Empty(breakRejected.Events);
    }

    private static GameState ReachEncounter(GameState state, BehaviorCatalog behaviorCatalog)
    {
        GameState prompt = GameEngine.BeginRun(
            state,
            RunTimingTests.RemoteId(state),
            behaviorCatalog);
        return AdvanceToEncounter(prompt, behaviorCatalog);
    }

    private static GameState AdvanceToEncounter(GameState prompt, BehaviorCatalog behaviorCatalog)
    {
        for (int index = 0; index < 4; index++)
        {
            prompt = ApplyCatalog(prompt, behaviorCatalog, ActionKind.PassPriority);
        }

        Assert.Equal(RunTimingStep.EncounterPaidWindow, prompt.RunState!.Step);
        return prompt;
    }

    private static TurnHistoryState SuccessfulRunnerHistory(ServerId? serverId = null) =>
        new(
            1,
            corpThisTurn: null,
            corpLastTurn: null,
            runnerThisTurn: new TurnFactsState(
                1,
                PlayerSide.Runner,
                turnNumber: 1,
                clicksAllotted: 4,
                clicksLost: 0,
                turnBegan: true,
                mandatoryDrawPerformed: false,
                actionPhaseEnded: false,
                discardPhaseEnded: false,
                successfulRun: true,
                attemptedRunServerIds: [serverId ?? new ServerId("server:remote")],
                successfulRunServerIds: [serverId ?? new ServerId("server:remote")]),
            runnerLastTurn: null);

    private static (GameState State, CardInstanceId CardId) AddGripCard(
        GameState source,
        CardDefinitionId definitionId)
    {
        ZoneState grip = source.Zones.Single(zone =>
            zone.Kind == ZoneKind.RunnerGrip
            && zone.OwnerSide == PlayerSide.Runner);
        CardInstanceId cardId = new("card:cost-modifier-run-event");
        StableIdAllocator allocator = new(source.Allocator);
        if (source.Cards.Any(card => card.Id == cardId))
        {
            cardId = allocator.NextCardInstanceId();
        }

        CardInstanceState card = new(
            cardId,
            definitionId,
            grip.Id,
            PlayerSide.Runner,
            faceUp: true);
        ZoneState populatedGrip = new(
            grip.Id,
            grip.Kind,
            grip.OwnerSide,
            grip.ServerId,
            [.. grip.CardIds, cardId]);
        GameState next = new(
            source.SchemaVersion,
            source.GameId,
            source.RulesVersion,
            source.CardDataVersion,
            source.Players,
            source.Cards.Append(card).ToArray(),
            source.Zones.Select(zone => zone.Id == grip.Id ? populatedGrip : zone).ToArray(),
            source.Servers,
            source.Turn,
            source.Outcome,
            source.RandomState,
            allocator.State,
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
        StateInvariantValidator.Validate(next);
        return (next, cardId);
    }

    private static GameState CreateRunnerCardState(
        BehaviorCatalog behaviorCatalog,
        RuntimeCardCatalog? runtimeCatalog,
        int runnerCredits,
        TurnHistoryState? history)
    {
        GameState baseline = GameState.CreateNew(
            seed: 413,
            behaviorRegistryVersion: behaviorCatalog.BehaviorRegistryVersion,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint,
            runtimeCardCatalogVersion: runtimeCatalog?.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog?.Fingerprint);
        ZoneState grip = baseline.GetZone(ZoneKind.RunnerGrip);
        CardInstanceState card = new(
            SourceCardId,
            DefinitionId,
            grip.Id,
            PlayerSide.Runner);
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == grip.Id
                ? new ZoneState(
                    zone.Id,
                    zone.Kind,
                    zone.OwnerSide,
                    zone.ServerId,
                    [SourceCardId])
                : zone)
            .ToArray();
        PlayerState runner = baseline.Players.Single(player => player.Side == PlayerSide.Runner);
        PlayerState corp = baseline.Players.Single(player => player.Side == PlayerSide.Corp);
        TurnState turn = new(
            PlayerSide.Runner,
            TurnPhase.Runner,
            turnNumber: 1,
            TurnStep.RunnerAction,
            history);
        return new GameState(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            [
                corp,
                new PlayerState(runner.Id, runner.Side, runnerCredits, clicks: 4, runner.AgendaPoints),
            ],
            [card],
            zones,
            baseline.Servers,
            turn,
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            behaviorRegistryVersion: behaviorCatalog.BehaviorRegistryVersion,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint,
            runtimeCardCatalogVersion: runtimeCatalog?.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog?.Fingerprint);
    }

    private static GameState CreateRunnerInstallReplacementState(
        BehaviorCatalog behaviorCatalog,
        RuntimeCardCatalog runtimeCatalog,
        int runnerCredits)
    {
        GameState baseline = GameState.CreateNew(
            seed: 414,
            behaviorRegistryVersion: behaviorCatalog.BehaviorRegistryVersion,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);
        ZoneState grip = baseline.GetZone(ZoneKind.RunnerGrip);
        ZoneState rig = baseline.GetZone(ZoneKind.RunnerRig);
        CardInstanceState incoming = new(
            SourceCardId,
            DefinitionId,
            grip.Id,
            PlayerSide.Runner);
        CardInstanceState existing = new(
            ExistingCardId,
            ExistingDefinitionId,
            rig.Id,
            PlayerSide.Runner,
            faceUp: true);
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == grip.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [SourceCardId])
                : zone.Id == rig.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [ExistingCardId])
                    : zone)
            .ToArray();
        PlayerState runner = baseline.Players.Single(player => player.Side == PlayerSide.Runner);
        PlayerState corp = baseline.Players.Single(player => player.Side == PlayerSide.Corp);
        PlayerState[] players =
        [
            corp,
            new PlayerState(runner.Id, runner.Side, runnerCredits, clicks: 4, runner.AgendaPoints),
        ];
        RulesState defaults = RulesState.CreateDefault(players);
        PlayerRulesState[] rulesPlayers = defaults.Players
            .Select(player => player.PlayerId == runner.Id
                ? new PlayerRulesState(
                    player.PlayerId,
                    player.BaseHandSize,
                    player.CoreDamage,
                    player.Tags,
                    player.BaseMemory,
                    usedMemory: 1)
                : player)
            .ToArray();
        RulesState rules = new(
            defaults.Version,
            rulesPlayers,
            [new ProgramMemoryState(ExistingCardId, runner.Id, memoryCost: 1)],
            defaults.Counters,
            defaults.LingeringEffects,
            defaults.Triggers,
            defaults.Interrupts);
        GameState state = new(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            players,
            [incoming, existing],
            zones,
            baseline.Servers,
            new TurnState(
                PlayerSide.Runner,
                TurnPhase.Runner,
                turnNumber: 1,
                TurnStep.RunnerAction,
                SuccessfulRunnerHistory()),
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            rulesState: rules,
            behaviorRegistryVersion: behaviorCatalog.BehaviorRegistryVersion,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);
        StateInvariantValidator.Validate(state, behaviorCatalog, runtimeCatalog);
        return state;
    }

    private static GameState RebuildWithBehaviorCatalog(
        GameState source,
        BehaviorCatalog behaviorCatalog) =>
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
            source.PendingDecision,
            source.ActionSequence,
            source.InitialSeed,
            source.RunState,
            source.RulesState,
            behaviorCatalog.BehaviorRegistryVersion,
            behaviorCatalog.Fingerprint,
            source.RuntimeCardCatalogVersion,
            source.RuntimeCardCatalogFingerprint);

    private static GameState Rebuild(
        GameState source,
        TurnState turn,
        PendingDecision? pending) =>
        new(
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
            pending,
            source.ActionSequence,
            source.InitialSeed,
            source.RunState,
            source.RulesState,
            source.BehaviorRegistryVersion,
            source.BehaviorCatalogFingerprint,
            source.RuntimeCardCatalogVersion,
            source.RuntimeCardCatalogFingerprint);

    private static GameState ReplacePending(GameState source, PendingDecision pending) =>
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

    private static GameState ApplyCatalog(
        GameState state,
        BehaviorCatalog behaviorCatalog,
        ActionKind kind)
    {
        LegalAction action = Assert.Single(
            state.PendingDecision!.LegalActions,
            candidate => candidate.Kind == kind);
        return ApplyCatalog(state, behaviorCatalog, action);
    }

    private static GameState ApplyCatalog(
        GameState state,
        BehaviorCatalog behaviorCatalog,
        LegalAction action)
        => ApplyCatalogResult(state, behaviorCatalog, action).State;

    private static ApplyResult ApplyCatalogResult(
        GameState state,
        BehaviorCatalog behaviorCatalog,
        LegalAction action)
    {
        ApplyResult result = GameEngine.Apply(
            state,
            new ActionSubmission(
                state.PendingDecision!.Owner,
                state.PendingDecision.Id,
                state.PendingDecision.Revision,
                action.Id),
            behaviorCatalog);
        Assert.True(result.Accepted, result.Rejection?.Message);
        return result;
    }
}

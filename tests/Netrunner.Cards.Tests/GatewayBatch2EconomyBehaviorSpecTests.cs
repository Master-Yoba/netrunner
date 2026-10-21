using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

public sealed class GatewayBatch2EconomyBehaviorSpecTests
{
    [Fact]
    public void EconomyDeclarationsHaveStableCatalogSurfaceAndIncludeSmartware()
    {
        BehaviorCatalog first = GatewayBehaviorCatalog.Create();
        BehaviorCatalog second = GatewayBehaviorCatalog.Create();

        CardDefinitionId[] expectedAssigned =
        [
            GatewayBatch2CardCodes.Carnivore,
            GatewayBatch2CardCodes.Fermenter,
            GatewayBatch2CardCodes.Cookbook,
            GatewayBatch2CardCodes.Pennyshaver,
            GatewayBatch2CardCodes.Carmen,
            GatewayBatch2CardCodes.Marjanah,
            GatewayBatch2CardCodes.RedTeam,
            GatewayBatch2CardCodes.Pantograph,
            GatewayBatch2CardCodes.Conduit,
            GatewayBatch2CardCodes.TeleworkContract,
            GatewayBatch2CardCodes.SmartwareDistributor,
            GatewayCardCodes.NicoCampaign,
            GatewayBatch2CardCodes.SpinDoctor,
            GatewayBatch2CardCodes.RegolithMiningLicense,
        ];
        Assert.Equal(expectedAssigned, GatewayBatch2BehaviorSpecs.AssignedDefinitionIds);
        Assert.Empty(GatewayBatch2BehaviorSpecs.BlockedDefinitionIds);
        Assert.Equal(
            expectedAssigned,
            GatewayBatch2BehaviorSpecs.GenericSpecs.Select(static spec => spec.DefinitionId));
        Assert.Equal(first.Fingerprint, second.Fingerprint);

        Assert.Contains(GatewayBatch2CardCodes.SmartwareDistributor, first.AllDefinitionIds);
        Assert.True(first.TryGet(GatewayBatch2CardCodes.SmartwareDistributor, out _));
        Assert.Contains(GatewayBatch2CardCodes.Pennyshaver, first.AllDefinitionIds);
        Assert.Contains(GatewayBatch2CardCodes.TeleworkContract, first.AllDefinitionIds);
        Assert.Contains(GatewayBatch2CardCodes.RegolithMiningLicense, first.AllDefinitionIds);
        Assert.Contains(GatewayCardCodes.NicoCampaign, first.AllDefinitionIds);
        Assert.Equal(
            GatewayBatch2BehaviorSpecs.GenericSpecs.Select(static spec => spec.DefinitionId),
            first.GenericSpecs.Select(static spec => spec.DefinitionId)
                .Where(expectedAssigned.Contains));
    }

    [Fact]
    public void PennyshaverDeclaresMemorySuccessfulRunAndClickCashout()
    {
        CardBehaviorDefinition definition = Definition(GatewayBatch2CardCodes.Pennyshaver);
        BehaviorStaticModifier modifier = Assert.Single(definition.StaticModifiers);
        Assert.Equal(BehaviorStaticModifierKind.MemoryUnits, modifier.Kind);
        Assert.Equal(1, modifier.Delta);

        BehaviorTrigger successfulRun = Assert.Single(definition.Triggers);
        Assert.Equal(OccurrenceKind.RunSuccessful, successfulRun.Occurrence);
        Assert.Equal(TriggerMode.Mandatory, successfulRun.Mode);
        Assert.Equal(BehaviorTriggerScope.Controller, successfulRun.Scope);
        Assert.Equal(
            [BehaviorInstructionKind.PlaceHostedCredits],
            successfulRun.Instructions.Select(static instruction => instruction.Kind));
        Assert.Equal(1, Assert.Single(successfulRun.Instructions).Amount);
        Assert.Equal(
            HostedCreditSpendRestriction.CardEffectOnly,
            Assert.Single(successfulRun.Instructions).SpendRestriction);

        BehaviorPaidAbility ability = Assert.Single(definition.PaidAbilities);
        Assert.Equal(BehaviorAbilityActivation.ClickAction, ability.Activation);
        Assert.Equal(BehaviorAbilityTiming.ActionPhase, ability.Timing);
        Assert.Equal(BehaviorAbilityUsageLimit.Unlimited, ability.UsageLimit);
        Assert.Equal((BehaviorCostKind.Clicks, 1), Cost(ability));
        Assert.Equal(
            [BehaviorInstructionKind.PlaceHostedCredits, BehaviorInstructionKind.TakeAllHostedCredits],
            ability.Instructions.Select(static instruction => instruction.Kind));
    }

    [Fact]
    public void TeleworkRegolithAndNicoDeclareLoadTakeEmptyAndCashoutPaths()
    {
        CardBehaviorDefinition telework = Definition(GatewayBatch2CardCodes.TeleworkContract);
        Assert.Equal(PlayerSide.Runner, telework.Controller);
        Assert.Equal(2, telework.Triggers.Count);
        AssertTrigger(telework, "telework-install", OccurrenceKind.CardInstalled, BehaviorTriggerScope.Self,
            TriggerMode.Mandatory, BehaviorInstructionKind.LoadHostedCredits, 9);
        AssertTrigger(telework, "telework-empty", OccurrenceKind.HostedCreditPoolEmptied, BehaviorTriggerScope.Self,
            TriggerMode.Mandatory, BehaviorInstructionKind.TrashSelf, 0);
        BehaviorPaidAbility teleworkAbility = Assert.Single(telework.PaidAbilities);
        Assert.Equal(BehaviorAbilityUsageLimit.OncePerTurn, teleworkAbility.UsageLimit);
        Assert.Equal((BehaviorCostKind.Clicks, 1), Cost(teleworkAbility));
        Assert.Equal(BehaviorInstructionKind.TakeHostedCredits, Assert.Single(teleworkAbility.Instructions).Kind);
        Assert.Equal(3, teleworkAbility.Instructions[0].Amount);

        CardBehaviorDefinition regolith = Definition(GatewayBatch2CardCodes.RegolithMiningLicense);
        Assert.Equal(PlayerSide.Corp, regolith.Controller);
        AssertTrigger(regolith, "regolith-rez", OccurrenceKind.CardRezzed, BehaviorTriggerScope.Self,
            TriggerMode.Mandatory, BehaviorInstructionKind.LoadHostedCredits, 15);
        AssertTrigger(regolith, "regolith-empty", OccurrenceKind.HostedCreditPoolEmptied, BehaviorTriggerScope.Self,
            TriggerMode.Mandatory, BehaviorInstructionKind.TrashSelf, 0);
        BehaviorPaidAbility regolithAbility = Assert.Single(regolith.PaidAbilities);
        Assert.Equal(BehaviorAbilityUsageLimit.Unlimited, regolithAbility.UsageLimit);
        Assert.Equal((BehaviorCostKind.Clicks, 1), Cost(regolithAbility));
        Assert.Equal(3, Assert.Single(regolithAbility.Instructions).Amount);

        CardBehaviorDefinition nico = Definition(GatewayCardCodes.NicoCampaign);
        Assert.Equal(PlayerSide.Corp, nico.Controller);
        AssertTrigger(nico, "nico-rez", OccurrenceKind.CardRezzed, BehaviorTriggerScope.Self,
            TriggerMode.Mandatory, BehaviorInstructionKind.LoadHostedCredits, 9);
        AssertTrigger(nico, "nico-turn-begin", OccurrenceKind.TurnBegin, BehaviorTriggerScope.Controller,
            TriggerMode.Mandatory, BehaviorInstructionKind.TakeHostedCredits, 3);
        BehaviorTrigger empty = nico.Triggers.Single(trigger => trigger.Key == "nico-empty");
        Assert.Equal(OccurrenceKind.HostedCreditPoolEmptied, empty.Occurrence);
        Assert.Equal(BehaviorTriggerScope.Self, empty.Scope);
        Assert.Equal(
            [BehaviorInstructionKind.TrashSelf, BehaviorInstructionKind.Draw],
            empty.Instructions.Select(static instruction => instruction.Kind));
        Assert.Equal(1, empty.Instructions[1].Amount);
    }

    [Fact]
    public void SmartwareDeclarationUsesMandatoryTakeUpToAndClickPlacement()
    {
        CardBehaviorDefinition smartware = Definition(GatewayBatch2CardCodes.SmartwareDistributor);
        BehaviorTrigger turnBegin = Assert.Single(smartware.Triggers);
        Assert.Equal(OccurrenceKind.TurnBegin, turnBegin.Occurrence);
        Assert.Equal(TriggerMode.Mandatory, turnBegin.Mode);
        Assert.Equal(BehaviorTriggerScope.Controller, turnBegin.Scope);
        Assert.Equal(BehaviorInstructionKind.TakeUpToHostedCredits, Assert.Single(turnBegin.Instructions).Kind);
        Assert.Equal(1, turnBegin.Instructions[0].Amount);
        Assert.Null(turnBegin.Instructions[0].SpendRestriction);

        BehaviorPaidAbility place = Assert.Single(smartware.PaidAbilities);
        Assert.Equal(BehaviorAbilityUsageLimit.Unlimited, place.UsageLimit);
        Assert.Equal((BehaviorCostKind.Clicks, 1), Cost(place));
        Assert.Equal(BehaviorInstructionKind.PlaceHostedCredits, Assert.Single(place.Instructions).Kind);
        Assert.Equal(3, place.Instructions[0].Amount);
    }

    [Fact]
    [Trait("GatewayCard", "30033")]
    public void SmartwareClickPlacementPlacesThreeCreditsThroughRealAction()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        CardInstanceId sourceId = new("card:economy-smartware-click");
        GameState state = BuildInstalledState(
            catalog,
            sourceId,
            GatewayBatch2CardCodes.SmartwareDistributor,
            PlayerSide.Runner,
            ZoneKind.RunnerRig,
            rezzed: false);
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;

        TurnFrame parent = new(
            1,
            ResolutionBehaviorIds.Turn,
            PlayerSide.Runner,
            state.Turn.TurnNumber,
            TurnFrameStage.AwaitingAction);
        GameState prompt = GameEngine.BeginResolution(state, parent, catalog);
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        LegalAction place = Assert.Single(
            decision.LegalActions,
            action => action.Kind == ActionKind.UseAbility
                && Assert.IsType<AbilityActionParameters>(action.Parameters).SourceCardId == sourceId);

        string promptJson = EngineJson.Serialize(prompt);
        GameState restoredPrompt = EngineJson.DeserializeState(promptJson, catalog);
        PendingDecision restoredDecision = Assert.IsType<PendingDecision>(restoredPrompt.PendingDecision);
        LegalAction restoredPlace = Assert.Single(
            restoredDecision.LegalActions,
            action => action.Kind == ActionKind.UseAbility
                && Assert.IsType<AbilityActionParameters>(action.Parameters).SourceCardId == sourceId);

        ApplyResult used = GameEngine.Apply(
            restoredPrompt,
            new ActionSubmission(
                restoredDecision.Owner,
                restoredDecision.Id,
                restoredDecision.Revision,
                restoredPlace.Id),
            catalog);
        Assert.True(used.Accepted, used.Rejection?.Message);
        Assert.Equal(5, used.State.GetPlayer(runner).Credits);
        Assert.Equal(3, used.State.GetPlayer(runner).Clicks);
        Assert.Equal(3, HostedCount(used.State, sourceId));
        HostedCreditPoolChangedEventPayload hosted = Assert.IsType<HostedCreditPoolChangedEventPayload>(
            Assert.Single(used.Events, @event => @event.Kind == EngineEventKinds.HostedCreditPoolChanged).Payload);
        Assert.Equal(HostedCreditPoolOperation.Place, hosted.Operation);
        Assert.Equal(3, hosted.Delta);
        Assert.Equal(3, hosted.CountAfter);
        Assert.False(hosted.Emptied);
    }

    [Fact]
    public void SmartwareTurnBeginTakesOneFromNonzeroPoolAndResumesThroughJson()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        CardInstanceId sourceId = new("card:economy-smartware-turn");
        GameState state = BuildInstalledState(
            catalog,
            sourceId,
            GatewayBatch2CardCodes.SmartwareDistributor,
            PlayerSide.Runner,
            ZoneKind.RunnerRig,
            rezzed: false,
            hostedCredits: 2);
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;

        GameEngine.TurnStartResult started = GameEngine.BeginTurnWithEvents(
            state,
            PlayerSide.Runner,
            catalog);
        Assert.Equal(DecisionKind.TurnTiming, started.State.PendingDecision?.Kind);

        // Resume from the first opening timing window so the mandatory
        // Controller TurnBegin trigger is reached through the production turn
        // shell, rather than injecting a boundary occurrence.
        GameState restored = EngineJson.DeserializeState(
            EngineJson.Serialize(started.State),
            catalog);
        ApplyResult firstWindow = Pass(restored, catalog);
        Assert.True(firstWindow.Accepted, firstWindow.Rejection?.Message);
        Assert.Equal(DecisionKind.TurnTiming, firstWindow.State.PendingDecision?.Kind);

        ApplyResult turnBegin = Pass(firstWindow.State, catalog);
        Assert.True(turnBegin.Accepted, turnBegin.Rejection?.Message);
        Assert.Equal(1, HostedCount(turnBegin.State, sourceId));
        Assert.Equal(6, turnBegin.State.GetPlayer(runner).Credits);
        HostedCreditPoolChangedEventPayload hosted = Assert.IsType<HostedCreditPoolChangedEventPayload>(
            Assert.Single(turnBegin.Events, @event => @event.Kind == EngineEventKinds.HostedCreditPoolChanged).Payload);
        Assert.Equal(HostedCreditPoolOperation.TakeUpTo, hosted.Operation);
        Assert.Equal(-1, hosted.Delta);
        Assert.Equal(1, hosted.CountAfter);
        Assert.False(hosted.Emptied);
        Assert.Contains(
            turnBegin.Events,
            @event => @event.Kind == EngineEventKinds.CreditsAdjusted
                && Assert.IsType<CreditsAdjustedEventPayload>(@event.Payload).Delta == 1);
        Assert.Empty(StateInvariantValidator.Collect(turnBegin.State, catalog));
    }

    [Fact]
    public void SmartwareTurnBeginWithoutPoolSucceedsWithoutEventsOrTrash()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        CardInstanceId sourceId = new("card:economy-smartware-empty");
        GameState state = BuildInstalledState(
            catalog,
            sourceId,
            GatewayBatch2CardCodes.SmartwareDistributor,
            PlayerSide.Runner,
            ZoneKind.RunnerRig,
            rezzed: false);
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;

        GameEngine.TurnStartResult started = GameEngine.BeginTurnWithEvents(
            state,
            PlayerSide.Runner,
            catalog);
        GameState restored = EngineJson.DeserializeState(
            EngineJson.Serialize(started.State),
            catalog);
        ApplyResult firstWindow = Pass(restored, catalog);
        ApplyResult turnBegin = Pass(firstWindow.State, catalog);

        Assert.True(turnBegin.Accepted, turnBegin.Rejection?.Message);
        Assert.Equal(0, HostedCount(turnBegin.State, sourceId));
        Assert.Equal(5, turnBegin.State.GetPlayer(runner).Credits);
        Assert.DoesNotContain(
            turnBegin.Events,
            @event => @event.Kind == EngineEventKinds.HostedCreditPoolChanged
                || @event.Kind == EngineEventKinds.CreditsAdjusted);
        Assert.Equal(
            ZoneKind.RunnerRig,
            turnBegin.State.GetZone(turnBegin.State.GetCard(sourceId).ZoneId).Kind);
        Assert.Empty(StateInvariantValidator.Collect(turnBegin.State, catalog));
    }

    [Fact]
    public void EconomyDeclarationsAndAssetAdaptersRoundTripWithoutTitleBranching()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        foreach (CardBehaviorDefinition definition in GatewayBatch2EconomyBehaviorSpecs.GenericSpecs)
        {
            foreach (BehaviorTrigger trigger in definition.Triggers)
            {
                string triggerJson = EngineJson.Serialize(trigger);
                BehaviorTrigger restoredTrigger = EngineJson.Deserialize<BehaviorTrigger>(triggerJson);
                Assert.Equal(triggerJson, EngineJson.Serialize(restoredTrigger));
                Assert.Equal(trigger, restoredTrigger);
            }

            foreach (BehaviorPaidAbility ability in definition.PaidAbilities)
            {
                string abilityJson = EngineJson.Serialize(ability);
                BehaviorPaidAbility restoredAbility = EngineJson.Deserialize<BehaviorPaidAbility>(abilityJson);
                Assert.Equal(abilityJson, EngineJson.Serialize(restoredAbility));
                Assert.Equal(ability, restoredAbility);
            }

        }

        Assert.Equal(catalog.Fingerprint, GatewayBehaviorCatalog.Create().Fingerprint);

        Assert.Equal(2, Asset(GatewayCardCodes.NicoCampaign).TrashCost);
        Assert.Equal(2, Asset(GatewayBatch2CardCodes.SpinDoctor).TrashCost);
        Assert.Equal(3, Asset(GatewayBatch2CardCodes.RegolithMiningLicense).TrashCost);
        Assert.Equal(
            new[]
            {
                GatewayCardCodes.NicoCampaign,
                GatewayBatch2CardCodes.SpinDoctor,
                GatewayBatch2CardCodes.RegolithMiningLicense,
            },
            GatewayAssetBehaviorSpecs.AssetSpecs.Select(static spec => spec.DefinitionId));
    }

    [Fact]
    [Trait("GatewayCard", "30014")]
    public void PennyshaverSuccessfulRunPlacesPublicCreditAndClickCashoutTransfersIt()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        CardInstanceId sourceId = new("card:economy-pennyshaver");
        GameState state = BuildInstalledState(
            catalog,
            sourceId,
            GatewayBatch2CardCodes.Pennyshaver,
            PlayerSide.Runner,
            ZoneKind.RunnerRig,
            rezzed: false);
        ServerId server = state.Servers.Single(candidate => candidate.Kind == ServerKind.RAndD).Id;
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;

        GameEngine.RunStartResult started = GameEngine.BeginRunWithEvents(state, server, catalog);
        ApplyResult runnerPass = Pass(started.State, catalog);
        ApplyResult completedRun = Pass(runnerPass.State, catalog);
        Assert.True(completedRun.Accepted, completedRun.Rejection?.Message);

        CardCounterState hosted = Assert.Single(
            completedRun.State.RulesState!.Counters,
            counter => counter.CardId == sourceId);
        Assert.Equal("credit", hosted.CounterName);
        Assert.Equal(CardCounterPurpose.HostedCredit, hosted.Purpose);
        Assert.Equal(1, hosted.Count);
        Assert.Equal(5, completedRun.State.GetPlayer(runner).Credits);

        string corpView = EngineJson.Serialize(
            ObservationBuilder.Build(completedRun.State, PlayerSide.Corp, catalog));
        string runnerView = EngineJson.Serialize(
            ObservationBuilder.Build(completedRun.State, PlayerSide.Runner, catalog));
        Assert.Contains(sourceId.Value, corpView, StringComparison.Ordinal);
        Assert.Contains(sourceId.Value, runnerView, StringComparison.Ordinal);
        Assert.Contains("\"counterName\":\"credit\"", corpView, StringComparison.Ordinal);
        Assert.Contains("\"counterName\":\"credit\"", runnerView, StringComparison.Ordinal);
        Assert.Contains("\"count\":1", corpView, StringComparison.Ordinal);
        Assert.Contains("\"count\":1", runnerView, StringComparison.Ordinal);

        TurnFrame parent = new(
            1,
            ResolutionBehaviorIds.Turn,
            PlayerSide.Runner,
            completedRun.State.Turn.TurnNumber,
            TurnFrameStage.AwaitingAction);
        GameState actionPrompt = GameEngine.BeginResolution(completedRun.State, parent, catalog);
        PendingDecision decision = Assert.IsType<PendingDecision>(actionPrompt.PendingDecision);
        LegalAction cashout = Assert.Single(
            decision.LegalActions,
            action => action.Kind == ActionKind.UseAbility
                && Assert.IsType<AbilityActionParameters>(action.Parameters).SourceCardId == sourceId);

        ApplyResult paid = GameEngine.Apply(
            actionPrompt,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, cashout.Id),
            catalog);
        Assert.True(paid.Accepted, paid.Rejection?.Message);
        Assert.Equal(7, paid.State.GetPlayer(runner).Credits);
        Assert.DoesNotContain(
            paid.State.RulesState!.Counters,
            counter => counter.CardId == sourceId && counter.Purpose == CardCounterPurpose.HostedCredit);
    }

    [Fact]
    [Trait("GatewayCard", "30037")]
    [Trait("GatewayCard", "30027")]
    [Trait("GatewayCard", "30071")]
    public void RealInstallAndRezOccurrencesLoadPoolsAndNicoEmptinessTrashesBeforeDrawing()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();

        CardInstanceId teleworkId = new("card:economy-telework-install");
        GameState telework = BuildInstalledState(
            catalog,
            teleworkId,
            GatewayBatch2CardCodes.TeleworkContract,
            PlayerSide.Runner,
            ZoneKind.RunnerRig,
            rezzed: false);
        PlayerId runner = telework.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameState installed = ResolveOccurrence(
            telework,
            runner,
            new GameplayOccurrenceEffect(OccurrenceKind.CardInstalled, runner, teleworkId),
            catalog);
        Assert.Equal(9, HostedCount(installed, teleworkId));

        CardInstanceId regolithId = new("card:economy-regolith-rez");
        GameState regolith = BuildInstalledState(
            catalog,
            regolithId,
            GatewayBatch2CardCodes.RegolithMiningLicense,
            PlayerSide.Corp,
            ZoneKind.ServerRoot,
            rezzed: true);
        PlayerId corp = regolith.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        GameState rezzed = ResolveOccurrence(
            regolith,
            corp,
            new GameplayOccurrenceEffect(OccurrenceKind.CardRezzed, corp, regolithId),
            catalog);
        Assert.Equal(15, HostedCount(rezzed, regolithId));

        CardInstanceId nicoRezId = new("card:economy-nico-rez");
        GameState nicoRez = BuildInstalledState(
            catalog,
            nicoRezId,
            GatewayCardCodes.NicoCampaign,
            PlayerSide.Corp,
            ZoneKind.ServerRoot,
            rezzed: true,
            corpDeckCardId: new CardInstanceId("card:economy-nico-turn-draw"));
        corp = nicoRez.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        nicoRez = ResolveOccurrence(
            nicoRez,
            corp,
            new GameplayOccurrenceEffect(OccurrenceKind.CardRezzed, corp, nicoRezId),
            catalog);
        Assert.Equal(9, HostedCount(nicoRez, nicoRezId));
        GameEngine.TurnStartResult turnStarted = GameEngine.BeginTurnWithEvents(
            nicoRez,
            PlayerSide.Corp,
            catalog);
        Assert.Equal(DecisionKind.TurnTiming, turnStarted.State.PendingDecision?.Kind);
        ApplyResult corpBeforeTurnPass = Pass(turnStarted.State, catalog);
        Assert.True(corpBeforeTurnPass.Accepted, corpBeforeTurnPass.Rejection?.Message);
        Assert.Equal(DecisionKind.TurnTiming, corpBeforeTurnPass.State.PendingDecision?.Kind);
        ApplyResult runnerBeforeTurnPass = Pass(corpBeforeTurnPass.State, catalog);
        Assert.True(runnerBeforeTurnPass.Accepted, runnerBeforeTurnPass.Rejection?.Message);
        nicoRez = runnerBeforeTurnPass.State;
        Assert.Equal(6, HostedCount(nicoRez, nicoRezId));
        Assert.Equal(8, nicoRez.GetPlayer(corp).Credits);

        CardInstanceId nicoId = new("card:economy-nico-empty");
        CardInstanceId deckCardId = new("card:economy-nico-draw");
        GameState nico = BuildInstalledState(
            catalog,
            nicoId,
            GatewayCardCodes.NicoCampaign,
            PlayerSide.Corp,
            ZoneKind.ServerRoot,
            rezzed: true,
            corpDeckCardId: deckCardId);
        corp = nico.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        GameState emptied = ResolveOccurrence(
            nico,
            corp,
            new GameplayOccurrenceEffect(OccurrenceKind.HostedCreditPoolEmptied, corp, nicoId),
            catalog);
        Assert.Equal(ZoneKind.CorpDiscard, emptied.GetZone(emptied.GetCard(nicoId).ZoneId).Kind);
        Assert.Contains(deckCardId, emptied.GetZone(ZoneKind.CorpHand).CardIds);
        Assert.DoesNotContain(deckCardId, emptied.GetZone(ZoneKind.CorpDeck).CardIds);
    }

    [Fact]
    public void TeleworkClickTransferMarksTheAbilityUsedForTheTurn()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        CardInstanceId sourceId = new("card:economy-telework-once");
        GameState state = BuildInstalledState(
            catalog,
            sourceId,
            GatewayBatch2CardCodes.TeleworkContract,
            PlayerSide.Runner,
            ZoneKind.RunnerRig,
            rezzed: false);
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        state = ResolveOccurrence(
            state,
            runner,
            new GameplayOccurrenceEffect(OccurrenceKind.CardInstalled, runner, sourceId),
            catalog);

        TurnFrame parent = new(
            1,
            ResolutionBehaviorIds.Turn,
            PlayerSide.Runner,
            state.Turn.TurnNumber,
            TurnFrameStage.AwaitingAction);
        GameState prompt = GameEngine.BeginResolution(state, parent, catalog);
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        LegalAction ability = Assert.Single(
            decision.LegalActions,
            action => action.Kind == ActionKind.UseAbility
                && Assert.IsType<AbilityActionParameters>(action.Parameters).SourceCardId == sourceId);
        ApplyResult used = GameEngine.Apply(
            prompt,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, ability.Id),
            catalog);
        Assert.True(used.Accepted, used.Rejection?.Message);
        Assert.Equal(6, HostedCount(used.State, sourceId));
        Assert.Equal(8, used.State.GetPlayer(runner).Credits);
        Assert.Equal(3, used.State.GetPlayer(runner).Clicks);
        Assert.DoesNotContain(
            used.State.PendingDecision?.LegalActions ?? [],
            action => action.Kind == ActionKind.UseAbility
                && Assert.IsType<AbilityActionParameters>(action.Parameters).SourceCardId == sourceId);
    }

    [Fact]
    public void TeleworkFinalTakeCompletesAfterItsMandatoryEmptyPoolTrash()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        CardInstanceId sourceId = new("card:economy-telework-final");
        GameState state = BuildInstalledState(catalog, sourceId, GatewayBatch2CardCodes.TeleworkContract,
            PlayerSide.Runner, ZoneKind.RunnerRig, rezzed: false, hostedCredits: 3);
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameState prompt = GameEngine.BeginResolution(state,
            new TurnFrame(1, ResolutionBehaviorIds.Turn, PlayerSide.Runner, state.Turn.TurnNumber, TurnFrameStage.AwaitingAction), catalog);
        PendingDecision pending = prompt.PendingDecision!;
        LegalAction ability = Assert.Single(pending.LegalActions, action => action.Kind == ActionKind.UseAbility);
        ApplyResult result = GameEngine.Apply(prompt,
            new ActionSubmission(pending.Owner, pending.Id, pending.Revision, ability.Id), catalog);
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Contains(sourceId, result.State.GetZone(ZoneKind.RunnerHeap).CardIds);
        Assert.Equal(0, HostedCount(result.State, sourceId));
        Assert.Equal(8, result.State.GetPlayer(runner).Credits);
        Assert.Equal(3, result.State.GetPlayer(runner).Clicks);
        Assert.Empty(result.State.ResolutionStack.OfType<AbilityFrame>());
        string json = EngineJson.Serialize(result.State);
        Assert.Equal(json, EngineJson.Serialize(EngineJson.DeserializeState(json, catalog)));
    }

    private static CardBehaviorDefinition Definition(CardDefinitionId id) =>
        GatewayBehaviorCatalog.Create().GenericSpecs.Single(definition => definition.DefinitionId == id);

    private static AssetBehaviorSpec Asset(CardDefinitionId id) =>
        GatewayAssetBehaviorSpecs.AssetSpecs.Single(spec => spec.DefinitionId == id);

    private static (BehaviorCostKind Kind, int Amount) Cost(BehaviorPaidAbility ability)
    {
        BehaviorCost cost = Assert.Single(ability.Costs);
        return (cost.Kind, cost.Amount);
    }

    private static void AssertTrigger(
        CardBehaviorDefinition definition,
        string key,
        OccurrenceKind occurrence,
        BehaviorTriggerScope scope,
        TriggerMode mode,
        BehaviorInstructionKind instruction,
        int amount)
    {
        BehaviorTrigger trigger = definition.Triggers.Single(candidate => candidate.Key == key);
        Assert.Equal(occurrence, trigger.Occurrence);
        Assert.Equal(scope, trigger.Scope);
        Assert.Equal(mode, trigger.Mode);
        BehaviorInstruction effect = Assert.Single(trigger.Instructions);
        Assert.Equal(instruction, effect.Kind);
        Assert.Equal(amount, effect.Amount);
    }

    private static ApplyResult Pass(GameState state, BehaviorCatalog catalog)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction pass = Assert.Single(
            decision.LegalActions,
            action => action.Kind == ActionKind.PassPriority);
        return GameEngine.Apply(
            state,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, pass.Id),
            catalog);
    }

    private static GameState ResolveOccurrence(
        GameState state,
        PlayerId controller,
        GameplayOccurrenceEffect occurrence,
        BehaviorCatalog catalog) =>
        GameEngine.BeginResolution(
            state,
            new EffectResolutionFrame(
                controller,
                new CostBundle(),
                new EffectBundle(occurrence)),
            catalog);

    private static int HostedCount(GameState state, CardInstanceId cardId) =>
        state.RulesState?.Counters
            .SingleOrDefault(counter =>
                counter.CardId == cardId
                && counter.Purpose == CardCounterPurpose.HostedCredit)
            ?.Count
        ?? 0;

    private static GameState BuildInstalledState(
        BehaviorCatalog catalog,
        CardInstanceId cardId,
        CardDefinitionId definitionId,
        PlayerSide owner,
        ZoneKind zoneKind,
        bool rezzed,
        CardInstanceId? corpDeckCardId = null,
        int? hostedCredits = null)
    {
        GameState baseline = GameState.CreateNew(
            seed: 44117,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint);
        ZoneState zone = zoneKind == ZoneKind.RunnerRig
            ? baseline.GetZone(ZoneKind.RunnerRig)
            : baseline.GetZone(
                baseline.Servers.Single(server => server.Kind == ServerKind.RAndD).RootZoneId);
        CardInstanceState card = new(cardId, definitionId, zone.Id, owner, faceUp: true, rezzed: rezzed);
        CardInstanceState[] cards = corpDeckCardId is { } deckCardId
            ? [card, new CardInstanceState(
                deckCardId,
                GatewayBatch1CardCodes.HedgeFund,
                baseline.GetZone(ZoneKind.CorpDeck).Id,
                PlayerSide.Corp,
                faceUp: false)]
            : [card];
        ZoneState[] zones = baseline.Zones
            .Select(candidate => candidate.Id == zone.Id
                ? new ZoneState(candidate.Id, candidate.Kind, candidate.OwnerSide, candidate.ServerId, [cardId])
                : corpDeckCardId is { } deckCardId
                    && candidate.Id == baseline.GetZone(ZoneKind.CorpDeck).Id
                        ? new ZoneState(candidate.Id, candidate.Kind, candidate.OwnerSide, candidate.ServerId, [deckCardId])
                : candidate)
            .ToArray();
        PlayerSide activeSide = owner;
        TurnPhase phase = owner == PlayerSide.Corp ? TurnPhase.Corp : TurnPhase.Runner;
        TurnStep step = owner == PlayerSide.Corp ? TurnStep.CorpAction : TurnStep.RunnerAction;
        TurnFactsState facts = new(
            1,
            owner,
            baseline.Turn.TurnNumber,
            owner == PlayerSide.Corp ? 3 : 4,
            clicksLost: 0,
            turnBegan: true,
            mandatoryDrawPerformed: owner == PlayerSide.Corp,
            actionPhaseEnded: false,
            discardPhaseEnded: false);
        TurnHistoryState history = owner == PlayerSide.Corp
            ? new TurnHistoryState(1, facts, null, null, null)
            : new TurnHistoryState(1, null, null, facts, null);
        RulesState defaults = RulesState.CreateDefault(baseline.Players);
        CardCounterState[] counters = hostedCredits is { } count
            ? [CardCounterState.CreateHostedCredit(cardId, count)]
            : [];
        RulesState rules = new(
            defaults.Version,
            defaults.Players,
            defaults.Programs,
            counters,
            defaults.LingeringEffects,
            defaults.Triggers,
            defaults.Interrupts);
        return new GameState(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            baseline.Players,
            cards,
            zones,
            baseline.Servers,
            new TurnState(activeSide, phase, baseline.Turn.TurnNumber, step, history),
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            rulesState: rules,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint);
    }
}

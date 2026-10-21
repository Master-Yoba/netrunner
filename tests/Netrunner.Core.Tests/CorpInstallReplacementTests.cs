using Xunit;

namespace Netrunner.Core.Tests;

public sealed class CorpInstallReplacementTests
{
    [Fact]
    public void AgendaOverAssetRequiresEveryAgendaAndAssetButLeavesUpgradeOptional()
    {
        CardRuntimeSpec incoming = Agenda(new CardDefinitionId("test:corp-replace-incoming-agenda"));
        CardRuntimeSpec existingAgenda = Agenda(new CardDefinitionId("test:corp-replace-existing-agenda"));
        CardRuntimeSpec existingAsset = Asset(new CardDefinitionId("test:corp-replace-existing-asset"));
        CardRuntimeSpec existingUpgrade = Upgrade(new CardDefinitionId("test:corp-replace-existing-upgrade"));
        Fixture fixture = CreateFixture(
            incoming,
            (existingAgenda, new CardInstanceId("card:corp-replace-existing-agenda")),
            (existingAsset, new CardInstanceId("card:corp-replace-existing-asset")),
            (existingUpgrade, new CardInstanceId("card:corp-replace-existing-upgrade")));
        GameState state = OpenCorpAction(fixture);

        LegalAction initial = InstallAction(state, fixture.RemoteId);
        InstallCardParameters initialParameters = Assert.IsType<InstallCardParameters>(initial.Parameters);
        Assert.Equal(
            initialParameters.CandidateReplacementCardIds,
            initialParameters.CandidateReplacementCardIds.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray());
        Assert.Equal(3, initialParameters.CandidateReplacementCardIds.Count);
        Assert.Equal(
            new[]
            {
                new CardInstanceId("card:corp-replace-existing-agenda"),
                new CardInstanceId("card:corp-replace-existing-asset"),
            },
            initialParameters.RequiredReplacementCardIds);
        Assert.False(initialParameters.FinalizeSelection);

        PlayerState corpBefore = state.GetPlayer(fixture.CorpId);
        ZoneState rootBefore = state.GetZone(state.GetServer(fixture.RemoteId).RootZoneId);
        ApplyResult opened = Apply(state, fixture, initial);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        Assert.Empty(opened.Events);
        Assert.Equal(corpBefore, opened.State.GetPlayer(fixture.CorpId));
        Assert.Equal(rootBefore.CardIds, opened.State.GetZone(rootBefore.Id).CardIds);
        Assert.Equal(state.GetCard(fixture.IncomingCardId), opened.State.GetCard(fixture.IncomingCardId));

        PendingDecision choice = Assert.IsType<PendingDecision>(opened.State.PendingDecision);
        Assert.Equal(3, choice.LegalActions.Count);
        Assert.DoesNotContain(choice.LegalActions, action => Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);

        // The optional Upgrade may be selected first. The stream remains
        // linear and still withholds finalization until both required classes
        // have been selected.
        CardInstanceId upgradeId = new("card:corp-replace-existing-upgrade");
        LegalAction selectUpgrade = Assert.Single(
            choice.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters).SelectedReplacementCardIds.SequenceEqual([upgradeId]));
        ApplyResult selectedUpgrade = Apply(opened.State, fixture, selectUpgrade);
        Assert.True(selectedUpgrade.Accepted, selectedUpgrade.Rejection?.Message);
        Assert.Empty(selectedUpgrade.Events);
        PendingDecision afterUpgrade = Assert.IsType<PendingDecision>(selectedUpgrade.State.PendingDecision);
        Assert.Equal(2, afterUpgrade.LegalActions.Count);
        Assert.DoesNotContain(afterUpgrade.LegalActions, action => Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);

        CardInstanceId agendaId = new("card:corp-replace-existing-agenda");
        LegalAction selectAgenda = Assert.Single(
            afterUpgrade.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters).SelectedReplacementCardIds.SequenceEqual([upgradeId, agendaId]));
        ApplyResult selectedAgenda = Apply(selectedUpgrade.State, fixture, selectAgenda);
        Assert.True(selectedAgenda.Accepted, selectedAgenda.Rejection?.Message);
        PendingDecision afterAgenda = Assert.IsType<PendingDecision>(selectedAgenda.State.PendingDecision);
        Assert.Single(afterAgenda.LegalActions);

        CardInstanceId assetId = new("card:corp-replace-existing-asset");
        LegalAction selectAsset = Assert.Single(
            afterAgenda.LegalActions,
            action => !Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection
                && Assert.IsType<InstallCardParameters>(action.Parameters).SelectedReplacementCardIds.SequenceEqual([upgradeId, agendaId, assetId]));
        ApplyResult selectedAsset = Apply(selectedAgenda.State, fixture, selectAsset);
        Assert.True(selectedAsset.Accepted, selectedAsset.Rejection?.Message);

        PendingDecision finalChoice = Assert.IsType<PendingDecision>(selectedAsset.State.PendingDecision);
        LegalAction finalize = Assert.Single(
            finalChoice.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);
        InstallCardParameters finalParameters = Assert.IsType<InstallCardParameters>(finalize.Parameters);
        Assert.Equal([upgradeId, agendaId, assetId], finalParameters.SelectedReplacementCardIds);

        ApplyResult result = Apply(selectedAsset.State, fixture, finalize);
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(corpBefore.Credits, result.State.GetPlayer(fixture.CorpId).Credits);
        Assert.Equal(corpBefore.Clicks - 1, result.State.GetPlayer(fixture.CorpId).Clicks);
        Assert.Equal(
            new[] { upgradeId, agendaId, assetId, fixture.IncomingCardId },
            result.Events
                .Where(value => value.Kind == EngineEventKinds.CardMoved)
                .Select(value => Assert.IsType<CardMovedEventPayload>(value.Payload).CardId));
        Assert.Equal(
            new[]
            {
                EngineEventKinds.CardMoved,
                EngineEventKinds.CardMoved,
                EngineEventKinds.CardMoved,
                EngineEventKinds.ClickSpent,
                EngineEventKinds.CardMoved,
                EngineEventKinds.CardInstalled,
                EngineEventKinds.TurnTimingWindowOpened,
            },
            result.Events.Select(value => value.Kind));
        Assert.DoesNotContain(result.Events, value => value.Kind == EngineEventKinds.CreditsAdjusted);
        Assert.Equal(
            new[] { upgradeId, agendaId, assetId },
            result.State.GetZone(ZoneKind.CorpDiscard).CardIds
                .Where(id => id != fixture.IncomingCardId)
                .ToArray());
        ZoneState rootAfter = result.State.GetZone(result.State.GetServer(fixture.RemoteId).RootZoneId);
        Assert.Equal([fixture.IncomingCardId], rootAfter.CardIds);
        Assert.False(result.State.GetCard(fixture.IncomingCardId).FaceUp);
        Assert.False(result.State.GetCard(fixture.IncomingCardId).Rezzed);
        Assert.Contains(fixture.IncomingCardId, result.State.Turn.ThisTurnFacts!.InstalledCardIds);
        Assert.Equal(DecisionKind.TurnTiming, result.State.PendingDecision!.Kind);
        Assert.Equal(TurnFrameStage.ActionPriority, Assert.IsType<TurnFrame>(result.State.ResolutionStack[^1]).Stage);
    }

    [Fact]
    public void AssetOverAgendaAndAssetRequiresTheSameCrossClassMandatorySet()
    {
        CardInstanceId agendaId = new("card:corp-replace-asset-existing-agenda");
        CardInstanceId assetId = new("card:corp-replace-asset-existing-asset");
        CardRuntimeSpec incoming = Asset(new CardDefinitionId("test:corp-replace-asset-incoming"));
        Fixture fixture = CreateFixture(
            incoming,
            (Agenda(new CardDefinitionId("test:corp-replace-asset-agenda")), agendaId),
            (Asset(new CardDefinitionId("test:corp-replace-asset-asset")), assetId));
        GameState state = OpenCorpAction(fixture);

        LegalAction initial = InstallAction(state, fixture.RemoteId);
        InstallCardParameters initialParameters = Assert.IsType<InstallCardParameters>(initial.Parameters);
        Assert.Equal(new[] { agendaId, assetId }, initialParameters.CandidateReplacementCardIds);
        Assert.Equal(new[] { agendaId, assetId }, initialParameters.RequiredReplacementCardIds);
        Assert.False(initialParameters.FinalizeSelection);

        ApplyResult opened = Apply(state, fixture, initial);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        PendingDecision choice = Assert.IsType<PendingDecision>(opened.State.PendingDecision);
        Assert.Equal(2, choice.LegalActions.Count);
        Assert.DoesNotContain(choice.LegalActions, action => Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);

        LegalAction selectAgenda = Assert.Single(
            choice.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters).SelectedReplacementCardIds.SequenceEqual([agendaId]));
        ApplyResult afterAgenda = Apply(opened.State, fixture, selectAgenda);
        Assert.True(afterAgenda.Accepted, afterAgenda.Rejection?.Message);
        PendingDecision assetChoice = Assert.IsType<PendingDecision>(afterAgenda.State.PendingDecision);
        Assert.Single(assetChoice.LegalActions);
        Assert.DoesNotContain(assetChoice.LegalActions, action => Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);

        LegalAction selectAsset = Assert.Single(
            assetChoice.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters).SelectedReplacementCardIds.SequenceEqual([agendaId, assetId]));
        ApplyResult afterAsset = Apply(afterAgenda.State, fixture, selectAsset);
        Assert.True(afterAsset.Accepted, afterAsset.Rejection?.Message);
        LegalAction finalize = Assert.Single(
            Assert.IsType<PendingDecision>(afterAsset.State.PendingDecision).LegalActions,
            action =>
            {
                InstallCardParameters parameters = Assert.IsType<InstallCardParameters>(action.Parameters);
                return parameters.FinalizeSelection
                    && parameters.SelectedReplacementCardIds.SequenceEqual([agendaId, assetId]);
            });

        ApplyResult result = Apply(afterAsset.State, fixture, finalize);
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(new[] { agendaId, assetId }, result.State.GetZone(ZoneKind.CorpDiscard).CardIds.Where(id => id != fixture.IncomingCardId));
        Assert.Equal(new[] { fixture.IncomingCardId }, result.State.GetZone(result.State.GetServer(fixture.RemoteId).RootZoneId).CardIds);
    }

    [Fact]
    public void RegionUpgradeRequiresEveryCaseInsensitiveRegionAndNonRegionUpgradeMayRemain()
    {
        CardRuntimeSpec incoming = Upgrade(
            new CardDefinitionId("test:corp-replace-region-incoming"),
            "rEgIoN");
        CardRuntimeSpec regionOne = Upgrade(
            new CardDefinitionId("test:corp-replace-region-one"),
            "REGION");
        CardRuntimeSpec regionTwo = Upgrade(
            new CardDefinitionId("test:corp-replace-region-two"),
            "region");
        CardRuntimeSpec ordinaryUpgrade = Upgrade(new CardDefinitionId("test:corp-replace-ordinary-upgrade"));
        Fixture fixture = CreateFixture(
            incoming,
            (regionOne, new CardInstanceId("card:corp-replace-region-one")),
            (regionTwo, new CardInstanceId("card:corp-replace-region-two")),
            (ordinaryUpgrade, new CardInstanceId("card:corp-replace-ordinary-upgrade")));
        GameState state = OpenCorpAction(fixture);
        LegalAction initial = InstallAction(state, fixture.RemoteId);
        InstallCardParameters initialParameters = Assert.IsType<InstallCardParameters>(initial.Parameters);
        Assert.Equal(
            new[]
            {
                new CardInstanceId("card:corp-replace-region-one"),
                new CardInstanceId("card:corp-replace-region-two"),
            },
            initialParameters.RequiredReplacementCardIds);

        ApplyResult opened = Apply(state, fixture, initial);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        PendingDecision choice = Assert.IsType<PendingDecision>(opened.State.PendingDecision);
        Assert.DoesNotContain(choice.LegalActions, action => Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);

        CardInstanceId ordinaryId = new("card:corp-replace-ordinary-upgrade");
        LegalAction ordinary = Assert.Single(
            choice.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters).SelectedReplacementCardIds.SequenceEqual([ordinaryId]));
        ApplyResult selectedOrdinary = Apply(opened.State, fixture, ordinary);
        Assert.True(selectedOrdinary.Accepted, selectedOrdinary.Rejection?.Message);
        PendingDecision afterOrdinary = Assert.IsType<PendingDecision>(selectedOrdinary.State.PendingDecision);
        Assert.DoesNotContain(afterOrdinary.LegalActions, action => Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);

        CardInstanceId firstRegionId = new("card:corp-replace-region-one");
        LegalAction firstRegion = Assert.Single(
            afterOrdinary.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters).SelectedReplacementCardIds.SequenceEqual([ordinaryId, firstRegionId]));
        ApplyResult selectedFirstRegion = Apply(selectedOrdinary.State, fixture, firstRegion);
        Assert.True(selectedFirstRegion.Accepted, selectedFirstRegion.Rejection?.Message);
        PendingDecision afterFirstRegion = Assert.IsType<PendingDecision>(selectedFirstRegion.State.PendingDecision);
        Assert.DoesNotContain(afterFirstRegion.LegalActions, action => Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);

        CardInstanceId secondRegionId = new("card:corp-replace-region-two");
        LegalAction secondRegion = Assert.Single(
            afterFirstRegion.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters).SelectedReplacementCardIds.SequenceEqual([ordinaryId, firstRegionId, secondRegionId]));
        ApplyResult selectedAll = Apply(selectedFirstRegion.State, fixture, secondRegion);
        Assert.True(selectedAll.Accepted, selectedAll.Rejection?.Message);
        LegalAction finalize = Assert.Single(
            Assert.IsType<PendingDecision>(selectedAll.State.PendingDecision).LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);
        ApplyResult result = Apply(selectedAll.State, fixture, finalize);
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(
            new[] { ordinaryId, firstRegionId, secondRegionId },
            result.State.GetZone(ZoneKind.CorpDiscard).CardIds
                .Where(id => id != fixture.IncomingCardId)
                .ToArray());
    }

    [Fact]
    public void NonRegionUpgradeMayFinalizeWithNoReplacementSelection()
    {
        CardRuntimeSpec incoming = Upgrade(new CardDefinitionId("test:corp-replace-upgrade-incoming"));
        CardRuntimeSpec existingAsset = Asset(new CardDefinitionId("test:corp-replace-upgrade-existing-asset"));
        Fixture fixture = CreateFixture(
            incoming,
            (existingAsset, new CardInstanceId("card:corp-replace-upgrade-existing-asset")));
        GameState state = OpenCorpAction(fixture);
        LegalAction initial = InstallAction(state, fixture.RemoteId);
        InstallCardParameters initialParameters = Assert.IsType<InstallCardParameters>(initial.Parameters);
        Assert.Empty(initialParameters.RequiredReplacementCardIds);

        ApplyResult opened = Apply(state, fixture, initial);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        PendingDecision choice = Assert.IsType<PendingDecision>(opened.State.PendingDecision);
        Assert.Contains(
            choice.LegalActions,
            action =>
            {
                InstallCardParameters parameters = Assert.IsType<InstallCardParameters>(action.Parameters);
                return !parameters.FinalizeSelection
                    && parameters.SelectedReplacementCardIds.SequenceEqual([new CardInstanceId("card:corp-replace-upgrade-existing-asset")]);
            });
        LegalAction finalize = Assert.Single(
            choice.LegalActions,
            action =>
            {
                InstallCardParameters parameters = Assert.IsType<InstallCardParameters>(action.Parameters);
                return parameters.FinalizeSelection && parameters.SelectedReplacementCardIds.Count == 0;
            });
        ApplyResult result = Apply(opened.State, fixture, finalize);
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(1, result.Events.Count(value => value.Kind == EngineEventKinds.CardMoved));
        Assert.Equal(1, result.Events.Count(value => value.Kind == EngineEventKinds.ClickSpent));
        Assert.Equal(1, result.Events.Count(value => value.Kind == EngineEventKinds.CardInstalled));
        Assert.DoesNotContain(result.Events, value => value.Kind == EngineEventKinds.CreditsAdjusted);
        Assert.Contains(
            new CardInstanceId("card:corp-replace-upgrade-existing-asset"),
            result.State.GetZone(result.State.GetServer(fixture.RemoteId).RootZoneId).CardIds);
        Assert.Contains(fixture.IncomingCardId, result.State.GetZone(result.State.GetServer(fixture.RemoteId).RootZoneId).CardIds);
    }

    [Fact]
    public void UnknownOccupiedRootMetadataFailsClosedAndDoesNotOfferReplacementAction()
    {
        CardDefinitionId incomingDefinition = new("test:corp-replace-unknown-incoming");
        CardInstanceId incomingCard = new("card:corp-replace-unknown-incoming");
        CardDefinitionId unknownDefinition = new("test:corp-replace-unknown-root");
        CardInstanceId unknownCard = new("card:corp-replace-unknown-root");
        CardRuntimeSpec incoming = Agenda(incomingDefinition);
        Fixture fixture = CreateFixture(
            incoming,
            new[]
            {
                (new CardRuntimeSpec(
                    unknownDefinition,
                    PlayerSide.Corp,
                    RuntimeCardClass.Asset,
                    rezCost: 0),
                 unknownCard),
            },
            includeRootSpecsInCatalog: false,
            incomingCardId: incomingCard);
        GameState state = OpenCorpAction(fixture);
        Assert.DoesNotContain(
            state.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.InstallCard
                && Assert.IsType<InstallCardParameters>(action.Parameters).ServerId == fixture.RemoteId);
    }

    [Fact]
    public void CorpInstallChoiceIsOwnerOnlyAndRoundTripsWithExactHash()
    {
        CardRuntimeSpec incoming = Agenda(new CardDefinitionId("test:corp-replace-observe-incoming"));
        CardRuntimeSpec existing = Asset(new CardDefinitionId("test:corp-replace-observe-existing"));
        Fixture fixture = CreateFixture(
            incoming,
            (existing, new CardInstanceId("card:corp-replace-observe-existing")));
        GameState state = OpenCorpAction(fixture);
        LegalAction initial = InstallAction(state, fixture.RemoteId);
        ApplyResult opened = Apply(state, fixture, initial);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        PendingDecision pending = Assert.IsType<PendingDecision>(opened.State.PendingDecision);
        InstallFrame frame = Assert.IsType<InstallFrame>(opened.State.ResolutionStack[^1]);
        ulong allocatorBeforeMaterialization = opened.State.Allocator.NextSequence;
        string hashBeforeMaterialization = DeterministicHash.ForState(opened.State);
        Assert.Empty(StateInvariantValidator.Collect(opened.State, fixture.BehaviorCatalog, fixture.RuntimeCardCatalog));
        IReadOnlyList<InstallCardParameters> materializedChoice = GameEngine.MaterializeInstallChoiceParameters(
            opened.State,
            frame,
            fixture.BehaviorCatalog,
            fixture.RuntimeCardCatalog);
        Assert.Equal(pending.LegalActions.Count, materializedChoice.Count);
        Assert.All(
            materializedChoice,
            expected => Assert.Contains(
                pending.LegalActions,
                action =>
                {
                    InstallCardParameters actual = Assert.IsType<InstallCardParameters>(action.Parameters);
                    return actual.FinalizeSelection == expected.FinalizeSelection
                        && actual.SelectedReplacementCardIds.SequenceEqual(expected.SelectedReplacementCardIds);
                }));
        IReadOnlyList<InstallCardParameters> materializedTurn = GameEngine.MaterializeCorpInstallParameters(
            state,
            fixture.BehaviorCatalog,
            fixture.RuntimeCardCatalog);
        Assert.Contains(materializedTurn, parameters => parameters.CardId == fixture.IncomingCardId);
        Assert.Equal(allocatorBeforeMaterialization, opened.State.Allocator.NextSequence);
        Assert.Equal(hashBeforeMaterialization, DeterministicHash.ForState(opened.State));
        string hash = DeterministicHash.ForState(opened.State);
        string json = EngineJson.Serialize(opened.State);
        GameState roundTrip = EngineJson.DeserializeState(json, fixture.BehaviorCatalog, fixture.RuntimeCardCatalog);
        Assert.Equal(json, EngineJson.Serialize(roundTrip));
        Assert.Equal(hash, DeterministicHash.ForState(roundTrip));

        Observation corp = ObservationBuilder.Build(opened.State, PlayerSide.Corp, fixture.BehaviorCatalog);
        Observation runner = ObservationBuilder.Build(opened.State, PlayerSide.Runner, fixture.BehaviorCatalog);
        string corpJson = EngineJson.Serialize(corp);
        string runnerJson = EngineJson.Serialize(runner);
        Assert.Equal(pending.Owner, corp.Decision!.Owner);
        Assert.Equal(DecisionKind.InstallChoice, corp.Decision.Kind);
        Assert.Equal(pending.LegalActions.Count, corp.LegalActions.Count);
        Assert.Contains(new CardInstanceId("card:corp-replace-observe-existing").Value, corpJson);
        Assert.Contains(incoming.DefinitionId.Value, corpJson);
        Assert.Empty(runner.LegalActions);
        Assert.Null(runner.Decision);
        Assert.DoesNotContain(
            pending.LegalActions.Select(action => action.Id.Value),
            actionId => runnerJson.Contains(actionId, StringComparison.Ordinal));
        Assert.DoesNotContain(new CardInstanceId("card:corp-replace-observe-existing").Value, runnerJson);
        Assert.DoesNotContain(incoming.DefinitionId.Value, runnerJson);
    }

    [Fact]
    public void CorpInstallFrameRejectsCandidateMovementAndDestinationMutationWithoutChangingHash()
    {
        CardRuntimeSpec incoming = Agenda(new CardDefinitionId("test:corp-replace-stale-incoming"));
        CardRuntimeSpec existing = Asset(new CardDefinitionId("test:corp-replace-stale-existing"));
        Fixture fixture = CreateFixture(
            incoming,
            (existing, new CardInstanceId("card:corp-replace-stale-existing")));
        GameState state = OpenCorpAction(fixture);
        LegalAction initial = InstallAction(state, fixture.RemoteId);
        ApplyResult opened = Apply(state, fixture, initial);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        InstallFrame frame = Assert.IsType<InstallFrame>(opened.State.ResolutionStack[^1]);
        ZoneState root = opened.State.GetZone(frame.DestinationZoneId!.Value);
        ZoneState discard = opened.State.GetZone(ZoneKind.CorpDiscard);
        CardInstanceId candidate = frame.CandidateReplacementCardIds[0];
        GameState stale = ReplaceCardsAndZones(
            opened.State,
            opened.State.Cards.Select(card => card.Id == candidate
                ? new CardInstanceState(card.Id, card.DefinitionId, discard.Id, card.OwnerSide, card.FaceUp, card.Rezzed, card.AdvancementCounters)
                : card),
            opened.State.Zones.Select(zone => zone.Id == root.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Where(id => id != candidate).ToArray())
                : zone.Id == discard.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(candidate).ToArray())
                    : zone));
        string hash = DeterministicHash.ForState(stale);
        LegalAction action = Assert.Single(stale.PendingDecision!.LegalActions);
        ApplyResult rejected = Apply(stale, fixture, action);
        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Equal(hash, DeterministicHash.ForState(rejected.State));
        Assert.Contains(
            StateInvariantValidator.Collect(stale, fixture.BehaviorCatalog, fixture.RuntimeCardCatalog),
            violation => violation.Code is "resolution.stale-install-candidate" or "decision.install-materialization-failed");
    }

    [Fact]
    public void CorpInstallFrameRejectsChangedCandidateDefinitionEvenWhenRequiredSetIsUnchanged()
    {
        CardRuntimeSpec incoming = Upgrade(new CardDefinitionId("test:corp-replace-class-incoming"));
        CardRuntimeSpec existing = Asset(new CardDefinitionId("test:corp-replace-class-existing"));
        Fixture fixture = CreateFixture(
            incoming,
            (existing, new CardInstanceId("card:corp-replace-class-existing")));
        GameState state = OpenCorpAction(fixture);
        ApplyResult opened = Apply(state, fixture, InstallAction(state, fixture.RemoteId));
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        InstallFrame frame = Assert.IsType<InstallFrame>(opened.State.ResolutionStack[^1]);
        CardInstanceId candidate = frame.CandidateReplacementCardIds[0];
        GameState changed = ReplaceCardsAndZones(
            opened.State,
            opened.State.Cards.Select(card => card.Id == candidate
                ? new CardInstanceState(card.Id, incoming.DefinitionId, card.ZoneId, card.OwnerSide, card.FaceUp, card.Rezzed, card.AdvancementCounters)
                : card),
            opened.State.Zones);
        string hash = DeterministicHash.ForState(changed);
        LegalAction continuation = changed.PendingDecision!.LegalActions[0];
        ApplyResult rejected = Apply(changed, fixture, continuation);
        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Equal(hash, DeterministicHash.ForState(rejected.State));
        Assert.Contains(
            StateInvariantValidator.Collect(changed, fixture.BehaviorCatalog, fixture.RuntimeCardCatalog),
            violation => violation.Code == "resolution.install-candidate-definitions-mismatch");
    }

    [Fact]
    public void CorpInstallFrameRejectsForgedOwnerSourceClassTargetCostAndRequiredSetWithoutMutation()
    {
        CardRuntimeSpec incoming = Agenda(new CardDefinitionId("test:corp-replace-forged-incoming"));
        CardRuntimeSpec existing = Asset(new CardDefinitionId("test:corp-replace-forged-existing"));
        Fixture fixture = CreateFixture(
            incoming,
            (existing, new CardInstanceId("card:corp-replace-forged-existing")));
        GameState state = OpenCorpAction(fixture);
        ApplyResult opened = Apply(state, fixture, InstallAction(state, fixture.RemoteId));
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        InstallFrame frame = Assert.IsType<InstallFrame>(opened.State.ResolutionStack[^1]);
        PlayerId runnerId = opened.State.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        ZoneId discardId = opened.State.GetZone(ZoneKind.CorpDiscard).Id;
        ServerState headquarters = opened.State.Servers.Single(server => server.Kind == ServerKind.Headquarters);

        var forgedFrames = new[]
        {
            new InstallFrame(
                runnerId,
                frame.CardId,
                frame.DefinitionId,
                frame.CardClass,
                frame.SourceZoneId,
                frame.SourceZoneKind,
                frame.DestinationKind,
                frame.ServerId,
                frame.CreateNewRemote,
                frame.InstallCost,
                frame.CandidateReplacementCardIds,
                frame.RequiredReplacementCardIds,
                frame.SelectedReplacementCardIds,
                frame.DestinationZoneId,
                frame.CandidateReplacementDefinitionIds),
            new InstallFrame(
                frame.Owner,
                frame.CardId,
                frame.DefinitionId,
                frame.CardClass,
                discardId,
                frame.SourceZoneKind,
                frame.DestinationKind,
                frame.ServerId,
                frame.CreateNewRemote,
                frame.InstallCost,
                frame.CandidateReplacementCardIds,
                frame.RequiredReplacementCardIds,
                frame.SelectedReplacementCardIds,
                frame.DestinationZoneId,
                frame.CandidateReplacementDefinitionIds),
            new InstallFrame(
                frame.Owner,
                frame.CardId,
                frame.DefinitionId,
                RuntimeCardClass.Asset,
                frame.SourceZoneId,
                frame.SourceZoneKind,
                frame.DestinationKind,
                frame.ServerId,
                frame.CreateNewRemote,
                frame.InstallCost,
                frame.CandidateReplacementCardIds,
                frame.RequiredReplacementCardIds,
                frame.SelectedReplacementCardIds,
                frame.DestinationZoneId,
                frame.CandidateReplacementDefinitionIds),
            new InstallFrame(
                frame.Owner,
                frame.CardId,
                frame.DefinitionId,
                frame.CardClass,
                frame.SourceZoneId,
                frame.SourceZoneKind,
                InstallDestinationKind.ServerRoot,
                headquarters.Id,
                createNewRemote: false,
                frame.InstallCost,
                frame.CandidateReplacementCardIds,
                frame.RequiredReplacementCardIds,
                frame.SelectedReplacementCardIds,
                headquarters.RootZoneId,
                frame.CandidateReplacementDefinitionIds),
            new InstallFrame(
                frame.Owner,
                frame.CardId,
                frame.DefinitionId,
                frame.CardClass,
                frame.SourceZoneId,
                frame.SourceZoneKind,
                frame.DestinationKind,
                frame.ServerId,
                frame.CreateNewRemote,
                installCost: 1,
                frame.CandidateReplacementCardIds,
                frame.RequiredReplacementCardIds,
                frame.SelectedReplacementCardIds,
                frame.DestinationZoneId,
                frame.CandidateReplacementDefinitionIds),
            new InstallFrame(
                frame.Owner,
                frame.CardId,
                frame.DefinitionId,
                frame.CardClass,
                frame.SourceZoneId,
                frame.SourceZoneKind,
                frame.DestinationKind,
                frame.ServerId,
                frame.CreateNewRemote,
                frame.InstallCost,
                frame.CandidateReplacementCardIds,
                [],
                frame.SelectedReplacementCardIds,
                frame.DestinationZoneId,
                frame.CandidateReplacementDefinitionIds),
        };

        foreach (InstallFrame forgedFrame in forgedFrames)
        {
            GameState forged = ReplaceFrame(opened.State, forgedFrame);
            string hash = DeterministicHash.ForState(forged);
            ApplyResult rejected = Apply(forged, fixture, Assert.Single(forged.PendingDecision!.LegalActions));
            Assert.False(rejected.Accepted);
            Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
            Assert.Equal(hash, DeterministicHash.ForState(rejected.State));
        }
    }

    [Fact]
    public void CorpInstallChoiceRejectsOmittedAndDuplicateContinuationWithoutMutation()
    {
        CardRuntimeSpec incoming = Agenda(new CardDefinitionId("test:corp-replace-continuation-incoming"));
        CardRuntimeSpec existing = Asset(new CardDefinitionId("test:corp-replace-continuation-existing"));
        Fixture fixture = CreateFixture(
            incoming,
            (existing, new CardInstanceId("card:corp-replace-continuation-existing")));
        GameState state = OpenCorpAction(fixture);
        ApplyResult opened = Apply(state, fixture, InstallAction(state, fixture.RemoteId));
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        PendingDecision choice = Assert.IsType<PendingDecision>(opened.State.PendingDecision);
        LegalAction only = Assert.Single(choice.LegalActions);

        GameState omitted = ReplacePending(opened.State, []);
        string omittedHash = DeterministicHash.ForState(omitted);
        ApplyResult omittedResult = Apply(omitted, fixture, only);
        Assert.False(omittedResult.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, omittedResult.Rejection?.Code);
        Assert.Equal(omittedHash, DeterministicHash.ForState(omittedResult.State));

        LegalAction duplicate = new(
            new ActionId("action:corp-replace-continuation-duplicate"),
            only.Kind,
            only.Description,
            only.Parameters);
        GameState duplicated = ReplacePending(opened.State, [only, duplicate]);
        string duplicateHash = DeterministicHash.ForState(duplicated);
        ApplyResult duplicateResult = Apply(duplicated, fixture, only);
        Assert.False(duplicateResult.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, duplicateResult.Rejection?.Code);
        Assert.Equal(duplicateHash, DeterministicHash.ForState(duplicateResult.State));
    }

    private static LegalAction InstallAction(GameState state, ServerId serverId) =>
        Assert.Single(
            state.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.InstallCard
                && Assert.IsType<InstallCardParameters>(action.Parameters).ServerId == serverId);

    private static ApplyResult Apply(GameState state, Fixture fixture, LegalAction action) =>
        GameEngine.Apply(
            state,
            new ActionSubmission(state.PendingDecision!.Owner, state.PendingDecision.Id, state.PendingDecision.Revision, action.Id),
            fixture.BehaviorCatalog,
            fixture.RuntimeCardCatalog);

    private static GameState OpenCorpAction(Fixture fixture)
    {
        GameState state = GameEngine.BeginTurn(
            fixture.State,
            PlayerSide.Corp,
            fixture.BehaviorCatalog,
            fixture.RuntimeCardCatalog);
        for (int index = 0; index < 4; index++)
        {
            PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction pass = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.PassPriority);
            ApplyResult result = GameEngine.Apply(
                state,
                new ActionSubmission(decision.Owner, decision.Id, decision.Revision, pass.Id),
                fixture.BehaviorCatalog,
                fixture.RuntimeCardCatalog);
            Assert.True(result.Accepted, result.Rejection?.Message);
            state = result.State;
        }

        Assert.Equal(DecisionKind.TurnAction, state.PendingDecision!.Kind);
        return state;
    }

    private static Fixture CreateFixture(
        CardRuntimeSpec incoming,
        params (CardRuntimeSpec Spec, CardInstanceId Id)[] rootCards) =>
        CreateFixture(incoming, rootCards, includeRootSpecsInCatalog: true, incomingCardId: new CardInstanceId("card:corp-replace-incoming"));

    private static Fixture CreateFixture(
        CardRuntimeSpec incoming,
        IReadOnlyList<(CardRuntimeSpec Spec, CardInstanceId Id)> rootCards,
        bool includeRootSpecsInCatalog,
        CardInstanceId? incomingCardId = null)
    {
        CardDefinitionId drawDefinition = new("test:corp-replace-draw");
        CardInstanceId drawCard = new("card:corp-replace-draw");
        RuntimeCardCatalogBuilder catalogBuilder = new RuntimeCardCatalogBuilder("runtime:corp-replacement")
            .Add(incoming);
        if (includeRootSpecsInCatalog)
        {
            foreach ((CardRuntimeSpec spec, _) in rootCards)
            {
                catalogBuilder.Add(spec);
            }
        }

        RuntimeCardCatalog runtimeCatalog = catalogBuilder.Build();
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder(EngineCompatibility.BehaviorRegistryVersion).Build();
        GameState baseline = GameState.CreateNew(
            seed: 405,
            behaviorRegistryVersion: EngineCompatibility.BehaviorRegistryVersion,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);
        StableIdAllocator allocator = new(baseline.Allocator);
        ServerId remoteId = allocator.NextServerId();
        ZoneId remoteIceId = allocator.NextZoneId();
        ZoneId remoteRootId = allocator.NextZoneId();
        ServerState remote = new(remoteId, ServerKind.Remote, remoteIceId, remoteRootId);
        var zones = baseline.Zones.ToList();
        ZoneState hand = zones.Single(zone => zone.Kind == ZoneKind.CorpHand && zone.ServerId is null);
        ZoneState deck = zones.Single(zone => zone.Kind == ZoneKind.CorpDeck && zone.ServerId is null);
        CardInstanceId incomingId = incomingCardId ?? new CardInstanceId("card:corp-replace-incoming");
        zones[zones.IndexOf(hand)] = new ZoneState(
            hand.Id,
            hand.Kind,
            hand.OwnerSide,
            hand.ServerId,
            hand.CardIds.Append(incomingId).ToArray());
        zones[zones.IndexOf(deck)] = new ZoneState(
            deck.Id,
            deck.Kind,
            deck.OwnerSide,
            deck.ServerId,
            deck.CardIds.Append(drawCard).ToArray());
        zones.Add(new ZoneState(remoteIceId, ZoneKind.ServerIce, PlayerSide.Corp, remoteId));
        zones.Add(new ZoneState(
            remoteRootId,
            ZoneKind.ServerRoot,
            PlayerSide.Corp,
            remoteId,
            rootCards.Select(value => value.Id).ToArray()));
        var cards = new List<CardInstanceState>
        {
            new(incomingId, incoming.DefinitionId, hand.Id, PlayerSide.Corp),
            new(drawCard, drawDefinition, deck.Id, PlayerSide.Corp),
        };
        cards.AddRange(rootCards.Select(value => new CardInstanceState(value.Id, value.Spec.DefinitionId, remoteRootId, PlayerSide.Corp)));
        GameState state = new(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            baseline.Players,
            cards,
            zones,
            baseline.Servers.Append(remote).ToArray(),
            new TurnState(PlayerSide.Corp, TurnPhase.Corp, 1),
            baseline.Outcome,
            baseline.RandomState,
            allocator.State,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);
        return new Fixture(
            state,
            behaviorCatalog,
            runtimeCatalog,
            baseline.Players.Single(player => player.Side == PlayerSide.Corp).Id,
            remoteId,
            incomingId);
    }

    private static GameState ReplaceCardsAndZones(
        GameState source,
        IEnumerable<CardInstanceState> cards,
        IEnumerable<ZoneState> zones) =>
        new(
            source.SchemaVersion,
            source.GameId,
            source.RulesVersion,
            source.CardDataVersion,
            source.Players,
            cards.ToArray(),
            zones.ToArray(),
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

    private static GameState ReplaceFrame(GameState source, InstallFrame frame) =>
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
            source.ResolutionStack.Take(source.ResolutionStack.Count - 1).Append(frame).ToArray(),
            source.PendingDecision,
            source.ActionSequence,
            source.InitialSeed,
            source.RunState,
            source.RulesState,
            source.BehaviorRegistryVersion,
            source.BehaviorCatalogFingerprint,
            source.RuntimeCardCatalogVersion,
            source.RuntimeCardCatalogFingerprint);

    private static GameState ReplacePending(GameState source, IReadOnlyList<LegalAction> actions) =>
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
            new PendingDecision(
                source.PendingDecision!.Id,
                source.PendingDecision.Revision,
                source.PendingDecision.Owner,
                source.PendingDecision.Kind,
                source.PendingDecision.Prompt,
                actions),
            source.ActionSequence,
            source.InitialSeed,
            source.RunState,
            source.RulesState,
            source.BehaviorRegistryVersion,
            source.BehaviorCatalogFingerprint,
            source.RuntimeCardCatalogVersion,
            source.RuntimeCardCatalogFingerprint);

    private static CardRuntimeSpec Agenda(CardDefinitionId id, params string[] subtypes) => new(
        id,
        PlayerSide.Corp,
        RuntimeCardClass.Agenda,
        agendaPoints: 2,
        advancementRequirement: 3,
        subtypes: subtypes);

    private static CardRuntimeSpec Asset(CardDefinitionId id, params string[] subtypes) => new(
        id,
        PlayerSide.Corp,
        RuntimeCardClass.Asset,
        rezCost: 0,
        subtypes: subtypes);

    private static CardRuntimeSpec Upgrade(CardDefinitionId id, params string[] subtypes) => new(
        id,
        PlayerSide.Corp,
        RuntimeCardClass.Upgrade,
        rezCost: 0,
        subtypes: subtypes);

    private sealed record Fixture(
        GameState State,
        BehaviorCatalog BehaviorCatalog,
        RuntimeCardCatalog RuntimeCardCatalog,
        PlayerId CorpId,
        ServerId RemoteId,
        CardInstanceId IncomingCardId);
}

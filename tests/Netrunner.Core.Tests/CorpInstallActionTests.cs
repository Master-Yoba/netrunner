using Xunit;

namespace Netrunner.Core.Tests;

public sealed class CorpInstallActionTests
{
    [Fact]
    public void CorpInstallDispatchesActiveControllerTriggerForFacedownTypedSubject()
    {
        CardDefinitionId targetDefinitionId = new("test:corp-install-trigger-target");
        CardInstanceId targetCardId = new("card:corp-install-trigger-target");
        CardDefinitionId sourceDefinitionId = new("test:corp-install-trigger-source");
        CardInstanceId sourceCardId = new("card:corp-install-trigger-source");
        CardBehaviorDefinition targetBehavior = new(
            targetDefinitionId,
            PlayerSide.Corp,
            triggers:
            [
                new BehaviorTrigger(
                    "target-self",
                    OccurrenceKind.CardInstalled,
                    TriggerMode.Mandatory,
                    BehaviorTriggerScope.Self,
                    subjectCardFilter: null,
                    [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, 5)]),
            ]);
        CardBehaviorDefinition sourceBehavior = new(
            sourceDefinitionId,
            PlayerSide.Corp,
            triggers:
            [
                new BehaviorTrigger(
                    "controller-agenda",
                    OccurrenceKind.CardInstalled,
                    TriggerMode.Mandatory,
                    BehaviorTriggerScope.Controller,
                    new BehaviorCardFilter(BehaviorCardType.Agenda, ownerSide: PlayerSide.Corp),
                    [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, 2)]),
            ]);
        CardRuntimeSpec sourceSpec = Asset(sourceDefinitionId);
        CorpFixture fixture = CreateFixture(
            Agenda(targetDefinitionId),
            targetCardId,
            activeSources: [(sourceCardId, sourceSpec)],
            extraBehaviorDefinitions: [targetBehavior, sourceBehavior]);
        BehaviorCatalog behaviorCatalog = fixture.BehaviorCatalog;
        GameState state = OpenCorpAction(fixture);
        LegalAction install = Assert.Single(
            state.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.InstallCard
                && Assert.IsType<InstallCardParameters>(action.Parameters).ServerId == fixture.RemoteId);

        ApplyResult result = Apply(state, fixture, install);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.False(result.State.GetCard(targetCardId).FaceUp);
        Assert.False(result.State.GetCard(targetCardId).Rezzed);
        Assert.Equal(7, result.State.GetPlayer(fixture.CorpId).Credits);
        int installedIndex = Array.FindIndex(result.Events.ToArray(), value => value.Kind == EngineEventKinds.CardInstalled);
        int triggerCreditIndex = Array.FindIndex(
            result.Events.ToArray(),
            value => value.Kind == EngineEventKinds.CreditsAdjusted
                && Assert.IsType<CreditsAdjustedEventPayload>(value.Payload).Delta == 2);
        Assert.True(installedIndex >= 0);
        Assert.True(triggerCreditIndex > installedIndex);
        Assert.Equal(1, result.Events.Count(value => value.Kind == EngineEventKinds.CardInstalled));
        Assert.Empty(StateInvariantValidator.Collect(result.State, behaviorCatalog, fixture.RuntimeCardCatalog));
    }

    [Fact]
    public void CorpAgendaInstallIntoEmptyRemotePaysZeroCreditsOneClickAndResumesPriority()
    {
        CardDefinitionId definitionId = new("test:corp-agenda-install");
        CardInstanceId cardId = new("card:corp-agenda-install");
        CorpFixture fixture = CreateFixture(Agenda(definitionId), cardId);
        GameState state = OpenCorpAction(fixture);
        ServerId remoteId = fixture.RemoteId;

        LegalAction action = Assert.Single(
            state.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.InstallCard
                && Assert.IsType<InstallCardParameters>(candidate.Parameters).ServerId == remoteId);
        InstallCardParameters parameters = Assert.IsType<InstallCardParameters>(action.Parameters);
        Assert.Equal(cardId, parameters.CardId);
        Assert.Equal(InstallDestinationKind.ServerRoot, parameters.DestinationKind);
        Assert.Equal(remoteId, parameters.ServerId);
        Assert.False(parameters.CreateNewRemote);
        Assert.Equal(0, parameters.CreditCost);
        Assert.True(parameters.FinalizeSelection);
        Assert.Empty(parameters.CandidateReplacementCardIds);
        Assert.Empty(parameters.RequiredReplacementCardIds);
        Assert.Empty(parameters.SelectedReplacementCardIds);

        PlayerState corpBefore = state.GetPlayer(fixture.CorpId);
        ApplyResult result = Apply(state, fixture, action);

        Assert.True(result.Accepted, result.Rejection?.Message);
        PlayerState corpAfter = result.State.GetPlayer(fixture.CorpId);
        Assert.Equal(corpBefore.Credits, corpAfter.Credits);
        Assert.Equal(corpBefore.Clicks - 1, corpAfter.Clicks);
        CardInstanceState installed = result.State.GetCard(cardId);
        Assert.Equal(result.State.GetServer(remoteId).RootZoneId, installed.ZoneId);
        Assert.False(installed.FaceUp);
        Assert.False(installed.Rezzed);
        Assert.Contains(cardId, result.State.Turn.ThisTurnFacts!.InstalledCardIds);
        Assert.Equal(DecisionKind.TurnTiming, result.State.PendingDecision!.Kind);
        Assert.Equal(TurnFrameStage.ActionPriority, Assert.IsType<TurnFrame>(result.State.ResolutionStack[^1]).Stage);
        Assert.Single(result.Events, value => value.Kind == EngineEventKinds.CardMoved);
        Assert.Single(result.Events, value => value.Kind == EngineEventKinds.CardInstalled);
        Assert.Single(result.Events, value => value.Kind == EngineEventKinds.ClickSpent);
        Assert.DoesNotContain(result.Events, value => value.Kind == EngineEventKinds.CreditsAdjusted);
        CardInstalledEventPayload installedEvent = Assert.IsType<CardInstalledEventPayload>(
            result.Events.Single(value => value.Kind == EngineEventKinds.CardInstalled).Payload);
        Assert.Equal(0, installedEvent.Cost);
        Assert.Equal(PlayerSide.Corp, installedEvent.Side);
    }

    [Fact]
    public void CorpAssetInstallIntoEmptyRemotePaysZeroCreditsOneClickAndResumesPriority()
    {
        CardDefinitionId definitionId = new("test:corp-asset-install");
        CardInstanceId cardId = new("card:corp-asset-install");
        CorpFixture fixture = CreateFixture(Asset(definitionId), cardId);
        GameState state = OpenCorpAction(fixture);

        LegalAction action = Assert.Single(
            state.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.InstallCard
                && Assert.IsType<InstallCardParameters>(candidate.Parameters).ServerId == fixture.RemoteId);
        ApplyResult result = Apply(state, fixture, action);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(4, result.State.Servers.Count);
        Assert.Equal(5, result.State.GetPlayer(fixture.CorpId).Credits);
        Assert.Equal(2, result.State.GetPlayer(fixture.CorpId).Clicks);
        CardInstanceState installed = result.State.GetCard(cardId);
        Assert.Equal(result.State.GetServer(fixture.RemoteId).RootZoneId, installed.ZoneId);
        Assert.False(installed.FaceUp);
        Assert.False(installed.Rezzed);
        Assert.Equal(DecisionKind.TurnTiming, result.State.PendingDecision!.Kind);
    }

    [Fact]
    public void CorpUpgradeInstallMayTargetAnEmptyCentralRoot()
    {
        CardDefinitionId definitionId = new("test:corp-upgrade-install");
        CardInstanceId cardId = new("card:corp-upgrade-install");
        CorpFixture fixture = CreateFixture(Upgrade(definitionId), cardId);
        GameState state = OpenCorpAction(fixture);
        ServerId headquarters = fixture.State.Servers.Single(server => server.Kind == ServerKind.Headquarters).Id;

        LegalAction action = Assert.Single(
            state.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.InstallCard
                && Assert.IsType<InstallCardParameters>(candidate.Parameters).ServerId == headquarters);
        InstallCardParameters parameters = Assert.IsType<InstallCardParameters>(action.Parameters);
        Assert.Equal(0, parameters.CreditCost);
        Assert.False(parameters.CreateNewRemote);

        ApplyResult result = Apply(state, fixture, action);

        Assert.True(result.Accepted, result.Rejection?.Message);
        CardInstanceState installed = result.State.GetCard(cardId);
        Assert.Equal(result.State.GetServer(headquarters).RootZoneId, installed.ZoneId);
        Assert.False(installed.FaceUp);
        Assert.False(installed.Rezzed);
        Assert.Equal(2, result.State.GetPlayer(fixture.CorpId).Clicks);
        Assert.Equal(5, result.State.GetPlayer(fixture.CorpId).Credits);
    }

    [Fact]
    public void CorpAgendaInstallCanCreateExactlyOneRemoteAtCommit()
    {
        CardDefinitionId definitionId = new("test:corp-agenda-new-remote");
        CardInstanceId cardId = new("card:corp-agenda-new-remote");
        CorpFixture fixture = CreateFixture(Agenda(definitionId), cardId);
        GameState state = OpenCorpAction(fixture);
        int serverCount = state.Servers.Count;
        int zoneCount = state.Zones.Count;
        ulong nextSequence = state.Allocator.NextSequence;
        LegalAction action = Assert.Single(
            state.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.InstallCard
                && Assert.IsType<InstallCardParameters>(candidate.Parameters).CreateNewRemote);

        // Materialized validation/planning is detached and must not consume
        // topology IDs, even when the action explicitly requests a new remote.
        Assert.Equal(serverCount, state.Servers.Count);
        Assert.Equal(zoneCount, state.Zones.Count);
        Assert.Equal(nextSequence, state.Allocator.NextSequence);

        ApplyResult result = Apply(state, fixture, action);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(serverCount + 1, result.State.Servers.Count);
        Assert.Equal(zoneCount + 2, result.State.Zones.Count);
        ServerState created = Assert.Single(
            result.State.Servers,
            server => !state.Servers.Any(existing => existing.Id == server.Id));
        Assert.Equal(ServerKind.Remote, created.Kind);
        Assert.Empty(result.State.GetZone(created.IceZoneId).CardIds);
        Assert.Equal(created.RootZoneId, result.State.GetCard(cardId).ZoneId);
        Assert.Single(result.State.GetZone(created.RootZoneId).CardIds, card => card == cardId);
        Assert.Equal(2, result.State.GetPlayer(fixture.CorpId).Clicks);
        Assert.Equal(5, result.State.GetPlayer(fixture.CorpId).Credits);
    }

    [Fact]
    public void CorpUpgradeInstallCanCreateExactlyOneRemoteAtCommit()
    {
        CardDefinitionId definitionId = new("test:corp-upgrade-new-remote");
        CardInstanceId cardId = new("card:corp-upgrade-new-remote");
        CorpFixture fixture = CreateFixture(Upgrade(definitionId), cardId);
        GameState state = OpenCorpAction(fixture);
        LegalAction action = Assert.Single(
            state.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.InstallCard
                && Assert.IsType<InstallCardParameters>(candidate.Parameters).CreateNewRemote);

        ApplyResult result = Apply(state, fixture, action);

        Assert.True(result.Accepted, result.Rejection?.Message);
        ServerState created = Assert.Single(
            result.State.Servers,
            server => !state.Servers.Any(existing => existing.Id == server.Id));
        Assert.Equal(ServerKind.Remote, created.Kind);
        Assert.Equal(created.RootZoneId, result.State.GetCard(cardId).ZoneId);
        Assert.Empty(result.State.GetZone(created.IceZoneId).CardIds);
        Assert.Equal(2, result.State.GetPlayer(fixture.CorpId).Clicks);
        Assert.Equal(5, result.State.GetPlayer(fixture.CorpId).Credits);
    }

    [Fact]
    public void CorpAgendaAndAssetAreNeverIssuedForCentralRoots()
    {
        foreach (CardRuntimeSpec spec in new[]
        {
            Agenda(new CardDefinitionId("test:corp-agenda-central")),
            Asset(new CardDefinitionId("test:corp-asset-central")),
        })
        {
            CorpFixture fixture = CreateFixture(spec, new CardInstanceId($"card:{spec.CardClass.ToString().ToLowerInvariant()}-central"));
            GameState state = OpenCorpAction(fixture);
            ServerId[] centralServers = state.Servers
                .Where(server => server.Kind is ServerKind.Headquarters or ServerKind.RAndD or ServerKind.Archives)
                .Select(server => server.Id)
                .ToArray();

            Assert.DoesNotContain(
                state.PendingDecision!.LegalActions,
                action => action.Kind == ActionKind.InstallCard
                    && Assert.IsType<InstallCardParameters>(action.Parameters).ServerId is { } serverId
                    && centralServers.Contains(serverId));
        }
    }

    [Fact]
    public void CorpOccupiedRootOpensReplacementCheckpoint()
    {
        CardDefinitionId definitionId = new("test:corp-occupied-agenda");
        CardInstanceId cardId = new("card:corp-occupied-agenda");
        CorpFixture fixture = CreateFixture(Agenda(definitionId), cardId, occupyRemote: true);
        GameState state = OpenCorpAction(fixture);

        LegalAction occupied = Assert.Single(
            state.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.InstallCard
                && Assert.IsType<InstallCardParameters>(action.Parameters).ServerId == fixture.RemoteId);
        InstallCardParameters parameters = Assert.IsType<InstallCardParameters>(occupied.Parameters);
        Assert.False(parameters.FinalizeSelection);
        Assert.Equal([new CardInstanceId("card:corp-install-occupied")], parameters.CandidateReplacementCardIds);
        Assert.Equal(parameters.CandidateReplacementCardIds, parameters.RequiredReplacementCardIds);

        ApplyResult result = Apply(state, fixture, occupied);
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(state.GetPlayer(fixture.CorpId).Clicks, result.State.GetPlayer(fixture.CorpId).Clicks);
        Assert.Equal(state.GetPlayer(fixture.CorpId).Credits, result.State.GetPlayer(fixture.CorpId).Credits);
        Assert.Equal(state.GetZone(state.GetServer(fixture.RemoteId).RootZoneId).CardIds,
            result.State.GetZone(result.State.GetServer(fixture.RemoteId).RootZoneId).CardIds);
        Assert.Equal(state.GetCard(cardId), result.State.GetCard(cardId));
        Assert.IsType<InstallFrame>(result.State.ResolutionStack[^1]);
        Assert.Equal(DecisionKind.InstallChoice, result.State.PendingDecision!.Kind);
        LegalAction select = Assert.Single(result.State.PendingDecision.LegalActions);
        ApplyResult selected = Apply(result.State, fixture, select);
        Assert.True(selected.Accepted, selected.Rejection?.Message);
        LegalAction finalize = Assert.Single(
            selected.State.PendingDecision!.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);
        Assert.Equal(parameters.RequiredReplacementCardIds, Assert.IsType<InstallCardParameters>(finalize.Parameters).SelectedReplacementCardIds);
    }

    [Fact]
    public void CorpUniqueInstallIsNotSuppressedByAnActiveCopyBeforeRez()
    {
        CardDefinitionId definitionId = new("test:corp-unique-asset");
        CardInstanceId cardId = new("card:corp-unique-asset");
        CardRuntimeSpec spec = new(
            definitionId,
            PlayerSide.Corp,
            RuntimeCardClass.Asset,
            rezCost: 0,
            unique: true,
            uniquenessKey: "test:corp-unique-asset");
        CorpFixture fixture = CreateFixture(spec, cardId);
        ServerState headquarters = fixture.State.Servers.Single(server => server.Kind == ServerKind.Headquarters);
        ZoneState headquartersRoot = fixture.State.GetZone(headquarters.RootZoneId);
        CardInstanceId activeCopyId = new("card:corp-unique-asset-active-copy");
        GameState stateWithActiveCopy = ReplaceCardsAndZones(
            fixture.State,
            fixture.State.Cards.Append(new CardInstanceState(
                activeCopyId,
                definitionId,
                headquartersRoot.Id,
                PlayerSide.Corp,
                faceUp: true,
                rezzed: true)),
            fixture.State.Zones.Select(zone => zone.Id == headquartersRoot.Id
                ? new ZoneState(
                    zone.Id,
                    zone.Kind,
                    zone.OwnerSide,
                    zone.ServerId,
                    zone.CardIds.Append(activeCopyId).ToArray())
                : zone));
        fixture = fixture with { State = stateWithActiveCopy };

        GameState state = OpenCorpAction(fixture);

        Assert.Contains(
            state.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.InstallCard
                && Assert.IsType<InstallCardParameters>(action.Parameters).ServerId == fixture.RemoteId);
    }

    [Fact]
    public void CorpOwnerSeesHqDefinitionAndInstallTargetsButRunnerSeesNeitherActionNorFacedownIdentity()
    {
        CardDefinitionId definitionId = new("test:corp-hidden-agenda");
        CardInstanceId cardId = new("card:corp-hidden-agenda");
        CorpFixture fixture = CreateFixture(Agenda(definitionId), cardId);
        GameState state = OpenCorpAction(fixture);
        Observation corpObservation = ObservationBuilder.Build(state, PlayerSide.Corp, fixture.BehaviorCatalog);
        Observation runnerObservation = ObservationBuilder.Build(state, PlayerSide.Runner, fixture.BehaviorCatalog);

        Assert.Contains(corpObservation.LegalActions, action => action.Kind == ActionKind.InstallCard);
        Assert.Contains(definitionId.Value, EngineJson.Serialize(corpObservation));
        Assert.Empty(runnerObservation.LegalActions);
        Assert.DoesNotContain(cardId.Value, EngineJson.Serialize(runnerObservation));
        Assert.DoesNotContain(definitionId.Value, EngineJson.Serialize(runnerObservation));

        LegalAction action = Assert.Single(
            state.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.InstallCard
                && Assert.IsType<InstallCardParameters>(candidate.Parameters).ServerId == fixture.RemoteId);
        ApplyResult result = Apply(state, fixture, action);
        Assert.True(result.Accepted, result.Rejection?.Message);
        Observation runnerAfter = ObservationBuilder.Build(result.State, PlayerSide.Runner, fixture.BehaviorCatalog);
        Assert.DoesNotContain(cardId.Value, EngineJson.Serialize(runnerAfter));
        Assert.DoesNotContain(definitionId.Value, EngineJson.Serialize(runnerAfter));
    }

    [Fact]
    public void CorpForgedInstallTargetAndCostFailInvariantParityWithoutTopologyMutation()
    {
        CardDefinitionId definitionId = new("test:corp-forged-install");
        CardInstanceId cardId = new("card:corp-forged-install");
        CorpFixture fixture = CreateFixture(Agenda(definitionId), cardId);
        GameState state = OpenCorpAction(fixture);
        PendingDecision pending = state.PendingDecision!;
        LegalAction issued = Assert.Single(
            pending.LegalActions,
            action => action.Kind == ActionKind.InstallCard
                && Assert.IsType<InstallCardParameters>(action.Parameters).CreateNewRemote);
        InstallCardParameters forgedParameters = new(
            cardId,
            InstallDestinationKind.ServerRoot,
            state.Servers.Single(server => server.Kind == ServerKind.Headquarters).Id,
            createNewRemote: false,
            creditCost: 1,
            candidateReplacementCardIds: [],
            requiredReplacementCardIds: [],
            selectedReplacementCardIds: [],
            finalizeSelection: true);
        LegalAction forged = new(issued.Id, ActionKind.InstallCard, issued.Description, forgedParameters);
        GameState forgedState = ReplacePending(state, pending.LegalActions.Select(action => action.Id == issued.Id ? forged : action).ToArray());

        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(
            forgedState,
            fixture.BehaviorCatalog,
            fixture.RuntimeCardCatalog);
        Assert.Contains(violations, violation => violation.Code == "decision.extra-install-action");
        int serverCount = forgedState.Servers.Count;
        int zoneCount = forgedState.Zones.Count;
        ApplyResult rejected = GameEngine.Apply(
            forgedState,
            new ActionSubmission(pending.Owner, pending.Id, pending.Revision, forged.Id),
            fixture.BehaviorCatalog,
            fixture.RuntimeCardCatalog);
        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Equal(serverCount, rejected.State.Servers.Count);
        Assert.Equal(zoneCount, rejected.State.Zones.Count);
        Assert.Equal(DeterministicHash.ForState(forgedState), DeterministicHash.ForState(rejected.State));
    }

    private static ApplyResult Apply(GameState state, CorpFixture fixture, LegalAction action) =>
        GameEngine.Apply(
            state,
            new ActionSubmission(
                state.PendingDecision!.Owner,
                state.PendingDecision.Id,
                state.PendingDecision.Revision,
                action.Id),
            fixture.BehaviorCatalog,
            fixture.RuntimeCardCatalog);

    private static GameState OpenCorpAction(CorpFixture fixture)
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

    private static CorpFixture CreateFixture(
        CardRuntimeSpec targetSpec,
        CardInstanceId targetCardId,
        bool occupyRemote = false,
        IReadOnlyList<(CardInstanceId CardId, CardRuntimeSpec Spec)>? activeSources = null,
        IReadOnlyList<CardBehaviorDefinition>? extraBehaviorDefinitions = null)
    {
        CardDefinitionId drawDefinitionId = new("test:corp-install-draw");
        CardInstanceId drawCardId = new("card:corp-install-draw");
        CardDefinitionId occupiedDefinitionId = new("test:corp-install-occupied");
        CardInstanceId occupiedCardId = new("card:corp-install-occupied");
        RuntimeCardCatalogBuilder runtimeBuilder = new RuntimeCardCatalogBuilder("runtime:corp-install")
            .Add(targetSpec)
            .Add(Asset(occupiedDefinitionId));
        foreach (var activeSource in activeSources ?? [])
        {
            runtimeBuilder.Add(activeSource.Spec);
        }

        RuntimeCardCatalog runtimeCardCatalog = runtimeBuilder.Build();
        BehaviorCatalogBuilder behaviorBuilder = new BehaviorCatalogBuilder(EngineCompatibility.BehaviorRegistryVersion);
        foreach (CardBehaviorDefinition definition in extraBehaviorDefinitions ?? [])
        {
            behaviorBuilder.Add(definition);
        }

        BehaviorCatalog behaviorCatalog = behaviorBuilder.Build();
        GameState baseline = GameState.CreateNew(
            seed: 405,
            behaviorRegistryVersion: EngineCompatibility.BehaviorRegistryVersion,
            runtimeCardCatalogVersion: runtimeCardCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCardCatalog.Fingerprint);
        StableIdAllocator allocator = new(baseline.Allocator);
        ServerId remoteId = allocator.NextServerId();
        ZoneId remoteIceId = allocator.NextZoneId();
        ZoneId remoteRootId = allocator.NextZoneId();
        ServerState remote = new(remoteId, ServerKind.Remote, remoteIceId, remoteRootId);
        var servers = baseline.Servers.Append(remote).ToArray();
        var zones = baseline.Zones.ToList();
        ZoneState hand = zones.Single(zone => zone.Kind == ZoneKind.CorpHand && zone.ServerId is null);
        ZoneState deck = zones.Single(zone => zone.Kind == ZoneKind.CorpDeck && zone.ServerId is null);
        zones[zones.IndexOf(hand)] = new ZoneState(
            hand.Id,
            hand.Kind,
            hand.OwnerSide,
            hand.ServerId,
            hand.CardIds.Append(targetCardId).ToArray());
        zones[zones.IndexOf(deck)] = new ZoneState(
            deck.Id,
            deck.Kind,
            deck.OwnerSide,
            deck.ServerId,
            deck.CardIds.Append(drawCardId).ToArray());
        zones.Add(new ZoneState(remoteIceId, ZoneKind.ServerIce, PlayerSide.Corp, remoteId));
        zones.Add(new ZoneState(
            remoteRootId,
            ZoneKind.ServerRoot,
            PlayerSide.Corp,
            remoteId,
            occupyRemote ? [occupiedCardId] : []));
        ServerState headquarters = baseline.Servers.Single(server => server.Kind == ServerKind.Headquarters);
        ZoneState headquartersRoot = zones.Single(zone => zone.Id == headquarters.RootZoneId);
        if (activeSources is { Count: > 0 })
        {
            zones[zones.IndexOf(headquartersRoot)] = new ZoneState(
                headquartersRoot.Id,
                headquartersRoot.Kind,
                headquartersRoot.OwnerSide,
                headquartersRoot.ServerId,
                headquartersRoot.CardIds.Concat(activeSources.Select(source => source.CardId)).ToArray());
        }

        var cards = new List<CardInstanceState>
        {
            new(targetCardId, targetSpec.DefinitionId, hand.Id, PlayerSide.Corp),
            new(drawCardId, drawDefinitionId, deck.Id, PlayerSide.Corp),
        };
        if (occupyRemote)
        {
            cards.Add(new CardInstanceState(occupiedCardId, occupiedDefinitionId, remoteRootId, PlayerSide.Corp));
        }

        foreach ((CardInstanceId cardId, CardRuntimeSpec spec) in activeSources ?? [])
        {
            cards.Add(new CardInstanceState(
                cardId,
                spec.DefinitionId,
                headquartersRoot.Id,
                PlayerSide.Corp,
                faceUp: true,
                rezzed: true));
        }

        GameState state = new(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            baseline.Players,
            cards,
            zones,
            servers,
            new TurnState(PlayerSide.Corp, TurnPhase.Corp, 1),
            baseline.Outcome,
            baseline.RandomState,
            allocator.State,
            runtimeCardCatalogVersion: runtimeCardCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCardCatalog.Fingerprint);
        return new CorpFixture(state, behaviorCatalog, runtimeCardCatalog, baseline.Players.Single(player => player.Side == PlayerSide.Corp).Id, remoteId);
    }

    private static GameState ReplacePending(GameState source, IReadOnlyList<LegalAction> actions)
    {
        PendingDecision pending = source.PendingDecision!;
        PendingDecision replacement = new(pending.Id, pending.Revision, pending.Owner, pending.Kind, pending.Prompt, actions);
        return new GameState(
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
            replacement,
            source.ActionSequence,
            source.InitialSeed,
            source.RunState,
            source.RulesState,
            source.BehaviorRegistryVersion,
            source.BehaviorCatalogFingerprint,
            source.RuntimeCardCatalogVersion,
            source.RuntimeCardCatalogFingerprint);
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

    private static CardRuntimeSpec Agenda(CardDefinitionId id) => new(
        id,
        PlayerSide.Corp,
        RuntimeCardClass.Agenda,
        agendaPoints: 2,
        advancementRequirement: 3);

    private static CardRuntimeSpec Asset(CardDefinitionId id) => new(
        id,
        PlayerSide.Corp,
        RuntimeCardClass.Asset,
        rezCost: 0);

    private static CardRuntimeSpec Upgrade(CardDefinitionId id) => new(
        id,
        PlayerSide.Corp,
        RuntimeCardClass.Upgrade,
        rezCost: 0);

    private sealed record CorpFixture(
        GameState State,
        BehaviorCatalog BehaviorCatalog,
        RuntimeCardCatalog RuntimeCardCatalog,
        PlayerId CorpId,
        ServerId RemoteId);
}

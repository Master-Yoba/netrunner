using Xunit;

namespace Netrunner.Core.Tests;

public sealed class CorpIceInstallTests
{
    [Fact]
    public void CorpIceEnumeratesEveryExistingServerAndOnlyServerIceNewRemote()
    {
        CardRuntimeSpec incoming = Ice(new CardDefinitionId("test:corp-ice-enumerate"));
        Fixture fixture = CreateFixture(incoming, credits: 5);
        GameState state = OpenCorpAction(fixture);

        InstallCardParameters[] installs = state.PendingDecision!.LegalActions
            .Where(action => action.Kind == ActionKind.InstallCard)
            .Select(action => Assert.IsType<InstallCardParameters>(action.Parameters))
            .Where(parameters => parameters.CardId == fixture.IncomingId)
            .ToArray();

        Assert.Equal(state.Servers.Count + 1, installs.Length);
        Assert.Equal(state.Servers.Select(server => server.Id), installs
            .Where(parameters => !parameters.CreateNewRemote)
            .Select(parameters => parameters.ServerId!.Value));
        Assert.Single(installs, parameters => parameters.CreateNewRemote);
        Assert.All(installs, parameters => Assert.Equal(InstallDestinationKind.ServerIce, parameters.DestinationKind));
        Assert.All(installs, parameters => Assert.Equal(PlayerSide.Corp, state.GetCard(parameters.CardId).OwnerSide));
        Assert.DoesNotContain(installs, parameters => parameters.DestinationKind == InstallDestinationKind.ServerRoot);
    }

    [Fact]
    public void EmptyExistingIceInstallsForZeroCreditsAndAppendsOutermost()
    {
        CardRuntimeSpec incoming = Ice(new CardDefinitionId("test:corp-ice-empty"));
        Fixture fixture = CreateFixture(incoming, credits: 0);
        GameState state = OpenCorpAction(fixture);
        LegalAction action = FindInstall(state, fixture.RemoteId, createNewRemote: false);
        InstallCardParameters parameters = Assert.IsType<InstallCardParameters>(action.Parameters);
        Assert.Equal(0, parameters.CreditCost);
        Assert.True(parameters.FinalizeSelection);
        Assert.Empty(parameters.CandidateReplacementCardIds);

        ZoneId iceZoneId = state.GetServer(fixture.RemoteId).IceZoneId;
        ApplyResult result = Apply(state, fixture, action);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal([fixture.IncomingId], result.State.GetZone(iceZoneId).CardIds);
        Assert.Empty(result.State.GetZone(result.State.GetServer(fixture.RemoteId).RootZoneId).CardIds);
        Assert.Equal(0, result.State.GetPlayer(fixture.CorpId).Credits);
        Assert.Equal(2, result.State.GetPlayer(fixture.CorpId).Clicks);
        Assert.Equal(
            new[]
            {
                EngineEventKinds.ClickSpent,
                EngineEventKinds.CardMoved,
                EngineEventKinds.CardInstalled,
                EngineEventKinds.TurnTimingWindowOpened,
            },
            result.Events.Select(value => value.Kind));
        Assert.DoesNotContain(result.Events, value => value.Kind == EngineEventKinds.CreditsAdjusted);
        Assert.False(result.State.GetCard(fixture.IncomingId).FaceUp);
        Assert.False(result.State.GetCard(fixture.IncomingId).Rezzed);
        Assert.Contains(fixture.IncomingId, result.State.Turn.ThisTurnFacts!.InstalledCardIds);
        Assert.Equal(DecisionKind.TurnTiming, result.State.PendingDecision!.Kind);
        Assert.Equal(TurnFrameStage.ActionPriority, Assert.IsType<TurnFrame>(result.State.ResolutionStack[^1]).Stage);

        string corpJson = EngineJson.Serialize(ObservationBuilder.Build(result.State, PlayerSide.Corp, fixture.BehaviorCatalog));
        string runnerJson = EngineJson.Serialize(ObservationBuilder.Build(result.State, PlayerSide.Runner, fixture.BehaviorCatalog));
        Assert.Contains(fixture.IncomingId.Value, corpJson, StringComparison.Ordinal);
        Assert.Contains(parameters.CardId.Value, corpJson, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.IncomingId.Value, runnerJson, StringComparison.Ordinal);
        Assert.DoesNotContain(parameters.CardId.Value, runnerJson, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingIceUsesPhysicalOrderAndDynamicRemainingCost()
    {
        CardRuntimeSpec incoming = Ice(new CardDefinitionId("test:corp-ice-existing-incoming"));
        CardRuntimeSpec inner = Ice(new CardDefinitionId("test:corp-ice-inner"));
        CardRuntimeSpec outer = Ice(new CardDefinitionId("test:corp-ice-outer"));
        Fixture fixture = CreateFixture(
            incoming,
            credits: 5,
            (inner, new CardInstanceId("card:z-inner")),
            (outer, new CardInstanceId("card:a-outer")));
        GameState state = OpenCorpAction(fixture);
        LegalAction initial = FindInstall(state, fixture.RemoteId, createNewRemote: false);
        InstallCardParameters initialParameters = Assert.IsType<InstallCardParameters>(initial.Parameters);
        Assert.Equal(2, initialParameters.CreditCost);
        Assert.False(initialParameters.FinalizeSelection);
        Assert.Equal([new CardInstanceId("card:z-inner"), new CardInstanceId("card:a-outer")], initialParameters.CandidateReplacementCardIds);
        Assert.Empty(initialParameters.RequiredReplacementCardIds);

        ZoneId iceZoneId = state.GetServer(fixture.RemoteId).IceZoneId;
        ApplyResult opened = Apply(state, fixture, initial);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        Assert.Equal(state.GetZone(iceZoneId).CardIds, opened.State.GetZone(iceZoneId).CardIds);
        Assert.Equal(state.GetPlayer(fixture.CorpId).Credits, opened.State.GetPlayer(fixture.CorpId).Credits);
        Assert.Equal(3, opened.State.PendingDecision!.LegalActions.Count);
        AssertChoiceParity(opened.State, fixture);
        Assert.Contains(opened.State.PendingDecision.LegalActions, action =>
            Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection
            && Assert.IsType<InstallCardParameters>(action.Parameters).CreditCost == 2);

        LegalAction selectInner = Assert.Single(opened.State.PendingDecision.LegalActions, action =>
            !Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection
            && Assert.IsType<InstallCardParameters>(action.Parameters).SelectedReplacementCardIds.SequenceEqual([new CardInstanceId("card:z-inner")]));
        ApplyResult selected = Apply(opened.State, fixture, selectInner);
        Assert.True(selected.Accepted, selected.Rejection?.Message);
        AssertChoiceParity(selected.State, fixture);
        InstallCardParameters[] continuations = selected.State.PendingDecision!.LegalActions
            .Select(action => Assert.IsType<InstallCardParameters>(action.Parameters))
            .ToArray();
        Assert.Equal(2, continuations.Length);
        Assert.Contains(continuations, parameters => parameters.FinalizeSelection
            && parameters.CreditCost == 1
            && parameters.SelectedReplacementCardIds.SequenceEqual([new CardInstanceId("card:z-inner")]));
        Assert.Contains(continuations, parameters => !parameters.FinalizeSelection
            && parameters.CreditCost == 0
            && parameters.SelectedReplacementCardIds.SequenceEqual([
                new CardInstanceId("card:z-inner"),
                new CardInstanceId("card:a-outer")
            ]));

        LegalAction finalize = Assert.Single(selected.State.PendingDecision.LegalActions, action =>
            Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);
        ApplyResult result = Apply(selected.State, fixture, finalize);
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal([
            new CardInstanceId("card:a-outer"),
            fixture.IncomingId,
        ], result.State.GetZone(iceZoneId).CardIds);
        Assert.Equal([new CardInstanceId("card:z-inner")], result.State.GetZone(ZoneKind.CorpDiscard).CardIds
            .Where(id => id == new CardInstanceId("card:z-inner")));
        Assert.Equal(4, result.State.GetPlayer(fixture.CorpId).Credits);
        Assert.Equal(2, result.State.GetPlayer(fixture.CorpId).Clicks);
        Assert.Equal(2, result.Events.Count(value => value.Kind == EngineEventKinds.CardMoved));
        Assert.Equal(
            new[]
            {
                EngineEventKinds.CardMoved,
                EngineEventKinds.CreditsAdjusted,
                EngineEventKinds.ClickSpent,
                EngineEventKinds.CardMoved,
                EngineEventKinds.CardInstalled,
                EngineEventKinds.TurnTimingWindowOpened,
            },
            result.Events.Select(value => value.Kind));
    }

    [Fact]
    public void UnaffordableFinalizeIsOmittedUntilSelectionsReduceCost()
    {
        CardRuntimeSpec incoming = Ice(new CardDefinitionId("test:corp-ice-unaffordable"));
        CardRuntimeSpec first = Ice(new CardDefinitionId("test:corp-ice-unaffordable-first"));
        CardRuntimeSpec second = Ice(new CardDefinitionId("test:corp-ice-unaffordable-second"));
        Fixture fixture = CreateFixture(
            incoming,
            credits: 1,
            (first, new CardInstanceId("card:ice-unaffordable-first")),
            (second, new CardInstanceId("card:ice-unaffordable-second")));
        GameState state = OpenCorpAction(fixture);
        ApplyResult opened = Apply(state, fixture, FindInstall(state, fixture.RemoteId, createNewRemote: false));
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        Assert.Equal(2, opened.State.PendingDecision!.LegalActions.Count);
        Assert.DoesNotContain(opened.State.PendingDecision.LegalActions, action => Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);
        Assert.Single(
            opened.State.PendingDecision.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters).SelectedReplacementCardIds
                .SequenceEqual([new CardInstanceId("card:ice-unaffordable-first")]));
        Assert.Single(
            opened.State.PendingDecision.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters).SelectedReplacementCardIds
                .SequenceEqual([new CardInstanceId("card:ice-unaffordable-second")]));

        LegalAction select = Assert.Single(opened.State.PendingDecision.LegalActions, action =>
            !Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection
            && Assert.IsType<InstallCardParameters>(action.Parameters).SelectedReplacementCardIds.SequenceEqual([new CardInstanceId("card:ice-unaffordable-first")]));
        ApplyResult afterOne = Apply(opened.State, fixture, select);
        Assert.True(afterOne.Accepted, afterOne.Rejection?.Message);
        AssertChoiceParity(afterOne.State, fixture);
        Assert.Equal(2, afterOne.State.PendingDecision!.LegalActions.Count);
        Assert.Contains(afterOne.State.PendingDecision!.LegalActions, action =>
            Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection
            && Assert.IsType<InstallCardParameters>(action.Parameters).CreditCost == 1);
        Assert.Contains(afterOne.State.PendingDecision.LegalActions, action =>
            !Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection
            && Assert.IsType<InstallCardParameters>(action.Parameters).SelectedReplacementCardIds
                .SequenceEqual([
                    new CardInstanceId("card:ice-unaffordable-first"),
                    new CardInstanceId("card:ice-unaffordable-second")
                ])
            && Assert.IsType<InstallCardParameters>(action.Parameters).CreditCost == 0);
    }

    [Fact]
    public void SelectAllIceAllowsZeroCreditFinalizeWithoutCreditEvent()
    {
        CardRuntimeSpec incoming = Ice(new CardDefinitionId("test:corp-ice-select-all"));
        CardRuntimeSpec first = Ice(new CardDefinitionId("test:corp-ice-select-all-first"));
        CardRuntimeSpec second = Ice(new CardDefinitionId("test:corp-ice-select-all-second"));
        Fixture fixture = CreateFixture(
            incoming,
            credits: 0,
            (first, new CardInstanceId("card:ice-select-all-first")),
            (second, new CardInstanceId("card:ice-select-all-second")));
        GameState state = OpenCorpAction(fixture);
        ApplyResult opened = Apply(state, fixture, FindInstall(state, fixture.RemoteId, createNewRemote: false));
        Assert.True(opened.Accepted, opened.Rejection?.Message);

        LegalAction firstSelection = Assert.Single(opened.State.PendingDecision!.LegalActions, action =>
            !Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection
            && Assert.IsType<InstallCardParameters>(action.Parameters).SelectedReplacementCardIds.SequenceEqual([new CardInstanceId("card:ice-select-all-first")]));
        ApplyResult afterFirst = Apply(opened.State, fixture, firstSelection);
        Assert.True(afterFirst.Accepted, afterFirst.Rejection?.Message);
        LegalAction secondSelection = Assert.Single(afterFirst.State.PendingDecision!.LegalActions, action =>
            !Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);
        ApplyResult afterSecond = Apply(afterFirst.State, fixture, secondSelection);
        Assert.True(afterSecond.Accepted, afterSecond.Rejection?.Message);
        AssertChoiceParity(afterSecond.State, fixture);
        LegalAction finalize = Assert.Single(afterSecond.State.PendingDecision!.LegalActions, action =>
            Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);
        InstallCardParameters finalizeParameters = Assert.IsType<InstallCardParameters>(finalize.Parameters);
        Assert.Equal(0, finalizeParameters.CreditCost);
        Assert.Equal(
            new[]
            {
                new CardInstanceId("card:ice-select-all-first"),
                new CardInstanceId("card:ice-select-all-second"),
            },
            finalizeParameters.SelectedReplacementCardIds);

        ApplyResult result = Apply(afterSecond.State, fixture, finalize);
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(0, result.State.GetPlayer(fixture.CorpId).Credits);
        Assert.DoesNotContain(result.Events, value => value.Kind == EngineEventKinds.CreditsAdjusted);
        Assert.Equal(2, result.State.GetZone(ZoneKind.CorpDiscard).CardIds.Count);
        Assert.Equal(
            new[]
            {
                EngineEventKinds.CardMoved,
                EngineEventKinds.CardMoved,
                EngineEventKinds.ClickSpent,
                EngineEventKinds.CardMoved,
                EngineEventKinds.CardInstalled,
                EngineEventKinds.TurnTimingWindowOpened,
            },
            result.Events.Select(value => value.Kind));
    }

    [Fact]
    public void NewRemoteIceAllocatesOnlyAtCommitAndTargetsIceZone()
    {
        CardRuntimeSpec incoming = Ice(new CardDefinitionId("test:corp-ice-new-remote"));
        Fixture fixture = CreateFixture(incoming, credits: 0);
        GameState state = OpenCorpAction(fixture);
        int serverCount = state.Servers.Count;
        int zoneCount = state.Zones.Count;
        ulong nextSequence = state.Allocator.NextSequence;
        LegalAction action = FindInstall(state, serverId: null, createNewRemote: true);

        Assert.Equal(serverCount, state.Servers.Count);
        Assert.Equal(zoneCount, state.Zones.Count);
        Assert.Equal(nextSequence, state.Allocator.NextSequence);
        ApplyResult result = Apply(state, fixture, action);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(serverCount + 1, result.State.Servers.Count);
        Assert.Equal(zoneCount + 2, result.State.Zones.Count);
        ServerState created = Assert.Single(result.State.Servers, server => !state.Servers.Any(old => old.Id == server.Id));
        Assert.Equal(ServerKind.Remote, created.Kind);
        Assert.Equal(created.IceZoneId, result.State.GetCard(fixture.IncomingId).ZoneId);
        Assert.Equal([fixture.IncomingId], result.State.GetZone(created.IceZoneId).CardIds);
        Assert.Empty(result.State.GetZone(created.RootZoneId).CardIds);
    }

    [Fact]
    public void PendingIceFrameRoundTripsAndRemainsOwnerOnly()
    {
        CardRuntimeSpec incoming = Ice(new CardDefinitionId("test:corp-ice-roundtrip"));
        CardRuntimeSpec existing = Ice(new CardDefinitionId("test:corp-ice-roundtrip-existing"));
        Fixture fixture = CreateFixture(
            incoming,
            credits: 0,
            (existing, new CardInstanceId("card:corp-ice-roundtrip-existing")));
        GameState state = OpenCorpAction(fixture);
        ApplyResult opened = Apply(state, fixture, FindInstall(state, fixture.RemoteId, createNewRemote: false));
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        Assert.IsType<InstallFrame>(opened.State.ResolutionStack[^1]);
        string json = EngineJson.Serialize(opened.State);
        GameState restored = EngineJson.DeserializeState(json, fixture.BehaviorCatalog, fixture.RuntimeCardCatalog);
        Assert.Equal(DeterministicHash.ForState(opened.State), DeterministicHash.ForState(restored));
        AssertChoiceParity(restored, fixture);
        ulong nextSequence = restored.Allocator.NextSequence;
        string hashBeforeCollection = DeterministicHash.ForState(restored);
        Assert.Empty(StateInvariantValidator.Collect(restored, fixture.BehaviorCatalog, fixture.RuntimeCardCatalog));
        Assert.Equal(nextSequence, restored.Allocator.NextSequence);
        Assert.Equal(hashBeforeCollection, DeterministicHash.ForState(restored));
        _ = GameEngine.MaterializeCorpInstallParameters(restored, fixture.BehaviorCatalog, fixture.RuntimeCardCatalog);
        _ = GameEngine.MaterializeInstallChoiceParameters(
            restored,
            Assert.IsType<InstallFrame>(restored.ResolutionStack[^1]),
            fixture.BehaviorCatalog,
            fixture.RuntimeCardCatalog);
        Assert.Equal(nextSequence, restored.Allocator.NextSequence);
        GameState snapshotClone = GameState.FromSnapshot(
            restored.CreateSnapshot(fixture.BehaviorCatalog, fixture.RuntimeCardCatalog),
            fixture.BehaviorCatalog,
            fixture.RuntimeCardCatalog);
        Assert.Equal(json, EngineJson.Serialize(snapshotClone));
        Assert.Equal(DeterministicHash.ForState(restored), DeterministicHash.ForState(snapshotClone));

        Observation corp = ObservationBuilder.Build(restored, PlayerSide.Corp, fixture.BehaviorCatalog);
        Observation runner = ObservationBuilder.Build(restored, PlayerSide.Runner, fixture.BehaviorCatalog);
        Assert.Contains(corp.LegalActions, action => action.Kind == ActionKind.InstallCard);
        Assert.Empty(runner.LegalActions);
        Assert.Contains(incoming.DefinitionId.Value, EngineJson.Serialize(corp));
        Assert.DoesNotContain(incoming.DefinitionId.Value, EngineJson.Serialize(runner));
        Assert.DoesNotContain(fixture.IncomingId.Value, EngineJson.Serialize(runner));
    }

    [Fact]
    public void IceFrameRejectsForgedDynamicCostWithoutMutation()
    {
        CardRuntimeSpec incoming = Ice(new CardDefinitionId("test:corp-ice-forged-cost"));
        CardRuntimeSpec existing = Ice(new CardDefinitionId("test:corp-ice-forged-cost-existing"));
        Fixture fixture = CreateFixture(
            incoming,
            credits: 5,
            (existing, new CardInstanceId("card:corp-ice-forged-cost-existing")));
        GameState state = OpenCorpAction(fixture);
        ApplyResult opened = Apply(state, fixture, FindInstall(state, fixture.RemoteId, createNewRemote: false));
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        InstallFrame frame = Assert.IsType<InstallFrame>(opened.State.ResolutionStack[^1]);
        InstallFrame forged = new(
            frame.Owner,
            frame.CardId,
            frame.DefinitionId,
            frame.CardClass,
            frame.SourceZoneId,
            frame.SourceZoneKind,
            frame.DestinationKind,
            frame.ServerId,
            frame.CreateNewRemote,
            frame.InstallCost + 1,
            frame.CandidateReplacementCardIds,
            frame.RequiredReplacementCardIds,
            frame.SelectedReplacementCardIds,
            frame.DestinationZoneId,
            frame.CandidateReplacementDefinitionIds);
        GameState forgedState = new(
            opened.State.SchemaVersion,
            opened.State.GameId,
            opened.State.RulesVersion,
            opened.State.CardDataVersion,
            opened.State.Players,
            opened.State.Cards,
            opened.State.Zones,
            opened.State.Servers,
            opened.State.Turn,
            opened.State.Outcome,
            opened.State.RandomState,
            opened.State.Allocator,
            opened.State.DecisionRevision,
            opened.State.EventSequence,
            opened.State.ResolutionStack.Take(opened.State.ResolutionStack.Count - 1).Append(forged).ToArray(),
            opened.State.PendingDecision,
            opened.State.ActionSequence,
            opened.State.InitialSeed,
            opened.State.RunState,
            opened.State.RulesState,
            opened.State.BehaviorRegistryVersion,
            opened.State.BehaviorCatalogFingerprint,
            opened.State.RuntimeCardCatalogVersion,
            opened.State.RuntimeCardCatalogFingerprint);
        string hash = DeterministicHash.ForState(forgedState);
        LegalAction pendingAction = Assert.Single(
            forgedState.PendingDecision!.LegalActions,
            action => Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection);
        ApplyResult rejected = Apply(forgedState, fixture, pendingAction);
        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Equal(hash, DeterministicHash.ForState(rejected.State));
        Assert.Contains(StateInvariantValidator.Collect(forgedState, fixture.BehaviorCatalog, fixture.RuntimeCardCatalog),
            violation => violation.Code == "resolution.install-cost-mismatch");
    }

    [Fact]
    public void IceInstallFailsClosedForUnknownOrKnownNonIceProtectorMetadata()
    {
        CardRuntimeSpec incoming = Ice(new CardDefinitionId("test:corp-ice-metadata-incoming"));
        CardRuntimeSpec unknown = Ice(new CardDefinitionId("test:corp-ice-metadata-unknown"));
        Fixture unknownFixture = CreateFixtureWithoutExistingMetadata(
            incoming,
            credits: 5,
            (unknown, new CardInstanceId("card:corp-ice-metadata-unknown")));
        GameState unknownState = OpenCorpAction(unknownFixture);
        Assert.DoesNotContain(
            unknownState.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.InstallCard
                && Assert.IsType<InstallCardParameters>(action.Parameters).ServerId == unknownFixture.RemoteId);

        CardRuntimeSpec nonIce = Asset(new CardDefinitionId("test:corp-ice-metadata-asset"));
        Fixture nonIceFixture = CreateFixture(incoming, credits: 5, (nonIce, new CardInstanceId("card:corp-ice-metadata-asset")));
        GameState nonIceState = OpenCorpAction(nonIceFixture);
        Assert.DoesNotContain(
            nonIceState.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.InstallCard
                && Assert.IsType<InstallCardParameters>(action.Parameters).ServerId == nonIceFixture.RemoteId);
    }

    [Fact]
    public void IceChoiceRejectsRebuiltDestinationAndMovedCandidateWithoutMutation()
    {
        CardRuntimeSpec incoming = Ice(new CardDefinitionId("test:corp-ice-stale-incoming"));
        CardRuntimeSpec existing = Ice(new CardDefinitionId("test:corp-ice-stale-existing"));
        CardInstanceId existingId = new("card:corp-ice-stale-existing");
        Fixture fixture = CreateFixture(incoming, credits: 5, (existing, existingId));
        GameState state = OpenCorpAction(fixture);
        LegalAction initial = FindInstall(state, fixture.RemoteId, createNewRemote: false);
        ApplyResult opened = Apply(state, fixture, initial);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        InstallFrame frame = Assert.IsType<InstallFrame>(opened.State.ResolutionStack[^1]);
        LegalAction continuation = opened.State.PendingDecision!.LegalActions[0];

        ServerState server = opened.State.GetServer(fixture.RemoteId);
        ZoneState oldIce = opened.State.GetZone(server.IceZoneId);
        ZoneId rebuiltIceId = new("zone:corp-ice-rebuilt");
        ServerState rebuiltServer = new(server.Id, server.Kind, rebuiltIceId, server.RootZoneId);
        ZoneState rebuiltIce = new(rebuiltIceId, ZoneKind.ServerIce, PlayerSide.Corp, server.Id, oldIce.CardIds);
        GameState rebuilt = RebuildState(
            opened.State,
            opened.State.Cards.Select(card => card.ZoneId == oldIce.Id
                ? new CardInstanceState(card.Id, card.DefinitionId, rebuiltIceId, card.OwnerSide, card.FaceUp, card.Rezzed, card.AdvancementCounters)
                : card),
            opened.State.Zones.Where(zone => zone.Id != oldIce.Id).Append(rebuiltIce),
            opened.State.Servers.Select(value => value.Id == server.Id ? rebuiltServer : value));
        AssertRejectedAsInvalidState(rebuilt, fixture, continuation);
        Assert.Contains(
            StateInvariantValidator.Collect(rebuilt, fixture.BehaviorCatalog, fixture.RuntimeCardCatalog),
            violation => violation.Code == "resolution.install-destination-mismatch");

        ZoneState discard = opened.State.GetZone(ZoneKind.CorpDiscard);
        GameState moved = RebuildState(
            opened.State,
            opened.State.Cards.Select(card => card.Id == existingId
                ? new CardInstanceState(card.Id, card.DefinitionId, discard.Id, card.OwnerSide, card.FaceUp, card.Rezzed, card.AdvancementCounters)
                : card),
            opened.State.Zones.Select(zone => zone.Id == oldIce.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Where(id => id != existingId).ToArray())
                : zone.Id == discard.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(existingId).ToArray())
                    : zone));
        AssertRejectedAsInvalidState(moved, fixture, continuation);
        Assert.Contains(
            StateInvariantValidator.Collect(moved, fixture.BehaviorCatalog, fixture.RuntimeCardCatalog),
            violation => violation.Code is "resolution.stale-install-candidate" or "resolution.install-candidates-mismatch");

        static void AssertRejectedAsInvalidState(GameState stale, Fixture fixture, LegalAction action)
        {
            string hash = DeterministicHash.ForState(stale);
            ApplyResult rejected = Apply(stale, fixture, action);
            Assert.False(rejected.Accepted);
            Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
            Assert.Equal(hash, DeterministicHash.ForState(rejected.State));
        }
    }

    private static void AssertChoiceParity(GameState state, Fixture fixture)
    {
        InstallFrame frame = Assert.IsType<InstallFrame>(state.ResolutionStack[^1]);
        IReadOnlyList<InstallCardParameters> expected = GameEngine.MaterializeInstallChoiceParameters(
            state,
            frame,
            fixture.BehaviorCatalog,
            fixture.RuntimeCardCatalog);
        InstallCardParameters[] actual = state.PendingDecision!.LegalActions
            .Select(action => Assert.IsType<InstallCardParameters>(action.Parameters))
            .ToArray();
        Assert.Equal(expected.Count, actual.Length);
        foreach (InstallCardParameters expectedParameters in expected)
        {
            Assert.Contains(actual, actualParameters => InstallParametersMatch(actualParameters, expectedParameters));
        }
    }

    private static bool InstallParametersMatch(InstallCardParameters actual, InstallCardParameters expected) =>
        actual.CardId == expected.CardId
        && actual.DestinationKind == expected.DestinationKind
        && actual.ServerId == expected.ServerId
        && actual.CreateNewRemote == expected.CreateNewRemote
        && actual.CreditCost == expected.CreditCost
        && actual.CandidateReplacementCardIds.SequenceEqual(expected.CandidateReplacementCardIds)
        && actual.RequiredReplacementCardIds.SequenceEqual(expected.RequiredReplacementCardIds)
        && actual.SelectedReplacementCardIds.SequenceEqual(expected.SelectedReplacementCardIds)
        && actual.FinalizeSelection == expected.FinalizeSelection;

    private static LegalAction FindInstall(GameState state, ServerId? serverId, bool createNewRemote)
    {
        return Assert.Single(
            state.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.InstallCard
                && Assert.IsType<InstallCardParameters>(action.Parameters).DestinationKind == InstallDestinationKind.ServerIce
                && Assert.IsType<InstallCardParameters>(action.Parameters).ServerId == serverId
                && Assert.IsType<InstallCardParameters>(action.Parameters).CreateNewRemote == createNewRemote);
    }

    private static ApplyResult Apply(GameState state, Fixture fixture, LegalAction action) =>
        GameEngine.Apply(
            state,
            new ActionSubmission(state.PendingDecision!.Owner, state.PendingDecision.Id, state.PendingDecision.Revision, action.Id),
            fixture.BehaviorCatalog,
            fixture.RuntimeCardCatalog);

    private static GameState OpenCorpAction(Fixture fixture)
    {
        GameState state = GameEngine.BeginTurn(fixture.State, PlayerSide.Corp, fixture.BehaviorCatalog, fixture.RuntimeCardCatalog);
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
        int credits,
        params (CardRuntimeSpec Spec, CardInstanceId Id)[] iceCards) =>
        CreateFixtureCore(incoming, credits, includeExistingMetadata: true, iceCards);

    private static Fixture CreateFixtureWithoutExistingMetadata(
        CardRuntimeSpec incoming,
        int credits,
        params (CardRuntimeSpec Spec, CardInstanceId Id)[] iceCards) =>
        CreateFixtureCore(incoming, credits, includeExistingMetadata: false, iceCards);

    private static Fixture CreateFixtureCore(
        CardRuntimeSpec incoming,
        int credits,
        bool includeExistingMetadata,
        params (CardRuntimeSpec Spec, CardInstanceId Id)[] iceCards)
    {
        CardDefinitionId drawDefinition = new("test:corp-ice-draw");
        CardInstanceId drawCard = new("card:corp-ice-draw");
        RuntimeCardCatalogBuilder catalogBuilder = new RuntimeCardCatalogBuilder("runtime:corp-ice-install")
            .Add(incoming);
        if (includeExistingMetadata)
        {
            foreach ((CardRuntimeSpec spec, _) in iceCards)
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
        ZoneState hand = baseline.Zones.Single(zone => zone.Kind == ZoneKind.CorpHand && zone.ServerId is null);
        ZoneState deck = baseline.Zones.Single(zone => zone.Kind == ZoneKind.CorpDeck && zone.ServerId is null);
        var zones = baseline.Zones
            .Select(zone => zone.Id == hand.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(incomingCardId()).ToArray())
                : zone.Id == deck.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(drawCard).ToArray())
                    : zone)
            .Append(new ZoneState(remoteIceId, ZoneKind.ServerIce, PlayerSide.Corp, remoteId, iceCards.Select(value => value.Id).ToArray()))
            .Append(new ZoneState(remoteRootId, ZoneKind.ServerRoot, PlayerSide.Corp, remoteId))
            .ToArray();
        var players = baseline.Players
            .Select(player => player.Side == PlayerSide.Corp
                ? new PlayerState(player.Id, player.Side, credits, player.Clicks, player.AgendaPoints)
                : player)
            .ToArray();
        var cards = new List<CardInstanceState>
        {
            new(incomingCardId(), incoming.DefinitionId, hand.Id, PlayerSide.Corp),
            new(drawCard, drawDefinition, deck.Id, PlayerSide.Corp),
        };
        cards.AddRange(iceCards.Select(value => new CardInstanceState(value.Id, value.Spec.DefinitionId, remoteIceId, PlayerSide.Corp)));
        GameState state = new(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            players,
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
            players.Single(player => player.Side == PlayerSide.Corp).Id,
            remoteId,
            incomingCardId());

        CardInstanceId incomingCardId() => new("card:corp-ice-incoming");
    }

    private static CardRuntimeSpec Ice(CardDefinitionId id) => new(
        id,
        PlayerSide.Corp,
        RuntimeCardClass.Ice,
        rezCost: 0);

    private static CardRuntimeSpec Asset(CardDefinitionId id) => new(
        id,
        PlayerSide.Corp,
        RuntimeCardClass.Asset,
        rezCost: 0);

    private static GameState RebuildState(
        GameState source,
        IEnumerable<CardInstanceState> cards,
        IEnumerable<ZoneState> zones,
        IEnumerable<ServerState>? servers = null) =>
        new(
            source.SchemaVersion,
            source.GameId,
            source.RulesVersion,
            source.CardDataVersion,
            source.Players,
            cards.ToArray(),
            zones.ToArray(),
            (servers ?? source.Servers).ToArray(),
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

    private sealed record Fixture(
        GameState State,
        BehaviorCatalog BehaviorCatalog,
        RuntimeCardCatalog RuntimeCardCatalog,
        PlayerId CorpId,
        ServerId RemoteId,
        CardInstanceId IncomingId);
}

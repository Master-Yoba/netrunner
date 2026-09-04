using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// Public Apply-boundary coverage for the typed printed ICE subroutine
/// continuations.  The fixture deliberately uses synthetic definitions so
/// each test can isolate one declaration while retaining the production
/// planner, run cursor, and serialized frame machinery.
/// </summary>
public sealed class IceSubroutineRuntimeTests
{
    private static readonly CardDefinitionId TrashIce = new("fixture:ice-choice-trash");
    private static readonly CardDefinitionId PayIce = new("fixture:ice-choice-pay");
    private static readonly CardDefinitionId NormalInstallIce = new("fixture:ice-choice-normal-install");
    private static readonly CardDefinitionId InwardInstallIce = new("fixture:ice-choice-inward-install");
    private static readonly CardDefinitionId RestrictionIce = new("fixture:ice-choice-run-restriction");
    private static readonly CardDefinitionId RunnerProgram = new("fixture:ice-choice-runner-program");
    private static readonly CardDefinitionId RunnerHardware = new("fixture:ice-choice-runner-hardware");
    private static readonly CardDefinitionId RunnerResource = new("fixture:ice-choice-runner-resource");
    private static readonly CardDefinitionId CorpAsset = new("fixture:ice-choice-corp-asset");
    private static readonly CardDefinitionId CorpIce = new("fixture:ice-choice-corp-ice");

    [Fact]
    public void TrashChoiceOffersEveryInstalledRunnerClassAndMovesOnlyTheSelectedCard()
    {
        BehaviorCatalog behavior = Catalog(TrashIce, new TrashInstalledRunnerCardSubroutineSpec());
        RuntimeCardCatalog runtime = RuntimeCatalog(
            new CardRuntimeSpec(TrashIce, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 0),
            new CardRuntimeSpec(RunnerProgram, PlayerSide.Runner, RuntimeCardClass.Program, installCost: 0, memoryUnits: 1),
            new CardRuntimeSpec(RunnerHardware, PlayerSide.Runner, RuntimeCardClass.Hardware, installCost: 0),
            new CardRuntimeSpec(RunnerResource, PlayerSide.Runner, RuntimeCardClass.Resource, installCost: 0));
        GameState initial = BuildState(
            behavior,
            runtime,
            TrashIce,
            placements:
            [
                (RunnerProgram, PlayerSide.Runner, ZoneKind.RunnerRig),
                (RunnerHardware, PlayerSide.Runner, ZoneKind.RunnerRig),
                (RunnerResource, PlayerSide.Runner, ZoneKind.RunnerRig),
            ]);

        GameState choice = ReachIceChoice(initial, behavior, runtime);
        PendingDecision decision = Assert.IsType<PendingDecision>(choice.PendingDecision);
        CardInstanceId runnerProgramId = FindCard(choice, RunnerProgram).Id;
        CardInstanceId runnerHardwareId = FindCard(choice, RunnerHardware).Id;
        CardInstanceId runnerResourceId = FindCard(choice, RunnerResource).Id;
        IceSubroutineChoiceParameters[] choices = decision.LegalActions
            .Select(action => Assert.IsType<IceSubroutineChoiceParameters>(action.Parameters))
            .ToArray();
        Assert.Equal(3, choices.Length);
        Assert.Equal(
            new[] { runnerHardwareId, runnerProgramId, runnerResourceId }.OrderBy(id => id.Value),
            choices.Select(choice => choice.CardId!.Value).OrderBy(id => id.Value));
        Assert.All(choices, selected => Assert.Equal(IceSubroutineChoiceKind.TrashInstalledRunnerCard, selected.Choice));

        LegalAction hardware = Assert.Single(
            decision.LegalActions,
            action => Assert.IsType<IceSubroutineChoiceParameters>(action.Parameters).CardId == runnerHardwareId);
        ApplyResult result = Apply(choice, hardware, behavior, runtime);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(RunTimingStep.MovementPaidWindow, result.State.RunState!.Step);
        Assert.Contains(runnerHardwareId, result.State.GetZone(ZoneKind.RunnerHeap).CardIds);
        Assert.DoesNotContain(runnerProgramId, result.State.GetZone(ZoneKind.RunnerHeap).CardIds);
        Assert.DoesNotContain(runnerResourceId, result.State.GetZone(ZoneKind.RunnerHeap).CardIds);
        Assert.Contains(
            result.Events,
            value => value.Kind == EngineEventKinds.CardMoved
                && value.Payload is CardMovedEventPayload payload
                && payload.CardId == runnerHardwareId);
        Assert.Contains(result.Events, value => value.Kind == EngineEventKinds.EncounterCompleted);
        Assert.Empty(StateInvariantValidator.Collect(result.State, behavior, runtime));
    }

    [Fact]
    public void FunhouseChoiceEnumeratesEveryLegalHostedSplitAndTagFallback()
    {
        BehaviorCatalog behavior = Catalog(PayIce, new PayCreditsOrTagSubroutineSpec(4, 1));
        RuntimeCardCatalog runtime = RuntimeCatalog(
            new CardRuntimeSpec(PayIce, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 0));
        GameState initial = BuildState(behavior, runtime, PayIce, runnerCredits: 4, includeRulesState: true);
        GameState encounterPaid = ReachEncounterPaid(initial, behavior, runtime);
        GameState withRunCredits = WithRunState(encounterPaid, encounterPaid.RunState!.WithRunCredits(2));
        GameState choice = PassTwice(withRunCredits, behavior, runtime);

        PendingDecision decision = Assert.IsType<PendingDecision>(choice.PendingDecision);
        IceSubroutineChoiceParameters[] payChoices = decision.LegalActions
            .Select(action => Assert.IsType<IceSubroutineChoiceParameters>(action.Parameters))
            .Where(parameters => parameters.Choice == IceSubroutineChoiceKind.PayCredits)
            .ToArray();
        Assert.Equal([0, 1, 2], payChoices.Select(parameters => parameters.HostedCredits).ToArray());
        Assert.Contains(decision.LegalActions, action =>
            Assert.IsType<IceSubroutineChoiceParameters>(action.Parameters).Choice == IceSubroutineChoiceKind.TakeTag);

        LegalAction split = Assert.Single(
            decision.LegalActions,
            action => action.Parameters is IceSubroutineChoiceParameters parameters
                && parameters.Choice == IceSubroutineChoiceKind.PayCredits
                && parameters.HostedCredits == 2);
        ApplyResult paid = Apply(choice, split, behavior, runtime);
        Assert.True(paid.Accepted, paid.Rejection?.Message);
        Assert.Equal(2, paid.State.Players.Single(player => player.Side == PlayerSide.Runner).Credits);
        Assert.Equal(0, paid.State.RunState!.RunCredits);
        Assert.Equal(0, paid.State.RulesState!.Players.Single(player => player.PlayerId == paid.State.Players.Single(candidate => candidate.Side == PlayerSide.Runner).Id).Tags);
        Assert.Contains(paid.Events, value => value.Kind == EngineEventKinds.RunCreditsAdjusted);

        LegalAction fallback = Assert.Single(
            decision.LegalActions,
            action => action.Parameters is IceSubroutineChoiceParameters parameters
                && parameters.Choice == IceSubroutineChoiceKind.TakeTag);
        ApplyResult tagged = Apply(choice, fallback, behavior, runtime);
        Assert.True(tagged.Accepted, tagged.Rejection?.Message);
        Assert.Equal(1, tagged.State.RulesState!.Players.Single(player => player.PlayerId == tagged.State.Players.Single(candidate => candidate.Side == PlayerSide.Runner).Id).Tags);
        Assert.Equal(4, tagged.State.Players.Single(player => player.Side == PlayerSide.Runner).Credits);
        Assert.Equal(2, tagged.State.RunState!.RunCredits);
    }

    [Fact]
    public void ChoiceFrameRoundTripsAndRejectsTheConsumedActionAsStale()
    {
        BehaviorCatalog behavior = Catalog(PayIce, new PayCreditsOrTagSubroutineSpec(4, 1));
        RuntimeCardCatalog runtime = RuntimeCatalog(
            new CardRuntimeSpec(PayIce, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 0));
        GameState choice = ReachIceChoice(
            BuildState(behavior, runtime, PayIce, runnerCredits: 4, includeRulesState: true),
            behavior,
            runtime);
        Assert.IsType<IceSubroutineFrame>(choice.ResolutionStack[^1]);
        string json = EngineJson.Serialize(choice);
        Assert.Contains("\"$type\":\"ice-subroutine\"", json, StringComparison.Ordinal);
        GameState restored = EngineJson.DeserializeState(json, behavior, runtime);
        Assert.Equal(json, EngineJson.Serialize(restored));

        LegalAction original = Assert.Single(
            choice.PendingDecision!.LegalActions,
            action => action.Parameters is IceSubroutineChoiceParameters parameters
                && parameters.Choice == IceSubroutineChoiceKind.TakeTag);
        ApplyResult left = Apply(choice, original, behavior, runtime);
        LegalAction replay = Assert.Single(
            restored.PendingDecision!.LegalActions,
            action => action.Parameters is IceSubroutineChoiceParameters parameters
                && parameters.Choice == IceSubroutineChoiceKind.TakeTag);
        ApplyResult right = Apply(restored, replay, behavior, runtime);
        Assert.True(left.Accepted, left.Rejection?.Message);
        Assert.True(right.Accepted, right.Rejection?.Message);
        Assert.Equal(EngineJson.Serialize(left.State), EngineJson.Serialize(right.State));
        Assert.Equal(DeterministicHash.ForEvents(left.Events), DeterministicHash.ForEvents(right.Events));

        ApplyResult stale = Apply(left.State, original, behavior, runtime);
        Assert.Equal(ApplyRejectionCode.UnknownAction, stale.Rejection?.Code);
        Assert.Same(left.State, stale.State);
    }

    [Fact]
    public void ForgedFrameChoiceSetAndPendingActionBijectionAreRejectedAtomically()
    {
        BehaviorCatalog behavior = Catalog(PayIce, new PayCreditsOrTagSubroutineSpec(4, 1));
        RuntimeCardCatalog runtime = RuntimeCatalog(
            new CardRuntimeSpec(PayIce, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 0));
        GameState choice = ReachIceChoice(
            BuildState(behavior, runtime, PayIce, runnerCredits: 4, includeRulesState: true),
            behavior,
            runtime);
        IceSubroutineFrame frame = Assert.IsType<IceSubroutineFrame>(choice.ResolutionStack[^1]);
        IceSubroutineChoiceParameters forged = new(
            frame.FrameIdentity,
            IceSubroutineChoiceKind.PayCredits,
            creditCost: 3);
        IceSubroutineChoiceParameters[] alteredChoices = frame.Choices
            .Select(existing => existing.Choice == IceSubroutineChoiceKind.PayCredits ? forged : existing)
            .ToArray();
        IceSubroutineFrame altered = new(
            frame.Version,
            frame.BehaviorId,
            frame.FrameIdentity,
            frame.RunId,
            frame.EncounterId,
            frame.IceId,
            frame.IceDefinitionId,
            frame.SubroutineId,
            frame.Ordinal,
            frame.ServerId,
            frame.IceZoneId,
            frame.IceZoneCardIds,
            frame.CatalogFingerprint,
            frame.Stage,
            alteredChoices);
        GameState tampered = ReplaceTopFrame(choice, altered);
        string before = EngineJson.Serialize(tampered);
        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(tampered, behavior, runtime);
        Assert.Contains(violations, violation => violation.Code == "resolution.subroutine-choice-set-mismatch");
        Assert.Contains(violations, violation => violation.Code == "resolution.subroutine-action-bijection");

        LegalAction action = Assert.Single(
            tampered.PendingDecision!.LegalActions,
            candidate => candidate.Parameters is IceSubroutineChoiceParameters parameters
                && parameters.Choice == IceSubroutineChoiceKind.TakeTag);
        ApplyResult rejected = Apply(tampered, action, behavior, runtime);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Same(tampered, rejected.State);
        Assert.Equal(before, EngineJson.Serialize(rejected.State));
    }

    [Fact]
    public void NormalInstallUsesNoClickAndCanTrashTheEncounteredIceThenPassesThatPosition()
    {
        BehaviorCatalog behavior = Catalog(NormalInstallIce, new InstallCardSubroutineSpec());
        RuntimeCardCatalog runtime = RuntimeCatalog(
            new CardRuntimeSpec(NormalInstallIce, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 0),
            new CardRuntimeSpec(CorpIce, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 0));
        GameState initial = BuildState(
            behavior,
            runtime,
            NormalInstallIce,
            placements: [(CorpIce, PlayerSide.Corp, ZoneKind.CorpHand)],
            corpCredits: 3);
        GameState choice = ReachIceChoice(initial, behavior, runtime);
        ServerId remote = choice.RunState!.AttackedServer;
        CardInstanceId currentIce = choice.RunState.Encounter!.IceId;
        CardInstanceId corpIceId = FindCard(choice, CorpIce).Id;
        LegalAction install = Assert.Single(
            choice.PendingDecision!.LegalActions,
            action => action.Parameters is IceSubroutineChoiceParameters parameters
                && parameters.Choice == IceSubroutineChoiceKind.InstallCard
                && parameters.CardId == corpIceId
                && parameters.DestinationKind == InstallDestinationKind.ServerIce
                && parameters.ServerId == remote
                && !parameters.CreateNewRemote);
        Assert.Equal(1, Assert.IsType<IceSubroutineChoiceParameters>(install.Parameters).CreditCost);

        ApplyResult opened = Apply(choice, install, behavior, runtime);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        Assert.Equal(DecisionKind.InstallChoice, opened.State.PendingDecision!.Kind);
        LegalAction selectEncountered = Assert.Single(
            opened.State.PendingDecision.LegalActions,
            action => action.Parameters is InstallCardParameters parameters
                && !parameters.FinalizeSelection
                && parameters.SelectedReplacementCardIds.SequenceEqual([currentIce]));
        ApplyResult selected = Apply(opened.State, selectEncountered, behavior, runtime);
        Assert.True(selected.Accepted, selected.Rejection?.Message);
        LegalAction finalize = Assert.Single(
            selected.State.PendingDecision!.LegalActions,
            action => action.Parameters is InstallCardParameters parameters
                && parameters.FinalizeSelection
                && parameters.SelectedReplacementCardIds.SequenceEqual([currentIce]));
        ApplyResult committed = Apply(selected.State, finalize, behavior, runtime);

        Assert.True(committed.Accepted, committed.Rejection?.Message);
        Assert.Equal(RunTimingStep.MovementPaidWindow, committed.State.RunState!.Step);
        Assert.Null(committed.State.RunState.CurrentIceId);
        Assert.Equal(0, committed.State.RunState.CurrentIceIndex);
        Assert.Equal(3, committed.State.Players.Single(player => player.Side == PlayerSide.Corp).Credits);
        Assert.Equal(3, committed.State.Players.Single(player => player.Side == PlayerSide.Corp).Clicks);
        ZoneState iceZone = committed.State.GetZone(committed.State.GetServer(remote).IceZoneId);
        Assert.Single(iceZone.CardIds, id => id == corpIceId);
        Assert.Contains(currentIce, committed.State.GetZone(ZoneKind.CorpDiscard).CardIds);
        Assert.True(committed.State.GetCard(currentIce).FaceUp);
        Assert.Contains(committed.Events, value => value.Kind == EngineEventKinds.EncounterCompleted);
        Assert.Empty(StateInvariantValidator.Collect(committed.State, behavior, runtime));

        GameState jackOut = PassTwice(committed.State, behavior, runtime);
        ApplyResult continued = Apply(
            jackOut,
            Assert.Single(jackOut.PendingDecision!.LegalActions, action => action.Kind == ActionKind.ContinueRun),
            behavior,
            runtime);
        Assert.True(continued.Accepted, continued.Rejection?.Message);
        Assert.Equal(RunTimingStep.AfterMovePaidWindow, continued.State.RunState!.Step);
        Assert.Equal(-1, continued.State.RunState.CurrentIceIndex);
        Assert.Null(continued.State.RunState.CurrentIceId);
    }

    [Fact]
    public void InwardInstallPlacesNewIceBeforeBranAndItIsApproachedNext()
    {
        BehaviorCatalog behavior = Catalog(
            InwardInstallIce,
            new InstallCardSubroutineSpec(
                IceInstallDestinationRule.DirectlyInward,
                IceInstallCostRule.IgnoreAllCosts,
                iceOnly: true));
        RuntimeCardCatalog runtime = RuntimeCatalog(
            new CardRuntimeSpec(InwardInstallIce, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 0),
            new CardRuntimeSpec(CorpIce, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 0));
        GameState initial = BuildState(
            behavior,
            runtime,
            InwardInstallIce,
            iceCount: 2,
            placements: [(CorpIce, PlayerSide.Corp, ZoneKind.CorpHand)],
            corpCredits: 0);
        GameState choice = ReachIceChoice(initial, behavior, runtime);
        CardInstanceId corpIceId = FindCard(choice, CorpIce).Id;
        LegalAction install = Assert.Single(
            choice.PendingDecision!.LegalActions,
            action => action.Parameters is IceSubroutineChoiceParameters parameters
                && parameters.Choice == IceSubroutineChoiceKind.InstallCard
                && parameters.CardId == corpIceId);
        IceSubroutineChoiceParameters installParameters = Assert.IsType<IceSubroutineChoiceParameters>(install.Parameters);
        Assert.Equal(InstallDestinationKind.ServerIce, installParameters.DestinationKind);
        Assert.Equal(0, installParameters.CreditCost);
        Assert.False(installParameters.CreateNewRemote);

        ApplyResult installed = Apply(choice, install, behavior, runtime);
        Assert.True(installed.Accepted, installed.Rejection?.Message);
        RunState run = installed.State.RunState!;
        ZoneState iceZone = installed.State.GetZone(installed.State.GetServer(run.AttackedServer).IceZoneId);
        Assert.Equal([iceZone.CardIds[0], corpIceId, run.Encounter!.IceId], iceZone.CardIds);
        Assert.Equal(run.Encounter.IceId, run.CurrentIceId);
        Assert.Equal(2, run.CurrentIceIndex);
        Assert.Equal(0, installed.State.Players.Single(player => player.Side == PlayerSide.Corp).Credits);
        Assert.Equal(3, installed.State.Players.Single(player => player.Side == PlayerSide.Corp).Clicks);

        GameState afterMovePaid = PassTwice(installed.State, behavior, runtime);
        ApplyResult continued = Apply(
            afterMovePaid,
            Assert.Single(afterMovePaid.PendingDecision!.LegalActions, action => action.Kind == ActionKind.ContinueRun),
            behavior,
            runtime);
        Assert.True(continued.Accepted, continued.Rejection?.Message);
        Assert.Equal(RunTimingStep.AfterMovePaidWindow, continued.State.RunState!.Step);
        Assert.Equal(corpIceId, continued.State.RunState.CurrentIceId);
        Assert.Equal(1, continued.State.RunState.CurrentIceIndex);
    }

    [Fact]
    public void InsufficientFunhouseResourcesOnlyOffersTheTypedFallback()
    {
        BehaviorCatalog behavior = Catalog(PayIce, new PayCreditsOrTagSubroutineSpec(4, 1));
        RuntimeCardCatalog runtime = RuntimeCatalog(
            new CardRuntimeSpec(PayIce, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 0));
        GameState choice = ReachIceChoice(BuildState(behavior, runtime, PayIce, runnerCredits: 0, includeRulesState: true), behavior, runtime);
        IceSubroutineChoiceParameters[] parameters = choice.PendingDecision!.LegalActions
            .Select(action => Assert.IsType<IceSubroutineChoiceParameters>(action.Parameters))
            .ToArray();
        Assert.Single(parameters);
        Assert.Equal(IceSubroutineChoiceKind.TakeTag, parameters[0].Choice);
    }

    [Fact]
    public void RunRestrictionRequiresItsExactResolvedSubroutineLedgerEntry()
    {
        BehaviorCatalog behavior = Catalog(
            RestrictionIce,
            new RunRestrictionSubroutineSpec(RunRestrictionKind.CannotStealOrTrashCorpCards));
        RuntimeCardCatalog runtime = RuntimeCatalog(
            new CardRuntimeSpec(RestrictionIce, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 0));
        GameState initial = BuildState(behavior, runtime, RestrictionIce);
        GameState state = GameEngine.BeginRun(
            initial,
            RunTimingTests.RemoteId(initial),
            behavior,
            runtime);
        while (state.RunState?.RunRestrictions is not { Count: > 0 })
        {
            PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction pass = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.PassPriority);
            ApplyResult result = Apply(state, pass, behavior, runtime);
            Assert.True(result.Accepted, result.Rejection?.Message);
            state = result.State;
        }

        RunRestriction restriction = Assert.Single(state.RunState!.RunRestrictions!);
        RunResolvedSubroutineLedgerEntry ledger = Assert.Single(state.RunState.ResolvedSubroutineLedger!);
        Assert.Equal(state.RunState.Id, ledger.RunId);
        Assert.Equal(restriction.SourceIceId, ledger.IceId);
        Assert.Equal(restriction.SourceIceDefinitionId, ledger.IceDefinitionId);
        Assert.Equal(restriction.SourceSubroutineId, ledger.SubroutineId);
        Assert.Equal(restriction.SourceOrdinal, ledger.Ordinal);
        Assert.Empty(StateInvariantValidator.Collect(state, behavior, runtime));

        GameState forged = WithRunState(state, state.RunState.WithResolvedSubroutineLedger(null));
        Assert.Contains(
            StateInvariantValidator.Collect(forged, behavior, runtime),
            violation => violation.Code == "run.restriction-ledger-missing");
        PendingDecision forgedDecision = Assert.IsType<PendingDecision>(forged.PendingDecision);
        LegalAction forgedPass = Assert.Single(
            forgedDecision.LegalActions,
            action => action.Kind == ActionKind.PassPriority);
        ApplyResult rejected = Apply(forged, forgedPass, behavior, runtime);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Same(forged, rejected.State);
    }

    private static BehaviorCatalog Catalog(CardDefinitionId definitionId, IceSubroutineSpec subroutine)
    {
        return new BehaviorCatalogBuilder($"fixture:ice-choice-catalog-{definitionId.Value}")
            .AddIce(new IceBehaviorSpec(
                definitionId,
                strength: 0,
                iceType: IceType.Barrier,
                rezCost: 0,
                subroutines: [subroutine],
                strengthModifiers: []))
            .Build();
    }

    private static RuntimeCardCatalog RuntimeCatalog(params CardRuntimeSpec[] specs)
    {
        RuntimeCardCatalogBuilder builder = new("fixture:ice-choice-runtime");
        foreach (CardRuntimeSpec spec in specs)
        {
            builder.Add(spec);
        }

        return builder.Build();
    }

    private static GameState BuildState(
        BehaviorCatalog behavior,
        RuntimeCardCatalog runtime,
        CardDefinitionId iceDefinition,
        int iceCount = 1,
        int runnerCredits = 8,
        int corpCredits = 3,
        bool includeRulesState = false,
        params (CardDefinitionId DefinitionId, PlayerSide Owner, ZoneKind Zone)[] placements)
    {
        GameState baseline = RunTimingTests.CreateRunState(
            iceCount,
            rezzed: true,
            iceDefinition,
            corpCredits);
        StableIdAllocator allocator = new(baseline.Allocator);
        ZoneState runnerRig = baseline.Zones.Single(zone => zone.Kind == ZoneKind.RunnerRig && zone.ServerId is null);
        ZoneState corpHand = baseline.Zones.Single(zone => zone.Kind == ZoneKind.CorpHand && zone.ServerId is null);
        ZoneState corpDiscard = baseline.Zones.Single(zone => zone.Kind == ZoneKind.CorpDiscard && zone.ServerId is null);
        var cards = baseline.Cards.ToList();
        var addedByZone = new Dictionary<ZoneId, List<CardInstanceId>>
        {
            [runnerRig.Id] = [],
            [corpHand.Id] = [],
            [corpDiscard.Id] = [],
        };
        foreach ((CardDefinitionId definitionId, PlayerSide owner, ZoneKind zoneKind) in placements)
        {
            ZoneState destination = zoneKind switch
            {
                ZoneKind.RunnerRig => runnerRig,
                ZoneKind.CorpHand => corpHand,
                ZoneKind.CorpDiscard => corpDiscard,
                _ => throw new ArgumentOutOfRangeException(nameof(placements), zoneKind, "Unsupported ICE-choice fixture zone."),
            };
            CardInstanceId cardId = allocator.NextCardInstanceId();
            cards.Add(new CardInstanceState(
                cardId,
                definitionId,
                destination.Id,
                owner,
                faceUp: zoneKind == ZoneKind.RunnerRig));
            addedByZone[destination.Id].Add(cardId);
        }

        ZoneState[] zones = baseline.Zones
            .Select(zone => addedByZone.TryGetValue(zone.Id, out List<CardInstanceId>? additions) && additions.Count > 0
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Concat(additions).ToArray())
                : zone)
            .ToArray();
        PlayerState[] players = baseline.Players
            .Select(player => player.Side == PlayerSide.Runner
                ? new PlayerState(player.Id, player.Side, runnerCredits, player.Clicks, player.AgendaPoints)
                : player)
            .ToArray();
        return new GameState(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            players,
            cards,
            zones,
            baseline.Servers,
            baseline.Turn,
            baseline.Outcome,
            baseline.RandomState,
            allocator.State,
            baseline.DecisionRevision,
            baseline.EventSequence,
            baseline.ResolutionStack,
            baseline.PendingDecision,
            baseline.ActionSequence,
            baseline.InitialSeed,
            baseline.RunState,
            includeRulesState ? RulesState.CreateDefault(players) : baseline.RulesState,
            behavior.Version,
            behavior.Fingerprint,
            runtime.Version,
            runtime.Fingerprint);
    }

    private static GameState ReachIceChoice(GameState initial, BehaviorCatalog behavior, RuntimeCardCatalog runtime)
    {
        GameState state = GameEngine.BeginRun(
            initial,
            RunTimingTests.RemoteId(initial),
            behavior,
            runtime);
        while (state.PendingDecision?.Kind != DecisionKind.IceSubroutineChoice)
        {
            PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction pass = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.PassPriority);
            ApplyResult result = Apply(state, pass, behavior, runtime);
            Assert.True(result.Accepted, result.Rejection?.Message);
            state = result.State;
        }

        return state;
    }

    private static GameState ReachEncounterPaid(GameState initial, BehaviorCatalog behavior, RuntimeCardCatalog runtime)
    {
        GameState state = GameEngine.BeginRun(
            initial,
            RunTimingTests.RemoteId(initial),
            behavior,
            runtime);
        while (state.RunState?.Step != RunTimingStep.EncounterPaidWindow)
        {
            PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction pass = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.PassPriority);
            ApplyResult result = Apply(state, pass, behavior, runtime);
            Assert.True(result.Accepted, result.Rejection?.Message);
            state = result.State;
        }

        return state;
    }

    private static GameState PassTwice(GameState state, BehaviorCatalog behavior, RuntimeCardCatalog runtime)
    {
        PendingDecision firstDecision = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction firstPass = Assert.Single(firstDecision.LegalActions, action => action.Kind == ActionKind.PassPriority);
        ApplyResult first = Apply(state, firstPass, behavior, runtime);
        Assert.True(first.Accepted, first.Rejection?.Message);
        PendingDecision secondDecision = Assert.IsType<PendingDecision>(first.State.PendingDecision);
        LegalAction secondPass = Assert.Single(secondDecision.LegalActions, action => action.Kind == ActionKind.PassPriority);
        ApplyResult second = Apply(first.State, secondPass, behavior, runtime);
        Assert.True(second.Accepted, second.Rejection?.Message);
        return second.State;
    }

    private static ApplyResult Apply(
        GameState state,
        LegalAction action,
        BehaviorCatalog behavior,
        RuntimeCardCatalog runtime)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        return GameEngine.Apply(
            state,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id),
            behavior,
            runtime);
    }

    private static CardInstanceState FindCard(GameState state, CardDefinitionId definitionId) =>
        Assert.Single(state.Cards, card => card.DefinitionId == definitionId);

    private static GameState WithRunState(GameState state, RunState run)
    {
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
            state.ResolutionStack,
            state.PendingDecision,
            state.ActionSequence,
            state.InitialSeed,
            run,
            state.RulesState,
            state.BehaviorRegistryVersion,
            state.BehaviorCatalogFingerprint,
            state.RuntimeCardCatalogVersion,
            state.RuntimeCardCatalogFingerprint);
    }

    private static GameState ReplaceTopFrame(GameState state, ResolutionFrame frame)
    {
        ResolutionFrame[] stack = state.ResolutionStack.ToArray();
        stack[^1] = frame;
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
            stack,
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
}

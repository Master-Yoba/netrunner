using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;

using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// SIM-401 headless exit evidence for the Slice 4.5 run interaction path.
/// The fixture is deliberately compact, but the run itself uses the public
/// reducer and observation boundary: no action payload is synthesized and no
/// card title, code, or description is inspected by either selector.
/// </summary>
public sealed class Slice45RunProofTests
{
    private const string SimulationBehaviorRegistryVersion = "gateway-m4-4.5-sim-proof-c1";
    private const string BlankIceDefinition = "fixture:slice45-blank-ice";

    [Fact]
    [Trait("Slice", "4.5")]
    [Trait("SIM", "401")]
    public async Task FullRunReachesAllSlice45BoundariesWithScriptedAndContractPolicies()
    {
        BehaviorCatalog gateway = GatewayBehaviorCatalog.Create();
        BehaviorCatalog behaviorCatalog = CreateSimulationCatalog(gateway);
        RuntimeCardCatalog runtimeCatalog = CreateRuntimeCatalog();
        GameState initial = BuildProofState(behaviorCatalog, runtimeCatalog);

        RunProofResult scripted = await DriveRunAsync(
            initial,
            behaviorCatalog,
            runtimeCatalog,
            SelectScriptedHumanActionAsync);
        RunProofResult agent = await DriveRunAsync(
            initial,
            behaviorCatalog,
            runtimeCatalog,
            SelectContractActionAsync);

        Assert.True(
            string.Equals(
                DeterministicHash.ForState(scripted.State),
                DeterministicHash.ForState(agent.State),
                StringComparison.Ordinal),
            $"scripted: {string.Join(" | ", scripted.SelectedActions.Select(ActionSignature))}\n"
                + $"agent: {string.Join(" | ", agent.SelectedActions.Select(ActionSignature))}");
        Assert.Equal(
            scripted.Events.Select(static value => CanonicalJson.Serialize(value)),
            agent.Events.Select(static value => CanonicalJson.Serialize(value)));
        Assert.Equal(
            scripted.SelectedActions.Select(static value => CanonicalJson.Serialize(value)),
            agent.SelectedActions.Select(static value => CanonicalJson.Serialize(value)));
        Assert.Equal(GameOutcomeKind.Ongoing, scripted.State.Outcome.Kind);
        Assert.Null(scripted.State.RunState);
        Assert.Empty(scripted.State.ResolutionStack);
        Assert.True(
            scripted.RunRestrictionObserved,
            string.Join(", ", scripted.SelectedActions.Select(static action => action.Kind.ToString())));

        Assert.Contains(scripted.Events, value => value.Kind == EngineEventKinds.ServerApproached);
        Assert.Contains(scripted.Events, value => value.Kind == EngineEventKinds.CardInstalled
            && Assert.IsType<CardInstalledEventPayload>(value.Payload).CardId == new CardInstanceId("card:z-ansel-normal-install"));
        Assert.Contains(scripted.Events, value => value.Kind == EngineEventKinds.CardInstalled
            && Assert.IsType<CardInstalledEventPayload>(value.Payload).CardId == new CardInstanceId("card:a-bran-inward-install"));
        Assert.Contains(scripted.Events, value => value.Kind == EngineEventKinds.CreditsAdjusted
            && Assert.IsType<CreditsAdjustedEventPayload>(value.Payload).Delta == -1);
        Assert.Contains(scripted.Events, value => value.Kind == EngineEventKinds.CreditsAdjusted
            && Assert.IsType<CreditsAdjustedEventPayload>(value.Payload).Delta == -4);
        Assert.Contains(scripted.Events, value => value.Kind == EngineEventKinds.CreditsAdjusted
            && Assert.IsType<CreditsAdjustedEventPayload>(value.Payload).Delta == -5);
        Assert.Contains(scripted.Events, value => value.Kind == EngineEventKinds.RunSuccessful);

        LegalAction branInstall = Assert.Single(
            scripted.SelectedActions,
            action => action.Kind == ActionKind.ResolveIceSubroutine
                && action.Parameters is IceSubroutineChoiceParameters choice
                && choice.Choice == IceSubroutineChoiceKind.InstallCard
                && choice.CardId == new CardInstanceId("card:a-bran-inward-install"));
        IceSubroutineChoiceParameters branInstallParameters = Assert.IsType<IceSubroutineChoiceParameters>(branInstall.Parameters);
        Assert.Equal(InstallDestinationKind.ServerIce, branInstallParameters.DestinationKind);
        Assert.Equal(0, branInstallParameters.CreditCost);
        Assert.False(branInstallParameters.CreateNewRemote);

        LegalAction anselInstall = Assert.Single(
            scripted.SelectedActions,
            action => action.Kind == ActionKind.ResolveIceSubroutine
                && action.Parameters is IceSubroutineChoiceParameters choice
                && choice.Choice == IceSubroutineChoiceKind.InstallCard
                && choice.CardId == new CardInstanceId("card:z-ansel-normal-install"));
        IceSubroutineChoiceParameters anselInstallParameters = Assert.IsType<IceSubroutineChoiceParameters>(anselInstall.Parameters);
        Assert.Equal(InstallDestinationKind.ServerIce, anselInstallParameters.DestinationKind);
        Assert.True(anselInstallParameters.CreditCost > 0);
        Assert.False(anselInstallParameters.CreateNewRemote);

        LegalAction[] branBioroidActions = scripted.SelectedActions
            .Where(action => action.Kind == ActionKind.UseBioroidAbility
                && action.Parameters is RunBioroidAbilityParameters bioroid
                && bioroid.IceId == new CardInstanceId("card:bran"))
            .ToArray();
        Assert.Equal(2, branBioroidActions.Length);
        Assert.Equal(
            [2, 1],
            branBioroidActions.Select(static action =>
                Assert.IsType<RunBioroidAbilityParameters>(action.Parameters).SubroutineOrdinal));

        LegalAction funhouseEncounter = Assert.Single(
            scripted.SelectedActions,
            action => action.Kind == ActionKind.ChooseBehavior
                && action.Parameters is BehaviorChoiceParameters choice
                && choice.ChoiceKey == "funhouse-encounter-choice");
        Assert.Equal(
            ["take-tag"],
            Assert.IsType<BehaviorChoiceParameters>(funhouseEncounter.Parameters).OptionKeys);

        LegalAction manegarmChoice = Assert.Single(
            scripted.SelectedActions,
            action => action.Kind == ActionKind.ChooseBehavior
                && action.Parameters is BehaviorChoiceParameters choice
                && choice.ChoiceKey == "manegarm-approach-choice");
        Assert.Equal(
            ["pay-credits"],
            Assert.IsType<BehaviorChoiceParameters>(manegarmChoice.Parameters).OptionKeys);

        CardInstanceState normalInstalled = scripted.State.GetCard(new CardInstanceId("card:z-ansel-normal-install"));
        Assert.Equal(PlayerSide.Corp, normalInstalled.OwnerSide);
        Assert.False(normalInstalled.FaceUp);
        CardInstanceState inwardInstalled = scripted.State.GetCard(new CardInstanceId("card:a-bran-inward-install"));
        Assert.Equal(PlayerSide.Corp, inwardInstalled.OwnerSide);
        Assert.False(inwardInstalled.FaceUp);

        Assert.Equal(
            1,
            scripted.State.RulesState!.Players
                .Single(player => player.PlayerId == scripted.State.Players
                    .Single(player => player.Side == PlayerSide.Runner)
                    .Id)
                .Tags);
    }

    private static async Task<RunProofResult> DriveRunAsync(
        GameState initial,
        BehaviorCatalog behaviorCatalog,
        RuntimeCardCatalog runtimeCatalog,
        Func<Observation, Task<ActionId>> selector)
    {
        GameEngine.RunStartResult started = GameEngine.BeginRunWithEvents(
            initial,
            initial.Servers.Single(server => server.Kind == ServerKind.Headquarters).Id,
            behaviorCatalog,
            runtimeCatalog,
            initial.Players.Single(player => player.Side == PlayerSide.Runner).Id,
            new CardInstanceId("card:jailbreak"));
        var events = new List<EngineEvent>(started.Events);
        var selectedActions = new List<LegalAction>();
        bool runRestrictionObserved = false;
        GameState state = started.State;
        GameState replayState = started.State;

        for (int step = 0; step < 256; step++)
        {
            if (state.PendingDecision is null)
            {
                if (state.RunState is null && state.ResolutionStack.Count == 0)
                {
                    StateInvariantValidator.Validate(state, behaviorCatalog, runtimeCatalog);
                    StateInvariantValidator.Validate(replayState, behaviorCatalog, runtimeCatalog);
                    Assert.Equal(DeterministicHash.ForState(state), DeterministicHash.ForState(replayState));
                    return new RunProofResult(state, events, selectedActions, runRestrictionObserved);
                }

                throw new Xunit.Sdk.XunitException(
                    $"The run yielded no decision at step {step} while resolution remained active.");
            }

            StateInvariantValidator.Validate(state, behaviorCatalog, runtimeCatalog);
            runRestrictionObserved |= state.RunState?.RunRestrictions?.Any(restriction =>
                restriction.Kind == RunRestrictionKind.CannotStealOrTrashCorpCards) == true;
            string encoded = EngineJson.Serialize(state);
            GameState restored = EngineJson.DeserializeState(encoded, behaviorCatalog, runtimeCatalog);
            Assert.Equal(encoded, EngineJson.Serialize(restored));
            Assert.Equal(DeterministicHash.ForState(state), DeterministicHash.ForState(restored));
            state = restored;
            Assert.Equal(DeterministicHash.ForState(state), DeterministicHash.ForState(replayState));

            PendingDecision pending = state.PendingDecision
                ?? throw new Xunit.Sdk.XunitException("The pending decision disappeared after the round-trip boundary.");
            Observation observation = ObservationBuilder.Build(state, pending.Owner == state.Players.Single(player => player.Side == PlayerSide.Runner).Id
                ? PlayerSide.Runner
                : PlayerSide.Corp, behaviorCatalog);
            Assert.NotNull(observation.Decision);
            Assert.Equal(pending.Id, observation.Decision!.Id);
            Assert.Equal(pending.Revision, observation.Decision.Revision);

            ActionId selected = await selector(observation);
            LegalAction issued = Assert.Single(pending.LegalActions, action => action.Id == selected);
            selectedActions.Add(issued);
            ApplyResult result = GameEngine.Apply(
                state,
                new ActionSubmission(pending.Owner, pending.Id, pending.Revision, selected),
                behaviorCatalog,
                runtimeCatalog);
            Assert.True(result.Accepted, result.Rejection?.Message);
            Assert.NotNull(result.AcceptedAction);
            Assert.Equal(DeterministicHash.ForState(result.State), result.AcceptedAction!.ResultingStateHash);
            Assert.NotNull(issued.Parameters);

            // Replay the exact engine-issued submission in lockstep. This
            // checks every yield boundary rather than only the final state:
            // action sequence, resulting state, event-batch hash, and event
            // payloads must all be reproduced by the same public reducer.
            ReplayAction replayAction = ReplayAction.From(result.AcceptedAction);
            ApplyResult replayResult = GameEngine.Apply(
                replayState,
                replayAction.Submission,
                behaviorCatalog,
                runtimeCatalog);
            Assert.True(replayResult.Accepted, replayResult.Rejection?.Message);
            Assert.NotNull(replayResult.AcceptedAction);
            Assert.Equal(replayAction.Sequence, replayResult.AcceptedAction!.Sequence);
            Assert.Equal(replayAction.ExpectedStateHash, replayResult.AcceptedAction.ResultingStateHash);
            Assert.Equal(replayAction.ExpectedEventBatchHash, replayResult.AcceptedAction.ResultingEventBatchHash);
            Assert.Equal(
                result.Events.Select(static value => CanonicalJson.Serialize(value)),
                replayResult.Events.Select(static value => CanonicalJson.Serialize(value)));
            StateInvariantValidator.Validate(replayResult.State, behaviorCatalog, runtimeCatalog);
            replayState = replayResult.State;

            events.AddRange(result.Events);
            state = result.State;
        }

        throw new Xunit.Sdk.XunitException("The Slice 4.5 run exceeded the deterministic proof step cap.");
    }

    private static Task<ActionId> SelectContractActionAsync(Observation observation)
    {
        var agent = new FullGameContractAgent(new FullGameContractPolicy(
            observation.Viewer,
            runActionOrder:
            [
                ActionKind.UseBioroidAbility,
                ActionKind.RezIce,
                ActionKind.BreakSubroutines,
                ActionKind.PumpBreaker,
                ActionKind.ContinueRun,
                ActionKind.ChooseBreachCandidate,
                ActionKind.NoAccessAction,
                ActionKind.PassPriority,
            ],
            bioroidSubroutinePreference: BioroidSubroutinePreference.HighestOrdinalFirst));
        return agent.ChooseActionAsync(observation).AsTask();
    }

    private static Task<ActionId> SelectScriptedHumanActionAsync(Observation observation)
    {
        Assert.NotNull(observation.Decision);
        LegalAction selected = observation.Decision!.Kind switch
        {
            DecisionKind.RunTiming => SelectRunTimingAction(observation.LegalActions),
            DecisionKind.BehaviorChoice => SelectBehaviorAction(observation.LegalActions),
            DecisionKind.InstallChoice => SelectInstallAction(observation.LegalActions),
            DecisionKind.IceSubroutineChoice => SelectIceAction(observation.LegalActions),
            _ => throw new Xunit.Sdk.XunitException(
                $"The scripted human selector received unsupported decision '{observation.Decision.Kind}'."),
        };
        return Task.FromResult(selected.Id);
    }

    private static LegalAction SelectRunTimingAction(IReadOnlyList<LegalAction> actions)
    {
        LegalAction? rez = actions.FirstOrDefault(action => action.Kind == ActionKind.RezIce);
        if (rez is not null)
        {
            return rez;
        }

        LegalAction[] bioroid = actions
            .Where(action => action.Kind == ActionKind.UseBioroidAbility
                && action.Parameters is RunBioroidAbilityParameters)
            .OrderByDescending(action => Assert.IsType<RunBioroidAbilityParameters>(action.Parameters).SubroutineOrdinal)
            .ThenBy(action => action.Id.Value, StringComparer.Ordinal)
            .ToArray();
        if (bioroid.Length > 0)
        {
            return bioroid[0];
        }

        LegalAction? pass = actions.FirstOrDefault(action => action.Kind == ActionKind.PassPriority);
        if (pass is not null)
        {
            return pass;
        }

        LegalAction? continueRun = actions.FirstOrDefault(action => action.Kind == ActionKind.ContinueRun);
        if (continueRun is not null)
        {
            return continueRun;
        }

        LegalAction? candidate = actions.FirstOrDefault(action => action.Kind == ActionKind.ChooseBreachCandidate);
        if (candidate is not null)
        {
            return candidate;
        }

        LegalAction? noAccess = actions.FirstOrDefault(action => action.Kind == ActionKind.NoAccessAction);
        return noAccess ?? throw new Xunit.Sdk.XunitException("The scripted selector found no typed run action.");
    }

    private static LegalAction SelectBehaviorAction(IReadOnlyList<LegalAction> actions)
    {
        LegalAction? spendClicks = actions.FirstOrDefault(action =>
            action.Parameters is BehaviorChoiceParameters choice
            && choice.OptionKeys.Contains("spend-clicks", StringComparer.Ordinal));
        if (spendClicks is not null)
        {
            return spendClicks;
        }

        LegalAction? takeTag = actions.FirstOrDefault(action =>
            action.Parameters is BehaviorChoiceParameters choice
            && choice.OptionKeys.Contains("take-tag", StringComparer.Ordinal));
        if (takeTag is not null)
        {
            return takeTag;
        }

        LegalAction? pay = actions.FirstOrDefault(action =>
            action.Parameters is BehaviorChoiceParameters choice
            && choice.OptionKeys.Contains("pay-credits", StringComparer.Ordinal));
        return pay ?? actions.FirstOrDefault(action =>
            action.Parameters is BehaviorChoiceParameters choice
            && choice.OptionKeys.Contains("end-run", StringComparer.Ordinal))
            ?? throw new Xunit.Sdk.XunitException("The scripted selector found no typed behavior option.");
    }

    private static LegalAction SelectInstallAction(IReadOnlyList<LegalAction> actions)
    {
        LegalAction[] final = actions
            .Where(action =>
                action.Kind == ActionKind.InstallCard
                && action.Parameters is InstallCardParameters install
                && install.FinalizeSelection)
            .OrderBy(static action =>
                Assert.IsType<InstallCardParameters>(action.Parameters).CreditCost)
            .ThenBy(static action => CanonicalJson.Serialize(action.Parameters), StringComparer.Ordinal)
            .ThenBy(static action => action.Id.Value, StringComparer.Ordinal)
            .ToArray();
        return final.Length > 0
            ? final[0]
            : throw new Xunit.Sdk.XunitException("The scripted selector found no final typed install choice.");
    }

    private static LegalAction SelectIceAction(IReadOnlyList<LegalAction> actions)
    {
        LegalAction? trash = actions.FirstOrDefault(action =>
            action.Parameters is IceSubroutineChoiceParameters choice
            && choice.Choice == IceSubroutineChoiceKind.TrashInstalledRunnerCard);
        if (trash is not null)
        {
            return trash;
        }

        LegalAction[] installs = actions
            .Where(action =>
                action.Parameters is IceSubroutineChoiceParameters choice
                && choice.Choice == IceSubroutineChoiceKind.InstallCard)
            .ToArray();
        if (installs.Length > 0)
        {
            // The normal Ansel branch must demonstrate a paid placement on
            // an existing server. New-remote is a distinct typed destination
            // and is retained only as a fail-closed fallback for fixtures
            // that offer no existing-server candidate.
            LegalAction[] existingServer = installs
                .Where(static action => !Assert.IsType<IceSubroutineChoiceParameters>(action.Parameters).CreateNewRemote)
                .ToArray();
            return (existingServer.Length > 0 ? existingServer : installs)
                .OrderBy(static action => Assert.IsType<IceSubroutineChoiceParameters>(action.Parameters).CreditCost)
                .ThenBy(static action => CanonicalJson.Serialize(action.Parameters), StringComparer.Ordinal)
                .ThenBy(static action => action.Id.Value, StringComparer.Ordinal)
                .First();
        }

        LegalAction? pay = actions.FirstOrDefault(action =>
            action.Parameters is IceSubroutineChoiceParameters choice
            && choice.Choice == IceSubroutineChoiceKind.PayCredits);
        if (pay is not null)
        {
            return pay;
        }

        return actions.FirstOrDefault(action =>
            action.Parameters is IceSubroutineChoiceParameters choice
            && choice.Choice == IceSubroutineChoiceKind.TakeTag)
            ?? actions.FirstOrDefault(action =>
                action.Parameters is IceSubroutineChoiceParameters choice
                && choice.Choice == IceSubroutineChoiceKind.Decline)
            ?? throw new Xunit.Sdk.XunitException("The scripted selector found no typed ICE option.");
    }

    private static string ActionSignature(LegalAction action) =>
        $"{action.Kind}:{CanonicalJson.Serialize(action.Parameters)}";

    private static GameState BuildProofState(
        BehaviorCatalog behaviorCatalog,
        RuntimeCardCatalog runtimeCatalog)
    {
        GameState baseline = GameState.CreateNew(
            seed: 451401,
            rulesVersion: EngineCompatibility.RulesVersion,
            cardDataVersion: EngineCompatibility.GatewayCardDataVersion,
            behaviorRegistryVersion: behaviorCatalog.Version,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);
        PlayerState runnerBase = baseline.Players.Single(player => player.Side == PlayerSide.Runner);
        PlayerState corpBase = baseline.Players.Single(player => player.Side == PlayerSide.Corp);
        PlayerState[] players =
        [
            new PlayerState(runnerBase.Id, PlayerSide.Runner, credits: 20, clicks: 2, runnerBase.AgendaPoints),
            new PlayerState(corpBase.Id, PlayerSide.Corp, credits: 40, clicks: 3, corpBase.AgendaPoints),
        ];

        ServerState headquarters = baseline.Servers.Single(server => server.Kind == ServerKind.Headquarters);
        ServerState research = baseline.Servers.Single(server => server.Kind == ServerKind.RAndD);
        ServerState archives = baseline.Servers.Single(server => server.Kind == ServerKind.Archives);
        ZoneState hand = baseline.GetZone(ZoneKind.CorpHand);
        ZoneState discard = baseline.GetZone(ZoneKind.CorpDiscard);
        ZoneState runnerGrip = baseline.GetZone(ZoneKind.RunnerGrip);
        ZoneState runnerRig = baseline.GetZone(ZoneKind.RunnerRig);
        ZoneState runnerStack = baseline.GetZone(ZoneKind.RunnerStack);
        ZoneState hqRoot = baseline.GetZone(headquarters.RootZoneId);
        ZoneState hqIce = baseline.GetZone(headquarters.IceZoneId);
        ZoneState researchIce = baseline.GetZone(research.IceZoneId);
        ZoneState archivesIce = baseline.GetZone(archives.IceZoneId);

        CardInstanceId jailbreak = new("card:jailbreak");
        CardInstanceId drawTarget = new("card:runner-draw-target");
        CardInstanceId runnerTarget = new("card:runner-installed-target");
        CardInstanceId funhouse = new("card:funhouse");
        CardInstanceId bran = new("card:bran");
        CardInstanceId ansel = new("card:ansel");
        CardInstanceId manegarm = new("card:manegarm");
        // The proof selectors use different ordering rules: the scripted
        // selector follows source-zone order, while the contract agent uses a
        // typed semantic key. Keep the inward blank lexically first so both
        // deterministic policies select the same candidate.
        CardInstanceId normalInstall = new("card:z-ansel-normal-install");
        CardInstanceId inwardInstall = new("card:a-bran-inward-install");
        CardInstanceId researchProtector = new("card:research-protector");
        CardInstanceId archivesProtector = new("card:archives-protector");

        CardInstanceState[] cards =
        [
            new(jailbreak, GatewayBatch4CardCodes.Jailbreak, runnerGrip.Id, PlayerSide.Runner),
            new(drawTarget, GatewayBatch1CardCodes.T400MemoryDiamond, runnerStack.Id, PlayerSide.Runner),
            new(runnerTarget, GatewayBatch1CardCodes.T400MemoryDiamond, runnerRig.Id, PlayerSide.Runner, faceUp: true),
            // ICE order is inner-to-outer. The run first encounters Brân,
            // then Ansel, then Funhouse. Brân inserts a blank ICE directly
            // inward from itself, so the inserted card is also traversed.
            new(funhouse, GatewayBatch4CardCodes.Funhouse, hqIce.Id, PlayerSide.Corp, faceUp: true, rezzed: true),
            new(bran, GatewayBatch4CardCodes.Bran, hqIce.Id, PlayerSide.Corp, faceUp: true, rezzed: true),
            new(ansel, GatewayBatch4CardCodes.Ansel, hqIce.Id, PlayerSide.Corp, faceUp: true, rezzed: true),
            new(manegarm, GatewayBatch4CardCodes.Manegarm, hqRoot.Id, PlayerSide.Corp, faceUp: true, rezzed: true),
            new(normalInstall, GatewayBatch4CardCodes.Ansel, discard.Id, PlayerSide.Corp, faceUp: true),
            new(inwardInstall, new CardDefinitionId(BlankIceDefinition), hand.Id, PlayerSide.Corp),
            new(researchProtector, new CardDefinitionId(BlankIceDefinition), researchIce.Id, PlayerSide.Corp, faceUp: true, rezzed: true),
            new(archivesProtector, new CardDefinitionId(BlankIceDefinition), archivesIce.Id, PlayerSide.Corp, faceUp: true, rezzed: true),
        ];

        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == runnerGrip.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [jailbreak])
                : zone.Id == runnerStack.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [drawTarget])
                    : zone.Id == runnerRig.Id
                        ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [runnerTarget])
                        : zone.Id == hand.Id
                            ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [inwardInstall])
                            : zone.Id == discard.Id
                                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [normalInstall])
                            : zone.Id == hqIce.Id
                                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [funhouse, ansel, bran])
                                : zone.Id == researchIce.Id
                                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [researchProtector])
                                    : zone.Id == archivesIce.Id
                                        ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [archivesProtector])
                                : zone.Id == hqRoot.Id
                                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [manegarm])
                                    : zone)
            .ToArray();

        TurnFactsState facts = new(
            version: 1,
            side: PlayerSide.Runner,
            turnNumber: baseline.Turn.TurnNumber,
            clicksAllotted: 4,
            clicksLost: 0,
            turnBegan: true,
            mandatoryDrawPerformed: false,
            actionPhaseEnded: false,
            discardPhaseEnded: false);
        TurnHistoryState history = new(1, null, null, facts, null);
        TurnState turn = new(
            PlayerSide.Runner,
            TurnPhase.Runner,
            baseline.Turn.TurnNumber,
            TurnStep.RunnerAction,
            history);
        RulesState defaults = RulesState.CreateDefault(players);
        GameState state = new(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            players,
            cards,
            zones,
            baseline.Servers,
            turn,
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            resolutionStack: [],
            pendingDecision: null,
            actionSequence: baseline.ActionSequence,
            eventSequence: baseline.EventSequence,
            decisionRevision: baseline.DecisionRevision,
            initialSeed: baseline.InitialSeed,
            runState: null,
            rulesState: defaults,
            behaviorRegistryVersion: behaviorCatalog.Version,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);
        StateInvariantValidator.Validate(state, behaviorCatalog, runtimeCatalog);
        return state;
    }

    private static BehaviorCatalog CreateSimulationCatalog(BehaviorCatalog gateway)
    {
        // Brân's inward install is demonstrated with a test-only blank ICE.
        // Its empty subroutine list lets the run continue through the newly
        // inserted card without adding another production-card interaction
        // to this focused proof.
        return new BehaviorCatalogBuilder(SimulationBehaviorRegistryVersion, gateway)
            .AddIce(new IceBehaviorSpec(
                new CardDefinitionId(BlankIceDefinition),
                strength: 0,
                iceType: IceType.Barrier,
                rezCost: 0,
                subroutines: [],
                strengthModifiers: []))
            .Build();
    }

    private static RuntimeCardCatalog CreateRuntimeCatalog()
    {
        CardRuntimeSpec[] cards =
        [
            Runtime("30028", PlayerSide.Runner, RuntimeCardClass.Event, playCost: 0),
            Runtime("30038", PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 6),
            Runtime("30039", PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 6),
            Runtime("30042", PlayerSide.Corp, RuntimeCardClass.Upgrade, rezCost: 0, trashCost: 0),
            Runtime("30050", PlayerSide.Corp, RuntimeCardClass.Asset, rezCost: 0, trashCost: 0),
            Runtime("30054", PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 5),
            Runtime(BlankIceDefinition, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 0),
            Runtime("30031", PlayerSide.Runner, RuntimeCardClass.Hardware, installCost: 2),
        ];
        var builder = new RuntimeCardCatalogBuilder("gateway-sim-slice45-runtime");
        foreach (CardRuntimeSpec card in cards)
        {
            builder.Add(card);
        }

        return builder.Build();
    }

    private static CardRuntimeSpec Runtime(
        string code,
        PlayerSide side,
        RuntimeCardClass cardClass,
        int? playCost = null,
        int? installCost = null,
        int? rezCost = null,
        int? trashCost = null) =>
        new(
            new CardDefinitionId(code),
            side,
            cardClass,
            playCost,
            installCost,
            rezCost,
            trashCost: trashCost);

    private sealed record RunProofResult(
        GameState State,
        IReadOnlyList<EngineEvent> Events,
        IReadOnlyList<LegalAction> SelectedActions,
        bool RunRestrictionObserved);
}

using System.Security.Cryptography;
using System.Text;

using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

/// <summary>
/// Real-turn lifecycle evidence for Fermenter (30007).  The fixture supplies
/// only typed runtime metadata; the executable behavior is the production
/// Gateway catalog declaration.
/// </summary>
public sealed class GatewayBatch2FermenterBehaviorTests
{
    [Fact]
    [Trait("GatewayCard", "30007")]
    public void FermenterHasOneMemoryAndNoStaticMemoryModifier()
    {
        Fixture fixture = CreateFixture();
        CardBehaviorDefinition definition = Definition(fixture);
        CardRuntimeSpec runtime = fixture.RuntimeCatalog.GetSpec(fixture.DefinitionId);

        Assert.Equal(PlayerSide.Runner, definition.Controller);
        Assert.Empty(definition.StaticModifiers);
        Assert.Equal(RuntimeCardClass.Program, runtime.CardClass);
        Assert.Equal(PlayerSide.Runner, runtime.Side);
        Assert.Equal(1, runtime.InstallCost);
        Assert.Equal(1, runtime.MemoryUnits);
        Assert.Contains("virus", runtime.Subtypes);

        BehaviorPaidAbility ability = Assert.Single(definition.PaidAbilities);
        Assert.Equal(
            [BehaviorCostKind.Clicks, BehaviorCostKind.TrashSelf],
            ability.Costs.Select(static cost => cost.Kind));
        BehaviorInstruction gain = Assert.Single(ability.Instructions);
        Assert.Equal(BehaviorInstructionKind.GainCredits, gain.Kind);
        Assert.Equal(new BehaviorAmountOperand("virus", 2), gain.AmountOperand);
    }

    [Fact]
    [Trait("GatewayCard", "30007")]
    public void RealInstallAddsOneVirusAndRunnerTurnBeginAddsAnother()
    {
        Fixture fixture = CreateFixture();
        GameState turnAction = OpenRunnerAction(fixture.State, fixture);

        LegalAction install = Assert.Single(
            turnAction.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.InstallCard
                && Assert.IsType<InstallCardParameters>(action.Parameters).CardId == fixture.CardId);
        ApplyResult installed = Apply(fixture, turnAction, install);

        Assert.True(installed.Accepted, installed.Rejection?.Message);
        Assert.Equal(DecisionKind.TurnTiming, installed.State.PendingDecision?.Kind);
        Assert.Equal(
            ZoneKind.RunnerRig,
            installed.State.GetZone(installed.State.GetCard(fixture.CardId).ZoneId).Kind);
        Assert.Equal(1, VirusCount(installed.State, fixture.CardId));
        Assert.Contains(
            installed.State.RulesState!.Programs,
            program => program.CardId == fixture.CardId && program.MemoryCost == 1);
        Assert.Equal(
            1,
            installed.Events.Count(value => value.Kind == EngineEventKinds.CardInstalled));
        Assert.Single(
            installed.Events,
            value => value.Kind == EngineEventKinds.CounterAdjusted
                && Assert.IsType<CounterAdjustedEventPayload>(value.Payload).CardId == fixture.CardId
                && Assert.IsType<CounterAdjustedEventPayload>(value.Payload).CounterName == "virus"
                && Assert.IsType<CounterAdjustedEventPayload>(value.Payload).Delta == 1
                && Assert.IsType<CounterAdjustedEventPayload>(value.Payload).CountAfter == 1);
        AssertEventAfter(
            installed.Events,
            EngineEventKinds.CardInstalled,
            EngineEventKinds.CounterAdjusted);
        Assert.Empty(StateInvariantValidator.Collect(
            installed.State,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog));

        // The install has yielded a real timing decision. Persist and resume
        // that boundary before opening an independent real Runner turn with
        // the installed card and its earned counter.
        GameState persistedInstall = RoundTrip(installed.State, fixture);
        Assert.Equal(1, VirusCount(persistedInstall, fixture.CardId));

        GameState installedTurnState = CreateInstalledState(fixture, virusCount: 1);
        GameEngine.TurnStartResult started = GameEngine.BeginTurnWithEvents(
            installedTurnState,
            PlayerSide.Runner,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.Equal(DecisionKind.TurnTiming, started.State.PendingDecision?.Kind);
        GameState restoredStart = RoundTrip(started.State, fixture);

        ApplyResult firstPriority = Pass(fixture, restoredStart);
        Assert.True(firstPriority.Accepted, firstPriority.Rejection?.Message);
        GameState persistedPriority = RoundTrip(firstPriority.State, fixture);
        ApplyResult formalBegin = Pass(fixture, persistedPriority);

        Assert.True(formalBegin.Accepted, formalBegin.Rejection?.Message);
        Assert.Equal(2, VirusCount(formalBegin.State, fixture.CardId));
        Assert.Single(
            formalBegin.Events,
            value => value.Kind == EngineEventKinds.CounterAdjusted
                && Assert.IsType<CounterAdjustedEventPayload>(value.Payload).CardId == fixture.CardId
                && Assert.IsType<CounterAdjustedEventPayload>(value.Payload).CounterName == "virus"
                && Assert.IsType<CounterAdjustedEventPayload>(value.Payload).Delta == 1
                && Assert.IsType<CounterAdjustedEventPayload>(value.Payload).CountAfter == 2);

        GameState runnerAction = ResumeRunnerAction(fixture, formalBegin.State);
        Assert.Equal(DecisionKind.TurnAction, runnerAction.PendingDecision?.Kind);
        Assert.Equal(2, VirusCount(runnerAction, fixture.CardId));
        Assert.Empty(StateInvariantValidator.Collect(
            runnerAction,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog));
    }

    [Fact]
    [Trait("GatewayCard", "30007")]
    public void RealClickTrashSelfCashoutGainsTwoPerVirusAndClearsCounters()
    {
        Fixture fixture = CreateFixture();
        GameState installed = CreateInstalledState(fixture, virusCount: 2);
        TurnFrame parent = TurnParent(installed);
        GameState prompt = GameEngine.BeginResolution(
            installed,
            parent,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.Equal(DecisionKind.TurnAction, prompt.PendingDecision?.Kind);

        GameState restoredPrompt = RoundTrip(prompt, fixture);
        PendingDecision decision = Assert.IsType<PendingDecision>(restoredPrompt.PendingDecision);
        LegalAction cashout = Assert.Single(
            decision.LegalActions,
            action => action.Kind == ActionKind.UseAbility
                && Assert.IsType<AbilityActionParameters>(action.Parameters).SourceCardId == fixture.CardId);
        AbilityActionParameters parameters = Assert.IsType<AbilityActionParameters>(cashout.Parameters);
        Assert.Equal(
            [BehaviorCostKind.Clicks, BehaviorCostKind.TrashSelf],
            parameters.Costs.Select(static cost => cost.Kind));

        ApplyResult completed = Apply(fixture, restoredPrompt, cashout);

        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Equal(9, completed.State.GetPlayer(fixture.RunnerId).Credits);
        Assert.Equal(3, completed.State.GetPlayer(fixture.RunnerId).Clicks);
        Assert.Equal(
            ZoneKind.RunnerHeap,
            completed.State.GetZone(completed.State.GetCard(fixture.CardId).ZoneId).Kind);
        Assert.Equal(0, VirusCount(completed.State, fixture.CardId));
        Assert.DoesNotContain(
            completed.State.RulesState!.Programs,
            program => program.CardId == fixture.CardId);
        Assert.Single(
            completed.Events,
            value => value.Kind == EngineEventKinds.CreditsAdjusted
                && Assert.IsType<CreditsAdjustedEventPayload>(value.Payload).Target == fixture.RunnerId
                && Assert.IsType<CreditsAdjustedEventPayload>(value.Payload).Delta == 4
                && Assert.IsType<CreditsAdjustedEventPayload>(value.Payload).CreditsAfter == 9);
        Assert.Single(
            completed.Events,
            value => value.Kind == EngineEventKinds.ClickSpent
                && Assert.IsType<ClickSpentEventPayload>(value.Payload).Side == PlayerSide.Runner
                && Assert.IsType<ClickSpentEventPayload>(value.Payload).ClicksRemaining == 3);
        Assert.Equal(DecisionKind.TurnTiming, completed.State.PendingDecision?.Kind);
        Assert.Empty(StateInvariantValidator.Collect(
            completed.State,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog));
    }

    [Fact]
    [Trait("GatewayCard", "30007")]
    public void PaidTrashCheckpointRetainsCountersThroughExactChildAndRejectsForgedAuthorization()
    {
        Fixture fixture = CreateFixture();
        GameState installed = CreateInstalledState(fixture, virusCount: 2);
        TurnFrame parent = TurnParent(installed);
        AbilityFrame frame = CashoutFrame(fixture, installed, parent, AbilityLifecycleStage.PayCosts);
        GameState payment = ReplaceResolutionStack(installed, [parent, frame]);
        Assert.Empty(StateInvariantValidator.Collect(
            payment,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog));

        GameState checkpoint = GameEngine.BeginResolution(
            payment,
            frame,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog,
            maxResolutionSteps: 1);
        AbilityFrame checkpointFrame = Assert.IsType<AbilityFrame>(checkpoint.ResolutionStack[^1]);
        Assert.Equal(AbilityLifecycleStage.CostCheckpoint, checkpointFrame.Stage);
        Assert.Equal(ZoneKind.RunnerHeap, checkpoint.GetZone(checkpoint.GetCard(fixture.CardId).ZoneId).Kind);
        Assert.Equal(2, VirusCount(checkpoint, fixture.CardId));
        Assert.Equal(5, checkpoint.GetPlayer(fixture.RunnerId).Credits);
        Assert.Equal(3, checkpoint.GetPlayer(fixture.RunnerId).Clicks);
        Assert.Empty(StateInvariantValidator.Collect(
            checkpoint,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog));

        GameState restoredCheckpoint = RoundTrip(checkpoint, fixture);
        AbilityFrame restoredFrame = Assert.IsType<AbilityFrame>(restoredCheckpoint.ResolutionStack[^1]);
        Assert.Equal(AbilityLifecycleStage.CostCheckpoint, restoredFrame.Stage);
        Assert.Equal(2, VirusCount(restoredCheckpoint, fixture.CardId));

        GameState completed = GameEngine.BeginResolution(
            restoredCheckpoint,
            restoredFrame,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.Equal(9, completed.GetPlayer(fixture.RunnerId).Credits);
        Assert.Equal(3, completed.GetPlayer(fixture.RunnerId).Clicks);
        Assert.Equal(ZoneKind.RunnerHeap, completed.GetZone(completed.GetCard(fixture.CardId).ZoneId).Kind);
        Assert.Equal(0, VirusCount(completed, fixture.CardId));
        Assert.DoesNotContain(completed.ResolutionStack, static value => value is AbilityFrame);
        Assert.Empty(StateInvariantValidator.Collect(
            completed,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog));

        AbilityFrame forgedFrame = new(
            restoredFrame.Version,
            restoredFrame.BehaviorId,
            restoredFrame.DefinitionId,
            restoredFrame.SourceCardId,
            "forged-fermenter-cashout",
            restoredFrame.Controller,
            restoredFrame.Timing,
            restoredFrame.Activation,
            restoredFrame.Costs,
            restoredFrame.TargetCardIds,
            restoredFrame.Usage,
            restoredFrame.CatalogFingerprint,
            restoredFrame.DefinitionFingerprint,
            restoredFrame.SourceZoneId,
            restoredFrame.SourceZoneKind,
            restoredFrame.Stage,
            restoredFrame.ParentFrameIdentity);
        GameState forged = ReplaceTopFrame(restoredCheckpoint, forgedFrame);

        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(
            forged,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.Contains(
            violations,
            violation => violation.Code == "resolution.ability-source-counters-unauthorized");
    }

    private static CardBehaviorDefinition Definition(Fixture fixture) =>
        fixture.BehaviorCatalog.GenericSpecs.Single(
            definition => definition.DefinitionId == fixture.DefinitionId);

    private static Fixture CreateFixture()
    {
        BehaviorCatalog behaviorCatalog = GatewayBehaviorCatalog.Create();
        RuntimeCardCatalog runtimeCatalog = new(
            "test:gateway-fermenter-runtime",
            new CardRuntimeSpec(
                GatewayBatch2CardCodes.Fermenter,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 1,
                memoryUnits: 1,
                subtypes: ["virus"]));
        GameState baseline = GameState.CreateNew(
            seed: 30007,
            cardDataVersion: "test:gateway-fermenter-card-data",
            behaviorRegistryVersion: behaviorCatalog.Version,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);
        PlayerId runnerId = baseline.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        PlayerState[] players = baseline.Players
            .Select(player => player.Id == runnerId
                ? new PlayerState(player.Id, player.Side, credits: 5, clicks: 4, player.AgendaPoints)
                : player)
            .ToArray();
        ZoneState grip = baseline.GetZone(ZoneKind.RunnerGrip);
        CardInstanceId cardId = new("card:gateway-fermenter-30007");
        CardInstanceState card = new(
            cardId,
            GatewayBatch2CardCodes.Fermenter,
            grip.Id,
            PlayerSide.Runner,
            faceUp: false);
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == grip.Id
                ? new ZoneState(
                    zone.Id,
                    zone.Kind,
                    zone.OwnerSide,
                    zone.ServerId,
                    zone.CardIds.Append(cardId).ToArray())
                : zone)
            .ToArray();
        RulesState rules = RulesState.CreateDefault(players);
        GameState state = new(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            players,
            [card],
            zones,
            baseline.Servers,
            new TurnState(PlayerSide.Runner, TurnPhase.Runner, baseline.Turn.TurnNumber),
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            rulesState: rules,
            behaviorRegistryVersion: behaviorCatalog.Version,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);
        return new Fixture(
            state,
            behaviorCatalog,
            runtimeCatalog,
            runnerId,
            cardId,
            GatewayBatch2CardCodes.Fermenter);
    }

    private static GameState CreateInstalledState(
        Fixture fixture,
        int virusCount,
        IReadOnlyList<ResolutionFrame>? resolutionStack = null)
    {
        GameState baseline = fixture.State;
        PlayerState[] players = baseline.Players
            .Select(player => player.Id == fixture.RunnerId
                ? new PlayerState(player.Id, player.Side, credits: 5, clicks: 4, player.AgendaPoints)
                : player)
            .ToArray();
        ZoneState rig = baseline.GetZone(ZoneKind.RunnerRig);
        ZoneState grip = baseline.GetZone(ZoneKind.RunnerGrip);
        CardInstanceState card = new(
            fixture.CardId,
            fixture.DefinitionId,
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
                    zone.CardIds.Append(fixture.CardId).ToArray())
                : zone.Id == grip.Id
                    ? new ZoneState(
                        zone.Id,
                        zone.Kind,
                        zone.OwnerSide,
                        zone.ServerId,
                        zone.CardIds.Where(cardId => cardId != fixture.CardId).ToArray())
                : zone)
            .ToArray();
        TurnFactsState facts = new(
            1,
            PlayerSide.Runner,
            baseline.Turn.TurnNumber,
            clicksAllotted: 4,
            clicksLost: 0,
            turnBegan: true,
            mandatoryDrawPerformed: false,
            actionPhaseEnded: false,
            discardPhaseEnded: false);
        RulesState defaults = RulesState.CreateDefault(players);
        CardCounterState[] counters = virusCount > 0
            ? [new CardCounterState(fixture.CardId, "virus", virusCount)]
            : [];
        RulesState rules = new(
            defaults.Version,
            defaults.Players.Select(player => player.PlayerId == fixture.RunnerId
                ? new PlayerRulesState(
                    player.PlayerId,
                    player.BaseHandSize,
                    player.CoreDamage,
                    player.Tags,
                    player.BaseMemory,
                    usedMemory: 1)
                : player).ToArray(),
            [new ProgramMemoryState(fixture.CardId, fixture.RunnerId, memoryCost: 1)],
            counters,
            defaults.LingeringEffects,
            defaults.Triggers,
            defaults.Interrupts);
        return new GameState(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            players,
            [card],
            zones,
            baseline.Servers,
            new TurnState(
                PlayerSide.Runner,
                TurnPhase.Runner,
                baseline.Turn.TurnNumber,
                TurnStep.RunnerAction,
                new TurnHistoryState(1, null, null, facts, null)),
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            resolutionStack: resolutionStack,
            rulesState: rules,
            behaviorRegistryVersion: fixture.BehaviorCatalog.Version,
            behaviorCatalogFingerprint: fixture.BehaviorCatalog.Fingerprint,
            runtimeCardCatalogVersion: fixture.RuntimeCatalog.Version,
            runtimeCardCatalogFingerprint: fixture.RuntimeCatalog.Fingerprint);
    }

    private static GameState OpenRunnerAction(
        GameState state,
        Fixture fixture) => ResumeRunnerAction(fixture, GameEngine.BeginTurn(
            state,
            PlayerSide.Runner,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog));

    private static GameState ResumeRunnerAction(Fixture fixture, GameState state)
    {
        while (state.PendingDecision?.Kind != DecisionKind.TurnAction)
        {
            ApplyResult result = Pass(fixture, state);
            Assert.True(result.Accepted, result.Rejection?.Message);
            state = result.State;
        }

        return state;
    }

    private static ApplyResult Pass(Fixture fixture, GameState state)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction pass = Assert.Single(
            decision.LegalActions,
            action => action.Kind == ActionKind.PassPriority);
        return Apply(fixture, state, pass);
    }

    private static ApplyResult Apply(Fixture fixture, GameState state, LegalAction action)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        return GameEngine.Apply(
            state,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id),
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
    }

    private static GameState RoundTrip(GameState state, Fixture fixture)
    {
        string json = EngineJson.Serialize(state);
        GameState restored = EngineJson.DeserializeState(
            json,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(state), DeterministicHash.ForState(restored));
        return restored;
    }

    private static TurnFrame TurnParent(GameState? installed) => new(
        1,
        ResolutionBehaviorIds.Turn,
        PlayerSide.Runner,
        installed?.Turn.TurnNumber ?? 1,
        TurnFrameStage.AwaitingAction);

    private static AbilityFrame CashoutFrame(
        Fixture fixture,
        GameState state,
        TurnFrame parent,
        AbilityLifecycleStage stage)
    {
        BehaviorPaidAbility ability = Assert.Single(Definition(fixture).PaidAbilities);
        ZoneState sourceZone = state.GetZone(ZoneKind.RunnerRig);
        return new AbilityFrame(
            1,
            ResolutionBehaviorIds.Ability,
            fixture.DefinitionId,
            fixture.CardId,
            ability.Key,
            fixture.RunnerId,
            ability.Timing,
            ability.Activation,
            ability.Costs,
            [],
            new AbilityUsageSnapshot(ability.UsageLimit, usesThisTurn: 0),
            fixture.BehaviorCatalog.Fingerprint,
            fixture.BehaviorCatalog.GetDefinitionFingerprint(fixture.DefinitionId),
            sourceZone.Id,
            sourceZone.Kind,
            stage,
            ParentIdentity(parent));
    }

    private static string ParentIdentity(ResolutionFrame frame) =>
        "parent:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(EngineJson.Serialize(frame))))
            .ToLowerInvariant();

    private static GameState ReplaceResolutionStack(
        GameState source,
        IReadOnlyList<ResolutionFrame> resolutionStack) =>
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
            resolutionStack,
            source.PendingDecision,
            source.ActionSequence,
            source.InitialSeed,
            source.RunState,
            source.RulesState,
            source.BehaviorRegistryVersion,
            source.BehaviorCatalogFingerprint,
            source.RuntimeCardCatalogVersion,
            source.RuntimeCardCatalogFingerprint);

    private static GameState ReplaceTopFrame(GameState source, ResolutionFrame frame)
    {
        ResolutionFrame[] stack = source.ResolutionStack
            .Take(source.ResolutionStack.Count - 1)
            .Append(frame)
            .ToArray();
        return ReplaceResolutionStack(source, stack);
    }

    private static int VirusCount(GameState state, CardInstanceId cardId) =>
        state.RulesState?.Counters.SingleOrDefault(counter =>
            counter.CardId == cardId
            && counter.Purpose == CardCounterPurpose.Virus)?.Count
        ?? 0;

    private static void AssertEventAfter(
        IReadOnlyList<EngineEvent> events,
        string firstKind,
        string secondKind)
    {
        int first = Array.FindIndex(events.ToArray(), value => value.Kind == firstKind);
        int second = Array.FindIndex(events.ToArray(), value => value.Kind == secondKind);
        Assert.True(first >= 0);
        Assert.True(second > first);
    }

    private sealed record Fixture(
        GameState State,
        BehaviorCatalog BehaviorCatalog,
        RuntimeCardCatalog RuntimeCatalog,
        PlayerId RunnerId,
        CardInstanceId CardId,
        CardDefinitionId DefinitionId);
}

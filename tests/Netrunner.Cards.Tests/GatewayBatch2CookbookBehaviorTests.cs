using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

/// <summary>
/// Real-turn acceptance evidence for Cookbook (30009). The runtime metadata
/// fixture deliberately uses the production card-code behavior declaration
/// with test-only printed metadata, so this lane exercises the ordinary
/// Runner install reducer even when clean CI embeds the seven-card fixture.
/// </summary>
public sealed class GatewayBatch2CookbookBehaviorTests
{
    [Fact]
    [Trait("GatewayCard", "30009")]
    public void InstallingVirusProgramOffersOptionalTriggerAndAcceptPlacesOneCounterOnInstalledSubject()
    {
        Fixture fixture = CreateFixture(Fixture.InstallKind.VirusProgram);
        GameState turnAction = OpenRunnerAction(fixture);

        LegalAction install = SingleInstall(turnAction, fixture.IncomingCardId);
        ApplyResult opened = Apply(fixture, turnAction, install);

        Assert.True(opened.Accepted, opened.Rejection?.Message);
        Assert.Equal(DecisionKind.TriggerChoice, opened.State.PendingDecision?.Kind);
        Assert.Equal(
            ZoneKind.RunnerRig,
            opened.State.GetZone(opened.State.GetCard(fixture.IncomingCardId).ZoneId).Kind);
        Assert.Equal(1, opened.Events.Count(value => value.Kind == EngineEventKinds.CardInstalled));
        Assert.Equal(
            fixture.IncomingCardId,
            Assert.IsType<CardInstalledEventPayload>(
                opened.Events.Single(value => value.Kind == EngineEventKinds.CardInstalled).Payload).CardId);

        PendingDecision triggerDecision = Assert.IsType<PendingDecision>(opened.State.PendingDecision);
        Assert.Equal(fixture.RunnerId, triggerDecision.Owner);
        Assert.Contains(triggerDecision.LegalActions, action => action.Kind == ActionKind.UseTrigger);
        Assert.Contains(triggerDecision.LegalActions, action => action.Kind == ActionKind.DeclineTrigger);

        Observation runnerObservation = ObservationBuilder.Build(
            opened.State,
            PlayerSide.Runner,
            fixture.BehaviorCatalog);
        Assert.Equal(triggerDecision.Id, runnerObservation.Decision?.Id);
        Assert.Contains(runnerObservation.LegalActions, action => action.Kind == ActionKind.UseTrigger);

        Observation corpObservation = ObservationBuilder.Build(
            opened.State,
            PlayerSide.Corp,
            fixture.BehaviorCatalog);
        Assert.Empty(corpObservation.LegalActions);
        Assert.Null(corpObservation.Decision);

        string json = EngineJson.Serialize(opened.State);
        GameState restored = EngineJson.DeserializeState(
            json,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(opened.State), DeterministicHash.ForState(restored));

        PendingDecision restoredDecision = Assert.IsType<PendingDecision>(restored.PendingDecision);
        LegalAction use = Assert.Single(
            restoredDecision.LegalActions,
            action => action.Kind == ActionKind.UseTrigger);
        TriggerChoiceParameters trigger = Assert.IsType<TriggerChoiceParameters>(use.Parameters);
        Assert.Equal(
            Assert.IsType<TriggerChoiceParameters>(
                Assert.Single(triggerDecision.LegalActions, action => action.Kind == ActionKind.UseTrigger).Parameters).TriggerId,
            trigger.TriggerId);

        ApplyResult completed = Apply(fixture, restored, use);

        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Equal(DecisionKind.TurnTiming, completed.State.PendingDecision?.Kind);
        Assert.Equal(1, completed.State.RulesState?.Counters.Count);
        CardCounterState virusCounter = Assert.Single(completed.State.RulesState!.Counters);
        Assert.Equal(fixture.IncomingCardId, virusCounter.CardId);
        Assert.Equal("virus", virusCounter.CounterName);
        Assert.Equal(CardCounterPurpose.Virus, virusCounter.Purpose);
        Assert.Equal(1, virusCounter.Count);
        Assert.DoesNotContain(
            completed.State.RulesState.Counters,
            counter => counter.CardId == fixture.CookbookCardId);
        Assert.Equal(
            1,
            completed.Events.Count(value => value.Kind == EngineEventKinds.CounterAdjusted));
        Assert.Empty(StateInvariantValidator.Collect(
            completed.State,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog));
    }

    [Fact]
    [Trait("GatewayCard", "30009")]
    public void DecliningCookbookVirusInstallTriggerLeavesInstalledProgramWithoutCounter()
    {
        Fixture fixture = CreateFixture(Fixture.InstallKind.VirusProgram);
        GameState turnAction = OpenRunnerAction(fixture);
        ApplyResult opened = Apply(
            fixture,
            turnAction,
            SingleInstall(turnAction, fixture.IncomingCardId));

        Assert.True(opened.Accepted, opened.Rejection?.Message);
        PendingDecision triggerDecision = Assert.IsType<PendingDecision>(opened.State.PendingDecision);
        LegalAction decline = Assert.Single(
            triggerDecision.LegalActions,
            action => action.Kind == ActionKind.DeclineTrigger);

        ApplyResult completed = Apply(fixture, opened.State, decline);

        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Equal(DecisionKind.TurnTiming, completed.State.PendingDecision?.Kind);
        Assert.Equal(
            ZoneKind.RunnerRig,
            completed.State.GetZone(completed.State.GetCard(fixture.IncomingCardId).ZoneId).Kind);
        Assert.Empty(completed.State.RulesState?.Counters ?? []);
        Assert.DoesNotContain(completed.Events, value => value.Kind == EngineEventKinds.CounterAdjusted);
        Assert.DoesNotContain(completed.Events, value => value.Kind == EngineEventKinds.CardInstalled);
        Assert.Empty(StateInvariantValidator.Collect(
            completed.State,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog));
    }

    [Fact]
    [Trait("GatewayCard", "30009")]
    public void InstallingNonVirusProgramDoesNotOfferCookbookTrigger()
    {
        Fixture fixture = CreateFixture(Fixture.InstallKind.NonVirusProgram);
        GameState turnAction = OpenRunnerAction(fixture);

        ApplyResult completed = Apply(
            fixture,
            turnAction,
            SingleInstall(turnAction, fixture.IncomingCardId));

        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Equal(DecisionKind.TurnTiming, completed.State.PendingDecision?.Kind);
        Assert.Equal(
            ZoneKind.RunnerRig,
            completed.State.GetZone(completed.State.GetCard(fixture.IncomingCardId).ZoneId).Kind);
        Assert.Empty(completed.State.RulesState?.Counters ?? []);
        Assert.DoesNotContain(completed.Events, value => value.Kind == EngineEventKinds.CounterAdjusted);
        Assert.Equal(1, completed.Events.Count(value => value.Kind == EngineEventKinds.CardInstalled));
        Assert.Empty(StateInvariantValidator.Collect(
            completed.State,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog));
    }

    [Fact]
    [Trait("GatewayCard", "30009")]
    public void ForgedCookbookTriggerSubjectIsRejectedWithoutMutatingState()
    {
        Fixture fixture = CreateFixture(Fixture.InstallKind.VirusProgram);
        GameState turnAction = OpenRunnerAction(fixture);
        ApplyResult opened = Apply(
            fixture,
            turnAction,
            SingleInstall(turnAction, fixture.IncomingCardId));

        Assert.True(opened.Accepted, opened.Rejection?.Message);
        PendingDecision triggerDecision = Assert.IsType<PendingDecision>(opened.State.PendingDecision);
        LegalAction use = Assert.Single(
            triggerDecision.LegalActions,
            action => action.Kind == ActionKind.UseTrigger);
        TriggerChoiceParameters useParameters = Assert.IsType<TriggerChoiceParameters>(use.Parameters);
        EffectCheckpointFrame checkpoint = Assert.IsType<EffectCheckpointFrame>(
            opened.State.ResolutionStack[^1]);
        TriggerInvocation invocation = Assert.Single(
            checkpoint.TriggerQueue,
            candidate => candidate.Id == useParameters.TriggerId);
        CardInstanceId forgedSubjectId = new("card:gateway-cookbook-forged-subject");
        GameplayOccurrenceEffect gameplay = Assert.IsType<GameplayOccurrenceEffect>(invocation.Occurrence.Effect);
        OccurrenceSnapshot forgedOccurrence = new(
            invocation.Occurrence.Version,
            invocation.Occurrence.Id,
            invocation.Occurrence.Kind,
            invocation.Occurrence.Phase,
            invocation.Occurrence.Controller,
            invocation.Occurrence.TargetPlayer,
            forgedSubjectId,
            invocation.Occurrence.SourceDefinitionId,
            invocation.Occurrence.SourceZoneId,
            new GameplayOccurrenceEffect(
                gameplay.Kind,
                gameplay.Controller,
                forgedSubjectId,
                gameplay.ServerId,
                gameplay.Successful));
        TriggerInvocation forgedInvocation = new(
            invocation.Version,
            invocation.Id,
            invocation.Controller,
            invocation.Mode,
            invocation.BehaviorId,
            invocation.SourceCardId,
            invocation.SourceDefinitionId,
            invocation.SourceZoneId,
            forgedOccurrence,
            invocation.Costs,
            invocation.Effects,
            invocation.TriggerKey,
            invocation.CatalogFingerprint,
            invocation.DefinitionFingerprint,
            invocation.SourceOwnerSide,
            invocation.SourceZoneKind);
        EffectCheckpointFrame forgedCheckpoint = new(
            checkpoint.Version,
            checkpoint.BehaviorId,
            checkpoint.Controller,
            checkpoint.Occurrence,
            checkpoint.OriginalEffect,
            checkpoint.CurrentEffect,
            checkpoint.Stage,
            checkpoint.TriggerQueue
                .Select(candidate => candidate.Id == invocation.Id ? forgedInvocation : candidate)
                .ToArray(),
            checkpoint.TriggerCursor,
            checkpoint.InterruptQueue,
            checkpoint.InterruptCursor,
            checkpoint.AppliedTriggers,
            checkpoint.AppliedInterrupts,
            checkpoint.ReplacementDepth,
            checkpoint.ChildKind,
            checkpoint.ChildTriggerId,
            checkpoint.ChildInterruptId,
            checkpoint.ResolvedOccurrence);
        GameState forged = ReplaceTopFrame(opened.State, forgedCheckpoint);
        string forgedHash = DeterministicHash.ForState(forged);

        ApplyResult rejected = GameEngine.Apply(
            forged,
            new ActionSubmission(
                triggerDecision.Owner,
                triggerDecision.Id,
                triggerDecision.Revision,
                use.Id),
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);

        Assert.False(rejected.Accepted);
        Assert.True(
            rejected.Rejection?.Code is ApplyRejectionCode.InvalidInputState
                or ApplyRejectionCode.ResolutionFailure);
        Assert.Equal(forgedHash, DeterministicHash.ForState(rejected.State));
        Assert.Empty(forged.RulesState?.Counters ?? []);
    }

    private static Fixture CreateFixture(Fixture.InstallKind installKind)
    {
        CardDefinitionId cookbookDefinitionId = GatewayBatch2CardCodes.Cookbook;
        CardDefinitionId virusDefinitionId = new("test:gateway-cookbook-virus-program");
        CardDefinitionId nonVirusDefinitionId = new("test:gateway-cookbook-non-virus-program");
        CardDefinitionId incomingDefinitionId = installKind == Fixture.InstallKind.VirusProgram
            ? virusDefinitionId
            : nonVirusDefinitionId;
        CardInstanceId cookbookCardId = new("card:gateway-cookbook-30009");
        CardInstanceId incomingCardId = new("card:gateway-cookbook-incoming-program");

        CardBehaviorDefinition cookbook = GatewayBatch2TriggerBehaviorSpecs.GenericSpecs.Single(
            definition => definition.DefinitionId == cookbookDefinitionId);
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder(
                EngineCompatibility.BehaviorRegistryVersion)
            .Add(cookbook)
            .Build();
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder(
                "test:gateway-cookbook-runtime")
            .Add(new CardRuntimeSpec(
                cookbookDefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Resource,
                installCost: 2))
            .Add(new CardRuntimeSpec(
                virusDefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 2,
                memoryUnits: 1,
                subtypes: ["virus"]))
            .Add(new CardRuntimeSpec(
                nonVirusDefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 2,
                memoryUnits: 1,
                subtypes: ["utility"]))
            .Build();

        GameState baseline = GameState.CreateNew(
            seed: 30009,
            cardDataVersion: "test:gateway-cookbook-card-data",
            behaviorRegistryVersion: behaviorCatalog.Version,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);
        PlayerId runnerId = baseline.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        PlayerState[] players = baseline.Players
            .Select(player => player.Id == runnerId
                ? new PlayerState(player.Id, player.Side, credits: 10, clicks: 4, player.AgendaPoints)
                : player)
            .ToArray();
        ZoneState rig = baseline.GetZone(ZoneKind.RunnerRig);
        ZoneState grip = baseline.GetZone(ZoneKind.RunnerGrip);
        CardInstanceState cookbookCard = new(
            cookbookCardId,
            cookbookDefinitionId,
            rig.Id,
            PlayerSide.Runner,
            faceUp: true);
        CardInstanceState incomingCard = new(
            incomingCardId,
            incomingDefinitionId,
            grip.Id,
            PlayerSide.Runner,
            faceUp: false);
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == rig.Id
                ? new ZoneState(
                    zone.Id,
                    zone.Kind,
                    zone.OwnerSide,
                    zone.ServerId,
                    zone.CardIds.Append(cookbookCardId).ToArray())
                : zone.Id == grip.Id
                    ? new ZoneState(
                        zone.Id,
                        zone.Kind,
                        zone.OwnerSide,
                        zone.ServerId,
                        zone.CardIds.Append(incomingCardId).ToArray())
                    : zone)
            .ToArray();
        RulesState rules = RulesState.CreateDefault(players);
        GameState state = new(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            players,
            [cookbookCard, incomingCard],
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
            cookbookCardId,
            incomingCardId);
    }

    private static GameState OpenRunnerAction(Fixture fixture)
    {
        GameState state = GameEngine.BeginTurn(
            fixture.State,
            PlayerSide.Runner,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        while (state.PendingDecision?.Kind != DecisionKind.TurnAction)
        {
            PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction pass = Assert.Single(
                pending.LegalActions,
                action => action.Kind == ActionKind.PassPriority);
            ApplyResult result = Apply(fixture, state, pass);
            Assert.True(result.Accepted, result.Rejection?.Message);
            state = result.State;
        }

        return state;
    }

    private static LegalAction SingleInstall(GameState state, CardInstanceId cardId) => Assert.Single(
        state.PendingDecision!.LegalActions,
        action => action.Kind == ActionKind.InstallCard
            && Assert.IsType<InstallCardParameters>(action.Parameters).CardId == cardId);

    private static ApplyResult Apply(Fixture fixture, GameState state, LegalAction action)
    {
        PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
        return GameEngine.Apply(
            state,
            new ActionSubmission(pending.Owner, pending.Id, pending.Revision, action.Id),
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
    }

    private static GameState ReplaceTopFrame(GameState source, ResolutionFrame frame)
    {
        ResolutionFrame[] stack = source.ResolutionStack
            .Take(source.ResolutionStack.Count - 1)
            .Append(frame)
            .ToArray();
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
            stack,
            source.PendingDecision,
            source.ActionSequence,
            source.InitialSeed,
            source.RunState,
            source.RulesState,
            source.BehaviorRegistryVersion,
            source.BehaviorCatalogFingerprint,
            source.RuntimeCardCatalogVersion,
            source.RuntimeCardCatalogFingerprint);
    }

    private sealed record Fixture(
        GameState State,
        BehaviorCatalog BehaviorCatalog,
        RuntimeCardCatalog RuntimeCatalog,
        PlayerId RunnerId,
        CardInstanceId CookbookCardId,
        CardInstanceId IncomingCardId)
    {
        public enum InstallKind
        {
            VirusProgram,
            NonVirusProgram,
        }
    }
}

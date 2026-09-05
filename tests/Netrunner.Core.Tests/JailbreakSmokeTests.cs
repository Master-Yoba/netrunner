using Xunit;

namespace Netrunner.Core.Tests;

public sealed class JailbreakSmokeTests
{
    [Fact]
    public void RealRunnerTurnOffersRunEventTargetsAndStartsNestedRun()
    {
        CardDefinitionId eventDefinition = new("30028");
        CardDefinitionId drawDefinition = new("test:jailbreak-draw-card");
        CardInstanceId eventCard = new("card:jailbreak");
        CardInstanceId drawCard = new("card:jailbreak-draw");
        BehaviorCatalog behavior = new BehaviorCatalogBuilder("test:jailbreak-runtime")
            .AddRunEvent(new RunEventBehaviorSpec(
                eventDefinition,
                cost: 0,
                hostedCredits: 0,
                allowedTargetServers: [ServerKind.Headquarters, ServerKind.RAndD],
                successDraw: new BehaviorRunSuccessDraw(1, "jailbreak-success-draw"),
                breachAccessModifier: new BehaviorRunBreachAccessModifier(1, "jailbreak-additional-access")))
            .Add(new CardBehaviorDefinition(
                eventDefinition,
                PlayerSide.Runner,
                triggers:
                [
                    new BehaviorTrigger(
                        "jailbreak-success-draw",
                        OccurrenceKind.RunSuccessful,
                        TriggerMode.Mandatory,
                        BehaviorTriggerScope.Self,
                        subjectCardFilter: null,
                        instructions: [new BehaviorInstruction(BehaviorInstructionKind.Draw, amount: 1)]),
            ]))
            .Build();
        RuntimeCardCatalog runtime = new RuntimeCardCatalogBuilder("runtime:jailbreak-runtime")
            .Add(new CardRuntimeSpec(eventDefinition, PlayerSide.Runner, RuntimeCardClass.Event, playCost: 0))
            .Add(new CardRuntimeSpec(drawDefinition, PlayerSide.Runner, RuntimeCardClass.Event, playCost: 0))
            .Build();

        GameState baseline = GameState.CreateNew(
            seed: 9,
            behaviorRegistryVersion: behavior.Version,
            behaviorCatalogFingerprint: behavior.Fingerprint,
            runtimeCardCatalogVersion: runtime.Version,
            runtimeCardCatalogFingerprint: runtime.Fingerprint);
        PlayerId runner = baseline.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        ZoneState grip = baseline.GetZone(ZoneKind.RunnerGrip);
        ZoneState stack = baseline.GetZone(ZoneKind.RunnerStack);
        ZoneState corpHand = baseline.GetZone(ZoneKind.CorpHand);
        CardInstanceState eventState = new(eventCard, eventDefinition, grip.Id, PlayerSide.Runner);
        CardInstanceState drawState = new(drawCard, drawDefinition, stack.Id, PlayerSide.Runner);
        ServerId hq = baseline.Servers.Single(server => server.Kind == ServerKind.Headquarters).Id;
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == grip.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [eventCard])
                : zone.Id == stack.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [drawCard])
                    : zone.Id == corpHand.Id
                        ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [new CardInstanceId("card:jailbreak-corp")])
                        : zone)
            .ToArray();
        CardInstanceState corpCard = new(
            new CardInstanceId("card:jailbreak-corp"),
            new CardDefinitionId("test:jailbreak-corp-card"),
            corpHand.Id,
            PlayerSide.Corp);
        GameState state = new(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            baseline.Players
                .Select(player => player.Id == runner
                    ? new PlayerState(player.Id, player.Side, credits: 5, clicks: 4)
                    : player)
                .ToArray(),
            [eventState, drawState, corpCard],
            zones,
            baseline.Servers,
            new TurnState(PlayerSide.Runner, TurnPhase.Runner, 1),
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            behaviorRegistryVersion: behavior.Version,
            behaviorCatalogFingerprint: behavior.Fingerprint,
            runtimeCardCatalogVersion: runtime.Version,
            runtimeCardCatalogFingerprint: runtime.Fingerprint,
            rulesState: baseline.RulesState);

        GameState turn = GameEngine.BeginTurn(state, PlayerSide.Runner, behavior, runtime);
        while (turn.PendingDecision?.Kind != DecisionKind.TurnAction)
        {
            PendingDecision decision = Assert.IsType<PendingDecision>(turn.PendingDecision);
            LegalAction pass = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.PassPriority);
            ApplyResult passed = GameEngine.Apply(
                turn,
                new ActionSubmission(decision.Owner, decision.Id, decision.Revision, pass.Id),
                behavior,
                runtime);
            Assert.True(passed.Accepted, passed.Rejection?.Message);
            turn = passed.State;
        }

        LegalAction[] runEvents = turn.PendingDecision!.LegalActions
            .Where(action => action.Kind == ActionKind.PlayCard
                && action.Parameters is PlayCardParameters { TargetServerId: not null })
            .ToArray();
        Assert.Equal(2, runEvents.Length);
        Assert.Contains(runEvents, action =>
            Assert.IsType<PlayCardParameters>(action.Parameters).TargetServerId == hq);
        Assert.DoesNotContain(runEvents, action =>
            Assert.IsType<PlayCardParameters>(action.Parameters).TargetServerId
                == baseline.Servers.Single(server => server.Kind == ServerKind.Archives).Id);

        LegalAction selected = runEvents.Single(action =>
            Assert.IsType<PlayCardParameters>(action.Parameters).TargetServerId == hq);
        PendingDecision actionDecision = turn.PendingDecision;
        ApplyResult started = GameEngine.Apply(
            turn,
            new ActionSubmission(actionDecision.Owner, actionDecision.Id, actionDecision.Revision, selected.Id),
            behavior,
            runtime);
        Assert.True(started.Accepted, started.Rejection?.Message);
        Assert.Equal(eventCard, started.State.RunState!.ActiveRunEventId);
        Assert.Equal(3, started.State.GetPlayer(runner).Clicks);
        Assert.Equal(5, started.State.GetPlayer(runner).Credits);
        Assert.Contains(started.Events, value => value.Kind == EngineEventKinds.RunEventPlayed);
    }
}

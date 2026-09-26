using System.Text.Json;
using System.Text.Json.Serialization;

using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;

using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>SCN-402 / SIM-401: both observation-only policies traverse real
/// damage, rez, access, and card-specific jack-out boundaries with replay and
/// save/load at every yielded decision. CR citations are fixture data.</summary>
public sealed class DamageTagProofTests
{
    private static readonly JsonSerializerOptions FixtureJsonOptions = new()
    { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    [Fact]
    [Trait("Slice", "4.6")]
    [Trait("SCN", "402")]
    [Trait("SIM", "401")]
    public async Task DamageScenarioFixtureReplaysBothPoliciesAtEveryYield()
    {
        Fixture fixture = JsonSerializer.Deserialize<Fixture>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios", "scn-402-damage.json")),
            FixtureJsonOptions)!;
        Assert.Equal("SCN-402", fixture.ScenarioId);
        Assert.Equal(EngineCompatibility.StateSchemaVersion, fixture.EngineStateSchemaVersion);
        Assert.Equal(EngineCompatibility.RulesVersion, fixture.RulesVersion);
        Assert.True(fixture.SaveLoadEveryYield && fixture.ReplayEveryAction);
        Assert.Contains(FullGameContractAgentVersion.AgentVersion, fixture.Policies);
        Assert.Contains(fixture.Citations, value => value.Contains("10.4.2a", StringComparison.Ordinal));
        BehaviorCatalog behavior = GatewayBehaviorCatalog.Create();
        Assert.Equal(behavior.Version, fixture.BehaviorRegistryVersion);
        RuntimeCardCatalog runtime = Runtime();
        Assert.Equal(7, fixture.Cases.Length);
        foreach (Case scenario in fixture.Cases)
        {
            GameState initial = Build(fixture.Seed, scenario, behavior, runtime);
            Result scripted = await Drive(initial, behavior, runtime, scenario.Ice, contract: false);
            Result agent = await Drive(initial, behavior, runtime, scenario.Ice, contract: true);
            Assert.Equal(DeterministicHash.ForState(scripted.State), DeterministicHash.ForState(agent.State));
            Assert.Equal(scripted.Events.Select(CanonicalJson.Serialize), agent.Events.Select(CanonicalJson.Serialize));
            Assert.Equal(scenario.ExpectedDamage, scripted.State.GetZone(ZoneKind.RunnerHeap).CardIds.Count);
            Assert.Equal(scenario.ExpectedTags, scripted.State.RulesState!.Players.Single(player => player.PlayerId == scripted.State.Players.Single(player => player.Side == PlayerSide.Runner).Id).Tags);
            Assert.Equal(scenario.JackOut, scripted.Events.Any(value => value.Kind == EngineEventKinds.IceSubroutineJackOut));
            RunTimingTrace trace = RunTimingTraceValidator.Validate(scripted.Events);
            Assert.True(trace.IsValid, $"{scenario.Id}: {trace.RenderText()}");
            if (scenario.JackOut)
            {
                int damage = scripted.Events.FindIndex(value => value.Kind == EngineEventKinds.DamageDealt);
                int jackOut = scripted.Events.FindIndex(value => value.Kind == EngineEventKinds.IceSubroutineJackOut);
                Assert.True(damage >= 0 && damage < jackOut);
                Assert.Single(scripted.Events, value => value.Kind == EngineEventKinds.DamageDealt);
                Assert.Contains(trace.Entries, entry => entry.EventKind == EngineEventKinds.IceSubroutineJackOut);
                // Removing the card-specific evidence must restore the normal
                // movement-window requirement rather than permit arbitrary jack out.
                Assert.False(RunTimingTraceValidator.Validate(scripted.Events.Where(value => value.Kind != EngineEventKinds.IceSubroutineJackOut).ToArray()).IsValid);
            }
            if (scenario.Code == "30073")
            {
                int damage = scripted.Events.FindIndex(value => value.Kind == EngineEventKinds.DamageDealt);
                int gain = scripted.Events.FindIndex(value => value.Payload is CreditsAdjustedEventPayload credits && credits.Delta == 1);
                Assert.True(damage >= 0 && gain > damage);
            }
            if (!scenario.Ice)
            {
                int access = scripted.Events.FindIndex(value => value.Kind == EngineEventKinds.CardAccessed);
                int damage = scripted.Events.FindIndex(value => value.Kind == EngineEventKinds.DamageDealt);
                Assert.True(access >= 0 && damage > access);
            }
        }
    }

    private static async Task<Result> Drive(GameState initial, BehaviorCatalog behavior, RuntimeCardCatalog runtime, bool rezIce, bool contract)
    {
        GameEngine.RunStartResult start = GameEngine.BeginRunWithEvents(initial,
            initial.Servers.Single(server => server.Kind == ServerKind.Remote).Id, behavior, runtime);
        GameState state = start.State;
        GameState replay = start.State;
        var events = new List<EngineEvent>(start.Events);
        for (int step = 0; step < 128; step++)
        {
            StateInvariantValidator.Validate(state, behavior, runtime);
            if (state.PendingDecision is not { } pending)
            {
                Assert.Null(state.RunState);
                Assert.Empty(state.ResolutionStack);
                return new Result(state, events);
            }
            string json = EngineJson.Serialize(state);
            state = EngineJson.DeserializeState(json, behavior, runtime);
            Assert.Equal(json, EngineJson.Serialize(state));
            Assert.Equal(DeterministicHash.ForState(state), DeterministicHash.ForState(replay));
            Observation observation = ObservationBuilder.Build(state, state.GetPlayer(pending.Owner).Side, behavior);
            ActionId selected = contract
                ? await new FullGameContractAgent(new FullGameContractPolicy(observation.Viewer, runActionOrder:
                    rezIce
                        ? [ActionKind.RezCard, ActionKind.RezIce, ActionKind.ContinueRun, ActionKind.ChooseBreachCandidate, ActionKind.NoAccessAction, ActionKind.PassPriority]
                        : [ActionKind.ContinueRun, ActionKind.ChooseBreachCandidate, ActionKind.NoAccessAction, ActionKind.PassPriority]), runtime).ChooseActionAsync(observation)
                : Scripted(observation, rezIce).Id;
            LegalAction issued = Assert.Single(pending.LegalActions, action => action.Id == selected);
            if (issued.Parameters is IceSubroutineChoiceParameters { Choice: IceSubroutineChoiceKind.JackOut })
            {
                Assert.Equal(2, state.GetZone(ZoneKind.RunnerHeap).CardIds.Count);
                Assert.Equal(PlayerSide.Runner, observation.Viewer);
            }
            var submission = new ActionSubmission(pending.Owner, pending.Id, pending.Revision, selected);
            ApplyResult result = GameEngine.Apply(state, submission, behavior, runtime);
            Assert.True(result.Accepted, result.Rejection?.Message);
            ReplayAction actionReplay = ReplayAction.From(result.AcceptedAction!);
            ApplyResult replayed = GameEngine.Apply(replay, actionReplay.Submission, behavior, runtime);
            Assert.True(replayed.Accepted, replayed.Rejection?.Message);
            Assert.Equal(actionReplay.ExpectedStateHash, replayed.AcceptedAction!.ResultingStateHash);
            Assert.Equal(actionReplay.ExpectedEventBatchHash, replayed.AcceptedAction.ResultingEventBatchHash);
            Assert.Equal(result.Events.Select(CanonicalJson.Serialize), replayed.Events.Select(CanonicalJson.Serialize));
            events.AddRange(result.Events);
            state = result.State;
            replay = replayed.State;
        }
        throw new Xunit.Sdk.XunitException("SCN-402 exceeded its bounded action cap.");
    }

    private static LegalAction Scripted(Observation observation, bool rezIce)
    {
        LegalAction? special = observation.LegalActions.FirstOrDefault(action =>
            action.Parameters is IceSubroutineChoiceParameters { Choice: IceSubroutineChoiceKind.JackOut or IceSubroutineChoiceKind.EndRun });
        if (special is not null) return special;
        foreach (ActionKind kind in new[] { ActionKind.RezCard, ActionKind.RezIce, ActionKind.ContinueRun,
            ActionKind.ChooseBreachCandidate, ActionKind.NoAccessAction, ActionKind.UseTrigger, ActionKind.PassPriority })
        {
            if (!rezIce && kind is ActionKind.RezCard or ActionKind.RezIce) continue;
            LegalAction? action = observation.LegalActions.FirstOrDefault(value => value.Kind == kind);
            if (action is not null) return action;
        }
        throw new Xunit.Sdk.XunitException($"Unsupported SCN-402 decision {observation.Decision!.Kind}.");
    }

    private static RuntimeCardCatalog Runtime() => new RuntimeCardCatalogBuilder("fixture:scn402-runtime")
        .Add(new CardRuntimeSpec(new CardDefinitionId("30031"), PlayerSide.Runner, RuntimeCardClass.Hardware, installCost: 2))
        .Add(new CardRuntimeSpec(new CardDefinitionId("30045"), PlayerSide.Corp, RuntimeCardClass.Asset, rezCost: 0, trashCost: 2))
        .Add(new CardRuntimeSpec(new CardDefinitionId("30046"), PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 2))
        .Add(new CardRuntimeSpec(new CardDefinitionId("30047"), PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 4))
        .Add(new CardRuntimeSpec(new CardDefinitionId("30055"), PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 2))
        .Add(new CardRuntimeSpec(new CardDefinitionId("30062"), PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 5))
        .Add(new CardRuntimeSpec(new CardDefinitionId("30063"), PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 7))
        .Add(new CardRuntimeSpec(new CardDefinitionId("30073"), PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 1))
        .Build();

    private static GameState Build(ulong seed, Case scenario, BehaviorCatalog behavior, RuntimeCardCatalog runtime)
    {
        GameState baseline = GameState.CreateNew(seed, rulesVersion: EngineCompatibility.RulesVersion,
            cardDataVersion: EngineCompatibility.GatewayCardDataVersion,
            behaviorRegistryVersion: behavior.Version, behaviorCatalogFingerprint: behavior.Fingerprint,
            runtimeCardCatalogVersion: runtime.Version, runtimeCardCatalogFingerprint: runtime.Fingerprint);
        var allocator = new StableIdAllocator(baseline.Allocator);
        ServerId remoteId = allocator.NextServerId();
        ZoneId iceZoneId = allocator.NextZoneId();
        ZoneId rootZoneId = allocator.NextZoneId();
        var remote = new ServerState(remoteId, ServerKind.Remote, iceZoneId, rootZoneId);
        ZoneId grip = baseline.GetZone(ZoneKind.RunnerGrip).Id;
        CardInstanceId source = allocator.NextCardInstanceId();
        CardInstanceId[] hand = Enumerable.Range(0, scenario.GripCount).Select(_ => allocator.NextCardInstanceId()).ToArray();
        CardInstanceState[] cards =
        [
            new(source, new CardDefinitionId(scenario.Code), scenario.Ice ? iceZoneId : rootZoneId,
                PlayerSide.Corp, faceUp: false, rezzed: false, advancementCounters: scenario.AdvancementCounters),
            .. hand.Select(id => new CardInstanceState(id, new CardDefinitionId("30031"), grip, PlayerSide.Runner)),
        ];
        ZoneState[] zones =
        [
            .. baseline.Zones.Select(zone => zone.Id == grip
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, hand) : zone),
            new(iceZoneId, ZoneKind.ServerIce, PlayerSide.Corp, remoteId, scenario.Ice ? [source] : []),
            new(rootZoneId, ZoneKind.ServerRoot, PlayerSide.Corp, remoteId, scenario.Ice ? [] : [source]),
        ];
        PlayerState[] players = baseline.Players.Select(player => new PlayerState(player.Id, player.Side, 40, 4, 0)).ToArray();
        var facts = new TurnFactsState(1, PlayerSide.Runner, baseline.Turn.TurnNumber, 4, 0, true, false, false, false);
        var turn = new TurnState(PlayerSide.Runner, TurnPhase.Runner, baseline.Turn.TurnNumber,
            TurnStep.RunnerAction, new TurnHistoryState(1, null, null, facts, null));
        return new GameState(baseline.SchemaVersion, baseline.GameId, baseline.RulesVersion, baseline.CardDataVersion,
            players, cards, zones, [.. baseline.Servers, remote], turn, baseline.Outcome,
            baseline.RandomState, allocator.State, rulesState: RulesState.CreateDefault(players),
            initialSeed: baseline.InitialSeed, behaviorRegistryVersion: behavior.Version,
            behaviorCatalogFingerprint: behavior.Fingerprint, runtimeCardCatalogVersion: runtime.Version,
            runtimeCardCatalogFingerprint: runtime.Fingerprint);
    }

    private sealed record Result(GameState State, List<EngineEvent> Events);
    private sealed record Fixture(int SchemaVersion, string ScenarioId, ulong Seed, string RulesVersion,
        int EngineStateSchemaVersion, string BehaviorRegistryVersion, string[] Policies,
        bool SaveLoadEveryYield, bool ReplayEveryAction, string[] Citations, Case[] Cases);
    private sealed record Case(string Id, string Code, bool Ice, int AdvancementCounters,
        int GripCount, int ExpectedDamage, int ExpectedTags, bool JackOut);
}

using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// Narrow headless evidence for the Slice 4.3 ability path. This intentionally
/// starts at a valid installed-card turn frame; it is not MATCH-401A setup or
/// a full-game setup/mulligan outcome.
/// </summary>
public sealed class Slice43AbilityProofTests
{
    private const string BasicFixtureId = "sim-slice45-batch4-basic";
    private const string ExpandedFixtureId = "sim-slice45-batch4-expanded";

    [Theory]
    [InlineData(BasicFixtureId)]
    [InlineData(ExpandedFixtureId)]
    public async Task FullGameAgentUsesTeleworkAbilityThroughProductionTurnBoundary(string fixtureId)
    {
        SimulationFixtureDocument fixture = LoadFixtures().Get(fixtureId);
        BehaviorCatalog behaviorCatalog = GatewayBehaviorCatalog.Create();
        fixture.ValidateOrThrow(behaviorCatalog: behaviorCatalog);

        GameState installed = BuildInstalledTeleworkState(fixture, behaviorCatalog);
        PlayerId runner = installed.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        CardInstanceId sourceCardId = new("card:sim-slice43-telework");

        // The real CardInstalled occurrence loads Telework's declared pool;
        // the proof never hand-injects the 9-credit result.
        GameState loaded = GameEngine.BeginResolution(
            installed,
            new EffectResolutionFrame(
                runner,
                new CostBundle(),
                new EffectBundle(new GameplayOccurrenceEffect(
                    OccurrenceKind.CardInstalled,
                    runner,
                    sourceCardId))),
            behaviorCatalog);
        Assert.Equal(9, HostedCount(loaded, sourceCardId));

        TurnFrame parent = new(
            1,
            ResolutionBehaviorIds.Turn,
            PlayerSide.Runner,
            loaded.Turn.TurnNumber,
            TurnFrameStage.AwaitingAction);
        GameState prompt = GameEngine.BeginResolution(loaded, parent, behaviorCatalog);
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        Assert.Equal(PlayerSide.Runner, prompt.Turn.ActiveSide);
        Assert.Equal(TurnStep.RunnerAction, prompt.Turn.Step);
        Assert.Equal(TurnFrameStage.AwaitingAction, Assert.IsType<TurnFrame>(prompt.ResolutionStack[^1]).Stage);
        Assert.Equal(DecisionKind.TurnAction, decision.Kind);

        LegalAction issuedAbility = Assert.Single(
            decision.LegalActions,
            action => action.Kind == ActionKind.UseAbility
                && Assert.IsType<AbilityActionParameters>(action.Parameters).SourceCardId == sourceCardId);
        Assert.Contains(decision.LegalActions, action => action.Kind == ActionKind.GainCredit);
        Assert.Contains(decision.LegalActions, action => action.Kind == ActionKind.StartRun);

        Observation observation = ObservationBuilder.Build(prompt, PlayerSide.Runner, behaviorCatalog);
        Assert.Equal(decision.Id.Value, observation.Decision?.Id.Value);
        Assert.Contains(observation.LegalActions, action => action.Kind == ActionKind.UseAbility);
        Assert.Contains(observation.LegalActions, action => action.Kind == ActionKind.GainCredit);
        Assert.Contains(observation.LegalActions, action => action.Kind == ActionKind.StartRun);

        // Save/load is part of the decision boundary. Rebuild the observation
        // from the restored state before asking the v3 agent to choose.
        string promptJson = EngineJson.Serialize(prompt);
        GameState restoredPrompt = EngineJson.DeserializeState(promptJson, behaviorCatalog);
        Assert.Equal(DeterministicHash.ForState(prompt), DeterministicHash.ForState(restoredPrompt));
        Observation restoredObservation = ObservationBuilder.Build(
            restoredPrompt,
            PlayerSide.Runner,
            behaviorCatalog);
        Assert.Equal(
            CanonicalJson.Serialize(observation),
            CanonicalJson.Serialize(restoredObservation));

        FullGameContractAgent agent = new(PlayerSide.Runner);
        ActionId selected = await agent.ChooseActionAsync(restoredObservation);
        PendingDecision restoredDecision = Assert.IsType<PendingDecision>(restoredPrompt.PendingDecision);
        LegalAction selectedIssuedAction = Assert.Single(
            restoredDecision.LegalActions,
            action => action.Id == selected);
        Assert.Equal(ActionKind.UseAbility, selectedIssuedAction.Kind);
        Assert.Equal(sourceCardId, Assert.IsType<AbilityActionParameters>(selectedIssuedAction.Parameters).SourceCardId);
        Assert.Equal(issuedAbility.Id, selected);

        ApplyResult used = GameEngine.Apply(
            restoredPrompt,
            new ActionSubmission(
                restoredDecision.Owner,
                restoredDecision.Id,
                restoredDecision.Revision,
                selected),
            behaviorCatalog);
        Assert.True(used.Accepted, used.Rejection?.Message);
        Assert.NotNull(used.AcceptedAction);
        Assert.Equal(1, used.State.ActionSequence);
        Assert.InRange(used.State.ActionSequence, 1, fixture.ActionCap - 1);

        PlayerState runnerAfter = used.State.GetPlayer(runner);
        Assert.Equal(4, loaded.GetPlayer(runner).Clicks);
        Assert.Equal(3, runnerAfter.Clicks);
        Assert.Equal(8, runnerAfter.Credits);
        Assert.Equal(6, HostedCount(used.State, sourceCardId));

        HostedCreditPoolChangedEventPayload transfer = Assert.IsType<HostedCreditPoolChangedEventPayload>(
            Assert.Single(
                used.Events,
                @event => @event.Kind == EngineEventKinds.HostedCreditPoolChanged).Payload);
        Assert.Equal(HostedCreditPoolOperation.Take, transfer.Operation);
        Assert.Equal(sourceCardId, transfer.CardId);
        Assert.Equal(-3, transfer.Delta);
        Assert.Equal(6, transfer.CountAfter);
        Assert.Equal(runner, transfer.Recipient);

        TurnFactsState runnerFacts = Assert.IsType<TurnFactsState>(used.State.Turn.ThisTurnFacts);
        Assert.Contains(
            runnerFacts.UsedAbilityKeys,
            usage => usage.SourceCardId == sourceCardId && usage.AbilityKey == "telework-take");

        Observation after = ObservationBuilder.Build(used.State, PlayerSide.Runner, behaviorCatalog);
        Assert.DoesNotContain(
            after.LegalActions,
            action => action.Kind == ActionKind.UseAbility
                && Assert.IsType<AbilityActionParameters>(action.Parameters).SourceCardId == sourceCardId);
        Assert.DoesNotContain(
            used.State.PendingDecision?.LegalActions ?? [],
            action => action.Kind == ActionKind.UseAbility
                && Assert.IsType<AbilityActionParameters>(action.Parameters).SourceCardId == sourceCardId);

        string usedJson = EngineJson.Serialize(used.State);
        GameState restoredUsed = EngineJson.DeserializeState(usedJson, behaviorCatalog);
        Assert.Equal(DeterministicHash.ForState(used.State), DeterministicHash.ForState(restoredUsed));
        Assert.Equal(used.AcceptedAction!.ResultingStateHash, DeterministicHash.ForState(restoredUsed));
        Assert.Equal(
            CanonicalJson.Serialize(after),
            CanonicalJson.Serialize(ObservationBuilder.Build(
                restoredUsed,
                PlayerSide.Runner,
                behaviorCatalog)));
    }

    private static SimulationFixtureCatalog LoadFixtures()
    {
        string directory = Path.Combine(FindRepositoryRoot(), "data", "m4", "sim");
        return SimulationFixtureProvider.LoadDirectory(directory);
    }

    private static GameState BuildInstalledTeleworkState(
        SimulationFixtureDocument fixture,
        BehaviorCatalog behaviorCatalog)
    {
        GameState baseline = GameState.CreateNew(
            fixture.Seed,
            fixture.RulesVersion,
            fixture.CardDataVersion,
            behaviorCatalog.Version,
            behaviorCatalog.Fingerprint);
        CardInstanceId sourceCardId = new("card:sim-slice43-telework");
        ZoneState rig = baseline.GetZone(ZoneKind.RunnerRig);
        CardInstanceState card = new(
            sourceCardId,
            GatewayBatch2CardCodes.TeleworkContract,
            rig.Id,
            PlayerSide.Runner,
            faceUp: true,
            rezzed: false);
        ZoneState[] zones = baseline.Zones
            .Select(candidate => candidate.Id == rig.Id
                ? new ZoneState(candidate.Id, candidate.Kind, candidate.OwnerSide, candidate.ServerId, [sourceCardId])
                : candidate)
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
            discardPhaseEnded: false,
            installedCardIds: [sourceCardId]);
        TurnHistoryState history = new(1, null, null, facts, null);
        RulesState defaults = RulesState.CreateDefault(baseline.Players);
        RulesState rules = new(
            defaults.Version,
            defaults.Players,
            defaults.Programs,
            [],
            defaults.LingeringEffects,
            defaults.Triggers,
            defaults.Interrupts);

        return new GameState(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            baseline.Players,
            [card],
            zones,
            baseline.Servers,
            new TurnState(
                PlayerSide.Runner,
                TurnPhase.Runner,
                baseline.Turn.TurnNumber,
                TurnStep.RunnerAction,
                history),
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            rulesState: rules,
            behaviorRegistryVersion: behaviorCatalog.Version,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint);
    }

    private static int HostedCount(GameState state, CardInstanceId cardId) =>
        state.RulesState?.Counters
            .SingleOrDefault(counter =>
                counter.CardId == cardId
                && counter.Purpose == CardCounterPurpose.HostedCredit)
            ?.Count
        ?? 0;

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Netrunner.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate the Netrunner repository root.");
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;

using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

/// <summary>
/// Executable SCN-401 evidence for the approach/encounter and run-event
/// checkpoints.  The fixture is a test-owned setup description; all yielded
/// decisions and state transitions below still come from the Core reducer.
/// </summary>
public sealed class Scn401TimingFixtureTests
{
    [Fact]
    public void CheckedInFixtureIsVersionedAndDescribesTheSlice45TimingScope()
    {
        Scn401TimingFixtureDocument fixture = Scn401TimingFixtureDocument.Load();

        fixture.Validate();
        Assert.Equal("SCN-401", fixture.ScenarioId);
        Assert.Equal(45041UL, fixture.Seed);
        Assert.Equal(EngineCompatibility.StateSchemaVersion, fixture.EngineStateSchemaVersion);
        Assert.Equal(fixture.Catalog.Version, fixture.BehaviorRegistryVersion);
        Assert.Equal(
            [
                "approach-anoetic-void",
                "approach-manegarm-skunkworks",
                "encounter-funhouse",
                "encounter-ansel-bioroid-repeated",
                "encounter-ansel-installed-card-target",
                "encounter-ansel-normal-install",
                "encounter-ansel-restriction-agenda",
                "encounter-ansel-restriction-asset",
                "encounter-bran-inward-install",
                "encounter-mayfly-used-run-end",
                "encounter-mayfly-unused-run-end",
                "jailbreak-hq-success-draw-access",
                "jailbreak-rnd-success-draw-access",
            ],
            fixture.Cases.Select(scenario => scenario.Id));
        Assert.Empty(fixture.Excluded);
    }

    [Fact]
    public void FixtureParserRejectsMissingAndUnknownFields()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Scenarios", "scn-401-timing.json");
        string source = File.ReadAllText(path);

        JsonObject missingCatalog = JsonNode.Parse(source)!.AsObject();
        missingCatalog.Remove("catalog");
        Assert.Throws<JsonException>(
            () => Scn401TimingFixtureDocument.Parse(missingCatalog.ToJsonString(EngineJson.Options)));

        JsonObject unknownField = JsonNode.Parse(source)!.AsObject();
        unknownField["unexpected"] = true;
        Assert.Throws<JsonException>(
            () => Scn401TimingFixtureDocument.Parse(unknownField.ToJsonString(EngineJson.Options)));

        JsonObject malformedReaction = JsonNode.Parse(source)!.AsObject();
        malformedReaction["cases"]![0]!["reaction"]!["triggerId"] = "";
        Scn401TimingFixtureDocument malformed = Scn401TimingFixtureDocument.Parse(
            malformedReaction.ToJsonString(EngineJson.Options));
        Assert.Contains(
            malformed.CollectProblems(),
            problem => problem.Contains("triggerId", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("approach-anoetic-void")]
    [InlineData("approach-manegarm-skunkworks")]
    [InlineData("encounter-funhouse")]
    [InlineData("encounter-ansel-bioroid-repeated")]
    [InlineData("encounter-ansel-installed-card-target")]
    [InlineData("encounter-ansel-normal-install")]
    [InlineData("encounter-ansel-restriction-agenda")]
    [InlineData("encounter-ansel-restriction-asset")]
    [InlineData("encounter-bran-inward-install")]
    [InlineData("encounter-mayfly-used-run-end")]
    [InlineData("encounter-mayfly-unused-run-end")]
    [InlineData("jailbreak-hq-success-draw-access")]
    [InlineData("jailbreak-rnd-success-draw-access")]
    public void EngineTraceMatchesTheFixtureAndRoundTripsEveryYieldedBoundary(string caseId)
    {
        Scn401TimingFixtureDocument fixture = Scn401TimingFixtureDocument.Load();
        fixture.Validate();
        Scn401TimingCase scenario = Assert.Single(fixture.Cases, value => value.Id == caseId);
        Scn401RunEvidence evidence = DriveCase(fixture, scenario);

        RunTimingTrace trace = RunTimingTraceValidator.Validate(evidence.Events);
        Assert.True(trace.IsValid, string.Join("; ", trace.Problems));
        Assert.Empty(trace.Problems);
        Assert.Equal(
            scenario.YieldedBoundaries
                .Where(boundary => boundary.Name != "anoetic-hidden-cost-choice")
                .Select(boundary => boundary.Name)
                .OrderBy(value => value, StringComparer.Ordinal),
            evidence.SaveLoadedBoundaries.OrderBy(value => value, StringComparer.Ordinal));

        AssertExpectedEventOrder(scenario, evidence.Events);
        AssertReactionCheckpoint(scenario, evidence.ReactionState);
        Assert.NotNull(evidence.FinalState);
        Assert.Null(evidence.FinalState.RunState);
        Assert.Null(evidence.FinalState.PendingDecision);
        StateInvariantValidator.Validate(evidence.FinalState, evidence.Catalog, evidence.RuntimeCatalog);
        AssertCaseOutcome(scenario, evidence);
    }

    [Fact]
    public void AnoeticHiddenHqCostIdsExistOnlyForTheCorpChooserAtTheSavedChoiceBoundary()
    {
        Scn401TimingFixtureDocument fixture = Scn401TimingFixtureDocument.Load();
        fixture.Validate();
        Scn401TimingCase scenario = Assert.Single(
            fixture.Cases,
            value => value.Id == "approach-anoetic-void");
        BehaviorCatalog catalog = Scn401TimingFixtureBuilder.CreateBehaviorCatalog(scenario);
        RuntimeCardCatalog runtime = Scn401TimingFixtureBuilder.CreateRuntimeCatalog(scenario);
        GameState initial = Scn401TimingFixtureBuilder.BuildState(fixture, scenario, catalog);
        CardInstanceState fixtureIce = initial.Cards.Single(card =>
            initial.Zones.Single(zone => zone.Id == card.ZoneId).Kind == ZoneKind.ServerIce);
        Assert.Equal(scenario.Id == "encounter-funhouse", fixtureIce.Rezzed);
        GameEngine.RunStartResult start = GameEngine.BeginRunWithEvents(
            initial,
            Scn401TimingFixtureBuilder.RemoteId(initial),
            catalog,
            runtime);
        GameState triggerState = AdvanceUntil(
            start.State,
            catalog,
            runtime,
            static state => state.PendingDecision?.Kind == DecisionKind.TriggerChoice,
            out _);

        Assert.Equal(PlayerSide.Corp, SideOf(triggerState, triggerState.PendingDecision!.Owner));
        LegalAction useTrigger = Assert.Single(
            triggerState.PendingDecision.LegalActions,
            action => action.Kind == ActionKind.UseTrigger);
        ApplyResult used = ApplyAndRoundTrip(triggerState, useTrigger, catalog, runtime);

        Assert.Equal(DecisionKind.BehaviorChoice, used.State.PendingDecision?.Kind);
        Assert.Equal(PlayerSide.Corp, SideOf(used.State, used.State.PendingDecision!.Owner));
        Scn401CardPlacement[] hiddenCards = scenario.Cards
            .Where(card => card.Zone == ZoneKind.CorpHand)
            .ToArray();
        Assert.Equal(2, hiddenCards.Length);
        CardInstanceId[] hiddenIds = hiddenCards
            .Select(card => new CardInstanceId(card.Id))
            .ToArray();

        string runnerObservation = EngineJson.Serialize(
            ObservationBuilder.Build(used.State, PlayerSide.Runner, catalog));
        string corpObservation = EngineJson.Serialize(
            ObservationBuilder.Build(used.State, PlayerSide.Corp, catalog));
        Assert.All(hiddenIds, cardId =>
            Assert.DoesNotContain(cardId.Value, runnerObservation, StringComparison.Ordinal));
        Assert.All(hiddenIds, cardId =>
            Assert.Contains(cardId.Value, corpObservation, StringComparison.Ordinal));

        LegalAction payment = Assert.Single(
            used.State.PendingDecision.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters parameters
                && parameters.ChoiceKey == "anoetic-void-approach-choice"
                && parameters.OptionKeys.SequenceEqual(["pay-and-trash-hq"]));
        BehaviorChoiceParameters paymentParameters =
            Assert.IsType<BehaviorChoiceParameters>(payment.Parameters);
        Assert.Equal(hiddenIds, paymentParameters.CardIds);
        Assert.Equal(
            EngineJson.Serialize(used.State),
            EngineJson.Serialize(EngineJson.DeserializeState(
                EngineJson.Serialize(used.State),
                catalog,
                runtime)));

        ApplyResult ended = ApplyAndRoundTrip(used.State, payment, catalog, runtime);
        Assert.Null(ended.State.RunState);
        Assert.Contains(ended.Events, value => value.Kind == EngineEventKinds.RunEnded);
        Assert.DoesNotContain(ended.Events, value => value.Kind == EngineEventKinds.RunSuccessful);
    }

    [Theory]
    [InlineData("jailbreak-hq-success-draw-access", "jailbreak-hq-breach-candidates")]
    [InlineData("jailbreak-rnd-success-draw-access", "jailbreak-rnd-breach-candidates")]
    public void JailbreakHiddenBreachCandidatesRemainCorpOnlyAtTheRunnerSelectionBoundary(
        string caseId,
        string boundaryName)
    {
        Scn401TimingFixtureDocument fixture = Scn401TimingFixtureDocument.Load();
        fixture.Validate();
        Scn401TimingCase scenario = Assert.Single(fixture.Cases, value => value.Id == caseId);
        Scn401RunEvidence evidence = DriveCase(fixture, scenario);
        Assert.True(evidence.BoundaryStates.TryGetValue(boundaryName, out GameState? candidateState));
        Assert.NotNull(candidateState);
        Assert.Equal(DecisionKind.RunTiming, candidateState.PendingDecision?.Kind);
        Assert.Equal(PlayerSide.Runner, SideOf(candidateState, candidateState.PendingDecision!.Owner));

        ZoneKind hiddenZone = scenario.TargetServer is "headquarters" or "hq"
            ? ZoneKind.CorpHand
            : ZoneKind.CorpDeck;
        CardInstanceId[] hiddenIds = scenario.Cards
            .Where(card => card.Zone == hiddenZone)
            .Select(card => new CardInstanceId(card.Id))
            .ToArray();
        Assert.Equal(2, hiddenIds.Length);

        string runnerObservation = EngineJson.Serialize(
            ObservationBuilder.Build(candidateState, PlayerSide.Runner, evidence.Catalog));
        string corpObservation = EngineJson.Serialize(
            ObservationBuilder.Build(candidateState, PlayerSide.Corp, evidence.Catalog));
        Assert.All(hiddenIds, cardId =>
            Assert.DoesNotContain(cardId.Value, runnerObservation, StringComparison.Ordinal));
        Assert.All(hiddenIds, cardId =>
        {
            if (hiddenZone == ZoneKind.CorpHand)
            {
                Assert.Contains(cardId.Value, corpObservation, StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain(cardId.Value, corpObservation, StringComparison.Ordinal);
            }
        });

        foreach (LegalAction action in candidateState.PendingDecision!.LegalActions)
        {
            string serializedAction = EngineJson.Serialize(action);
            Assert.All(hiddenIds, cardId =>
                Assert.DoesNotContain(cardId.Value, serializedAction, StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("encounter-ansel-normal-install", "ansel-normal-install-choice", "card:scn401-ansel-normal-install")]
    [InlineData("encounter-bran-inward-install", "bran-inward-install-choice", "card:scn401-bran-inward-target")]
    public void CorpInstallChoicesKeepHiddenSourceIdsOutOfRunnerObservations(
        string caseId,
        string boundaryName,
        string targetId)
    {
        Scn401TimingFixtureDocument fixture = Scn401TimingFixtureDocument.Load();
        fixture.Validate();
        Scn401TimingCase scenario = Assert.Single(fixture.Cases, value => value.Id == caseId);
        Scn401RunEvidence evidence = DriveCase(fixture, scenario);
        Assert.True(evidence.BoundaryStates.TryGetValue(boundaryName, out GameState? choiceState));
        Assert.NotNull(choiceState);
        Assert.Equal(PlayerSide.Corp, SideOf(choiceState, choiceState.PendingDecision!.Owner));

        string runnerObservation = EngineJson.Serialize(
            ObservationBuilder.Build(choiceState, PlayerSide.Runner, evidence.Catalog));
        string corpObservation = EngineJson.Serialize(
            ObservationBuilder.Build(choiceState, PlayerSide.Corp, evidence.Catalog));
        Assert.DoesNotContain(targetId, runnerObservation, StringComparison.Ordinal);
        Assert.Contains(targetId, corpObservation, StringComparison.Ordinal);
        Assert.Contains(
            choiceState.PendingDecision.LegalActions,
            action => EngineJson.Serialize(action).Contains(targetId, StringComparison.Ordinal));
    }

    private static Scn401RunEvidence DriveCase(
        Scn401TimingFixtureDocument fixture,
        Scn401TimingCase scenario)
    {
        BehaviorCatalog catalog = Scn401TimingFixtureBuilder.CreateBehaviorCatalog(scenario);
        RuntimeCardCatalog runtime = Scn401TimingFixtureBuilder.CreateRuntimeCatalog(scenario);
        GameState initial = Scn401TimingFixtureBuilder.BuildState(fixture, scenario, catalog);
        GameEngine.RunStartResult start = GameEngine.BeginRunWithEvents(
            initial,
            Scn401TimingFixtureBuilder.TargetServerId(initial, scenario.TargetServer),
            catalog,
            runtime,
            initiatingSource: Scn401TimingFixtureBuilder.TryInitiatingSource(scenario));
        var events = new List<EngineEvent>(start.Events);
        var saveLoadedBoundaries = new HashSet<string>(StringComparer.Ordinal);
        var boundaryStates = new Dictionary<string, GameState>(StringComparer.Ordinal);
        var observedBoundaries = new HashSet<string>(StringComparer.Ordinal);
        GameState state = start.State;
        GameState? reactionState = null;
        int iterations = 0;

        while (state.PendingDecision is not null)
        {
            Assert.True(++iterations < 64, $"SCN-401 case '{scenario.Id}' did not make progress.");
            RecordBoundary(
                scenario,
                state,
                saveLoadedBoundaries,
                boundaryStates,
                observedBoundaries,
                ref reactionState);

            PendingDecision decision = state.PendingDecision;
            LegalAction action = SelectAction(scenario, state, decision, catalog);
            ApplyResult result = ApplyAndRoundTrip(state, action, catalog, runtime);
            Assert.True(result.Accepted, result.Rejection?.Message);
            events.AddRange(result.Events);
            state = result.State;
        }

        return new Scn401RunEvidence(
            catalog,
            runtime,
            EngineEventValidator.FreezeBatch(events),
            reactionState ?? throw new Xunit.Sdk.XunitException(
                $"Case '{scenario.Id}' never yielded its reaction checkpoint. "
                + $"events={string.Join(',', events.Select(value => value.Kind))}; "
                + $"pending={state.PendingDecision?.Kind}; step={state.RunState?.Step}; "
                + $"run={state.RunState?.Successful}/{state.RunState?.EndReason}"),
            state,
            saveLoadedBoundaries,
            boundaryStates);
    }

    private static GameState AdvanceUntil(
        GameState state,
        BehaviorCatalog catalog,
        RuntimeCardCatalog? runtime,
        Func<GameState, bool> predicate,
        out IReadOnlyList<EngineEvent> events)
    {
        var emitted = new List<EngineEvent>();
        int iterations = 0;
        while (!predicate(state))
        {
            Assert.True(++iterations < 64, "SCN-401 pre-reaction driver did not make progress.");
            PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction action = decision.LegalActions.FirstOrDefault(candidate =>
                candidate.Kind == ActionKind.PassPriority)
                ?? decision.LegalActions.FirstOrDefault(candidate =>
                    candidate.Kind == ActionKind.ContinueRun)
                ?? throw new Xunit.Sdk.XunitException(
                    $"No deterministic pre-reaction action exists at '{decision.Kind}'.");
            ApplyResult result = ApplyAndRoundTrip(state, action, catalog, runtime);
            Assert.True(result.Accepted, result.Rejection?.Message);
            emitted.AddRange(result.Events);
            state = result.State;
        }

        events = emitted;
        return state;
    }

    private static void RecordBoundary(
        Scn401TimingCase scenario,
        GameState state,
        HashSet<string> saveLoadedBoundaries,
        Dictionary<string, GameState> boundaryStates,
        HashSet<string> observedBoundaries,
        ref GameState? reactionState)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        PlayerSide ownerSide = SideOf(state, decision.Owner);
        Scn401YieldBoundary? boundary = scenario.YieldedBoundaries.FirstOrDefault(candidate =>
            !observedBoundaries.Contains(candidate.Name)
            && candidate.DecisionKind == decision.Kind
            && candidate.RunStep == state.RunState?.Step
            && candidate.OwnerSide == ownerSide);
        if (boundary is null)
        {
            return;
        }

        observedBoundaries.Add(boundary.Name);
        if (boundary.Name != "anoetic-hidden-cost-choice")
        {
            saveLoadedBoundaries.Add(boundary.Name);
            boundaryStates[boundary.Name] = state;
        }

        if (boundary.Name is "optional-anoetic-trigger"
            or "mandatory-manegarm-choice"
            or "encounter-reaction"
            or "ansel-bioroid-first-use"
            or "ansel-target-encounter-window"
            or "ansel-normal-install-encounter-window"
            or "ansel-restriction-agenda-first-bioroid"
            or "ansel-restriction-asset-first-bioroid"
            or "bran-inward-encounter-window"
            or "mayfly-used-break"
            or "mayfly-unused-encounter"
            or "jailbreak-hq-breach-candidates"
            or "jailbreak-rnd-breach-candidates")
        {
            reactionState = state;
        }
    }

    private static LegalAction SelectAction(
        Scn401TimingCase scenario,
        GameState state,
        PendingDecision decision,
        BehaviorCatalog catalog)
    {
        if (decision.Kind == DecisionKind.TriggerChoice)
        {
            if (scenario.Id == "approach-anoetic-void")
            {
                return Assert.Single(
                    decision.LegalActions,
                    action => action.Kind == ActionKind.DeclineTrigger);
            }

            if (scenario.Id.StartsWith("jailbreak-", StringComparison.Ordinal))
            {
                return Assert.Single(
                    decision.LegalActions,
                    action => action.Kind == ActionKind.UseTrigger);
            }

            throw new Xunit.Sdk.XunitException(
                $"Unexpected trigger choice in case '{scenario.Id}'.");
        }

        if (decision.Kind == DecisionKind.BehaviorChoice)
        {
            string option = scenario.Id switch
            {
                "approach-manegarm-skunkworks" => "spend-clicks",
                "encounter-funhouse" => "take-tag",
                _ => throw new Xunit.Sdk.XunitException(
                    $"Unexpected behavior choice in case '{scenario.Id}'."),
            };
            return Assert.Single(
                decision.LegalActions,
                action => action.Parameters is BehaviorChoiceParameters parameters
                    && parameters.OptionKeys.SequenceEqual([option]));
        }

        if (decision.Kind == DecisionKind.IceSubroutineChoice)
        {
            if (scenario.Id == "encounter-funhouse")
            {
                return Assert.Single(
                    decision.LegalActions,
                    action => action.Kind == ActionKind.ResolveIceSubroutine
                        && action.Parameters is IceSubroutineChoiceParameters
                        {
                            Choice: IceSubroutineChoiceKind.TakeTag,
                        });
            }

            if (scenario.Id == "encounter-ansel-installed-card-target")
            {
                CardInstanceId target = new("card:scn401-ansel-runner-target");
                LegalAction? trash = decision.LegalActions.FirstOrDefault(
                    action => action.Kind == ActionKind.ResolveIceSubroutine
                        && action.Parameters is IceSubroutineChoiceParameters
                        {
                            Choice: IceSubroutineChoiceKind.TrashInstalledRunnerCard,
                            CardId: var cardId,
                        }
                        && cardId == target);
                return trash
                    ?? Assert.Single(
                        decision.LegalActions,
                        action => action.Kind == ActionKind.ResolveIceSubroutine
                            && action.Parameters is IceSubroutineChoiceParameters
                            {
                                Choice: IceSubroutineChoiceKind.Decline,
                            });
            }

            if (scenario.Id is "encounter-ansel-normal-install" or "encounter-bran-inward-install")
            {
                CardInstanceId target = new(
                    scenario.Id == "encounter-ansel-normal-install"
                        ? "card:scn401-ansel-normal-install"
                        : "card:scn401-bran-inward-target");
                ServerId remote = Scn401TimingFixtureBuilder.RemoteId(state);
                return Assert.Single(
                    decision.LegalActions,
                    action => action.Kind == ActionKind.ResolveIceSubroutine
                        && action.Parameters is IceSubroutineChoiceParameters
                        {
                            Choice: IceSubroutineChoiceKind.InstallCard,
                            CardId: var cardId,
                            DestinationKind: InstallDestinationKind.ServerIce,
                            ServerId: var serverId,
                        }
                        && cardId == target
                        && serverId == remote);
            }

            throw new Xunit.Sdk.XunitException(
                $"Unexpected ICE subroutine choice in case '{scenario.Id}'.");
        }

        if (decision.Kind == DecisionKind.InstallChoice)
        {
            Assert.Equal("encounter-ansel-normal-install", scenario.Id);
            CardInstanceId target = new("card:scn401-ansel-normal-install");
            ServerId remote = Scn401TimingFixtureBuilder.RemoteId(state);
            return Assert.Single(
                decision.LegalActions,
                action => action.Kind == ActionKind.InstallCard
                    && action.Parameters is InstallCardParameters
                    {
                        CardId: var cardId,
                        DestinationKind: InstallDestinationKind.ServerIce,
                        ServerId: var serverId,
                        FinalizeSelection: true,
                    }
                    && cardId == target
                    && serverId == remote);
        }

        if (decision.Kind != DecisionKind.RunTiming)
        {
            throw new Xunit.Sdk.XunitException(
                $"Unexpected decision kind '{decision.Kind}' in case '{scenario.Id}'.");
        }

        if (scenario.Id == "encounter-ansel-bioroid-repeated"
            && state.RunState?.Step == RunTimingStep.EncounterPaidWindow
            && SideOf(state, decision.Owner) == PlayerSide.Runner)
        {
            return decision.LegalActions.FirstOrDefault(
                       action => action.Kind == ActionKind.UseBioroidAbility)
                ?? Assert.Single(
                    decision.LegalActions,
                    action => action.Kind == ActionKind.PassPriority);
        }

        if (scenario.Id.StartsWith("encounter-ansel-restriction-", StringComparison.Ordinal)
            && state.RunState?.Step == RunTimingStep.EncounterPaidWindow
            && SideOf(state, decision.Owner) == PlayerSide.Runner)
        {
            int brokenCount = state.RunState.Encounter?.Subroutines.Count(subroutine => subroutine.Broken) ?? 0;
            if (brokenCount < 2)
            {
                return decision.LegalActions.FirstOrDefault(
                           action => action.Kind == ActionKind.UseBioroidAbility
                               && action.Parameters is RunBioroidAbilityParameters parameters
                               && parameters.SubroutineId == state.RunState.Encounter!.Subroutines
                                   .Single(subroutine => subroutine.Ordinal == brokenCount).Id)
                    ?? throw new Xunit.Sdk.XunitException(
                        $"Ansel restriction case '{scenario.Id}' did not expose bioroid target ordinal {brokenCount}.");
            }

            return Assert.Single(
                decision.LegalActions,
                action => action.Kind == ActionKind.PassPriority);
        }

        if (scenario.Id == "encounter-mayfly-used-run-end"
            && state.RunState?.Step == RunTimingStep.EncounterPaidWindow
            && SideOf(state, decision.Owner) == PlayerSide.Runner)
        {
            if (state.RunState.Encounter?.Subroutines.All(
                    subroutine => subroutine.Broken || subroutine.Resolved) == true)
            {
                return Assert.Single(
                    decision.LegalActions,
                    action => action.Kind == ActionKind.PassPriority);
            }

            LegalAction? breakAction = decision.LegalActions.SingleOrDefault(
                action => action.Kind == ActionKind.BreakSubroutines);
            if (breakAction is null)
            {
                LegalAction? pumpAction = decision.LegalActions.SingleOrDefault(
                    action => action.Kind == ActionKind.PumpBreaker);
                if (pumpAction is not null)
                {
                    return pumpAction;
                }
                return Assert.Single(
                    decision.LegalActions,
                    action => action.Kind == ActionKind.PassPriority);
            }

            return breakAction;
        }

        if (scenario.Id == "encounter-mayfly-unused-run-end"
            && state.RunState?.Step == RunTimingStep.EncounterPaidWindow
            && SideOf(state, decision.Owner) == PlayerSide.Runner)
        {
            return Assert.Single(
                decision.LegalActions,
                action => action.Kind == ActionKind.PassPriority);
        }

        if (scenario.Kind == Scn401TimingCaseKind.Encounter
            && state.RunState?.Step == RunTimingStep.ApproachPaidWindow
            && SideOf(state, decision.Owner) == PlayerSide.Corp)
        {
            LegalAction? rez = decision.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.RezCard)
                ?? decision.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.RezIce);
            if (rez is not null)
            {
                return rez;
            }
        }

        if (state.RunState?.Step == RunTimingStep.JackOutDecision)
        {
            return Assert.Single(
                decision.LegalActions,
                action => action.Kind == ActionKind.ContinueRun);
        }

        if (state.RunState?.Step == RunTimingStep.BreachCandidateSelection)
        {
            return decision.LegalActions.First(
                action => action.Kind == ActionKind.ChooseBreachCandidate);
        }

        if (state.RunState?.Step == RunTimingStep.BreachAccessAbility)
        {
            return Assert.Single(
                decision.LegalActions,
                action => action.Kind == ActionKind.NoAccessAction);
        }

        return Assert.Single(
            decision.LegalActions,
            action => action.Kind == ActionKind.PassPriority);
    }

    private static ApplyResult ApplyAndRoundTrip(
        GameState state,
        LegalAction action,
        BehaviorCatalog catalog,
        RuntimeCardCatalog? runtime = null)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        string json = EngineJson.Serialize(state);
        GameState restored = runtime is null
            ? EngineJson.DeserializeState(json, catalog)
            : EngineJson.DeserializeState(json, catalog, runtime);
        Assert.Equal(json, EngineJson.Serialize(restored));
        ActionSubmission submission =
            new(decision.Owner, decision.Id, decision.Revision, action.Id);
        ApplyResult left = runtime is null
            ? GameEngine.Apply(state, submission, catalog)
            : GameEngine.Apply(state, submission, catalog, runtime);
        ApplyResult right = runtime is null
            ? GameEngine.Apply(restored, submission, catalog)
            : GameEngine.Apply(restored, submission, catalog, runtime);
        Assert.Equal(left.Status, right.Status);
        Assert.Equal(EngineJson.Serialize(left.State), EngineJson.Serialize(right.State));
        Assert.Equal(DeterministicHash.ForEvents(left.Events), DeterministicHash.ForEvents(right.Events));
        return left;
    }

    private static void AssertReactionCheckpoint(
        Scn401TimingCase scenario,
        GameState state)
    {
        if (scenario.Id.StartsWith("jailbreak-", StringComparison.Ordinal))
        {
            // Jailbreak's success draw is mandatory. Core resolves that
            // trigger immediately at the RunSuccessful checkpoint, so there
            // is no user-facing TriggerChoice yield to save. The first
            // observable boundary after the reaction is breach candidate
            // selection; its frozen access limit and modifier prove that the
            // mandatory success effect has run before access begins.
            PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
            Assert.Equal(scenario.Reaction.DecisionKind, decision.Kind);
            Assert.Equal(scenario.Reaction.OwnerSide, SideOf(state, decision.Owner));
            Assert.Equal(scenario.Reaction.RunStep, state.RunState?.Step);
            Assert.Equal(2, state.RunState?.RandomAccessLimit);
            Assert.Contains(
                state.RunState?.BreachAccessModifiers ?? [],
                modifier => modifier.Delta == 1
                    && modifier.ReasonKey == "jailbreak-additional-access");
            Assert.DoesNotContain(state.ResolutionStack, frame => frame is EffectCheckpointFrame);
            return;
        }

        if (scenario.Id.StartsWith("encounter-ansel-", StringComparison.Ordinal)
            || scenario.Id == "encounter-bran-inward-install"
            || scenario.Id is "encounter-mayfly-used-run-end"
                or "encounter-mayfly-unused-run-end")
        {
            PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
            Assert.Equal(DecisionKind.RunTiming, decision.Kind);
            Assert.Equal(PlayerSide.Runner, SideOf(state, decision.Owner));
            Assert.Equal(RunTimingStep.EncounterPaidWindow, state.RunState?.Step);
            RunEncounterState encounter = Assert.IsType<RunEncounterState>(state.RunState?.Encounter);
            Assert.Equal(RunEncounterStatus.Initialized, encounter.Status);
            if (scenario.Id.StartsWith("encounter-ansel-", StringComparison.Ordinal)
                || scenario.Id == "encounter-bran-inward-install")
            {
                Assert.Equal(3, encounter.Subroutines.Count);
                Assert.Equal(3, decision.LegalActions.Count(action => action.Kind == ActionKind.UseBioroidAbility));
            }
            else
            {
                Assert.Single(encounter.Subroutines);
                Assert.Contains(decision.LegalActions, action => action.Kind == ActionKind.BreakSubroutines);
            }

            return;
        }

        Scn401ReactionExpectation expectation = scenario.Reaction;
        Assert.Equal(expectation.DecisionKind, state.PendingDecision?.Kind);
        Assert.Equal(expectation.RunStep, state.RunState?.Step);
        Assert.Equal(expectation.OwnerSide, SideOf(state, state.PendingDecision!.Owner));
        EffectCheckpointFrame checkpoint = Assert.Single(
            state.ResolutionStack.OfType<EffectCheckpointFrame>(),
            frame => frame.Occurrence.Kind == expectation.Occurrence);
        Assert.Contains(
            checkpoint.TriggerQueue,
            invocation => invocation.TriggerKey == expectation.TriggerId
                && invocation.Mode == expectation.Mode);

        GameplayOccurrenceEffect occurrence =
            Assert.IsType<GameplayOccurrenceEffect>(checkpoint.Occurrence.Effect);
        if (expectation.Occurrence == OccurrenceKind.IceEncountered)
        {
            Assert.Equal(new CardInstanceId(scenario.SourceCardId), checkpoint.Occurrence.SourceCardId);
            Assert.Equal(new CardInstanceId(scenario.SourceCardId), occurrence.SubjectCardId);
        }
        else if (expectation.Occurrence == OccurrenceKind.RunSuccessful)
        {
            Assert.Equal(new CardInstanceId(scenario.SourceCardId), checkpoint.Occurrence.SourceCardId);
            Assert.Null(occurrence.SubjectCardId);
        }
        else
        {
            Assert.Null(checkpoint.Occurrence.SourceCardId);
            Assert.Null(occurrence.SubjectCardId);
        }
    }

    private static void AssertExpectedEventOrder(
        Scn401TimingCase scenario,
        IReadOnlyList<EngineEvent> events)
    {
        int previous = -1;
        foreach (string marker in scenario.ExpectedEventOrder)
        {
            int current = FindEvent(events, marker, previous + 1);
            Assert.True(current > previous, $"Expected marker '{marker}' after index {previous}.");
            previous = current;
        }

        if (scenario.Id is not ("approach-anoetic-void" or "approach-manegarm-skunkworks" or "encounter-funhouse")
            && !scenario.Id.StartsWith("jailbreak-", StringComparison.Ordinal))
        {
            return;
        }

        int occurrence = FindEvent(
            events,
            scenario.Reaction.Occurrence == OccurrenceKind.ServerApproached
                ? "server-approached"
                : scenario.Reaction.Occurrence == OccurrenceKind.RunSuccessful
                    ? "run-successful"
                    : "encounter-initialized",
            0);
        int continuation = FindEvent(
            events,
            scenario.Reaction.Continuation,
            occurrence + 1);
        Assert.True(continuation > occurrence);

        if (scenario.Reaction.MustNotOpenPaidWindow)
        {
            Assert.DoesNotContain(
                events.Skip(occurrence + 1).Take(continuation - occurrence - 1),
                value => value.Kind == EngineEventKinds.RunTimingWindowOpened);
        }
        else
        {
            Assert.True(
                IsWindow(events[continuation], RunTimingStep.EncounterPaidWindow),
                "The existing encounter paid window must open as the reaction continuation.");
        }
    }

    private static int FindEvent(
        IReadOnlyList<EngineEvent> events,
        string marker,
        int start)
    {
        for (int index = start; index < events.Count; index++)
        {
            EngineEvent value = events[index];
            bool match = marker switch
            {
                "server-approached" => value.Kind == EngineEventKinds.ServerApproached,
                "run-successful" => value.Kind == EngineEventKinds.RunSuccessful,
                "run-event-played" => value.Kind == EngineEventKinds.RunEventPlayed,
                "run-initiated" => value.Kind == EngineEventKinds.RunInitiated,
                "run-ended" => value.Kind == EngineEventKinds.RunEnded,
                "breach-started" => value.Kind == EngineEventKinds.BreachStarted,
                "card-accessed" => value.Kind == EngineEventKinds.CardAccessed,
                "encounter-completed" => value.Kind == EngineEventKinds.EncounterCompleted,
                "card-installed" => value.Kind == EngineEventKinds.CardInstalled,
                "subroutines-broken" => value.Kind == EngineEventKinds.SubroutinesBroken,
                "bioroid-ability-used" => value.Kind == EngineEventKinds.BioroidAbilityUsed,
                "mayfly-run-end-trash" => value.Kind == EngineEventKinds.CardMoved
                    && value.Payload is CardMovedEventPayload moved
                    && moved.CardId.Value.Contains("mayfly", StringComparison.Ordinal),
                "encounter-initialized" => value.Kind == EngineEventKinds.EncounterInitialized,
                "run-timing-window-opened:breachCandidateSelection" => IsWindow(value, RunTimingStep.BreachCandidateSelection),
                "run-timing-window-opened:afterMovePaidWindow" => IsWindow(value, RunTimingStep.AfterMovePaidWindow),
                "run-timing-window-opened:encounterPaidWindow" => IsWindow(value, RunTimingStep.EncounterPaidWindow),
                _ => false,
            };
            if (match)
            {
                return index;
            }
        }

        throw new Xunit.Sdk.XunitException($"Event marker '{marker}' was not found.");
    }

    private static bool IsWindow(EngineEvent value, RunTimingStep step) =>
        value.Kind == EngineEventKinds.RunTimingWindowOpened
        && value.Payload is RunTimingWindowOpenedEventPayload payload
        && payload.Step == step;

    private static PlayerSide SideOf(GameState state, PlayerId playerId) => state.GetPlayer(playerId).Side;

    private static void AssertCaseOutcome(
        Scn401TimingCase scenario,
        Scn401RunEvidence evidence)
    {
        GameState final = evidence.FinalState;
        if (scenario.Id == "encounter-funhouse")
        {
            Assert.Single(evidence.Events, value => value.Kind == EngineEventKinds.EncounterInitialized);
            Assert.Single(evidence.Events, value => value.Kind == EngineEventKinds.EncounterCompleted);
            Assert.Equal(
                2,
                final.RulesState!.Players.Single(player => player.PlayerId == final.Players.Single(
                    candidate => candidate.Side == PlayerSide.Runner).Id).Tags);
            Assert.Equal(
                2,
                evidence.Events.Count(value => value.Kind == EngineEventKinds.TagsAdjusted));
            return;
        }

        if (scenario.Id == "encounter-ansel-bioroid-repeated")
        {
            Assert.Equal(1, final.Players.Single(player => player.Side == PlayerSide.Runner).Clicks);
            BioroidAbilityUsedEventPayload[] uses = evidence.Events
                .Where(value => value.Kind == EngineEventKinds.BioroidAbilityUsed)
                .Select(value => Assert.IsType<BioroidAbilityUsedEventPayload>(value.Payload))
                .ToArray();
            Assert.Equal(3, uses.Length);
            Assert.Equal(3, uses.Select(use => use.SubroutineId).Distinct().Count());
            Assert.All(uses, use =>
            {
                Assert.Equal(GatewayBatch4BioroidBehaviorSpecs.BioroidBreakAbilityKey, use.AbilityKey);
                Assert.Equal(1, use.ClickCost);
            });
            Assert.DoesNotContain(
                evidence.Events,
                value => value.Kind == EngineEventKinds.SubroutinesBroken);
            return;
        }

        if (scenario.Id == "encounter-ansel-installed-card-target")
        {
            CardInstanceId target = new("card:scn401-ansel-runner-target");
            Assert.Contains(target, final.GetZone(ZoneKind.RunnerHeap).CardIds);
            Assert.DoesNotContain(target, final.GetZone(ZoneKind.RunnerRig).CardIds);
            Assert.Contains(
                evidence.Events,
                value => value.Kind == EngineEventKinds.CardMoved
                    && value.Payload is CardMovedEventPayload moved
                    && moved.CardId == target
                    && moved.ToZoneId == final.GetZone(ZoneKind.RunnerHeap).Id);
            return;
        }

        if (scenario.Id == "encounter-ansel-normal-install")
        {
            ServerState remote = final.Servers.Single(server => server.Kind == ServerKind.Remote);
            ZoneState ice = final.Zones.Single(zone => zone.Id == remote.IceZoneId);
            CardInstanceId ansel = new("card:scn401-ansel-normal");
            CardInstanceId installed = new("card:scn401-ansel-normal-install");
            Assert.Equal([ansel, installed], ice.CardIds);
            CardInstalledEventPayload installation = Assert.IsType<CardInstalledEventPayload>(
                evidence.Events.Single(value => value.Kind == EngineEventKinds.CardInstalled).Payload);
            Assert.Equal(installed, installation.CardId);
            Assert.Equal(1, installation.Cost);
            Assert.Equal(1, final.Players.Single(player => player.Side == PlayerSide.Corp).Credits);
            Assert.Equal(3, final.Players.Single(player => player.Side == PlayerSide.Corp).Clicks);
            Assert.False(final.GetCard(installed).FaceUp);
            Assert.False(final.GetCard(installed).Rezzed);
            return;
        }

        if (scenario.Id == "encounter-bran-inward-install")
        {
            ServerState remote = final.Servers.Single(server => server.Kind == ServerKind.Remote);
            ZoneState ice = final.Zones.Single(zone => zone.Id == remote.IceZoneId);
            CardInstanceId inner = new("card:scn401-bran-inner");
            CardInstanceId bran = new("card:scn401-bran");
            CardInstanceId installed = new("card:scn401-bran-inward-target");
            Assert.Equal([inner, installed, bran], ice.CardIds);
            CardInstalledEventPayload installation = Assert.IsType<CardInstalledEventPayload>(
                evidence.Events.Single(value => value.Kind == EngineEventKinds.CardInstalled).Payload);
            Assert.Equal(installed, installation.CardId);
            Assert.Equal(0, installation.Cost);
            Assert.Equal(0, final.Players.Single(player => player.Side == PlayerSide.Corp).Credits);
            Assert.Equal(3, final.Players.Single(player => player.Side == PlayerSide.Corp).Clicks);
            Assert.True(evidence.BoundaryStates.TryGetValue("bran-inward-install-choice", out GameState? choiceState));
            Assert.NotNull(choiceState);
            Assert.Equal(bran, choiceState.RunState?.CurrentIceId);
            Assert.Equal(1, choiceState.RunState?.CurrentIceIndex);
            Assert.Equal(bran, choiceState.RunState?.Encounter?.IceId);
            return;
        }
        if (scenario.Id.StartsWith("encounter-ansel-restriction-", StringComparison.Ordinal))
        {
            bool agenda = scenario.Id.EndsWith("-agenda", StringComparison.Ordinal);
            string boundaryName = $"{scenario.Id["encounter-".Length..]}-access";
            Assert.True(
                evidence.BoundaryStates.TryGetValue(boundaryName, out GameState? accessState),
                $"Observed boundaries: {string.Join(',', evidence.BoundaryStates.Keys)}; "
                + $"events: {string.Join(',', evidence.Events.Select(value => value.Kind))}");
            Assert.NotNull(accessState);
            Assert.Equal(RunTimingStep.BreachAccessAbility, accessState.RunState?.Step);
            Assert.Contains(
                accessState.RunState?.RunRestrictions ?? [],
                restriction => restriction.Kind == RunRestrictionKind.CannotStealOrTrashCorpCards);
            PendingDecision accessDecision = Assert.IsType<PendingDecision>(accessState.PendingDecision);
            Assert.DoesNotContain(accessDecision.LegalActions, action => action.Kind == ActionKind.StealAgenda);
            Assert.DoesNotContain(accessDecision.LegalActions, action => action.Kind == ActionKind.TrashAccessedCard);

            CardInstanceId target = new($"card:scn401-ansel-restriction-{(agenda ? "agenda" : "asset")}-target");
            ServerState remote = final.Servers.Single(server => server.Kind == ServerKind.Remote);
            ZoneState root = final.Zones.Single(zone => zone.Id == remote.RootZoneId);
            Assert.Contains(target, root.CardIds);
            Assert.DoesNotContain(target, final.GetZone(ZoneKind.CorpDiscard).CardIds);
            Assert.DoesNotContain(
                evidence.Events,
                value => value.Kind is EngineEventKinds.AgendaStolen or EngineEventKinds.AccessedCardTrashed);
            if (agenda)
            {
                Assert.Equal(0, final.Players.Single(player => player.Side == PlayerSide.Runner).AgendaPoints);
            }

            return;
        }

        if (scenario.Id is "encounter-mayfly-used-run-end" or "encounter-mayfly-unused-run-end")
        {
            bool used = scenario.Id == "encounter-mayfly-used-run-end";
            string suffix = used ? "used" : "unused";
            CardInstanceId mayfly = new($"card:scn401-mayfly-{suffix}");
            Assert.Equal(used, final.GetZone(ZoneKind.RunnerHeap).CardIds.Contains(mayfly));
            Assert.Equal(!used, final.GetZone(ZoneKind.RunnerRig).CardIds.Contains(mayfly));
            Assert.Equal(
                used,
                evidence.Events.Any(value => value.Kind == EngineEventKinds.CardMoved
                    && value.Payload is CardMovedEventPayload moved
                    && moved.CardId == mayfly
                    && moved.ToZoneId == final.GetZone(ZoneKind.RunnerHeap).Id));
            Assert.Equal(
                used,
                evidence.Events.Any(value => value.Kind == EngineEventKinds.LingeringEffectAdded));
            Assert.Equal(
                used,
                evidence.Events.Any(value => value.Kind == EngineEventKinds.LingeringEffectExpired));
            if (!used)
            {
                Assert.DoesNotContain(
                    evidence.Events,
                    value => value.Kind == EngineEventKinds.SubroutinesBroken);
            }

            return;
        }

        if (scenario.Id.StartsWith("jailbreak-", StringComparison.Ordinal))
        {
            ServerKind target = scenario.Id.Contains("-hq-", StringComparison.Ordinal)
                ? ServerKind.Headquarters
                : ServerKind.RAndD;
            ServerId targetId = final.Servers.Single(server => server.Kind == target).Id;
            RunInitiatedEventPayload initiated = Assert.IsType<RunInitiatedEventPayload>(
                evidence.Events.Single(value => value.Kind == EngineEventKinds.RunInitiated).Payload);
            Assert.Equal(targetId, initiated.ServerId);
            Assert.Contains(
                final.GetZone(ZoneKind.RunnerGrip).CardIds,
                id => id.Value.EndsWith("-draw", StringComparison.Ordinal));
            Assert.Equal(2, evidence.Events.Count(value => value.Kind == EngineEventKinds.CardAccessed));
            RunStartedBreachAccessCount(evidence.Events, expected: 2);
        }
    }

    private static void RunStartedBreachAccessCount(
        IReadOnlyList<EngineEvent> events,
        int expected)
    {
        BreachStartedEventPayload started = Assert.IsType<BreachStartedEventPayload>(
            events.Single(value => value.Kind == EngineEventKinds.BreachStarted).Payload);
        Assert.Equal(expected, started.RandomAccessLimit);
    }

    private sealed record Scn401RunEvidence(
        BehaviorCatalog Catalog,
        RuntimeCardCatalog RuntimeCatalog,
        IReadOnlyList<EngineEvent> Events,
        GameState ReactionState,
        GameState FinalState,
        IReadOnlySet<string> SaveLoadedBoundaries,
        IReadOnlyDictionary<string, GameState> BoundaryStates);
}

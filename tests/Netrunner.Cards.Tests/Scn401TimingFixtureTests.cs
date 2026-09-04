using System.Text.Json;
using System.Text.Json.Nodes;

using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

/// <summary>
/// Executable SCN-401 evidence for the initial approach/encounter reaction
/// checkpoint.  The fixture is a test-owned setup description; all yielded
/// decisions and state transitions below still come from the Core reducer.
/// </summary>
public sealed class Scn401TimingFixtureTests
{
    [Fact]
    public void CheckedInFixtureIsVersionedAndDescribesOnlyTheInitialReactionScope()
    {
        Scn401TimingFixtureDocument fixture = Scn401TimingFixtureDocument.Load();

        fixture.Validate();
        Assert.Equal("SCN-401", fixture.ScenarioId);
        Assert.Equal(45041UL, fixture.Seed);
        Assert.Equal(EngineCompatibility.StateSchemaVersion, fixture.EngineStateSchemaVersion);
        Assert.Equal(fixture.Catalog.Version, fixture.BehaviorRegistryVersion);
        Assert.Equal(
            ["approach-anoetic-void", "approach-manegarm-skunkworks", "encounter-funhouse"],
            fixture.Cases.Select(scenario => scenario.Id));
        Assert.Contains(
            fixture.Excluded,
            value => value.Contains("Mayfly", StringComparison.Ordinal));
        Assert.Contains(
            fixture.Excluded,
            value => value.Contains("bioroid", StringComparison.OrdinalIgnoreCase));
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
        StateInvariantValidator.Validate(evidence.FinalState, evidence.Catalog);
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
        GameState initial = Scn401TimingFixtureBuilder.BuildState(fixture, scenario, catalog);
        CardInstanceState fixtureIce = initial.Cards.Single(card =>
            initial.Zones.Single(zone => zone.Id == card.ZoneId).Kind == ZoneKind.ServerIce);
        Assert.Equal(scenario.Id == "encounter-funhouse", fixtureIce.Rezzed);
        GameEngine.RunStartResult start = GameEngine.BeginRunWithEvents(
            initial,
            Scn401TimingFixtureBuilder.RemoteId(initial),
            catalog);
        GameState triggerState = AdvanceUntil(
            start.State,
            catalog,
            static state => state.PendingDecision?.Kind == DecisionKind.TriggerChoice,
            out _);

        Assert.Equal(PlayerSide.Corp, SideOf(triggerState, triggerState.PendingDecision!.Owner));
        LegalAction useTrigger = Assert.Single(
            triggerState.PendingDecision.LegalActions,
            action => action.Kind == ActionKind.UseTrigger);
        ApplyResult used = ApplyAndRoundTrip(triggerState, useTrigger, catalog);

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
                catalog)));

        ApplyResult ended = ApplyAndRoundTrip(used.State, payment, catalog);
        Assert.Null(ended.State.RunState);
        Assert.Contains(ended.Events, value => value.Kind == EngineEventKinds.RunEnded);
        Assert.DoesNotContain(ended.Events, value => value.Kind == EngineEventKinds.RunSuccessful);
    }

    private static Scn401RunEvidence DriveCase(
        Scn401TimingFixtureDocument fixture,
        Scn401TimingCase scenario)
    {
        BehaviorCatalog catalog = Scn401TimingFixtureBuilder.CreateBehaviorCatalog(scenario);
        GameState initial = Scn401TimingFixtureBuilder.BuildState(fixture, scenario, catalog);
        GameEngine.RunStartResult start = GameEngine.BeginRunWithEvents(
            initial,
            Scn401TimingFixtureBuilder.RemoteId(initial),
            catalog);
        var events = new List<EngineEvent>(start.Events);
        var saveLoadedBoundaries = new HashSet<string>(StringComparer.Ordinal);
        GameState state = start.State;
        GameState? reactionState = null;
        int iterations = 0;

        while (state.PendingDecision is not null)
        {
            Assert.True(++iterations < 64, $"SCN-401 case '{scenario.Id}' did not make progress.");
            RecordBoundary(scenario, state, saveLoadedBoundaries, ref reactionState);

            PendingDecision decision = state.PendingDecision;
            LegalAction action = SelectAction(scenario, state, decision);
            ApplyResult result = ApplyAndRoundTrip(state, action, catalog);
            Assert.True(result.Accepted, result.Rejection?.Message);
            events.AddRange(result.Events);
            state = result.State;
        }

        return new Scn401RunEvidence(
            catalog,
            EngineEventValidator.FreezeBatch(events),
            reactionState ?? throw new Xunit.Sdk.XunitException(
                $"Case '{scenario.Id}' never yielded its reaction checkpoint."),
            state,
            saveLoadedBoundaries);
    }

    private static GameState AdvanceUntil(
        GameState state,
        BehaviorCatalog catalog,
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
            ApplyResult result = ApplyAndRoundTrip(state, action, catalog);
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
        ref GameState? reactionState)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        PlayerSide ownerSide = SideOf(state, decision.Owner);
        foreach (Scn401YieldBoundary boundary in scenario.YieldedBoundaries)
        {
            if (boundary.DecisionKind == decision.Kind
                && boundary.RunStep == state.RunState?.Step
                && boundary.OwnerSide == ownerSide)
            {
                saveLoadedBoundaries.Add(boundary.Name);
                if (boundary.Name is "optional-anoetic-trigger"
                    or "mandatory-manegarm-choice"
                    or "encounter-reaction")
                {
                    reactionState = state;
                }
            }
        }
    }

    private static LegalAction SelectAction(
        Scn401TimingCase scenario,
        GameState state,
        PendingDecision decision)
    {
        if (decision.Kind == DecisionKind.TriggerChoice)
        {
            Assert.Equal("approach-anoetic-void", scenario.Id);
            return Assert.Single(
                decision.LegalActions,
                action => action.Kind == ActionKind.DeclineTrigger);
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

        if (decision.Kind != DecisionKind.RunTiming)
        {
            throw new Xunit.Sdk.XunitException(
                $"Unexpected decision kind '{decision.Kind}' in case '{scenario.Id}'.");
        }

        if (scenario.Kind == Scn401TimingCaseKind.Encounter
            && state.RunState?.Step == RunTimingStep.ApproachPaidWindow
            && SideOf(state, decision.Owner) == PlayerSide.Corp)
        {
            LegalAction? rez = decision.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.RezIce);
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
            return Assert.Single(
                decision.LegalActions,
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
        BehaviorCatalog catalog)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        string json = EngineJson.Serialize(state);
        GameState restored = EngineJson.DeserializeState(json, catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        ApplyResult left = GameEngine.Apply(
            state,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id),
            catalog);
        ApplyResult right = GameEngine.Apply(
            restored,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id),
            catalog);
        Assert.Equal(left.Status, right.Status);
        Assert.Equal(EngineJson.Serialize(left.State), EngineJson.Serialize(right.State));
        Assert.Equal(DeterministicHash.ForEvents(left.Events), DeterministicHash.ForEvents(right.Events));
        return left;
    }

    private static void AssertReactionCheckpoint(
        Scn401TimingCase scenario,
        GameState state)
    {
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

        int occurrence = FindEvent(
            events,
            scenario.Reaction.Occurrence == OccurrenceKind.ServerApproached
                ? "server-approached"
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
                "encounter-initialized" => value.Kind == EngineEventKinds.EncounterInitialized,
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

    private sealed record Scn401RunEvidence(
        BehaviorCatalog Catalog,
        IReadOnlyList<EngineEvent> Events,
        GameState ReactionState,
        GameState FinalState,
        IReadOnlySet<string> SaveLoadedBoundaries);
}

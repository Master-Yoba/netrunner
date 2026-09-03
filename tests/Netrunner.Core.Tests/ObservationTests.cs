using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// M2 Slice 3.4 W2 observation-boundary tests: <see cref="ObservationBuilder"/>
/// is the only production path from authoritative state to the agent/client
/// DTO. These tests pin the exact JSON shape for both viewers before, during,
/// and after access and prove that hidden Corp and Runner information never
/// leaks: the opponent's grip/stack/hand/deck, face-down ICE, hidden breach
/// candidates, and all engine bookkeeping (RNG, allocator, sequences).
/// </summary>
public sealed class ObservationTests
{
    [Fact]
    public void RemovedFromGameZonesArePublicOnlyAfterTheirLazyZoneExists()
    {
        GameState baseline = GameState.CreateNew(42);
        ZoneId corpZoneId = new("zone:obs-corp-rfg");
        ZoneId runnerZoneId = new("zone:obs-runner-rfg");
        CardInstanceId corpCardId = new("card:obs-corp-rfg");
        CardInstanceId runnerCardId = new("card:obs-runner-rfg");
        CardInstanceState corpCard = new(
            corpCardId,
            new CardDefinitionId("def:obs-corp-rfg"),
            corpZoneId,
            PlayerSide.Corp,
            faceUp: true);
        CardInstanceState runnerCard = new(
            runnerCardId,
            new CardDefinitionId("def:obs-runner-rfg"),
            runnerZoneId,
            PlayerSide.Runner,
            faceUp: true);
        GameState state = new(
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            players: baseline.Players,
            cards: [corpCard, runnerCard],
            zones: baseline.Zones
                .Append(new ZoneState(corpZoneId, ZoneKind.CorpRemovedFromGame, PlayerSide.Corp, null, [corpCardId]))
                .Append(new ZoneState(runnerZoneId, ZoneKind.RunnerRemovedFromGame, PlayerSide.Runner, null, [runnerCardId])),
            servers: baseline.Servers,
            turn: baseline.Turn,
            outcome: baseline.Outcome,
            randomState: baseline.RandomState,
            allocator: baseline.Allocator,
            schemaVersion: baseline.SchemaVersion,
            initialSeed: baseline.InitialSeed);

        foreach (PlayerSide viewer in new[] { PlayerSide.Corp, PlayerSide.Runner })
        {
            Observation observation = ObservationBuilder.Build(state, viewer);
            IReadOnlyList<object?> corpRfg = Assert.IsAssignableFrom<IReadOnlyList<object?>>(
                observation.PublicState["corpRemovedFromGame"]);
            IReadOnlyList<object?> runnerRfg = Assert.IsAssignableFrom<IReadOnlyList<object?>>(
                observation.PublicState["runnerRemovedFromGame"]);
            Assert.Equal(corpCardId.Value, ((Dictionary<string, object?>)Assert.Single(corpRfg)!)!["id"]);
            Assert.Equal(runnerCardId.Value, ((Dictionary<string, object?>)Assert.Single(runnerRfg)!)!["id"]);
            Assert.DoesNotContain("corpRemovedFromGame", EngineJson.Serialize(observation.PrivateState));
            Assert.DoesNotContain("runnerRemovedFromGame", EngineJson.Serialize(observation.PrivateState));
        }
    }

    [Fact]
    public void BeforeRunBothViewersHaveTheExactPinnedShape()
    {
        (GameState state, _) = BuildState();

        Assert.Equal(Goldens.BeforeRunner, EngineJson.Serialize(ObservationBuilder.Build(state, PlayerSide.Runner)));
        Assert.Equal(Goldens.BeforeCorp, EngineJson.Serialize(ObservationBuilder.Build(state, PlayerSide.Corp)));
    }

    [Fact]
    public void BeforeRunNeitherViewerSeesTheOpponentsHiddenZonesOrEngineInternals()
    {
        (GameState state, FixtureIds ids) = BuildState();
        string runnerJson = EngineJson.Serialize(ObservationBuilder.Build(state, PlayerSide.Runner));
        string corpJson = EngineJson.Serialize(ObservationBuilder.Build(state, PlayerSide.Corp));

        // The Runner never sees the Corp's hidden cards or face-down ICE, and
        // never the facedown cards of the Corp's own discard (Archives).
        Assert.DoesNotContain("def:corp-hand-one", runnerJson);
        Assert.DoesNotContain("def:corp-hand-two", runnerJson);
        Assert.DoesNotContain("def:corp-deck-one", runnerJson);
        Assert.DoesNotContain("def:ice-face-down", runnerJson);
        Assert.DoesNotContain("def:corp-archives-facedown", runnerJson);
        Assert.DoesNotContain(ids.CorpHandOne.Value, runnerJson);
        Assert.DoesNotContain(ids.CorpDeckOne.Value, runnerJson);
        Assert.DoesNotContain(ids.CorpArchivesFacedown.Value, runnerJson);

        // The faceup trash stays public to both viewers (CR 26.03 4.4.6c).
        Assert.Contains("def:corp-trashed", corpJson);

        // The Corp never sees the Runner's grip or stack.
        Assert.DoesNotContain("def:runner-grip-one", corpJson);
        Assert.DoesNotContain("def:runner-stack-one", corpJson);
        Assert.DoesNotContain(ids.RunnerGripOne.Value, corpJson);
        Assert.DoesNotContain(ids.RunnerStackOne.Value, corpJson);

        // Public information is visible to both viewers.
        foreach (string json in new[] { runnerJson, corpJson })
        {
            Assert.Contains("def:corp-trashed", json);
            Assert.Contains("def:corp-root-asset", json);
            Assert.Contains("def:ice-public", json);
            Assert.Contains("def:remote-asset", json);
            Assert.Contains("def:runner-program", json);
            Assert.Contains("\"corpHand\":2", json);
            Assert.Contains("\"corpDeck\":1", json);
            Assert.Contains("\"runnerGrip\":1", json);
            Assert.Contains("\"runnerStack\":1", json);
            Assert.DoesNotContain("\"run\"", json);
        }
    }

    [Fact]
    public void TypedCountersFollowCardVisibilityAndUseDeterministicOptionalProjection()
    {
        (GameState state, FixtureIds ids) = BuildState();
        state = WithCounters(
            state,
            [
                // Deliberately reverse the insertion order: observation order
                // is canonicalized by counter name.
                new CardCounterState(ids.RunnerProgram, "virus", 1),
                new CardCounterState(ids.RunnerProgram, "charge", 2),
                CardCounterState.CreateHostedCredit(ids.CorpRootAsset, 3, HostedCreditSpendRestriction.AnyCost),
                new CardCounterState(ids.CorpHandOne, "corp-secret", 2),
                new CardCounterState(ids.RunnerGripOne, "runner-secret", 4),
            ]);

        Observation runnerView = ObservationBuilder.Build(state, PlayerSide.Runner);
        Observation corpView = ObservationBuilder.Build(state, PlayerSide.Corp);

        Dictionary<string, object?> runnerProgram = Assert.IsType<Dictionary<string, object?>>(
            ((IReadOnlyList<object?>)runnerView.PublicState["runnerRig"]!).Single());
        IReadOnlyList<object?> runnerCounters = Assert.IsAssignableFrom<IReadOnlyList<object?>>(
            runnerProgram["counters"]);
        Assert.Equal(["charge", "virus"], runnerCounters
            .Select(counter => (string)((Dictionary<string, object?>)counter!)!["counterName"]!)
            .ToArray());
        Dictionary<string, object?> charge = (Dictionary<string, object?>)runnerCounters[0]!;
        Assert.Equal(CardCounterPurpose.Generic, charge["purpose"]);
        Assert.Equal(2, charge["count"]);
        Assert.Null(charge["spendRestriction"]);

        // The same public card view, including its hosted-credit restriction,
        // is visible to both viewers.
        foreach (Observation view in new[] { runnerView, corpView })
        {
            Dictionary<string, object?> root = RootEntry(view, 0, 0)!;
            Dictionary<string, object?> hosted = Assert.IsType<Dictionary<string, object?>>(
                Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<object?>>(root["counters"])));
            Assert.Equal("credit", hosted["counterName"]);
            Assert.Equal(CardCounterPurpose.HostedCredit, hosted["purpose"]);
            Assert.Equal(3, hosted["count"]);
            Assert.Equal(HostedCreditSpendRestriction.AnyCost, hosted["spendRestriction"]);
        }

        // The Corp owns the hidden hand card and sees its typed counter only
        // in the Corp-private view. The Runner never receives its identity or
        // the counter metadata.
        Dictionary<string, object?> corpHidden = Assert.IsType<Dictionary<string, object?>>(
            ((IReadOnlyList<object?>)corpView.PrivateState["hand"]!).Single(
                value => ((Dictionary<string, object?>)value!)!["id"]!.Equals(ids.CorpHandOne.Value)));
        Dictionary<string, object?> corpCounter = Assert.IsType<Dictionary<string, object?>>(
            Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<object?>>(corpHidden["counters"])));
        Assert.Equal("corp-secret", corpCounter["counterName"]);
        string runnerJson = EngineJson.Serialize(runnerView);
        Assert.DoesNotContain(ids.CorpHandOne.Value, runnerJson);
        Assert.DoesNotContain("corp-secret", runnerJson);

        // Likewise the Runner's hidden grip counter is private to the Runner.
        Dictionary<string, object?> runnerHidden = Assert.IsType<Dictionary<string, object?>>(
            ((IReadOnlyList<object?>)runnerView.PrivateState["grip"]!).Single());
        Assert.Equal("runner-secret", ((Dictionary<string, object?>)Assert.Single(
            Assert.IsAssignableFrom<IReadOnlyList<object?>>(runnerHidden["counters"]))!)["counterName"]);
        string corpJson = EngineJson.Serialize(corpView);
        Assert.DoesNotContain(ids.RunnerGripOne.Value, corpJson);
        Assert.DoesNotContain("runner-secret", corpJson);
    }

    [Fact]
    public void CardsWithoutTypedCountersRetainThePinnedObservationShape()
    {
        (GameState state, _) = BuildState();

        // The existing exact golden is also the regression assertion that the
        // optional counters key is omitted for every zero-counter card.
        Assert.Equal(Goldens.BeforeRunner, EngineJson.Serialize(ObservationBuilder.Build(state, PlayerSide.Runner)));
        Assert.Equal(Goldens.BeforeCorp, EngineJson.Serialize(ObservationBuilder.Build(state, PlayerSide.Corp)));
    }

    [Fact]
    public void FacedownCorpArchivesCardIsVisibleToTheCorpOnly()
    {
        // CR 26.03 4.4.6c: facedown cards in Archives are visible only to
        // the Corp. The fixture's discard holds one faceup card (public to
        // both) and one facedown card (Corp-private).
        (GameState state, FixtureIds ids) = BuildState();
        string runnerJson = EngineJson.Serialize(ObservationBuilder.Build(state, PlayerSide.Runner));
        string corpJson = EngineJson.Serialize(ObservationBuilder.Build(state, PlayerSide.Corp));

        Assert.Contains("def:corp-archives-facedown", corpJson);
        Assert.Contains(ids.CorpArchivesFacedown.Value, corpJson);
        Assert.DoesNotContain("def:corp-archives-facedown", runnerJson);
        Assert.DoesNotContain(ids.CorpArchivesFacedown.Value, runnerJson);

        // The Corp's private state lists the card with its definition (the
        // private card view, exactly like faceDownIce); the faceup trash is
        // never in the private listing; the Runner has no such private zone.
        Observation corpView = ObservationBuilder.Build(state, PlayerSide.Corp);
        Observation runnerView = ObservationBuilder.Build(state, PlayerSide.Runner);
        IReadOnlyList<object?> faceDownArchives = (IReadOnlyList<object?>)corpView.PrivateState["faceDownArchives"]!;
        Assert.Single(faceDownArchives);
        Dictionary<string, object?> entry = (Dictionary<string, object?>)faceDownArchives[0]!;
        Assert.Equal(ids.CorpArchivesFacedown.Value, entry["id"]);
        Assert.Equal("def:corp-archives-facedown", entry["definitionId"]);
        Assert.False(runnerView.PrivateState.ContainsKey("faceDownArchives"));
    }

    [Fact]
    public void FacedownArchivesCardIsNotDoubleExposed()
    {
        // A facedown Archives discard is public only after an actual Archives
        // breach boundary flips it under CR 7.5.2. This synthetic HQ breach
        // must not grant temporary or cross-server visibility; the Corp still
        // retains its private view of the card.
        (GameState _, FixtureIds ids) = BuildState();
        RunState run = new(
            1,
            new RunId("run:obs-archives"),
            ids.Archives,
            PlayerSide.Runner,
            ids.Runner,
            null,
            RunCrPhase.Success,
            RunTimingStep.BreachCandidateSelection,
            -1,
            null,
            true,
            false,
            null,
            null,
            null,
            [ids.CorpTrashed, ids.CorpArchivesFacedown],
            1,
            Array.Empty<CardInstanceId>(),
            Array.Empty<CardInstanceId>(),
            RunAccessState.NotStarted,
            0,
            null,
            []);
        (GameState breached, _) = BuildState(run: run);

        string runnerJson = EngineJson.Serialize(ObservationBuilder.Build(breached, PlayerSide.Runner));
        string corpJson = EngineJson.Serialize(ObservationBuilder.Build(breached, PlayerSide.Corp));

        // This is not an Archives breach, so the facedown discard is not
        // published to the Runner through the candidate list.
        Assert.DoesNotContain("def:corp-archives-facedown", runnerJson);
        Assert.Contains("def:corp-archives-facedown", corpJson);
        // The Corp additionally lists it in its private state. The faceup
        // trash is public exactly once (corpDiscard) and is never in the
        // private facedown listing.
        IReadOnlyDictionary<string, object?> corpPrivate = ObservationBuilder.Build(breached, PlayerSide.Corp).PrivateState;
        IReadOnlyList<object?> faceDownArchives = (IReadOnlyList<object?>)corpPrivate["faceDownArchives"]!;
        Assert.Single(faceDownArchives);
        Dictionary<string, object?> listed = (Dictionary<string, object?>)faceDownArchives[0]!;
        Assert.Equal(ids.CorpArchivesFacedown.Value, listed["id"]);
        Assert.False(ObservationBuilder.Build(breached, PlayerSide.Runner).PrivateState.ContainsKey("faceDownArchives"));

        // Flipped faceup (trashed through access): public in corpDiscard for
        // both viewers, gone from the private facedown listing.
        (GameState flipped, _) = BuildState(revealArchivesFacedown: true);
        Observation flippedCorp = ObservationBuilder.Build(flipped, PlayerSide.Corp);
        IReadOnlyList<object?> corpDiscard = (IReadOnlyList<object?>)flippedCorp.PublicState["corpDiscard"]!;
        Dictionary<string, object?> flippedEntry = (Dictionary<string, object?>)corpDiscard
            .Single(item => ((string)((Dictionary<string, object?>)item!)["id"]!) == ids.CorpArchivesFacedown.Value)!;
        Assert.Equal("def:corp-archives-facedown", flippedEntry["definitionId"]);
        Assert.Empty((IReadOnlyList<object?>)flippedCorp.PrivateState["faceDownArchives"]!);
        Assert.Contains("def:corp-archives-facedown", EngineJson.Serialize(ObservationBuilder.Build(flipped, PlayerSide.Runner)));
    }

    [Fact]
    public void DuringBreachCandidateSelectionTheRunnerCannotSeeTheHiddenPool()
    {
        (GameState state, FixtureIds ids) = BuildState();
        RunState run = BuildHqBreachRun(ids, step: RunTimingStep.BreachCandidateSelection);
        PendingDecision decision = BuildRunnerDecision(ids,
            "CR 7.5.4: choose a breach candidate to access.",
            [new LegalAction(
                new ActionId("action:obs-hidden"),
                ActionKind.ChooseBreachCandidate,
                "Access a random card from the Corp hand",
                new BreachCandidateParameters(BreachHiddenCandidateSource.CorpHand))]);
        (GameState breached, _) = BuildState(run: run, pending: decision);

        string runnerJson = EngineJson.Serialize(ObservationBuilder.Build(breached, PlayerSide.Runner));
        string corpJson = EngineJson.Serialize(ObservationBuilder.Build(breached, PlayerSide.Corp));

        // The hidden hand pool is never named for either viewer: the Runner
        // only gets an anonymous source, and the Corp's hand stays private.
        Assert.DoesNotContain("def:corp-hand-one", runnerJson);
        Assert.DoesNotContain("def:corp-hand-two", runnerJson);
        Assert.DoesNotContain(ids.CorpHandOne.Value, runnerJson);
        Assert.Contains("\"hiddenCandidateSource\":\"corpHand\"", runnerJson);
        Assert.Contains("\"randomAccessLimit\":1", runnerJson);
        Assert.Contains("\"candidates\":[", runnerJson);

        // The decision is owned by the Runner: the Corp sees no legal actions.
        Observation runnerView = ObservationBuilder.Build(breached, PlayerSide.Runner);
        Observation corpView = ObservationBuilder.Build(breached, PlayerSide.Corp);
        Assert.Single(runnerView.LegalActions);
        Assert.IsType<BreachCandidateParameters>(runnerView.LegalActions[0].Parameters);
        Assert.Empty(corpView.LegalActions);
        Assert.Equal(decision.Id.Value, runnerView.DecisionId);
        Assert.Equal(decision.Id.Value, corpView.DecisionId);

        Assert.Equal(Goldens.DuringCandidateRunnerRun, RunSection(runnerJson));
        Assert.Equal(Goldens.DuringCandidateCorpRun, RunSection(corpJson));
    }

    [Fact]
    public void DuringBreachMidAccessTheChosenHiddenCardIsRevealedToBothViewers()
    {
        (GameState _, FixtureIds ids) = BuildState();
        RunState run = BuildHqBreachRun(
            ids,
            step: RunTimingStep.BreachAccessAbility,
            chosen: [ids.CorpHandOne],
            accessed: [ids.CorpHandOne]);
        // CR 7.2.2: the non-agenda accessed card offers only the no-action.
        PendingDecision decision = BuildRunnerDecision(
            ids,
            "CR 7.2.2: take the mid-access ability or no action.",
            [new LegalAction(
                new ActionId("action:obs-no-access"),
                ActionKind.NoAccessAction,
                "No action",
                new NoActionParameters())]);
        (GameState midAccess, _) = BuildState(revealHandOne: true, run: run, pending: decision);

        string runnerJson = EngineJson.Serialize(ObservationBuilder.Build(midAccess, PlayerSide.Runner));
        string corpJson = EngineJson.Serialize(ObservationBuilder.Build(midAccess, PlayerSide.Corp));

        // The drawn card is now faceup: both viewers see its identity.
        Assert.Contains("def:corp-hand-one", runnerJson);
        Assert.Contains("def:corp-hand-one", corpJson);
        Assert.Contains("\"accessState\":\"inProgress\"", runnerJson);
        // The Runner never sees the undrawn hand card; the Corp's own private
        // view legitimately still lists it.
        Assert.DoesNotContain("def:corp-hand-two", runnerJson);
    }

    [Fact]
    public void AfterRunTheScoredCardIsPublicAndTheRemainingHandStaysHidden()
    {
        (GameState state, FixtureIds ids) = BuildState(scoreHandOne: true);
        string runnerJson = EngineJson.Serialize(ObservationBuilder.Build(state, PlayerSide.Runner));
        string corpJson = EngineJson.Serialize(ObservationBuilder.Build(state, PlayerSide.Corp));

        Assert.DoesNotContain("\"run\"", runnerJson);
        Assert.DoesNotContain("\"run\"", corpJson);
        // The stolen agenda is faceup in RunnerScore: public to both viewers.
        Assert.Contains("fixture:agenda-scored", runnerJson);
        Assert.Contains("fixture:agenda-scored", corpJson);
        // The remaining hand card stays private: the Runner never sees it.
        Assert.DoesNotContain("def:corp-hand-two", runnerJson);

        Assert.Equal(Goldens.AfterRunner, EngineJson.Serialize(ObservationBuilder.Build(state, PlayerSide.Runner)));
        Assert.Equal(Goldens.AfterCorp, EngineJson.Serialize(ObservationBuilder.Build(state, PlayerSide.Corp)));
    }

    [Fact]
    public void ApproachingFaceDownIceOnlyTheCorpKnowsTheIdentity()
    {
        (GameState _, FixtureIds ids) = BuildState();
        RunState run = new(
            1,
            new RunId("run:obs-approach"),
            ids.Headquarters,
            PlayerSide.Runner,
            ids.Runner,
            null,
            RunCrPhase.Approach,
            RunTimingStep.ApproachPaidWindow,
            currentIceIndex: 1,
            currentIceId: ids.IceFaceDown,
            false,
            false,
            null,
            new RunPriorityState(RunPriorityWindowKind.ApproachPaid, PlayerSide.Runner),
            null,
            [],
            0,
            [],
            [],
            RunAccessState.NotStarted,
            0,
            null,
            []);
        (GameState approaching, _) = BuildState(run: run);

        string runnerJson = EngineJson.Serialize(ObservationBuilder.Build(approaching, PlayerSide.Runner));
        string corpJson = EngineJson.Serialize(ObservationBuilder.Build(approaching, PlayerSide.Corp));

        // The position is public, the identity only to the owning Corp.
        Assert.DoesNotContain("def:ice-face-down", runnerJson);
        Assert.DoesNotContain(ids.IceFaceDown.Value, runnerJson);
        Assert.Contains("def:ice-face-down", corpJson);
        Assert.Contains("def:ice-public", runnerJson);
    }

    [Fact]
    public void FacedownRootCardPreservesAnAnonymousSlotForTheRunner()
    {
        (GameState state, FixtureIds ids) = BuildState(rootAssetFaceUp: false);

        Dictionary<string, object?>? runnerEntry = RootEntry(
            ObservationBuilder.Build(state, PlayerSide.Runner),
            serverIndex: 0,
            rootIndex: 0);
        Dictionary<string, object?>? corpEntry = RootEntry(
            ObservationBuilder.Build(state, PlayerSide.Corp),
            serverIndex: 0,
            rootIndex: 0);

        // The installed slot is public, but its identity remains Corp-private
        // until the card becomes faceup or is accessed.
        Assert.Null(runnerEntry);
        Assert.NotNull(corpEntry);
        Assert.Equal(ids.CorpRootAsset.Value, corpEntry!["id"]);
        Assert.Equal("def:corp-root-asset", corpEntry["definitionId"]);
        Assert.DoesNotContain(
            ids.CorpRootAsset.Value,
            EngineJson.Serialize(ObservationBuilder.Build(state, PlayerSide.Runner)));
    }

    [Fact]
    public void RezApproachedIceTurnsFaceupForBothViewers()
    {
        // An engine-driven run approaches the single facedown ICE on the
        // attacked remote server.
        GameState prompt = RunTimingTests.BeginRunPrompt(iceCount: 1, rezzed: false);
        GameState approach = RunTimingTests.PassTwice(prompt).State;
        Assert.Equal(RunTimingStep.ApproachPaidWindow, approach.RunState!.Step);
        CardInstanceId iceId = approach.RunState.CurrentIceId!.Value;
        int serverIndex = approach.Servers.ToList().FindIndex(server => server.Id == approach.RunState.AttackedServer);
        int iceIndex = approach.RunState.CurrentIceIndex;

        // While it is facedown, the slot is anonymous for the Runner and only
        // the owning Corp knows the identity.
        Assert.Null(IceEntry(ObservationBuilder.Build(approach, PlayerSide.Runner), serverIndex, iceIndex));
        Assert.NotNull(IceEntry(ObservationBuilder.Build(approach, PlayerSide.Corp), serverIndex, iceIndex));

        // The Corp rezzes the approached ICE via the legal action.
        ApplyResult runnerPass = RunTimingTests.ApplyKind(approach, ActionKind.PassPriority);
        Assert.True(runnerPass.Accepted, runnerPass.Rejection?.Message);
        GameState corpPrompt = runnerPass.State;
        LegalAction rez = Assert.Single(corpPrompt.PendingDecision!.LegalActions, action => action.Kind == ActionKind.RezIce);
        ApplyResult rezResult = RunTimingTests.ApplyAction(corpPrompt, rez);
        Assert.True(rezResult.Accepted, rezResult.Rejection?.Message);
        GameState rezzed = rezResult.State;

        // CR 26.03 8.1.1: the rezzed ICE is now faceup, so its definition is
        // visible in the server view of both viewers.
        CardInstanceState ice = rezzed.GetCard(iceId);
        Assert.True(ice.Rezzed);
        Assert.True(ice.FaceUp);
        foreach (PlayerSide viewer in new[] { PlayerSide.Runner, PlayerSide.Corp })
        {
            Dictionary<string, object?>? entry = IceEntry(ObservationBuilder.Build(rezzed, viewer), serverIndex, iceIndex);
            Assert.NotNull(entry);
            Assert.Equal(iceId.Value, entry!["id"]);
            Assert.Equal(FixtureIceBehaviorSpecs.Plain.Value, entry["definitionId"]);
            Assert.True((bool)entry["rezzed"]!);
        }
    }

    /// <summary>
    /// The server view's ICE slot at a fixed position: the card view when the
    /// viewer may know it, null when the slot is anonymous.
    /// </summary>
    private static Dictionary<string, object?>? IceEntry(Observation view, int serverIndex, int iceIndex)
    {
        object?[] servers = (object?[])view.PublicState["servers"]!;
        Dictionary<string, object?> server = (Dictionary<string, object?>)servers[serverIndex]!;
        IReadOnlyList<object?> ice = (IReadOnlyList<object?>)server["ice"]!;
        return (Dictionary<string, object?>?)ice[iceIndex];
    }

    private static Dictionary<string, object?>? RootEntry(Observation view, int serverIndex, int rootIndex)
    {
        object?[] servers = (object?[])view.PublicState["servers"]!;
        Dictionary<string, object?> server = (Dictionary<string, object?>)servers[serverIndex]!;
        IReadOnlyList<object?> root = (IReadOnlyList<object?>)server["root"]!;
        return (Dictionary<string, object?>?)root[rootIndex];
    }

    [Fact]
    public void AllFixturesValidateAsProducibleEngineStates()
    {
        (GameState before, _) = BuildState();
        AssertValid(before);

        (GameState _, FixtureIds ids) = BuildState();
        RunState candidateRun = BuildHqBreachRun(ids, step: RunTimingStep.BreachCandidateSelection);
        PendingDecision candidateDecision = BuildRunnerDecision(
            ids,
            "CR 7.5.4: choose a breach candidate to access.",
            [new LegalAction(
                new ActionId("action:obs-hidden"),
                ActionKind.ChooseBreachCandidate,
                "Access a random card from the Corp hand",
                new BreachCandidateParameters(BreachHiddenCandidateSource.CorpHand))]);
        (GameState candidate, _) = BuildState(run: candidateRun, pending: candidateDecision);
        AssertValid(candidate);

        RunState midRun = BuildHqBreachRun(
            ids,
            step: RunTimingStep.BreachAccessAbility,
            chosen: [ids.CorpHandOne],
            accessed: [ids.CorpHandOne]);
        PendingDecision midDecision = BuildRunnerDecision(
            ids,
            "CR 7.2.2: take the mid-access ability or no action.",
            [new LegalAction(
                new ActionId("action:obs-no-access"),
                ActionKind.NoAccessAction,
                "No action",
                new NoActionParameters())]);
        (GameState mid, _) = BuildState(revealHandOne: true, run: midRun, pending: midDecision);
        AssertValid(mid);

        (GameState after, _) = BuildState(scoreHandOne: true);
        AssertValid(after);

        static void AssertValid(GameState state)
        {
            bool valid = StateInvariantValidator.TryValidate(state, out IReadOnlyList<StateInvariantViolation> violations);
            Assert.True(valid, string.Join(" | ", violations.Select(violation => violation.ToString())));
        }
    }

    [Fact]
    public void SameStateAndViewerProduceByteIdenticalJsonAcrossSerializationRoundTrips()
    {
        (GameState state, _) = BuildState();
        GameState restored = EngineJson.DeserializeState(EngineJson.Serialize(state));

        string left = EngineJson.Serialize(ObservationBuilder.Build(state, PlayerSide.Runner));
        string right = EngineJson.Serialize(ObservationBuilder.Build(restored, PlayerSide.Runner));
        Assert.Equal(left, right);

        string leftCorp = EngineJson.Serialize(ObservationBuilder.Build(state, PlayerSide.Corp));
        string rightCorp = EngineJson.Serialize(ObservationBuilder.Build(restored, PlayerSide.Corp));
        Assert.Equal(leftCorp, rightCorp);
    }

    [Fact]
    public void TheObservationNeverExposesEngineInternals()
    {
        (GameState state, _) = BuildState();
        foreach (PlayerSide viewer in new[] { PlayerSide.Runner, PlayerSide.Corp })
        {
            string json = EngineJson.Serialize(ObservationBuilder.Build(state, viewer));
            foreach (string key in new[]
                     {
                         "randomState", "initialSeed", "allocator", "eventSequence",
                         "actionSequence", "decisionRevision", "schemaVersion",
                         "gameId", "rulesVersion", "cardDataVersion", "resolutionStack"
                     })
            {
                Assert.DoesNotContain($"\"{key}\":", json);
            }

            // The raw RNG values must not appear in any numeric form either.
            Assert.DoesNotContain("3735928559", json); // 0xDEADBEEF
            Assert.DoesNotContain("3405691582", json); // 0xCAFEBABE
        }
    }

    private static string RunSection(string observationJson)
    {
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(observationJson);
        return document.RootElement.GetProperty("publicState").GetProperty("run").GetRawText();
    }

    private static RunState BuildHqBreachRun(
        FixtureIds ids,
        RunTimingStep step,
        CardInstanceId[]? chosen = null,
        CardInstanceId[]? accessed = null)
    {
        return new RunState(
            1,
            new RunId("run:obs-breach"),
            ids.Headquarters,
            PlayerSide.Runner,
            ids.Runner,
            null,
            RunCrPhase.Success,
            step,
            -1,
            null,
            true,
            false,
            null,
            null,
            null,
            [],
            1,
            chosen ?? Array.Empty<CardInstanceId>(),
            accessed ?? Array.Empty<CardInstanceId>(),
            RunAccessState.InProgress,
            0,
            null,
            []);
    }

    private static PendingDecision BuildRunnerDecision(
        FixtureIds ids,
        string prompt,
        LegalAction[] actions)
    {
        return new PendingDecision(
            new DecisionId("decision:obs-breach"),
            1,
            ids.Runner,
            DecisionKind.RunTiming,
            prompt,
            actions);
    }

    private sealed record FixtureIds(
        PlayerId Corp,
        PlayerId Runner,
        ServerId Headquarters,
        ServerId Archives,
        CardInstanceId CorpHandOne,
        CardInstanceId CorpHandTwo,
        CardInstanceId CorpDeckOne,
        CardInstanceId CorpTrashed,
        CardInstanceId CorpArchivesFacedown,
        CardInstanceId CorpRootAsset,
        CardInstanceId IcePublic,
        CardInstanceId IceFaceDown,
        CardInstanceId RemoteAsset,
        CardInstanceId RunnerGripOne,
        CardInstanceId RunnerStackOne,
        CardInstanceId RunnerProgram);

    private static (GameState State, FixtureIds Ids) BuildState(
        bool revealHandOne = false,
        bool scoreHandOne = false,
        bool revealArchivesFacedown = false,
        bool rootAssetFaceUp = true,
        RunState? run = null,
        PendingDecision? pending = null,
        IReadOnlyList<CardCounterState>? counters = null)
    {
        StableIdAllocator allocator = new();
        GameId gameId = allocator.NextGameId();
        PlayerId corp = allocator.NextPlayerId();
        PlayerId runner = allocator.NextPlayerId();

        ServerId hq = allocator.NextServerId();
        ServerId rd = allocator.NextServerId();
        ServerId archives = allocator.NextServerId();
        ServerId remote = allocator.NextServerId();

        ZoneId hqIce = allocator.NextZoneId();
        ZoneId hqRoot = allocator.NextZoneId();
        ZoneId rdIce = allocator.NextZoneId();
        ZoneId rdRoot = allocator.NextZoneId();
        ZoneId arcIce = allocator.NextZoneId();
        ZoneId arcRoot = allocator.NextZoneId();
        ZoneId remIce = allocator.NextZoneId();
        ZoneId remRoot = allocator.NextZoneId();
        ZoneId deck = allocator.NextZoneId();
        ZoneId hand = allocator.NextZoneId();
        ZoneId discard = allocator.NextZoneId();
        ZoneId corpScore = allocator.NextZoneId();
        ZoneId stack = allocator.NextZoneId();
        ZoneId grip = allocator.NextZoneId();
        ZoneId heap = allocator.NextZoneId();
        ZoneId rig = allocator.NextZoneId();
        ZoneId runnerScore = allocator.NextZoneId();

        CardInstanceId handOne = allocator.NextCardInstanceId();
        CardInstanceId handTwo = allocator.NextCardInstanceId();
        CardInstanceId deckOne = allocator.NextCardInstanceId();
        CardInstanceId trashed = allocator.NextCardInstanceId();
        CardInstanceId rootAsset = allocator.NextCardInstanceId();
        CardInstanceId icePublic = allocator.NextCardInstanceId();
        CardInstanceId iceFaceDown = allocator.NextCardInstanceId();
        CardInstanceId remoteAsset = allocator.NextCardInstanceId();
        CardInstanceId gripOne = allocator.NextCardInstanceId();
        CardInstanceId stackOne = allocator.NextCardInstanceId();
        CardInstanceId program = allocator.NextCardInstanceId();
        CardInstanceId scoredAgenda = allocator.NextCardInstanceId();
        CardInstanceId archivesFacedown = allocator.NextCardInstanceId();

        var servers = new[]
        {
            new ServerState(hq, ServerKind.Headquarters, hqIce, hqRoot),
            new ServerState(rd, ServerKind.RAndD, rdIce, rdRoot),
            new ServerState(archives, ServerKind.Archives, arcIce, arcRoot),
            new ServerState(remote, ServerKind.Remote, remIce, remRoot),
        };

        var zones = new[]
        {
            new ZoneState(hqIce, ZoneKind.ServerIce, PlayerSide.Corp, hq, [icePublic, iceFaceDown]),
            new ZoneState(hqRoot, ZoneKind.ServerRoot, PlayerSide.Corp, hq, [rootAsset]),
            new ZoneState(rdIce, ZoneKind.ServerIce, PlayerSide.Corp, rd),
            new ZoneState(rdRoot, ZoneKind.ServerRoot, PlayerSide.Corp, rd),
            new ZoneState(arcIce, ZoneKind.ServerIce, PlayerSide.Corp, archives),
            new ZoneState(arcRoot, ZoneKind.ServerRoot, PlayerSide.Corp, archives),
            new ZoneState(remIce, ZoneKind.ServerIce, PlayerSide.Corp, remote),
            new ZoneState(remRoot, ZoneKind.ServerRoot, PlayerSide.Corp, remote, [remoteAsset]),
            new ZoneState(deck, ZoneKind.CorpDeck, PlayerSide.Corp, null, [deckOne]),
            new ZoneState(hand, ZoneKind.CorpHand, PlayerSide.Corp, null,
                scoreHandOne ? [handTwo] : [handTwo, handOne]),
            new ZoneState(discard, ZoneKind.CorpDiscard, PlayerSide.Corp, null, [trashed, archivesFacedown]),
            new ZoneState(corpScore, ZoneKind.CorpScore, PlayerSide.Corp),
            new ZoneState(stack, ZoneKind.RunnerStack, PlayerSide.Runner, null, [stackOne]),
            new ZoneState(grip, ZoneKind.RunnerGrip, PlayerSide.Runner, null, [gripOne]),
            new ZoneState(heap, ZoneKind.RunnerHeap, PlayerSide.Runner),
            new ZoneState(rig, ZoneKind.RunnerRig, PlayerSide.Runner, null, [program]),
            new ZoneState(runnerScore, ZoneKind.RunnerScore, PlayerSide.Runner, null,
                scoreHandOne ? [scoredAgenda] : null),
        };

        // The scored fixture models a hidden-pool breach that drew a
        // registered agenda and stole it (CR 7.1.6): the agenda is faceup in
        // RunnerScore and the rest of the hand stays hidden. In that variant
        // handOne never existed in the hand, so it is omitted entirely.
        var cards = new List<CardInstanceState>(new[]
        {
            new CardInstanceState(handTwo, new CardDefinitionId("def:corp-hand-two"), hand, PlayerSide.Corp),
            new CardInstanceState(deckOne, new CardDefinitionId("def:corp-deck-one"), deck, PlayerSide.Corp),
            new CardInstanceState(trashed, new CardDefinitionId("def:corp-trashed"), discard, PlayerSide.Corp, faceUp: true),
            new CardInstanceState(archivesFacedown, new CardDefinitionId("def:corp-archives-facedown"), discard, PlayerSide.Corp, faceUp: revealArchivesFacedown),
            new CardInstanceState(rootAsset, new CardDefinitionId("def:corp-root-asset"), hqRoot, PlayerSide.Corp, faceUp: rootAssetFaceUp, rezzed: rootAssetFaceUp),
            new CardInstanceState(icePublic, new CardDefinitionId("def:ice-public"), hqIce, PlayerSide.Corp, faceUp: true, rezzed: true),
            new CardInstanceState(iceFaceDown, new CardDefinitionId("def:ice-face-down"), hqIce, PlayerSide.Corp),
            new CardInstanceState(remoteAsset, new CardDefinitionId("def:remote-asset"), remRoot, PlayerSide.Corp, faceUp: true, rezzed: true),
            new CardInstanceState(gripOne, new CardDefinitionId("def:runner-grip-one"), grip, PlayerSide.Runner),
            new CardInstanceState(stackOne, new CardDefinitionId("def:runner-stack-one"), stack, PlayerSide.Runner),
            new CardInstanceState(program, new CardDefinitionId("def:runner-program"), rig, PlayerSide.Runner, faceUp: true),
        });
        if (scoreHandOne)
        {
            cards.Add(new CardInstanceState(scoredAgenda, FixtureAgendaSpecs.ScoredAgenda, runnerScore, PlayerSide.Corp, faceUp: true));
        }
        else
        {
            cards.Add(new CardInstanceState(handOne, new CardDefinitionId("def:corp-hand-one"), hand, PlayerSide.Corp,
                faceUp: revealHandOne));
        }

        var state = new GameState(
            gameId,
            "26.03",
            "fixture-observation-1",
            players:
            [
                new PlayerState(corp, PlayerSide.Corp, credits: 5, clicks: 3),
                new PlayerState(runner, PlayerSide.Runner, credits: 8, clicks: 4),
            ],
            cards: cards,
            zones: zones,
            servers: servers,
            turn: new TurnState(PlayerSide.Corp, TurnPhase.Corp, turnNumber: 1),
            outcome: new GameOutcome(),
            randomState: 0xDEADBEEF,
            allocator: allocator.State,
            initialSeed: 0xCAFEBABE,
            runState: run,
            // A live run owns exactly one run timing frame on the stack;
            // without it the state invariant validator rejects the state.
            resolutionStack: run is null
                ? null
                : [new RunTimingFrame(run.Id, run.Step)],
            // A pending decision must carry the state's current revision.
            decisionRevision: pending?.Revision ?? 0,
            pendingDecision: pending,
            rulesState: counters is null
                ? null
                : new RulesState(
                    [new PlayerRulesState(corp), new PlayerRulesState(runner)],
                    counters: counters));

        var ids = new FixtureIds(
            corp,
            runner,
            hq,
            archives,
            handOne,
            handTwo,
            deckOne,
            trashed,
            archivesFacedown,
            rootAsset,
            icePublic,
            iceFaceDown,
            remoteAsset,
            gripOne,
            stackOne,
            program);
        return (state, ids);
    }

    private static GameState WithCounters(
        GameState state,
        IReadOnlyList<CardCounterState> counters) =>
        new(
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
            state.RunState,
            new RulesState(
                state.Players.Select(player => new PlayerRulesState(player.Id)).ToArray(),
                counters: counters),
            state.BehaviorRegistryVersion,
            state.BehaviorCatalogFingerprint,
            state.RuntimeCardCatalogVersion,
            state.RuntimeCardCatalogFingerprint);

    private static class Goldens
    {
        public const string BeforeRunner = "{\"viewer\":\"runner\",\"decisionId\":\"\",\"legalActions\":[],\"publicState\":{\"turn\":{\"activeSide\":\"corp\",\"phase\":\"corp\",\"turnNumber\":1},\"players\":{\"corp\":{\"id\":\"player:2\",\"credits\":5,\"clicks\":3,\"agendaPoints\":0},\"runner\":{\"id\":\"player:3\",\"credits\":8,\"clicks\":4,\"agendaPoints\":0}},\"outcome\":{\"kind\":\"ongoing\",\"winner\":null,\"reason\":null},\"servers\":[{\"id\":\"server:4\",\"kind\":\"headquarters\",\"ice\":[{\"id\":\"card:30\",\"definitionId\":\"def:ice-public\",\"rezzed\":true,\"advancementCounters\":0},null],\"root\":[{\"id\":\"card:29\",\"definitionId\":\"def:corp-root-asset\",\"rezzed\":true,\"advancementCounters\":0}]},{\"id\":\"server:5\",\"kind\":\"rAndD\",\"ice\":[],\"root\":[]},{\"id\":\"server:6\",\"kind\":\"archives\",\"ice\":[],\"root\":[]},{\"id\":\"server:7\",\"kind\":\"remote\",\"ice\":[],\"root\":[{\"id\":\"card:32\",\"definitionId\":\"def:remote-asset\",\"rezzed\":true,\"advancementCounters\":0}]}],\"hiddenZoneCounts\":{\"corpHand\":2,\"corpDeck\":1,\"runnerGrip\":1,\"runnerStack\":1},\"corpDiscard\":[{\"id\":\"card:28\",\"definitionId\":\"def:corp-trashed\",\"rezzed\":false,\"advancementCounters\":0}],\"runnerHeap\":[],\"runnerRig\":[{\"id\":\"card:35\",\"definitionId\":\"def:runner-program\",\"rezzed\":false,\"advancementCounters\":0}],\"corpScore\":[],\"runnerScore\":[]},\"privateState\":{\"grip\":[{\"id\":\"card:33\",\"definitionId\":\"def:runner-grip-one\"}],\"stack\":[{\"id\":\"card:34\",\"definitionId\":\"def:runner-stack-one\"}]}}";
        public const string BeforeCorp = "{\"viewer\":\"corp\",\"decisionId\":\"\",\"legalActions\":[],\"publicState\":{\"turn\":{\"activeSide\":\"corp\",\"phase\":\"corp\",\"turnNumber\":1},\"players\":{\"corp\":{\"id\":\"player:2\",\"credits\":5,\"clicks\":3,\"agendaPoints\":0},\"runner\":{\"id\":\"player:3\",\"credits\":8,\"clicks\":4,\"agendaPoints\":0}},\"outcome\":{\"kind\":\"ongoing\",\"winner\":null,\"reason\":null},\"servers\":[{\"id\":\"server:4\",\"kind\":\"headquarters\",\"ice\":[{\"id\":\"card:30\",\"definitionId\":\"def:ice-public\",\"rezzed\":true,\"advancementCounters\":0},{\"id\":\"card:31\",\"definitionId\":\"def:ice-face-down\",\"rezzed\":false,\"advancementCounters\":0}],\"root\":[{\"id\":\"card:29\",\"definitionId\":\"def:corp-root-asset\",\"rezzed\":true,\"advancementCounters\":0}]},{\"id\":\"server:5\",\"kind\":\"rAndD\",\"ice\":[],\"root\":[]},{\"id\":\"server:6\",\"kind\":\"archives\",\"ice\":[],\"root\":[]},{\"id\":\"server:7\",\"kind\":\"remote\",\"ice\":[],\"root\":[{\"id\":\"card:32\",\"definitionId\":\"def:remote-asset\",\"rezzed\":true,\"advancementCounters\":0}]}],\"hiddenZoneCounts\":{\"corpHand\":2,\"corpDeck\":1,\"runnerGrip\":1,\"runnerStack\":1},\"corpDiscard\":[{\"id\":\"card:28\",\"definitionId\":\"def:corp-trashed\",\"rezzed\":false,\"advancementCounters\":0}],\"runnerHeap\":[],\"runnerRig\":[{\"id\":\"card:35\",\"definitionId\":\"def:runner-program\",\"rezzed\":false,\"advancementCounters\":0}],\"corpScore\":[],\"runnerScore\":[]},\"privateState\":{\"hand\":[{\"id\":\"card:26\",\"definitionId\":\"def:corp-hand-two\"},{\"id\":\"card:25\",\"definitionId\":\"def:corp-hand-one\"}],\"deck\":[{\"id\":\"card:27\",\"definitionId\":\"def:corp-deck-one\"}],\"faceDownIce\":[{\"id\":\"card:31\",\"definitionId\":\"def:ice-face-down\"}],\"faceDownArchives\":[{\"id\":\"card:37\",\"definitionId\":\"def:corp-archives-facedown\"}]}}";
        public const string DuringCandidateRunnerRun = "{\"id\":\"run:obs-breach\",\"attackedServer\":\"server:4\",\"initiatingSide\":\"runner\",\"initiatingSource\":null,\"phase\":\"success\",\"step\":\"breachCandidateSelection\",\"currentIceIndex\":-1,\"currentIce\":null,\"successful\":true,\"unsuccessful\":false,\"endReason\":null,\"priority\":null,\"encounter\":null,\"breach\":{\"candidates\":[],\"randomAccessLimit\":1,\"hiddenCandidateSource\":\"corpHand\",\"chosen\":[],\"accessed\":[],\"accessState\":\"inProgress\"},\"runCredits\":0,\"lingeringEffects\":[]}";
        public const string DuringCandidateCorpRun = "{\"id\":\"run:obs-breach\",\"attackedServer\":\"server:4\",\"initiatingSide\":\"runner\",\"initiatingSource\":null,\"phase\":\"success\",\"step\":\"breachCandidateSelection\",\"currentIceIndex\":-1,\"currentIce\":null,\"successful\":true,\"unsuccessful\":false,\"endReason\":null,\"priority\":null,\"encounter\":null,\"breach\":{\"candidates\":[],\"randomAccessLimit\":1,\"hiddenCandidateSource\":\"corpHand\",\"chosen\":[],\"accessed\":[],\"accessState\":\"inProgress\"},\"runCredits\":0,\"lingeringEffects\":[]}";
        public const string AfterRunner = "{\"viewer\":\"runner\",\"decisionId\":\"\",\"legalActions\":[],\"publicState\":{\"turn\":{\"activeSide\":\"corp\",\"phase\":\"corp\",\"turnNumber\":1},\"players\":{\"corp\":{\"id\":\"player:2\",\"credits\":5,\"clicks\":3,\"agendaPoints\":0},\"runner\":{\"id\":\"player:3\",\"credits\":8,\"clicks\":4,\"agendaPoints\":0}},\"outcome\":{\"kind\":\"ongoing\",\"winner\":null,\"reason\":null},\"servers\":[{\"id\":\"server:4\",\"kind\":\"headquarters\",\"ice\":[{\"id\":\"card:30\",\"definitionId\":\"def:ice-public\",\"rezzed\":true,\"advancementCounters\":0},null],\"root\":[{\"id\":\"card:29\",\"definitionId\":\"def:corp-root-asset\",\"rezzed\":true,\"advancementCounters\":0}]},{\"id\":\"server:5\",\"kind\":\"rAndD\",\"ice\":[],\"root\":[]},{\"id\":\"server:6\",\"kind\":\"archives\",\"ice\":[],\"root\":[]},{\"id\":\"server:7\",\"kind\":\"remote\",\"ice\":[],\"root\":[{\"id\":\"card:32\",\"definitionId\":\"def:remote-asset\",\"rezzed\":true,\"advancementCounters\":0}]}],\"hiddenZoneCounts\":{\"corpHand\":1,\"corpDeck\":1,\"runnerGrip\":1,\"runnerStack\":1},\"corpDiscard\":[{\"id\":\"card:28\",\"definitionId\":\"def:corp-trashed\",\"rezzed\":false,\"advancementCounters\":0}],\"runnerHeap\":[],\"runnerRig\":[{\"id\":\"card:35\",\"definitionId\":\"def:runner-program\",\"rezzed\":false,\"advancementCounters\":0}],\"corpScore\":[],\"runnerScore\":[{\"id\":\"card:36\",\"definitionId\":\"fixture:agenda-scored\",\"rezzed\":false,\"advancementCounters\":0}]},\"privateState\":{\"grip\":[{\"id\":\"card:33\",\"definitionId\":\"def:runner-grip-one\"}],\"stack\":[{\"id\":\"card:34\",\"definitionId\":\"def:runner-stack-one\"}]}}";
        public const string AfterCorp = "{\"viewer\":\"corp\",\"decisionId\":\"\",\"legalActions\":[],\"publicState\":{\"turn\":{\"activeSide\":\"corp\",\"phase\":\"corp\",\"turnNumber\":1},\"players\":{\"corp\":{\"id\":\"player:2\",\"credits\":5,\"clicks\":3,\"agendaPoints\":0},\"runner\":{\"id\":\"player:3\",\"credits\":8,\"clicks\":4,\"agendaPoints\":0}},\"outcome\":{\"kind\":\"ongoing\",\"winner\":null,\"reason\":null},\"servers\":[{\"id\":\"server:4\",\"kind\":\"headquarters\",\"ice\":[{\"id\":\"card:30\",\"definitionId\":\"def:ice-public\",\"rezzed\":true,\"advancementCounters\":0},{\"id\":\"card:31\",\"definitionId\":\"def:ice-face-down\",\"rezzed\":false,\"advancementCounters\":0}],\"root\":[{\"id\":\"card:29\",\"definitionId\":\"def:corp-root-asset\",\"rezzed\":true,\"advancementCounters\":0}]},{\"id\":\"server:5\",\"kind\":\"rAndD\",\"ice\":[],\"root\":[]},{\"id\":\"server:6\",\"kind\":\"archives\",\"ice\":[],\"root\":[]},{\"id\":\"server:7\",\"kind\":\"remote\",\"ice\":[],\"root\":[{\"id\":\"card:32\",\"definitionId\":\"def:remote-asset\",\"rezzed\":true,\"advancementCounters\":0}]}],\"hiddenZoneCounts\":{\"corpHand\":1,\"corpDeck\":1,\"runnerGrip\":1,\"runnerStack\":1},\"corpDiscard\":[{\"id\":\"card:28\",\"definitionId\":\"def:corp-trashed\",\"rezzed\":false,\"advancementCounters\":0}],\"runnerHeap\":[],\"runnerRig\":[{\"id\":\"card:35\",\"definitionId\":\"def:runner-program\",\"rezzed\":false,\"advancementCounters\":0}],\"corpScore\":[],\"runnerScore\":[{\"id\":\"card:36\",\"definitionId\":\"fixture:agenda-scored\",\"rezzed\":false,\"advancementCounters\":0}]},\"privateState\":{\"hand\":[{\"id\":\"card:26\",\"definitionId\":\"def:corp-hand-two\"}],\"deck\":[{\"id\":\"card:27\",\"definitionId\":\"def:corp-deck-one\"}],\"faceDownIce\":[{\"id\":\"card:31\",\"definitionId\":\"def:ice-face-down\"}],\"faceDownArchives\":[{\"id\":\"card:37\",\"definitionId\":\"def:corp-archives-facedown\"}]}}";
    }
}

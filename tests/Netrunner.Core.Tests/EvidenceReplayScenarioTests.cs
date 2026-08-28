using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Netrunner.Core.Tests;

public sealed class CanonicalEvidenceTests
{
    private static readonly int[] AscendingValues = [1, 2, 3];
    private static readonly int[] DescendingValues = [3, 2, 1];

    [Fact]
    public void CanonicalJsonSortsObjectsRecursivelyButPreservesArrayOrder()
    {
        var first = new Dictionary<string, Dictionary<string, int>>
        {
            ["z"] = new Dictionary<string, int>
            {
                ["beta"] = 2,
                ["alpha"] = 1,
            },
            ["a"] = new Dictionary<string, int>
            {
                ["delta"] = 4,
                ["gamma"] = 3,
            },
        };
        var second = new Dictionary<string, Dictionary<string, int>>
        {
            ["a"] = new Dictionary<string, int>
            {
                ["gamma"] = 3,
                ["delta"] = 4,
            },
            ["z"] = new Dictionary<string, int>
            {
                ["alpha"] = 1,
                ["beta"] = 2,
            },
        };

        Assert.Equal(CanonicalJson.Serialize(first), CanonicalJson.Serialize(second));
        Assert.Equal(DeterministicHash.Compute(first), DeterministicHash.Compute(second));
        Assert.Equal(
            "{\"a\":true,\"b\":[2,1]}",
            System.Text.Encoding.UTF8.GetString(CanonicalJson.Canonicalize(" { \"b\": [ 2, 1 ], \"a\": true } ")));

        string arrayFirst = CanonicalJson.Serialize(AscendingValues);
        string arraySecond = CanonicalJson.Serialize(DescendingValues);
        Assert.NotEqual(arrayFirst, arraySecond);
        Assert.NotEqual(DeterministicHash.Compute(AscendingValues), DeterministicHash.Compute(DescendingValues));
    }

    [Fact]
    public void CanonicalJsonNormalizesEquivalentNumbersAndRejectsDuplicateOrUnsupportedNumbers()
    {
        Assert.Equal("1", Utf8(CanonicalJson.Canonicalize("1.0")));
        Assert.Equal(Utf8(CanonicalJson.Canonicalize("1")), Utf8(CanonicalJson.Canonicalize("1e0")));
        Assert.Equal("0", Utf8(CanonicalJson.Canonicalize("-0")));
        Assert.Equal("18446744073709551615", Utf8(CanonicalJson.Canonicalize("18446744073709551615.0e0")));
        Assert.Equal("0.00123", Utf8(CanonicalJson.Canonicalize("123e-5")));

        Assert.Throws<JsonException>(() => CanonicalJson.Canonicalize("{\"value\":1,\"value\":2}"));
        Assert.Throws<JsonException>(() => CanonicalJson.Canonicalize("1e1000001"));
        Assert.ThrowsAny<JsonException>(() => CanonicalJson.Canonicalize("1."));
    }

    [Fact]
    public void TypedEventsRoundTripAndUnknownDiscriminatorsFailClosed()
    {
        var original = new EngineEvent(
            1,
            7,
            EngineEventKinds.CreditsAdjusted,
            new CreditsAdjustedEventPayload(new PlayerId("player:1"), 3, 12));

        string json = EngineJson.Serialize(original);
        EngineEvent restored = EngineJson.Deserialize<EngineEvent>(json);

        Assert.Equal(original, restored);
        Assert.Contains("\"$type\":\"credits-adjusted\"", json, StringComparison.Ordinal);

        string unknownType = json.Replace(
            "\"$type\":\"credits-adjusted\"",
            "\"$type\":\"unregistered-event\"",
            StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => EngineJson.Deserialize<EngineEvent>(unknownType));

        string unsupportedVersion = json.Replace("\"version\":1", "\"version\":2", StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => EngineJson.Deserialize<EngineEvent>(unsupportedVersion));
    }

    [Fact]
    public void EventBatchHashChangesWhenEnvelopeSequenceOrTypedPayloadChanges()
    {
        var baseline = new EngineEvent(
            1,
            1,
            EngineEventKinds.CreditsAdjusted,
            new CreditsAdjustedEventPayload(new PlayerId("player:1"), 3, 12));
        var differentSequence = new EngineEvent(
            1,
            2,
            EngineEventKinds.CreditsAdjusted,
            new CreditsAdjustedEventPayload(new PlayerId("player:1"), 3, 12));
        var differentPayload = new EngineEvent(
            1,
            1,
            EngineEventKinds.CreditsAdjusted,
            new CreditsAdjustedEventPayload(new PlayerId("player:1"), 4, 13));

        string baselineHash = DeterministicHash.ForEvents([baseline]);
        Assert.NotEqual(baselineHash, DeterministicHash.ForEvents([differentSequence]));
        Assert.NotEqual(baselineHash, DeterministicHash.ForEvents([differentPayload]));
        Assert.Contains("credits-adjusted", EngineJson.Serialize(baseline), StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptedApplyAdvancesActionSequenceAndRecordsCanonicalHashes()
    {
        GameState prompt = CreateNestedPrompt();
        PendingDecision decision = prompt.PendingDecision!;
        LegalAction action = decision.LegalActions.Single(candidate =>
            candidate.Parameters is NumberChoiceParameters { Value: 2 });
        var submission = new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id);

        ApplyResult result = GameEngine.Apply(prompt, submission);

        Assert.True(result.Accepted, result.Rejection?.Message);
        AcceptedActionRecord record = Assert.IsType<AcceptedActionRecord>(result.AcceptedAction);
        Assert.Equal(1L, result.State.ActionSequence);
        Assert.Equal(1L, record.Sequence);
        Assert.Equal(submission, record.Submission);
        Assert.Equal(DeterministicHash.ForState(result.State), record.ResultingStateHash);
        Assert.Equal(DeterministicHash.ForEvents(result.Events), record.ResultingEventBatchHash);
        Assert.Empty(result.Events);

        PendingDecision inner = result.State.PendingDecision!;
        LegalAction innerAction = inner.LegalActions.Single(candidate =>
            candidate.Parameters is NumberChoiceParameters { Value: 3 });
        ApplyResult completed = GameEngine.Apply(
            result.State,
            new ActionSubmission(inner.Owner, inner.Id, inner.Revision, innerAction.Id));

        Assert.True(completed.Accepted);
        Assert.Equal(2L, completed.State.ActionSequence);
        Assert.Equal(2L, completed.AcceptedAction!.Sequence);
        Assert.Single(completed.Events);
        Assert.Equal(1L, completed.Events[0].Sequence);
        Assert.Equal(DeterministicHash.ForEvents(completed.Events), completed.AcceptedAction.ResultingEventBatchHash);
    }

    [Fact]
    public void RejectedApplyHasNoRecordEventsOrCounterMutation()
    {
        GameState prompt = CreateNestedPrompt();
        string before = EngineJson.Serialize(prompt);
        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(
                prompt.PendingDecision!.Owner,
                prompt.PendingDecision.Id,
                prompt.PendingDecision.Revision,
                new ActionId("action:tampered")));

        Assert.False(result.Accepted);
        Assert.Equal(ApplyRejectionCode.UnknownAction, result.Rejection!.Code);
        Assert.Same(prompt, result.State);
        Assert.Null(result.AcceptedAction);
        Assert.Empty(result.Events);
        Assert.Equal(before, EngineJson.Serialize(prompt));
        Assert.Equal(0L, prompt.ActionSequence);
        Assert.Equal(0L, prompt.EventSequence);
    }

    [Fact]
    public void AcceptedEventBatchIsDeeplyFrozen()
    {
        (_, GameState completedPrompt) = CompleteNestedPrompt();
        ApplyResult result = GameEngine.Apply(
            completedPrompt,
            new ActionSubmission(
                completedPrompt.PendingDecision!.Owner,
                completedPrompt.PendingDecision.Id,
                completedPrompt.PendingDecision.Revision,
                completedPrompt.PendingDecision.LegalActions[0].Id));

        Assert.True(result.Accepted);
        var events = Assert.IsAssignableFrom<IList<EngineEvent>>(result.Events);
        Assert.Throws<NotSupportedException>(() => events.Clear());
        Assert.Single(result.Events);
    }

    [Fact]
    public void ReplayResubmitsActionsAndRoundTripsWithIdenticalHashes()
    {
        Flow flow = BuildFlow();
        var artifact = new ReplayArtifact(
            flow.OuterPrompt.CreateSnapshot(),
            seed: 42,
            actions:
            [
                ReplayAction.From(flow.InnerResult.AcceptedAction!),
                ReplayAction.From(flow.CompletedResult.AcceptedAction!),
            ],
            expectedFinalStateHash: DeterministicHash.ForState(flow.CompletedResult.State));

        ReplayVerificationResult first = ReplayVerifier.Verify(artifact);
        ReplayVerificationResult second = ReplayVerifier.Verify(
            EngineJson.Deserialize<ReplayArtifact>(EngineJson.Serialize(artifact)));

        Assert.True(first.Success, first.Message);
        Assert.True(second.Success, second.Message);
        Assert.Equal(first.FinalStateHash, second.FinalStateHash);
        Assert.Equal(
            EngineJson.Serialize(first.FinalState),
            EngineJson.Serialize(second.FinalState));
        Assert.Equal(
            flow.CompletedResult.AcceptedAction!.ResultingEventBatchHash,
            ReplayAction.From(flow.CompletedResult.AcceptedAction).ExpectedEventBatchHash);
    }

    [Fact]
    public void ReplayReportsTheExactFirstTamperedHashOrSubmission()
    {
        Flow flow = BuildFlow();
        ReplayAction original = ReplayAction.From(flow.InnerResult.AcceptedAction!);
        var tamperedHash = new ReplayAction(
            original.Sequence,
            original.Submission,
            new string('0', 64),
            original.ExpectedEventBatchHash);
        var hashArtifact = new ReplayArtifact(
            flow.OuterPrompt.CreateSnapshot(),
            42,
            [tamperedHash, ReplayAction.From(flow.CompletedResult.AcceptedAction!)]);

        ReplayVerificationResult hashResult = ReplayVerifier.Verify(hashArtifact);

        Assert.Equal(ReplayVerificationStatus.Diverged, hashResult.Status);
        Assert.Equal(1L, hashResult.ActionSequence);
        Assert.Equal(new string('0', 64), hashResult.ExpectedHash);
        Assert.Equal(flow.InnerResult.AcceptedAction!.ResultingStateHash, hashResult.ActualHash);
        Assert.Contains("1", hashResult.Message, StringComparison.Ordinal);

        var tamperedSubmission = new ReplayAction(
            original.Sequence,
            new ActionSubmission(
                original.Submission.Actor,
                original.Submission.DecisionId,
                original.Submission.Revision,
                new ActionId("action:not-legal")),
            original.ExpectedStateHash,
            original.ExpectedEventBatchHash);
        var submissionArtifact = new ReplayArtifact(
            flow.OuterPrompt.CreateSnapshot(),
            42,
            [tamperedSubmission, ReplayAction.From(flow.CompletedResult.AcceptedAction!)]);

        ReplayVerificationResult submissionResult = ReplayVerifier.Verify(submissionArtifact);

        Assert.Equal(ReplayVerificationStatus.Rejected, submissionResult.Status);
        Assert.Equal(1L, submissionResult.ActionSequence);
        Assert.Contains("UnknownAction", submissionResult.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayUsesInitialSeedWhenTheSnapshotRngHasAdvancedAndRejectsTamperedSeed()
    {
        GameState created = GameState.CreateNew(42);
        var random = DeterministicRandom.Restore(created.RandomState);
        _ = random.NextUInt64();
        GameState advanced = RebuildWithRandomState(created, random.State);
        Assert.Equal(42UL, advanced.InitialSeed);
        Assert.NotEqual(advanced.InitialSeed, advanced.RandomState);

        PlayerId corp = advanced.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        GameState prompt = GameEngine.BeginResolution(
            advanced,
            new ChooseNumberThenAdjustCreditsFrame(corp, corp, 1, 1, 1));
        PendingDecision decision = prompt.PendingDecision!;
        ApplyResult completed = GameEngine.Apply(
            prompt,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, decision.LegalActions[0].Id));
        Assert.True(completed.Accepted, completed.Rejection?.Message);

        var artifact = new ReplayArtifact(
            prompt.CreateSnapshot(),
            seed: 42,
            [ReplayAction.From(completed.AcceptedAction!)],
            expectedFinalStateHash: DeterministicHash.ForState(completed.State));
        Assert.True(ReplayVerifier.Verify(artifact).Success);

        var tampered = new ReplayArtifact(
            prompt.CreateSnapshot(),
            seed: 43,
            artifact.Actions,
            expectedFinalStateHash: artifact.ExpectedFinalStateHash);
        ReplayVerificationResult result = ReplayVerifier.Verify(tampered);
        Assert.Equal(ReplayVerificationStatus.InvalidArtifact, result.Status);
        Assert.Contains("seed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReplayRejectsUnknownFieldsAndUnsupportedVersions()
    {
        Flow flow = BuildFlow();
        var artifact = new ReplayArtifact(
            flow.OuterPrompt.CreateSnapshot(),
            42,
            [ReplayAction.From(flow.InnerResult.AcceptedAction!), ReplayAction.From(flow.CompletedResult.AcceptedAction!)]);
        string json = EngineJson.Serialize(artifact);

        string unknownField = json[..^1] + ",\"unexpected\":true}";
        Assert.Throws<JsonException>(() => EngineJson.Deserialize<ReplayArtifact>(unknownField));

        string unsupportedSchema = json.Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => EngineJson.Deserialize<ReplayArtifact>(unsupportedSchema));

        var unsupportedArtifact = new ReplayArtifact(
            2,
            EngineArtifactKinds.Replay,
            EngineCompatibility.RulesVersion,
            EngineCompatibility.CardDataVersion,
            EngineCompatibility.BehaviorRegistryVersion,
            42,
            flow.OuterPrompt.CreateSnapshot(),
            artifact.Actions);
        ReplayVerificationResult verification = ReplayVerifier.Verify(unsupportedArtifact);
        Assert.Equal(ReplayVerificationStatus.InvalidArtifact, verification.Status);
    }

    [Theory]
    [InlineData("initialSeed")]
    [InlineData("randomState")]
    [InlineData("decisionRevision")]
    [InlineData("eventSequence")]
    [InlineData("actionSequence")]
    public void StateAndSnapshotRequireDeterministicMembersEvenWhenZeroIsValid(string propertyName)
    {
        GameState state = GameState.CreateNew(0);

        Assert.Throws<JsonException>(() => EngineJson.DeserializeState(
            RemoveTopLevelProperty(EngineJson.Serialize(state), propertyName)));
        Assert.Throws<JsonException>(() => EngineJson.DeserializeSnapshot(
            RemoveTopLevelProperty(EngineJson.Serialize(state.CreateSnapshot()), propertyName)));
    }

    [Fact]
    public void NestedAuthoritativeMembersCannotBeOmittedFromStrictStateJson()
    {
        GameState prompt = CreateNestedPrompt();
        string stateJson = EngineJson.Serialize(prompt);

        string missingPlayerSide = RemoveNestedProperty(stateJson, "players", 0, "side");
        Assert.Throws<JsonException>(() => EngineJson.DeserializeState(missingPlayerSide));

        string missingTurnPhase = RemoveNestedProperty(stateJson, "turn", "phase");
        Assert.Throws<JsonException>(() => EngineJson.DeserializeState(missingTurnPhase));

        string missingFrameRange = RemoveNestedProperty(stateJson, "resolutionStack", 0, "minimum");
        Assert.Throws<JsonException>(() => EngineJson.DeserializeState(missingFrameRange));

        string missingFrameVersion = RemoveNestedProperty(stateJson, "resolutionStack", 0, "version");
        Assert.Throws<JsonException>(() => EngineJson.DeserializeState(missingFrameVersion));
    }

    [Fact]
    public void ReplayAndScenarioSeedsAreRequiredEvenWhenZeroIsValid()
    {
        GameSnapshot snapshot = GameState.CreateNew(0).CreateSnapshot();
        var replay = new ReplayArtifact(snapshot, seed: 0);
        Assert.Throws<JsonException>(() => EngineJson.Deserialize<ReplayArtifact>(
            RemoveTopLevelProperty(EngineJson.Serialize(replay), "seed")));

        var scenario = new ScenarioArtifact(seed: 0);
        Assert.Throws<JsonException>(() => EngineJson.Deserialize<ScenarioArtifact>(
            RemoveTopLevelProperty(EngineJson.Serialize(scenario), "seed")));
    }

    [Theory]
    [InlineData("base.json", "2569c9b49ff91115a1179e82dab3854cdd30d7f0dad2ffd56a701259546cef2e")]
    [InlineData("nested-outer.json", "5647a38b04a862146f948314abd7e48e65a57db4260439004ef206b507cec0e3")]
    [InlineData("nested-inner.json", "4b9130af24e4356905f3e9dcc6e741c4a9993a520b7d7d51098dbb319d3a951b")]
    public void CheckedInScenarioFixturesBuildToExactDeterministicStates(string fileName, string expectedHash)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Scenarios", fileName);
        ScenarioArtifact artifact = EngineJson.Deserialize<ScenarioArtifact>(File.ReadAllText(path));

        GameState first = ScenarioBuilder.Build(artifact);
        GameState second = ScenarioBuilder.Build(artifact);

        Assert.Equal(expectedHash, artifact.ExpectedStateHash);
        Assert.Equal(expectedHash, DeterministicHash.ForState(first));
        Assert.Equal(CanonicalJson.Serialize(first), CanonicalJson.Serialize(second));
        Assert.Equal(DeterministicHash.ForState(first), DeterministicHash.ForState(second));

        if (fileName == "nested-outer.json")
        {
            ChooseNumberThenAdjustCreditsFrame frame = Assert.IsType<ChooseNumberThenAdjustCreditsFrame>(first.ResolutionStack[0]);
            Assert.Equal(NestedNumberStage.OuterPrompt, frame.Stage);
            Assert.NotNull(first.PendingDecision);
        }
        else if (fileName == "nested-inner.json")
        {
            ChooseNumberThenAdjustCreditsFrame frame = Assert.IsType<ChooseNumberThenAdjustCreditsFrame>(first.ResolutionStack[0]);
            Assert.Equal(NestedNumberStage.InnerPrompt, frame.Stage);
            Assert.Equal(2, frame.ChosenOuterValue);
            Assert.Equal(2, first.ResolutionStack.Count);
            Assert.NotNull(first.PendingDecision);
        }
    }

    [Fact]
    public void ScenarioRejectsUnknownFieldsUnsupportedVersionsAndInvalidDefinitions()
    {
        string baseJson = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios", "base.json"));
        Assert.Throws<JsonException>(() => EngineJson.Deserialize<ScenarioArtifact>(baseJson[..^1] + ",\"unexpected\":true}"));
        Assert.Throws<JsonException>(() => EngineJson.Deserialize<ScenarioArtifact>(baseJson.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal)));
        Assert.Throws<JsonException>(() => EngineJson.Deserialize<ScenarioArtifact>(baseJson.Replace("\"rulesVersion\": \"26.03\"", "\"rulesVersion\": \"unsupported\"", StringComparison.Ordinal)));

        string invalidDefinition = baseJson.Replace(
            "\"expectedStateHash\": \"2569c9b49ff91115a1179e82dab3854cdd30d7f0dad2ffd56a701259546cef2e\"",
            "\"cards\":[{\"id\":\"card:100\",\"definitionId\":\"fixture:missing\",\"zone\":\"corpDeck\",\"ownerSide\":\"corp\"}]",
            StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => EngineJson.Deserialize<ScenarioArtifact>(invalidDefinition));

        string invalidOwner = baseJson.Replace(
            "\"expectedStateHash\": \"2569c9b49ff91115a1179e82dab3854cdd30d7f0dad2ffd56a701259546cef2e\"",
            "\"cards\":[{\"id\":\"card:101\",\"definitionId\":\"fixture:first\",\"zone\":\"corpDeck\",\"ownerSide\":\"runner\"}]",
            StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => EngineJson.Deserialize<ScenarioArtifact>(invalidOwner));

        string invalidInner = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios", "nested-inner.json"))
            .Replace("\"chosenOuterValue\": 2", "\"chosenOuterValue\": 99", StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => EngineJson.Deserialize<ScenarioArtifact>(invalidInner));
    }

    private static GameState CreateNestedPrompt()
    {
        GameState state = GameState.CreateNew(42);
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        return GameEngine.BeginResolution(state, new ChooseNumberThenAdjustCreditsFrame(corp, corp, 1, 3, 2));
    }

    private static GameState RebuildWithRandomState(GameState source, ulong randomState) => new(
        source.GameId,
        source.RulesVersion,
        source.CardDataVersion,
        source.Players,
        source.Cards,
        source.Zones,
        source.Servers,
        source.Turn,
        source.Outcome,
        randomState,
        source.Allocator,
        decisionRevision: source.DecisionRevision,
        eventSequence: source.EventSequence,
        resolutionStack: source.ResolutionStack,
        pendingDecision: source.PendingDecision,
        actionSequence: source.ActionSequence,
        initialSeed: source.InitialSeed);

    private static string Utf8(byte[] value) => System.Text.Encoding.UTF8.GetString(value);

    private static string RemoveTopLevelProperty(string json, string propertyName)
    {
        JsonObject root = JsonNode.Parse(json)!.AsObject();
        root.Remove(propertyName);
        return root.ToJsonString(EngineJson.Options);
    }

    private static string RemoveNestedProperty(
        string json,
        string collectionProperty,
        int index,
        string propertyName)
    {
        JsonObject root = JsonNode.Parse(json)!.AsObject();
        root[collectionProperty]!.AsArray()[index]!.AsObject().Remove(propertyName);
        return root.ToJsonString(EngineJson.Options);
    }

    private static string RemoveNestedProperty(
        string json,
        string objectProperty,
        string propertyName)
    {
        JsonObject root = JsonNode.Parse(json)!.AsObject();
        root[objectProperty]!.AsObject().Remove(propertyName);
        return root.ToJsonString(EngineJson.Options);
    }

    private sealed record Flow(
        GameState Initial,
        GameState OuterPrompt,
        GameState InnerPrompt,
        ApplyResult InnerResult,
        ApplyResult CompletedResult);

    private static Flow BuildFlow()
    {
        GameState initial = GameState.CreateNew(42);
        PlayerId corp = initial.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        GameState outerPrompt = GameEngine.BeginResolution(initial, new ChooseNumberThenAdjustCreditsFrame(corp, corp, 1, 3, 2));
        PendingDecision outerDecision = outerPrompt.PendingDecision!;
        LegalAction outerAction = outerDecision.LegalActions.Single(action =>
            action.Parameters is NumberChoiceParameters { Value: 2 });
        ApplyResult innerResult = GameEngine.Apply(
            outerPrompt,
            new ActionSubmission(outerDecision.Owner, outerDecision.Id, outerDecision.Revision, outerAction.Id));
        GameState innerPrompt = innerResult.State;
        PendingDecision innerDecision = innerPrompt.PendingDecision!;
        LegalAction innerAction = innerDecision.LegalActions.Single(action =>
            action.Parameters is NumberChoiceParameters { Value: 3 });
        ApplyResult completedResult = GameEngine.Apply(
            innerPrompt,
            new ActionSubmission(innerDecision.Owner, innerDecision.Id, innerDecision.Revision, innerAction.Id));
        return new Flow(initial, outerPrompt, innerPrompt, innerResult, completedResult);
    }

    private static (GameState Initial, GameState Prompt) CompleteNestedPrompt()
    {
        GameState state = GameState.CreateNew(43);
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        GameState outerPrompt = GameEngine.BeginResolution(state, new ChooseNumberThenAdjustCreditsFrame(corp, corp, 1, 1, 1));
        PendingDecision decision = outerPrompt.PendingDecision!;
        ApplyResult outerResult = GameEngine.Apply(
            outerPrompt,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, decision.LegalActions[0].Id));
        Assert.True(outerResult.Accepted);
        return (state, outerResult.State);
    }
}

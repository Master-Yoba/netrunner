using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// CR 6.9.3a encounter initialization tests: the encounter's ordered
/// subroutine instances come from the ICE's registered behavior spec and are
/// created unbroken/unresolved in printed order.
/// </summary>
public sealed class EncounterSubroutineTests
{
    [Fact]
    public void EncounterInitializationCreatesPrintedSubroutinesUnbrokenAndUnresolved()
    {
        ApplyResult result = PassToEncounter(FixtureIceBehaviorSpecs.TwoSubroutines);
        Assert.True(result.Accepted, result.Rejection?.Message);
        GameState state = result.State;

        Assert.Equal(RunTimingStep.EncounterPaidWindow, state.RunState!.Step);
        RunEncounterState encounter = state.RunState.Encounter!;
        Assert.NotNull(encounter);
        RunSubroutineState[] subroutines = encounter.Subroutines.ToArray();
        Assert.Equal(2, subroutines.Length);
        Assert.Equal(0, subroutines[0].Ordinal);
        Assert.Equal(1, subroutines[1].Ordinal);
        Assert.All(subroutines, value => Assert.False(value.Broken));
        Assert.All(subroutines, value => Assert.False(value.Resolved));
        Assert.All(subroutines, value => Assert.Equal(1, value.Version));
        Assert.Equal(2, subroutines.Select(value => value.Id).Distinct().Count());
        for (int index = 0; index < subroutines.Length; index++)
        {
            Assert.Equal($"subroutine:{encounter.Id.Value}:{index}", subroutines[index].Id.Value);
        }

        EncounterInitializedEventPayload payload = Assert.IsType<EncounterInitializedEventPayload>(
            result.Events.Single(candidate => candidate.Kind == EngineEventKinds.EncounterInitialized).Payload);
        Assert.Equal(2, payload.SubroutineCount);
        Assert.Equal(encounter.Id, payload.EncounterId);
        Assert.Equal(encounter.IceId, payload.IceId);
    }

    [Fact]
    public void PlainFixtureIceEncountersWithZeroSubroutines()
    {
        ApplyResult result = PassToEncounter(FixtureIceBehaviorSpecs.Plain);
        Assert.True(result.Accepted, result.Rejection?.Message);
        GameState state = result.State;

        Assert.Equal(RunTimingStep.EncounterPaidWindow, state.RunState!.Step);
        RunEncounterState encounter = state.RunState.Encounter!;
        Assert.NotNull(encounter);
        Assert.Empty(encounter.Subroutines);

        EncounterInitializedEventPayload payload = Assert.IsType<EncounterInitializedEventPayload>(
            result.Events.Single(candidate => candidate.Kind == EngineEventKinds.EncounterInitialized).Payload);
        Assert.Equal(0, payload.SubroutineCount);
    }

    [Fact]
    public void UnregisteredIceDefinitionFailsClosedAtEncounterInitialization()
    {
        GameState state = RunTimingTests.CreateRunState(
            iceCount: 1,
            rezzed: true,
            iceDefinition: new CardDefinitionId("fixture:ice-unregistered"));
        state = GameEngine.BeginRun(state, RunTimingTests.RemoteId(state), FixtureBehaviorCatalog.Catalog);
        state = RunTimingTests.PassTwice(state).State;
        ApplyResult firstPass = RunTimingTests.ApplyKind(state, ActionKind.PassPriority);
        Assert.True(firstPass.Accepted, firstPass.Rejection?.Message);

        string before = EngineJson.Serialize(firstPass.State);
        ApplyResult secondPass = RunTimingTests.ApplyKind(firstPass.State, ActionKind.PassPriority);

        Assert.False(secondPass.Accepted);
        Assert.Equal(ApplyRejectionCode.ResolutionFailure, secondPass.Rejection?.Code);
        Assert.Same(firstPass.State, secondPass.State);
        Assert.Equal(before, EngineJson.Serialize(secondPass.State));
    }

    [Fact]
    public void EncounterPaidWindowRoundTripsAndContinuesIdentically()
    {
        GameState original = PassToEncounter(FixtureIceBehaviorSpecs.TwoSubroutines).State;
        string json = EngineJson.Serialize(original);
        GameState restored = EngineJson.DeserializeState(json);
        Assert.Equal(json, EngineJson.Serialize(restored));

        PendingDecision decision = Assert.IsType<PendingDecision>(original.PendingDecision);
        LegalAction pass = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.PassPriority);
        ActionSubmission submission = new(decision.Owner, decision.Id, decision.Revision, pass.Id);

        ApplyResult left = GameEngine.Apply(original, submission, FixtureBehaviorCatalog.Catalog);
        ApplyResult right = GameEngine.Apply(restored, submission, FixtureBehaviorCatalog.Catalog);

        Assert.True(left.Accepted, left.Rejection?.Message);
        Assert.True(right.Accepted, right.Rejection?.Message);
        Assert.Equal(left.Status, right.Status);
        Assert.Equal(left.State.RunState!.Step, right.State.RunState!.Step);
        PendingDecision leftDecision = Assert.IsType<PendingDecision>(left.State.PendingDecision);
        PendingDecision rightDecision = Assert.IsType<PendingDecision>(right.State.PendingDecision);
        Assert.Equal(leftDecision.Owner, rightDecision.Owner);
        Assert.Equal(leftDecision.Kind, rightDecision.Kind);
        Assert.Equal(EngineJson.Serialize(left.State), EngineJson.Serialize(right.State));
    }

    private static ApplyResult PassToEncounter(CardDefinitionId iceDefinition)
    {
        GameState state = RunTimingTests.CreateRunState(iceCount: 1, rezzed: true, iceDefinition: iceDefinition);
        state = GameEngine.BeginRun(state, RunTimingTests.RemoteId(state), FixtureBehaviorCatalog.Catalog);
        state = RunTimingTests.PassTwice(state).State;
        return RunTimingTests.PassTwice(state);
    }
}

using Xunit;

namespace Netrunner.Core.Tests;

public sealed class SetupTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SetupRoundTripsAtEveryChoiceAndReplaysToDeckOut(bool corpMulligan, bool runnerMulligan)
    {
        var (behavior, runtime, corp, runner) = Fixture();
        GameState state = GameEngine.CreateMatch(42, corp, runner, EngineCompatibility.CardDataVersion, behavior, runtime).State;
        GameState initial = state;
        Assert.Equal(EngineJson.Serialize(state), EngineJson.Serialize(
            GameEngine.CreateMatch(42, corp, runner, EngineCompatibility.CardDataVersion, behavior, runtime).State));
        var actions = new List<ReplayAction>();
        foreach (PlayerSide side in Enum.GetValues<PlayerSide>())
        {
            Assert.Equal(side, Assert.IsType<SetupFrame>(state.ResolutionStack.Single()).Side);
            Assert.All(state.Players, player => { Assert.Equal(5, player.Credits); Assert.Equal(0, player.Clicks); });
            Assert.Equal(5, state.GetZone(ZoneKind.CorpHand).CardIds.Count);
            Assert.Equal(5, state.GetZone(ZoneKind.RunnerGrip).CardIds.Count);
            Observation hidden = ObservationBuilder.Build(state, side == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp, behavior);
            Assert.Null(hidden.Decision);
            Assert.Empty(hidden.LegalActions);
            if (side == PlayerSide.Corp ? corpMulligan : runnerMulligan)
            {
                Apply(state.PendingDecision!.LegalActions.Single(action => action.Parameters is NumberChoiceParameters { Value: 1 }));
                Assert.True(Assert.IsType<SetupFrame>(state.ResolutionStack.Single()).Redrawn);
                Assert.Single(state.PendingDecision!.LegalActions);
            }
            Apply(state.PendingDecision!.LegalActions.Single(action => action.Parameters is NumberChoiceParameters { Value: 0 }));
        }

        Assert.Equal(PlayerSide.Corp, state.Turn.ActiveSide);
        Assert.Equal(3, state.Players.Single(player => player.Side == PlayerSide.Corp).Clicks);
        for (int i = 0; i < 500 && state.Outcome.Kind == GameOutcomeKind.Ongoing; i++)
        {
            PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
            Apply(pending.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.PassPriority)
                ?? pending.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.GainCredit)
                ?? pending.LegalActions[0]);
        }
        Assert.Equal(GameWinReason.DeckOut, state.Outcome.Reason);
        Assert.Equal(PlayerSide.Runner, state.Outcome.Winner);
        Assert.True(state.Turn.TurnNumber > 1);
        ReplayArtifact replay = new(initial.CreateSnapshot(behavior, runtime), initial.InitialSeed, actions,
            expectedFinalStateHash: DeterministicHash.ForState(state));
        replay = EngineJson.Deserialize<ReplayArtifact>(EngineJson.Serialize(replay), behavior, runtime);
        ReplayVerificationResult verification = ReplayVerifier.Verify(replay, behavior, runtime);
        Assert.True(verification.Success, verification.Message);

        void Apply(LegalAction action)
        {
            PendingDecision pending = state.PendingDecision!;
            state = EngineJson.DeserializeState(EngineJson.Serialize(state), behavior, runtime);
            ApplyResult result = GameEngine.Apply(state, new ActionSubmission(pending.Owner, pending.Id, pending.Revision, action.Id), behavior, runtime);
            Assert.True(result.Accepted, result.Rejection?.Message);
            actions.Add(ReplayAction.From(result.AcceptedAction!));
            state = result.State;
        }
    }

    [Fact]
    public void SetupRejectsMissingDefinitionsAndUndealableDecks()
    {
        var (behavior, runtime, corp, runner) = Fixture();
        DeckRequest shortDeck = new(1, PlayerSide.Corp, corp.IdentityCode, [new DeckCardEntry("setup:corp-card", 4)]);
        Assert.Throws<ArgumentException>(() => GameEngine.CreateMatch(42, shortDeck, runner, EngineCompatibility.CardDataVersion, behavior, runtime));
        DeckRequest unknown = new(1, PlayerSide.Corp, "setup:unknown", corp.Cards);
        Assert.Throws<ArgumentException>(() => GameEngine.CreateMatch(42, unknown, runner, EngineCompatibility.CardDataVersion, behavior, runtime));
    }

    private static (BehaviorCatalog, RuntimeCardCatalog, DeckRequest, DeckRequest) Fixture()
    {
        var behavior = new BehaviorCatalogBuilder("setup:behavior");
        var runtime = new RuntimeCardCatalogBuilder("setup:runtime");
        foreach (PlayerSide side in Enum.GetValues<PlayerSide>())
        {
            string prefix = side == PlayerSide.Corp ? "setup:corp" : "setup:runner";
            foreach (bool identity in new[] { true, false })
            {
                CardDefinitionId id = new(prefix + (identity ? "-identity" : "-card"));
                behavior.Add(new CardBehaviorDefinition(id, side));
                runtime.Add(new CardRuntimeSpec(id, side, identity ? RuntimeCardClass.Identity
                    : side == PlayerSide.Corp ? RuntimeCardClass.Asset : RuntimeCardClass.Hardware,
                    installCost: !identity && side == PlayerSide.Runner ? 0 : null,
                    rezCost: !identity && side == PlayerSide.Corp ? 0 : null,
                    trashCost: !identity && side == PlayerSide.Corp ? 0 : null));
            }
        }
        return (behavior.Build(), runtime.Build(),
            new DeckRequest(1, PlayerSide.Corp, "setup:corp-identity", [new DeckCardEntry("setup:corp-card", 8)]),
            new DeckRequest(1, PlayerSide.Runner, "setup:runner-identity", [new DeckCardEntry("setup:runner-card", 8)]));
    }
}

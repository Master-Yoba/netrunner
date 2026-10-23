using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class BeneficialTriggersTests
{
    [Fact]
    public void OnlyTriggersThatPurelyBenefitThePlayerResolveOnTheirOwn()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // Playtest, 29 September: Pennyshaver, Conduit and Zahya stacked up as dock buttons after every run.
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition("gateway-quick-and-dirty~gateway-stolen-goods", 5474919460604003952);
        ReplayArtifact replay = EngineJson.Deserialize<ReplayArtifact>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", "playtest-weyland-archives-replay.json")), definition.Behavior, definition.Runtime);
        var catalog = new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior);
        GameState state = replay.InitialSnapshot.ToState();
        int offered = 0, automated = 0, zahya = 0;
        foreach (ReplayAction action in replay.Actions)
        {
            PendingDecision pending = state.PendingDecision!;
            if (state.GetPlayer(pending.Owner).Side == PlayerSide.Runner && pending.Kind is DecisionKind.TriggerChoice or DecisionKind.TriggerOrdering)
            {
                offered++;
                AgentFeatures features = ObservationReader.Read(ObservationBuilder.Build(state, PlayerSide.Runner, definition.Behavior, definition.Runtime),
                    AgentFeatures.VersionWithHiddenAdvancements);
                if (BeneficialTriggers.AutoAction(pending, features, catalog) is { } auto)
                {
                    automated++;
                    LegalAction chosen = pending.LegalActions.Single(candidate => candidate.Id == auto);
                    Assert.Contains(chosen.Kind, new[] { ActionKind.UseTrigger, ActionKind.OrderTriggers });
                    string trigger = chosen.Parameters switch
                    {
                        TriggerChoiceParameters use => use.TriggerId.Value,
                        TriggerOrderParameters order => order.TriggerId.Value,
                        _ => "",
                    };
                    Assert.True(BeneficialTriggers.IsBeneficial(trigger, features, catalog));
                }
                // Zahya resolves on her own after a run with accesses, and waits for the player otherwise.
                foreach (LegalAction use in pending.LegalActions.Where(candidate => candidate.Parameters is TriggerChoiceParameters parameters
                    && features.PrivateCards.Triggers.Any(trigger => trigger.Id == parameters.TriggerId.Value && trigger.Key == "zahya-run-end")))
                {
                    bool accessed = features.Board.RunEnd is { AccessOccurrenceCount: > 0 };
                    Assert.Equal(accessed, BeneficialTriggers.IsBeneficial(((TriggerChoiceParameters)use.Parameters).TriggerId.Value, features, catalog));
                    if (accessed) zahya++;
                }
            }
            state = GameEngine.Apply(state, action.Submission, definition.Behavior, definition.Runtime).State;
        }
        Assert.True(automated > 0, $"None of {offered} trigger decisions resolved on their own.");
        Assert.True(zahya > 0, "The fixture never offered Zahya after a run with accesses.");
    }

    [Fact]
    public void GainsResolveBeforeAChoiceWaitingToBeOrdered()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // Over 96 Startup, Vantage Point and Core Sets games, MuslihaT's choice and Open Market's credit waited to be ordered at
        // the start of every Runner turn: the credit now goes first, and the player gets MuslihaT's choice.
        (LadderSavedGame saved, FullMatchDefinition definition) = LadderGameArchive.ReadFile(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", "startup-muslihat-open-market-game.json"));
        var catalog = new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior);
        GameState state = saved.Replay.InitialSnapshot.ToState();
        int orderings = 0;
        foreach (ReplayAction action in saved.Replay.Actions)
        {
            PendingDecision pending = state.PendingDecision!;
            if (pending.Kind == DecisionKind.TriggerOrdering && state.GetPlayer(pending.Owner).Side == PlayerSide.Runner)
            {
                AgentFeatures features = ObservationReader.Read(ObservationBuilder.Build(state, PlayerSide.Runner, definition.Behavior, definition.Runtime),
                    AgentFeatures.VersionWithHiddenAdvancements);
                string[] keys = [.. features.PrivateCards.Triggers.Select(trigger => trigger.Key)];
                if (keys.Contains("muslihat-turn-begin") && keys.Contains("open-market-turn-begin"))
                {
                    orderings++;
                    LegalAction first = pending.LegalActions.Single(candidate => candidate.Id == BeneficialTriggers.AutoAction(pending, features, catalog));
                    string trigger = ((TriggerOrderParameters)first.Parameters).TriggerId.Value;
                    Assert.Equal("open-market-turn-begin", features.PrivateCards.Triggers.Single(offered => offered.Id == trigger).Key);
                }
            }
            state = GameEngine.Apply(state, action.Submission, definition.Behavior, definition.Runtime).State;
        }
        Assert.True(orderings > 0, "The fixture has no MuslihaT and Open Market ordering.");
    }

    [Fact]
    public void ZahyaAndRedTeamAfterAnHqRunNeedNoOrderingFromThePlayer()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // Playtest, 29 September: after an HQ run the player was asked to order Zahya and Red Team.
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition("gateway-advanced-yomi~gateway-stolen-goods", 12450155976749535184);
        ReplayArtifact replay = EngineJson.Deserialize<ReplayArtifact>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", "playtest-zahya-red-team-replay.json")), definition.Behavior, definition.Runtime);
        var catalog = new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior);
        GameState state = replay.InitialSnapshot.ToState();
        int orderings = 0;
        foreach (ReplayAction action in replay.Actions)
        {
            PendingDecision pending = state.PendingDecision!;
            if (pending.Kind == DecisionKind.TriggerOrdering && state.GetPlayer(pending.Owner).Side == PlayerSide.Runner)
            {
                AgentFeatures features = ObservationReader.Read(ObservationBuilder.Build(state, PlayerSide.Runner, definition.Behavior, definition.Runtime),
                    AgentFeatures.VersionWithHiddenAdvancements);
                string[] keys = [.. features.PrivateCards.Triggers.Select(trigger => trigger.Key)];
                if (keys.Contains("zahya-run-end") && keys.Contains("red-team-successful-run") && features.Board.RunEnd is { AccessOccurrenceCount: > 0 })
                {
                    orderings++;
                    Assert.NotNull(BeneficialTriggers.AutoAction(pending, features, catalog));
                }
            }
            state = GameEngine.Apply(state, action.Submission, definition.Behavior, definition.Runtime).State;
        }
        Assert.True(orderings > 0, "The fixture has no Zahya and Red Team ordering after a run with accesses.");
    }
}

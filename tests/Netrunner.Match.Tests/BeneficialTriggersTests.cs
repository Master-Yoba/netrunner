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
        int offered = 0, automated = 0;
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
            }
            state = GameEngine.Apply(state, action.Submission, definition.Behavior, definition.Runtime).State;
        }
        Assert.True(automated > 0, $"None of {offered} trigger decisions resolved on their own.");
    }
}

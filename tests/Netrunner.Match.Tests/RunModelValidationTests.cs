using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;
using Xunit.Abstractions;

namespace Netrunner.Match.Tests;

/// <summary>
/// A4a gate: the whole-run model's forecast agrees with the run played out. In
/// self-play on the Gateway pool, every run the Runner starts with a click or a
/// run event is forecast from its observation at that decision, then followed
/// through the replay. Where every piece of ICE was visible and the model said
/// the Runner could get in, the run should succeed; the rest (the Corp rezzing
/// something unseen, a trap, a choice to jack out) are the disagreements.
/// </summary>
public sealed class RunModelValidationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ForecastsOfVisibleRunsAgreeWithTheirOutcome()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        int visible = 0, agreed = 0, predictedBlockedButRan = 0, creditErrorTotal = 0, creditSamples = 0;
        IReadOnlyList<string> pool = RulesFuzzRunner.GatewayContentIds;
        for (int index = 0; index < 24; index++)
        {
            FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(pool[index % pool.Count], RulesFuzzRunner.DeriveGameSeed(9090, index));
            FullMatchResult result = await FullMatchRunner.RunAsync(definition, 3000,
                RatingLadder.CreateAgent("standard", PlayerSide.Corp, definition), RatingLadder.CreateAgent("standard", PlayerSide.Runner, definition));
            var catalog = new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior);
            GameState state = result.Replay.InitialSnapshot.ToState();
            RunForecast? pending = null;
            int creditsAtStart = 0;
            bool succeeded = false;
            foreach (ReplayAction action in result.Replay.Actions)
            {
                PendingDecision decision = state.PendingDecision!;
                LegalAction chosen = decision.LegalActions.Single(legal => legal.Id == action.Submission.ActionId);
                ServerId? target = chosen.Parameters switch
                {
                    RunServerParameters run when chosen.Kind == ActionKind.StartRun => run.ServerId,
                    PlayCardParameters { TargetServerId: { } server } => server,
                    _ => null,
                };
                if (target is { } serverId && state.RunState is null && state.GetPlayer(decision.Owner).Side == PlayerSide.Runner)
                {
                    Observation observation = ObservationBuilder.Build(state, PlayerSide.Runner, definition.Behavior);
                    pending = RunModel.Forecast(observation, catalog, serverId.Value);
                    creditsAtStart = state.Players.Single(player => player.Side == PlayerSide.Runner).Credits;
                    succeeded = false;
                }
                state = GameEngine.Apply(state, action.Submission, definition.Behavior, definition.Runtime, ApplyMode.Simulation).State;
                if (pending is null) continue;
                succeeded |= state.RunState?.Successful == true;
                if (state.RunState is { EndReason: null }) continue;
                if (pending is { UnknownIce: 0, CanGetIn: true } forecast)
                {
                    visible++;
                    if (succeeded) agreed++;
                    int spent = creditsAtStart - state.Players.Single(player => player.Side == PlayerSide.Runner).Credits;
                    if (succeeded && spent >= 0)
                    {
                        creditErrorTotal += Math.Abs(spent - forecast.Credits - forecast.CreditsLost);
                        creditSamples++;
                    }
                }
                else if (pending is { UnknownIce: 0, CanGetIn: false }) predictedBlockedButRan++;
                pending = null;
            }
        }
        double agreement = visible == 0 ? 0 : (double)agreed / visible;
        output.WriteLine($"visible runs={visible} agreed={agreed} ({agreement:P0}) mean credit error={(creditSamples == 0 ? 0 : (double)creditErrorTotal / creditSamples):F2} "
            + $"over {creditSamples} successful runs; runs the model called blocked but the Runner made={predictedBlockedButRan}");
        Assert.True(visible >= 30, $"Only {visible} fully visible runs in the sample.");
        Assert.True(agreement >= 0.85, $"The run model agreed with {agreement:P0} of fully visible runs.");
    }
}

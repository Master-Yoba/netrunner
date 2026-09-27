using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class TurnPreviewTests
{
    [Fact]
    public async Task ThePreviewMatchesTheTurnActionThatFollowsThePass()
    {
        int previews = 0;
        foreach (ulong seed in new ulong[] { 21, 22, 23, 24, 25 })
        {
            FuzzGameCase fuzzCase = RulesFuzzRunner.Plan(1, seed, Content)[0];
            FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(fuzzCase.ContentId, fuzzCase.GameSeed);
            await using var coordinator = new MatchCoordinator(definition, new RandomLegalAgent(seed),
                new MatchCoordinatorOptions { HumanSide = PlayerSide.Corp, AutoPassPriority = true })
            { AutoPassWindows = true };
            await coordinator.StartAsync();
            var agent = new RandomLegalAgent(seed + 50);
            for (int count = 0; count < 250; count++)
            {
                Observation observation = await coordinator.GetReadyObservationAsync(PlayerSide.Corp).WaitAsync(TimeSpan.FromSeconds(20));
                if (coordinator.CurrentSnapshot is not { } snapshot || snapshot.IsTerminal) break;
                if (snapshot.TurnPreview is { } preview)
                {
                    LegalAction pass = observation.LegalActions.First(action => action.Kind == ActionKind.PassPriority);
                    Assert.True((await coordinator.SubmitHumanActionAsync(pass.Id)).Accepted);
                    Observation next = await coordinator.GetReadyObservationAsync(PlayerSide.Corp).WaitAsync(TimeSpan.FromSeconds(20));
                    if (coordinator.CurrentSnapshot is { IsTerminal: true }) break;
                    if (next.Decision?.Kind == DecisionKind.TurnAction)
                    {
                        HashSet<string> real = next.LegalActions.Select(Signature).ToHashSet(StringComparer.Ordinal);
                        Assert.All(preview.Actions, action => Assert.Contains(Signature(action), real));
                        previews++;
                    }
                    observation = next;
                }
                Assert.True((await coordinator.SubmitHumanActionAsync(await agent.ChooseActionAsync(observation))).Accepted);
            }
        }
        if (Official) Assert.True(previews > 0, "No own-turn rez window produced a preview.");
    }

    private static string Signature(LegalAction action) => $"{action.Kind}:{EngineJson.Serialize(action.Parameters)}";

    private static string Signature(LegalActionViewModel action) => $"{action.Kind}:{EngineJson.Serialize(action.Parameters)}";

    private static bool Official => CardDataResource.EmbeddedMode == CardDataResource.OfficialMode;

    private static IReadOnlyList<string> Content => Official ? RulesFuzzRunner.OfficialContentIds : [RulesFuzzRunner.DeveloperContentId];
}

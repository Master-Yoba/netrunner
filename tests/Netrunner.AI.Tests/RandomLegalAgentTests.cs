using Netrunner.Core;
using Xunit;

namespace Netrunner.AI.Tests;

public sealed class RandomLegalAgentTests
{
    private static readonly LegalAction Pass = new(new ActionId("action:pass"), ActionKind.PassPriority,
        "Pass priority", new NoActionParameters());
    private static readonly LegalAction Credit = new(new ActionId("action:credit"), ActionKind.GainCredit,
        "Gain one credit", new NoActionParameters());
    private static readonly LegalAction Draw = new(new ActionId("action:draw"), ActionKind.DrawCard,
        "Draw one card", new NoActionParameters());

    [Fact]
    public async Task SameSeedChoosesTheSameSequence()
    {
        Observation observation = Decision(Pass, Credit, Draw);
        var first = new RandomLegalAgent(7);
        var second = new RandomLegalAgent(7);

        for (int index = 0; index < 50; index++)
        {
            Assert.Equal(await first.ChooseActionAsync(observation), await second.ChooseActionAsync(observation));
        }

        Assert.Equal(first.RandomState, second.RandomState);
    }

    [Fact]
    public async Task ChoosesOnlyIssuedActions()
    {
        Observation observation = Decision(Pass, Credit, Draw);
        var agent = new RandomLegalAgent(11);
        var issued = new HashSet<ActionId> { Pass.Id, Credit.Id, Draw.Id };

        for (int index = 0; index < 200; index++)
        {
            Assert.Contains(await agent.ChooseActionAsync(observation), issued);
        }
    }

    [Fact]
    public async Task PassLikeActionsAreChosenLessOften()
    {
        Observation observation = Decision(Pass, Credit);
        var agent = new RandomLegalAgent(3, activeWeight: 4);
        int passes = 0;
        const int Samples = 2000;

        for (int index = 0; index < Samples; index++)
        {
            if (await agent.ChooseActionAsync(observation) == Pass.Id)
            {
                passes++;
            }
        }

        // Expected pass share is 1 / (1 + 4) = 20%.
        Assert.InRange(passes, Samples * 15 / 100, Samples * 25 / 100);
    }

    [Fact]
    public async Task RejectsDecisionsWithoutActions()
    {
        var agent = new RandomLegalAgent(1);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await agent.ChooseActionAsync(Decision()));
    }

    [Fact]
    public void RejectsNonPositiveActiveWeight()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RandomLegalAgent(1, activeWeight: 0));
    }

    private static Observation Decision(params LegalAction[] actions) => new(
        PlayerSide.Corp,
        "decision:1",
        actions,
        new Dictionary<string, object?>(),
        new Dictionary<string, object?>());
}

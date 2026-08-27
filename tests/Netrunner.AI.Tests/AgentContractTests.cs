using Netrunner.Core;
using Xunit;

namespace Netrunner.AI.Tests;

public sealed class AgentContractTests
{
    [Fact]
    public async Task FirstLegalAgentReturnsEngineIssuedIdentifier()
    {
        LegalAction expected = new(new ActionId("action:gain-credit"), "gain-credit", "Gain one credit");
        Observation observation = new(
            PlayerSide.Corp,
            "decision:1",
            [expected],
            new Dictionary<string, object?>(),
            new Dictionary<string, object?>());

        FirstLegalAgent agent = new();

        Assert.Equal(expected.Id, await agent.ChooseActionAsync(observation));
    }
}

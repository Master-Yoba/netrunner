using Xunit;

namespace Netrunner.Cards.Tests;

public sealed class CardDefinitionTests
{
    [Fact]
    public void StableCodeIsRetainedAsOpaqueText()
    {
        CardDefinition card = new(
            "30001",
            "Example",
            "runner",
            "neutral-runner",
            "event",
            null,
            0,
            null,
            null,
            null,
            "Example text.",
            "system-gateway",
            3);

        Assert.Equal("30001", card.Code);
    }
}

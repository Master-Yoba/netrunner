using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>EL.3 presentation: agenda and flip counters, flipped identities and declared-use credits.</summary>
public sealed class ElevationPresentationTests
{
    private static CardViewModel Identity(params CardCounterViewModel[] counters) =>
        new CardViewModel("card:1", "35057", "Nebula Talent Management: Making Stars", "identity", true, false, 0,
            null, null, null, null, [], string.Empty, "nbn")
        { TypedCounters = counters };

    [Fact]
    public void AgendaAndFlipCountersHaveTheirOwnLabels()
    {
        Assert.Equal("AGENDA ×2", new CardCounterViewModel("agenda", CardCounterPurpose.Generic, 2, null).ChipText);
        Assert.Equal("FLIPPED ×1", new CardCounterViewModel("flipped", CardCounterPurpose.Generic, 1, null).ChipText);
        Assert.Equal(
            "HOSTED CREDIT ×2 · CARD'S OWN USES ONLY",
            new CardCounterViewModel("credit", CardCounterPurpose.HostedCredit, 2, HostedCreditSpendRestriction.DeclaredUseOnly).Summary);
    }

    [Fact]
    public void AFlippedIdentityShowsItsBackFace()
    {
        var front = new IdentityPanelViewModel(PlayerSide.Corp, Identity());
        var back = new IdentityPanelViewModel(PlayerSide.Corp,
            Identity(new CardCounterViewModel("flipped", CardCounterPurpose.Generic, 1, null)));

        Assert.False(front.IsFlipped);
        Assert.True(back.IsFlipped);
        Assert.EndsWith(" · back face", back.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("back face", front.Summary, StringComparison.Ordinal);
    }
}

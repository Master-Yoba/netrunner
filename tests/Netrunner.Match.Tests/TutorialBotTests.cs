using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>The tutorial bot is frozen; the legal decks get the latest bot (Wave 4).</summary>
public sealed class TutorialBotTests
{
    [Theory]
    [InlineData("expanded-expanded", true)]
    [InlineData("basic-expanded", true)]
    [InlineData("gateway-hyper-velocity~gateway-stolen-goods", false)]
    public void PlayerOpponentsUseTheFrozenTutorialBotOnlyOnTeachingDecks(string contentId, bool tutorial)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(contentId, 7);
        Assert.Equal(tutorial, OfflineMatchFactory.IsTutorial(definition));
        OfflineAgent corp = OfflineMatchFactory.CreatePlayerOpponent(definition, PlayerSide.Runner);
        OfflineAgent runner = OfflineMatchFactory.CreatePlayerOpponent(definition, PlayerSide.Corp, "easy");
        Assert.Equal(tutorial ? OfflineMatchFactory.TutorialCorpVersion : CorpHeuristicPolicy.PolicyVersion, corp.Configuration.PolicyVersion);
        Assert.Equal(tutorial ? OfflineMatchFactory.TutorialRunnerVersion : RunnerHeuristicPolicy.PolicyVersion, runner.Configuration.PolicyVersion);
    }
}

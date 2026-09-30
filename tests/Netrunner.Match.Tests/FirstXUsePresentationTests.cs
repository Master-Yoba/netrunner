using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class FirstXUsePresentationTests
{
    [Fact]
    public void AUseInBothPlayersTurnFactsIsShownOnce()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // Self-play review, 28 September: the Runner installed a program with Pantograph during the
        // Corp's turn, using DZMZ Optimizer's first-install discount, so the use is in both players'
        // current-turn facts; the view mapper threw on the "duplicate" and would have stopped the client.
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition("gateway-hyper-velocity~gateway-planning-ahead", 314);
        ReplayArtifact replay = EngineJson.Deserialize<ReplayArtifact>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", "selfplay-pantograph-dzmz-replay.json")), definition.Behavior, definition.Runtime);
        GameState state = replay.InitialSnapshot.ToState();
        var metadata = new CardCatalogMetadataResolver(CardDataResource.LoadEmbedded());
        foreach (ReplayAction action in replay.Actions.TakeWhile(action => action.Sequence <= 440))
            state = GameEngine.Apply(state, action.Submission, definition.Behavior, definition.Runtime).State;
        foreach (PlayerSide side in new[] { PlayerSide.Corp, PlayerSide.Runner })
        {
            MatchViewModel view = ObservationViewMapper.Build(ObservationBuilder.Build(state, side, definition.Behavior, definition.Runtime),
                metadata, definition.Behavior);
            Assert.NotNull(view);
        }
    }
}

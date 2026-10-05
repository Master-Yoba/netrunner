using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// A review of archetype self-play games (28 September, `ai-selfplay`) found
/// the Runner running an advanced unknown card whose trap damage would
/// flatline it, and the Corp's planner installing an agenda it couldn't score
/// into a remote without ICE. The fixed versions are asked at those positions.
/// </summary>
public sealed class SelfPlayReviewTests
{
    [Theory]
    [InlineData("102", "gateway-discretion-advised~gateway-planning-ahead", 102UL, 171L)]
    [InlineData("103", "gateway-advanced-yomi~gateway-party-hard", 103UL, 208L)]
    public async Task TheRunnerDoesNotRunAPossibleLethalTrap(string game, string content, ulong seed, long sequence)
    {
        if (!Official) return;
        (LegalAction chosen, GameState state) = await ChooseAtAsync(game, content, seed, sequence, PlayerSide.Runner, RunnerHeuristicPolicy.V12);
        if (chosen.Parameters is RunServerParameters run)
            Assert.DoesNotContain(state.Zones.Where(zone => zone.ServerId == run.ServerId && zone.Kind == ZoneKind.ServerRoot)
                .SelectMany(zone => zone.CardIds), id => state.GetCard(id).AdvancementCounters > 0 && state.GetCard(id).AdvancementCounters + 2 > state.GetZone(ZoneKind.RunnerGrip).CardIds.Count);
    }

    [Fact]
    public async Task TheCorpDoesNotInstallAnUnscorableAgendaWithoutIce()
    {
        if (!Official) return;
        (LegalAction chosen, GameState state) = await ChooseAtAsync("104", "gateway-hyper-velocity~gateway-stolen-goods", 104, 60,
            PlayerSide.Corp, CorpHeuristicPolicy.V12);
        if (chosen.Parameters is InstallCardParameters install && state.GetCard(install.CardId).DefinitionId.Value is { } code
            && new GatewayEvaluationCatalog(Definition("gateway-hyper-velocity~gateway-stolen-goods", 104).Runtime,
                Definition("gateway-hyper-velocity~gateway-stolen-goods", 104).Behavior).TryGet(new CardDefinitionId(code), out CardEvaluationDescriptor card)
            && card.Agenda is not null)
            Assert.False(install.CreateNewRemote, $"{chosen.Kind} {chosen.Description}");
    }

    private static bool Official => CardDataResource.EmbeddedMode == CardDataResource.OfficialMode;

    private static FullMatchDefinition Definition(string content, ulong seed) => RulesFuzzRunner.CreateDefinition(content, seed);

    private static async Task<(LegalAction Chosen, GameState State)> ChooseAtAsync(string game, string content, ulong seed, long sequence,
        PlayerSide side, string version)
    {
        FullMatchDefinition definition = Definition(content, seed);
        ReplayArtifact replay = SavedDecision.ReadReplay(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures",
            $"selfplay-review-{game}-replay.json"), definition);
        SavedChoice choice = await SavedDecision.ChooseAsync(replay, definition, sequence, side, version);
        return (choice.Chosen, choice.State);
    }
}

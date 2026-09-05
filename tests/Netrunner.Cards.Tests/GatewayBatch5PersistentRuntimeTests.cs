using Netrunner.Core;
using Xunit;

namespace Netrunner.Cards.Tests;

public sealed partial class GatewayBatch4ReactionRuntimeTests
{
    [Theory]
    [InlineData(true, true, true, 2, false)]
    [InlineData(true, true, true, 2, true)]
    [InlineData(true, true, false, 2, false)]
    [InlineData(true, false, true, 0, false)]
    [InlineData(false, true, true, 0, false)]
    [Trait("GatewayCard", "30058")]
    public void AmazeRunEndUsesOnlyThisRunAndPersistsOnlyFromRezzedAccessTrash(
        bool rezzed, bool steal, bool trash, int expectedTags, bool trashBeforeSteal)
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        RuntimeCardCatalog runtime = new("fixture:amaze-runtime",
            new CardRuntimeSpec(GatewayBatch5CardCodes.AmazeAmusements, PlayerSide.Corp, RuntimeCardClass.Upgrade, rezCost: 1),
            new CardRuntimeSpec(GatewayCardCodes.OffworldOffice, PlayerSide.Corp, RuntimeCardClass.Agenda,
                agendaPoints: 2, advancementRequirement: 4));
        GameState initial = BuildState(catalog, runtime, GatewayBatch5CardCodes.AmazeAmusements,
            sourceRezzed: rezzed, runnerCredits: 15);
        CardInstanceId amazeId = initial.Cards.Single().Id;
        if (steal)
        {
            StableIdAllocator allocator = new(initial.Allocator);
            ZoneState root = initial.Zones.Single(zone => zone.Id == initial.Cards.Single().ZoneId);
            CardInstanceState agenda = new(allocator.NextCardInstanceId(), GatewayCardCodes.OffworldOffice,
                root.Id, PlayerSide.Corp, faceUp: false);
            initial = new GameState(initial.SchemaVersion, initial.GameId, initial.RulesVersion, initial.CardDataVersion,
                initial.Players, initial.Cards.Append(agenda).ToArray(),
                initial.Zones.Select(zone => zone.Id == root.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, trashBeforeSteal ? [amazeId, agenda.Id] : [agenda.Id, amazeId]) : zone).ToArray(),
                initial.Servers, initial.Turn, initial.Outcome, initial.RandomState, allocator.State,
                initialSeed: initial.InitialSeed, rulesState: initial.RulesState,
                behaviorRegistryVersion: catalog.Version, behaviorCatalogFingerprint: catalog.Fingerprint,
                runtimeCardCatalogVersion: runtime.Version, runtimeCardCatalogFingerprint: runtime.Fingerprint);
        }
        GameState state = BeginRun(initial, catalog, runtime, ServerKind.Remote);
        bool trashed = false;
        for (int step = 0; step < 100 && state.RunState is not null; step++)
        {
            string json = EngineJson.Serialize(state);
            state = EngineJson.DeserializeState(json, catalog, runtime);
            Assert.Equal(json, EngineJson.Serialize(state));
            PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction? action = trash
                ? decision.LegalActions.FirstOrDefault(value => value.Kind == ActionKind.TrashAccessedCard) : null;
            if (action is not null) trashed = true;
            action ??= decision.LegalActions.FirstOrDefault(value => value.Kind == ActionKind.PassPriority)
                ?? decision.LegalActions.FirstOrDefault(value => value.Kind == ActionKind.ContinueRun)
                ?? decision.LegalActions.FirstOrDefault(value => value.Kind == ActionKind.NoAccessAction)
                ?? decision.LegalActions[0];
            ApplyResult result = Apply(state, action, catalog, runtime);
            Assert.True(result.Accepted, result.Rejection?.Message);
            state = result.State;
        }
        Assert.Null(state.RunState);
        Assert.Equal(expectedTags, Tags(state, PlayerSide.Runner));
        Assert.Equal(trash, trashed);
        if (trash) Assert.Equal(ZoneKind.CorpDiscard, state.GetZone(state.Cards.Single(value => value.Id == amazeId).ZoneId).Kind);
        // A separate run carries no stolen-agenda history from the first run.
        state = BeginRun(state, catalog, runtime, ServerKind.Headquarters);
        for (int step = 0; step < 100 && state.RunState is not null; step++)
        {
            LegalAction action = state.PendingDecision!.LegalActions.FirstOrDefault(value => value.Kind == ActionKind.PassPriority)
                ?? state.PendingDecision.LegalActions.FirstOrDefault(value => value.Kind == ActionKind.ContinueRun)
                ?? state.PendingDecision.LegalActions[0];
            ApplyResult result = Apply(state, action, catalog, runtime);
            Assert.True(result.Accepted, result.Rejection?.Message);
            state = result.State;
        }
        Assert.Equal(expectedTags, Tags(state, PlayerSide.Runner));
    }
}

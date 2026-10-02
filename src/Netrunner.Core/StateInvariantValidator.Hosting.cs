namespace Netrunner.Core;

public static partial class StateInvariantValidator
{
    private static void ValidateHosting(GameState state, IBehaviorCatalog catalog,
        IRuntimeCardCatalog? runtimeCatalog, List<StateInvariantViolation> violations)
    {
        foreach (CardInstanceState card in state.Cards)
        {
            ZoneState? zone = state.Zones.FirstOrDefault(value => value.Id == card.ZoneId);
            bool requiresHost = catalog.TryGet(card.DefinitionId, out CardBehaviorDefinition? definition)
                && definition.Hosting is not null;
            if (requiresHost && zone?.Kind == ZoneKind.RunnerRig && card.HostCardId is null)
            {
                Add(violations, "hosting.missing-host", "cards", "An installed host-only program must identify its host ICE.");
            }
            // EL-HOSTED-FACEUP: faceup-hosted cards are checked with their zone (cards.hosted-faceup).
            if (card.HostCardId is not { } hostId
                || zone?.Kind is ZoneKind.RunnerHostedFaceUp or ZoneKind.CorpHostedFaceUp)
            {
                continue;
            }
            CardInstanceState? host = state.Cards.FirstOrDefault(value => value.Id == hostId);
            ZoneState? hostZone = state.Zones.FirstOrDefault(value => value.Id == host?.ZoneId);
            if (!requiresHost || hostId == card.Id || host is null || host.OwnerSide != PlayerSide.Corp
                || hostZone?.Kind != ZoneKind.ServerIce || card.OwnerSide != PlayerSide.Runner
                || zone?.Kind != ZoneKind.RunnerRig
                || runtimeCatalog is not null && (!runtimeCatalog.TryGet(host.DefinitionId, out CardRuntimeSpec? spec)
                    || spec.CardClass != RuntimeCardClass.Ice))
            {
                Add(violations, "hosting.invalid-host", "cards", "Hosted programs require a hosting definition and a distinct installed Corp ICE host.");
            }
        }
    }
}

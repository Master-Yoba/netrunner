namespace Netrunner.Core;

public static partial class StateInvariantValidator
{
    private static void ValidateSetupAndIdentities(GameState state, IRuntimeCardCatalog? runtime, List<StateInvariantViolation> violations)
    {
        SetupFrame? setup = state.ResolutionStack.OfType<SetupFrame>().FirstOrDefault();
        bool hasIdentities = state.Zones.Any(zone => zone.Kind is ZoneKind.CorpIdentity or ZoneKind.RunnerIdentity);
        if (setup is not null)
        {
            if (state.Turn.Phase != TurnPhase.Setup || state.Turn.ActiveSide != PlayerSide.Corp || state.Turn.TurnNumber != 1
                || state.ResolutionStack.Count != 1 || state.RunState is not null
                || state.Players.Any(player => player.Credits != 5 || player.Clicks != 0 || player.AgendaPoints != 0)
                || state.PendingDecision is not { Kind: DecisionKind.Setup } pending
                || state.Players.FirstOrDefault(player => player.Side == setup.Side)?.Id != pending.Owner)
            {
                Add(violations, "setup.invalid-context", "resolutionStack", "Setup requires an opening-state decision owned by its current chooser.");
            }
        }
        else if (state.PendingDecision?.Kind == DecisionKind.Setup)
        {
            Add(violations, "setup.missing-frame", "pendingDecision", "A setup decision requires a setup frame.");
        }

        if (!hasIdentities && setup is null)
        {
            return;
        }

        foreach (PlayerSide side in Enum.GetValues<PlayerSide>())
        {
            ZoneKind identityKind = side == PlayerSide.Corp ? ZoneKind.CorpIdentity : ZoneKind.RunnerIdentity;
            ZoneState? identity = state.Zones.FirstOrDefault(zone => zone.Kind == identityKind);
            CardInstanceState? card = identity?.CardIds.Count == 1
                ? state.Cards.FirstOrDefault(value => value.Id == identity.CardIds[0]) : null;
            if (state.Zones.Count(zone => zone.Kind == identityKind) != 1 || identity?.CardIds.Count != 1
                || identity.OwnerSide != side || identity.ServerId is not null
                || card is not { FaceUp: true, Rezzed: false, AdvancementCounters: 0 } || card.OwnerSide != side
                || runtime is not null && (!runtime.TryGet(card.DefinitionId, out CardRuntimeSpec? spec)
                    || spec.CardClass != RuntimeCardClass.Identity || spec.Side != side))
            {
                Add(violations, "setup.invalid-identity", "zones", "A match requires exactly one faceup identity in each side's identity zone.");
            }

            ZoneKind handKind = side == PlayerSide.Corp ? ZoneKind.CorpHand : ZoneKind.RunnerGrip;
            if (setup is not null && state.Zones.FirstOrDefault(zone => zone.Kind == handKind)?.CardIds.Count != 5)
            {
                Add(violations, "setup.invalid-deal", "zones", "Both starting hands must contain exactly five cards.");
            }
        }
    }
}

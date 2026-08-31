namespace Netrunner.Core;

/// <summary>Install and agenda play reducers for the legacy toy rules.</summary>
public static partial class GameEngine
{
    private static void InstallToyCard(WorkingState working, PlayerSide side, CardInstanceId cardId)
    {
        CardInstanceState card = GetCard(working, cardId);
        ToyCardBehavior behavior = ToyCardBehaviorCatalog.Get(card.DefinitionId);
        ZoneKind handKind = side == PlayerSide.Corp ? ZoneKind.CorpHand : ZoneKind.RunnerGrip;
        ZoneState hand = GetZone(working, handKind);
        if (card.OwnerSide != side || card.ZoneId != hand.Id)
        {
            throw new ResolutionFailureException($"Card '{cardId}' is not in the active player's hand.");
        }

        if (behavior.OwnerSide != side
            || (side == PlayerSide.Corp && behavior.Kind != ToyCardKind.CorpAgenda)
            || (side == PlayerSide.Runner && behavior.Kind != ToyCardKind.RunnerProgram))
        {
            throw new ResolutionFailureException($"Card '{cardId}' is not installable by '{side}'.");
        }

        PlayerState player = GetPlayer(working, side);
        if (player.Credits < behavior.InstallCost)
        {
            throw new ResolutionFailureException($"Installing '{cardId}' requires {behavior.InstallCost} credits.");
        }

        ZoneState destination = side == PlayerSide.Corp
            ? GetRemoteRoot(working)
            : GetZone(working, ZoneKind.RunnerRig);
        if (side == PlayerSide.Corp && destination.CardIds.Count > 0)
        {
            throw new ResolutionFailureException("The fixture remote already contains an installed card.");
        }

        MoveCard(working, card, hand, destination);
        ApplyCreditDelta(working, player.Id, -behavior.InstallCost);
        Emit(working, EngineEventKinds.CardInstalled, new CardInstalledEventPayload(
            side,
            cardId,
            hand.Id,
            destination.Id,
            behavior.InstallCost));
    }

}

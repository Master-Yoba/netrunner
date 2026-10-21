namespace Netrunner.Core;

public static partial class GameEngine
{
    /// <summary>
    /// A terminal checkpoint cancels suspended play/run continuations. Finish
    /// their transient card movement without dispatching run-end abilities.
    /// </summary>
    private static void CleanupTerminalPlayAreas(WorkingState working)
    {
        foreach (ZoneState playArea in working.Zones.Where(zone => zone.Kind is ZoneKind.CorpPlayArea or ZoneKind.RunnerPlayArea).ToArray())
        {
            foreach (CardInstanceId cardId in playArea.CardIds)
            {
                CardInstanceState card = GetCard(working, cardId);
                ZoneState source = ZoneForCard(working, cardId);
                ZoneState discard = GetZone(working, card.OwnerSide == PlayerSide.Corp ? ZoneKind.CorpDiscard : ZoneKind.RunnerHeap);
                MoveCard(working, card, source, discard);
                ClearCardCounters(working, cardId);
            }
        }
        working.RunState = null;
    }
}

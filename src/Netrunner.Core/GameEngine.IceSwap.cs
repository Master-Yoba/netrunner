namespace Netrunner.Core;

public static partial class GameEngine
{
    private static void QueueRunTagEffect(WorkingState working, int amount) =>
        working.ResolutionStack.Add(new EffectResolutionFrame(
            GetPlayer(working, PlayerSide.Corp).Id, new CostBundle(),
            new EffectBundle(new TagAdjustmentEffect(GetPlayer(working, PlayerSide.Runner).Id, amount))));

    private static void ResolveRunAccessCredits(WorkingState working, BehaviorResolutionFrame frame)
    {
        if (frame.TriggerKey is null
            || working.ResolutionStack.Count < 2
            || working.ResolutionStack[^2] is not EffectCheckpointFrame checkpoint
            || checkpoint.Occurrence.Effect is not GameplayOccurrenceEffect
                { Kind: OccurrenceKind.RunEnded, RunContext: { } context })
        {
            throw new ResolutionFailureException("Per-access credits require a serialized run-end trigger context.");
        }

        ApplyCreditDeltaIfNonZero(working, frame.Controller, context.AccessOccurrenceCount);
    }

    private static void ResolveSwapInstalledIce(
        WorkingState working, BehaviorResolutionFrame frame, BehaviorInstruction instruction)
    {
        CardInstanceId[] selected = ResolveBehaviorCards(working, frame, instruction.Target).ToArray();
        if (selected.Length != 2 || selected[0] == selected[1])
            throw new ResolutionFailureException("An ICE swap requires two distinct installed pieces of ICE.");
        CardInstanceState first = GetCard(working, selected[0]);
        CardInstanceState second = GetCard(working, selected[1]);
        ZoneState firstZone = ZoneForCard(working, first.Id);
        ZoneState secondZone = ZoneForCard(working, second.Id);
        if (firstZone.Kind != ZoneKind.ServerIce || secondZone.Kind != ZoneKind.ServerIce
            || firstZone.ServerId is null || secondZone.ServerId is null)
            throw new ResolutionFailureException("An ICE swap cannot select uninstalled cards.");

        int firstIndex = firstZone.CardIds.ToList().IndexOf(first.Id);
        int secondIndex = secondZone.CardIds.ToList().IndexOf(second.Id);
        // A swap is simultaneous movement, not uninstall/reinstall: preserve
        // activation, counters, visibility, and stable host relationships.
        ReplaceZone(working, new ZoneState(firstZone.Id, firstZone.Kind, firstZone.OwnerSide,
            firstZone.ServerId, firstZone.CardIds.Select(id => id == first.Id ? second.Id
                : firstZone.Id == secondZone.Id && id == second.Id ? first.Id : id).ToArray()));
        if (firstZone.Id != secondZone.Id)
        {
            ReplaceZone(working, new ZoneState(secondZone.Id, secondZone.Kind, secondZone.OwnerSide,
                secondZone.ServerId, secondZone.CardIds.Select(id => id == second.Id ? first.Id : id).ToArray()));
            ReplaceCard(working, new CardInstanceState(first.Id, first.DefinitionId, secondZone.Id,
                first.OwnerSide, first.FaceUp, first.Rezzed, first.AdvancementCounters, first.HostCardId));
            ReplaceCard(working, new CardInstanceState(second.Id, second.DefinitionId, firstZone.Id,
                second.OwnerSide, second.FaceUp, second.Rezzed, second.AdvancementCounters, second.HostCardId));
        }

        // Score/steal reactions occur outside an encounter. During breach the
        // Runner remains at the server (index -1), regardless of either swap.
        // If used at an approach/movement boundary, retain the physical slot
        // and update its stable ICE identity instead of following a moved ICE.
        if (working.RunState is { CurrentIceIndex: >= 0 } run
            && (run.AttackedServer == firstZone.ServerId || run.AttackedServer == secondZone.ServerId))
        {
            ZoneState attackedIce = working.Zones.Single(zone => zone.Kind == ZoneKind.ServerIce
                && zone.ServerId == run.AttackedServer);
            CardInstanceId current = attackedIce.CardIds[run.CurrentIceIndex];
            if (run.Encounter is not null && current != run.CurrentIceId)
                throw new ResolutionFailureException("An active encounter cannot be replaced by a score/steal ICE swap.");
            working.RunState = run.WithTiming(run.Step, run.CurrentIceIndex, current,
                run.Successful, run.Unsuccessful, run.EndReason, run.Priority, run.Encounter, run.AccessState);
        }

        Emit(working, EngineEventKinds.IceSwapped, new IceSwappedEventPayload(
            first.Id, firstZone.Id, firstIndex, second.Id, secondZone.Id, secondIndex));
    }
}

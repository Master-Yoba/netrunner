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

    /// <summary>
    /// EL-SWAP-APPROACHED-ICE (Mitra Aman): the approached ICE leaves for the
    /// zone the chosen ICE came from (HQ or Archives); the chosen ICE takes its
    /// position, installed unrezzed, and is the ICE the Runner now approaches.
    /// </summary>
    private static void ResolveSwapApproachedIce(
        WorkingState working, BehaviorResolutionFrame frame, BehaviorInstruction instruction)
    {
        IReadOnlyList<CardInstanceId> chosen = instruction.Target is { Kind: BehaviorTargetKind.ChosenCard } chosenTarget
            && IsProvenEmptyChosenCard(frame, chosenTarget)
                ? []
                : ResolveBehaviorCards(working, frame, instruction.Target);
        if (chosen.Count == 0
            || working.RunState is not { CurrentIceId: { } approachedId, CurrentIceIndex: >= 0 } run)
        {
            AdvanceBehaviorCursor(working, frame);
            return;
        }

        CardInstanceState replacement = GetCard(working, chosen[0]);
        ZoneKind originKind = OriginalZoneKind(working, frame, replacement.Id);
        if (originKind is not (ZoneKind.CorpHand or ZoneKind.CorpDiscard)
            || working.RuntimeCardCatalog?.GetSpec(replacement.DefinitionId).CardClass != RuntimeCardClass.Ice)
        {
            throw new ResolutionFailureException("The approached ICE is swapped with a piece of ICE from HQ or Archives.");
        }

        ServerState server = working.Servers.Single(candidate => candidate.Id == run.AttackedServer);
        ZoneState iceZone = working.Zones.Single(zone => zone.Id == server.IceZoneId);
        int index = run.CurrentIceIndex;
        MoveCard(working, GetCard(working, approachedId), iceZone, GetZone(working, originKind));
        MoveCard(working, replacement, ZoneForCard(working, replacement.Id), working.Zones.Single(zone => zone.Id == server.IceZoneId));
        ZoneState placed = working.Zones.Single(zone => zone.Id == server.IceZoneId);
        List<CardInstanceId> ordered = placed.CardIds.Where(id => id != replacement.Id).ToList();
        ordered.Insert(index, replacement.Id);
        ReplaceZone(working, new ZoneState(placed.Id, placed.Kind, placed.OwnerSide, placed.ServerId, ordered));
        CardInstanceState installed = GetCard(working, replacement.Id);
        ReplaceCard(working, new CardInstanceState(installed.Id, installed.DefinitionId, installed.ZoneId,
            installed.OwnerSide, faceUp: false, rezzed: false, installed.AdvancementCounters));

        RunState current = working.RunState!;
        working.RunState = current.WithTiming(
            current.Step,
            index,
            replacement.Id,
            current.Successful,
            current.Unsuccessful,
            current.EndReason,
            current.Priority is { } priority
                ? new RunPriorityState(priority.Version, priority.Window, priority.PriorityHolder, priority.ConsecutivePasses,
                    corpMayRezApproachedIce: true, corpMayRezNonIce: priority.CorpMayRezNonIce)
                : null,
            current.Encounter,
            current.AccessState);

        var remainingSetAside = frame.SetAside.Where(selected => selected.CardId != replacement.Id).ToList();
        if (remainingSetAside.Count == 0
            && frame.SetAsideZoneId is { } emptiedId
            && working.Zones.SingleOrDefault(zone => zone.Id == emptiedId) is { CardIds.Count: 0 } emptied)
        {
            working.Zones.Remove(emptied);
        }

        AdvanceBehaviorCursor(working, frame.With(setAside: remainingSetAside, clearSetAsideZone: remainingSetAside.Count == 0));
    }

    /// <summary>VP-SWAP-WITH: the swap's named first piece of ICE is its source (ezaM: "Swap this ice").</summary>
    internal const string SwapWithSelf = "self";

    /// <summary>
    /// VP-SWAP-WITH: a behavior whose instructions swap its own source may finish with that source in another ICE
    /// position. Shared with the validator.
    /// </summary>
    internal static bool MaySwapItsSource(CardBehaviorDefinition definition, string? abilityKey, ZoneKind currentZone) =>
        currentZone == ZoneKind.ServerIce
        && BehaviorRunRules.FlattenInstructions(abilityKey is null
                ? definition.Instructions
                : definition.PaidAbilities.FirstOrDefault(ability => string.Equals(ability.Key, abilityKey, StringComparison.Ordinal))?.Instructions ?? [])
            .Any(static instruction => instruction.Kind == BehaviorInstructionKind.SwapInstalledIce
                && string.Equals(instruction.Argument, SwapWithSelf, StringComparison.Ordinal));

    private static void ResolveSwapInstalledIce(
        WorkingState working, BehaviorResolutionFrame frame, BehaviorInstruction instruction)
    {
        CardInstanceId[] selected = instruction.Argument is not null
            && instruction.Target is { Kind: BehaviorTargetKind.ChosenCard } chosenTarget
            && IsProvenEmptyChosenCard(frame, chosenTarget)
                ? []
                : ResolveBehaviorCards(working, frame, instruction.Target).ToArray();
        // VP-SWAP-WITH: the first piece of ICE named by the instruction: the source, or a binding (the trigger subject).
        if (instruction.Argument is { } namedKey)
        {
            CardInstanceId[] named = string.Equals(namedKey, SwapWithSelf, StringComparison.Ordinal)
                ? [frame.SourceCardId]
                : frame.Bindings.SingleOrDefault(binding => string.Equals(binding.Key, namedKey, StringComparison.Ordinal))?.CardIds.ToArray() ?? [];
            if (selected.Length == 0 || named.SequenceEqual(selected))
            {
                // "Another installed piece of ICE" may be none, or the named one itself: nothing is swapped.
                return;
            }

            selected = [.. named, .. selected];
        }

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
            // VP-SWAP-WITH: a completed encounter's ICE may move once passed (Sipa swaps the ICE it just passed).
            if (run.Encounter is { Status: RunEncounterStatus.Initialized } && current != run.CurrentIceId)
                throw new ResolutionFailureException("An active encounter cannot be replaced by a score/steal ICE swap.");
            // A passed ICE swapped away leaves the cursor empty, as a passed ICE trashed does; the Runner moves on by position.
            CardInstanceId? cursor = run.Encounter is { Status: RunEncounterStatus.Complete } passed && current != passed.IceId
                && run.CurrentIceId == passed.IceId
                    ? null
                    : current;
            working.RunState = run.WithTiming(run.Step, run.CurrentIceIndex, cursor,
                run.Successful, run.Unsuccessful, run.EndReason, run.Priority, run.Encounter, run.AccessState);
        }

        Emit(working, EngineEventKinds.IceSwapped, new IceSwappedEventPayload(
            first.Id, firstZone.Id, firstIndex, second.Id, secondZone.Id, secondIndex));
    }
}

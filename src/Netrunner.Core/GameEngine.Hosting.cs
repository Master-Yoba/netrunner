namespace Netrunner.Core;

public static partial class GameEngine
{
    private static HostingBehaviorSpec? HostingSpec(WorkingState working, CardInstanceState card) =>
        working.BehaviorCatalog.TryGet(card.DefinitionId, out CardBehaviorDefinition? definition)
            ? definition.Hosting
            : null;

    private static bool NeedsInstallHostChoice(WorkingState working, InstallFrame frame) =>
        frame.HostCardId is null && HostingSpec(working, GetCard(working, frame.CardId)) is not null;

    private static IEnumerable<CardInstanceId?> InstallHosts(WorkingState working, CardInstanceState card)
    {
        if (HostingSpec(working, card) is null)
        {
            yield return null;
            yield break;
        }
        foreach (CardInstanceState host in working.Cards)
        {
            if (IsInstalledIceHost(working, host.Id))
            {
                yield return host.Id;
            }
        }
    }

    private static bool IsInstalledIceHost(WorkingState working, CardInstanceId hostId)
    {
        CardInstanceState? host = working.Cards.FirstOrDefault(card => card.Id == hostId);
        return host is not null && host.OwnerSide == PlayerSide.Corp
            && working.Zones.Any(zone => zone.Id == host.ZoneId && zone.Kind == ZoneKind.ServerIce)
            && working.RuntimeCardCatalog is not null
            && working.RuntimeCardCatalog.TryGet(host.DefinitionId, out CardRuntimeSpec? spec)
            && spec.CardClass == RuntimeCardClass.Ice;
    }

    private static bool IsLegalInstallHost(WorkingState working, CardInstanceId cardId, CardInstanceId? hostId) =>
        HostingSpec(working, GetCard(working, cardId)) is null
            ? hostId is null
            : hostId is { } id && id != cardId && IsInstalledIceHost(working, id);

    private static void TrashHostedDescendants(WorkingState working, CardInstanceId hostId)
    {
        foreach (CardInstanceId childId in working.Cards.Where(card => card.HostCardId == hostId)
                     .Select(card => card.Id).ToArray())
        {
            CardInstanceState child = GetCard(working, childId);
            ZoneState source = working.Zones.Single(zone => zone.Id == child.ZoneId);
            ZoneState discard = GetZone(working,
                child.OwnerSide == PlayerSide.Runner ? ZoneKind.RunnerHeap : ZoneKind.CorpDiscard);
            TrashCard(working, child, source, discard);
            if (working.RulesState?.Programs.Any(program => program.CardId == childId) == true)
            {
                RemoveProgramRegistration(working, childId);
            }
        }
    }

    private static void CheckHostedVirusThreshold(WorkingState working, CardInstanceId cardId, int count)
    {
        CardInstanceState card = GetCard(working, cardId);
        if (!card.FaceUp || card.HostCardId is not { } hostId
            || HostingSpec(working, card)?.DerezAtVirusCounters is not { } threshold
            || count < threshold || !IsInstalledIceHost(working, hostId))
        {
            return;
        }
        CardInstanceState host = GetCard(working, hostId);
        if (!host.Rezzed)
        {
            return;
        }
        ReplaceCard(working, new CardInstanceState(host.Id, host.DefinitionId, host.ZoneId,
            host.OwnerSide, false, false, host.AdvancementCounters, host.HostCardId));
        Emit(working, EngineEventKinds.CardDerezzed, new CardDerezzedEventPayload(hostId));
    }

    internal static bool CanBreakHostedCounter(GameState state, IBehaviorCatalog catalog, CardInstanceState card, RunState run) =>
        CanBreakHostSubroutine(WorkingState.From(state, catalog), card, run);

    private static bool CanBreakHostSubroutine(WorkingState working, CardInstanceState card, RunState run) =>
        card.FaceUp && card.OwnerSide == PlayerSide.Runner
        && card.HostCardId == run.Encounter?.IceId
        && HostingSpec(working, card)?.BreakHostSubroutine == true
        && working.Zones.Any(zone => zone.Id == card.ZoneId && zone.Kind == ZoneKind.RunnerRig)
        && (working.RulesState?.Counters.Any(counter => counter.CardId == card.Id
            && counter.CounterName == "virus" && counter.Count > 0) ?? false)
        && (run.Encounter?.Subroutines.Any(subroutine => !subroutine.Broken && !subroutine.Resolved) ?? false);

    private static void ConsumeHostedCounterBreak(WorkingState working, RunState run, CardInstanceState card,
        int quantity, int hostedCredits, CostQuote? costQuote, SubroutineId? subroutineId)
    {
        if (quantity != 1 || hostedCredits != 0 || costQuote is not null || !CanBreakHostSubroutine(working, card, run))
        {
            throw new ResolutionFailureException("Hosted counter break requires one virus counter and an unbroken host subroutine.");
        }
        RunEncounterState encounter = run.Encounter!;
        if (subroutineId is not { } target || !encounter.Subroutines.Any(subroutine =>
            subroutine.Id == target && !subroutine.Broken && !subroutine.Resolved))
        {
            throw new ResolutionFailureException("Hosted counter break must identify one unbroken host subroutine.");
        }
        AdjustCounter(working, card.Id, "virus", -1);
        RunState broken = run.WithEncounter(new RunEncounterState(encounter.Version, encounter.Id,
            encounter.IceId, encounter.Status, encounter.Subroutines.Select(subroutine => subroutine.Id == target
                ? new RunSubroutineState(subroutine.Version, subroutine.Id, subroutine.Ordinal, true, subroutine.Resolved)
                : subroutine).ToArray()));
        Emit(working, EngineEventKinds.SubroutinesBroken,
            new SubroutinesBrokenEventPayload(run.Id, encounter.Id, card.Id, 1));
        RunPriorityState priority = run.Priority!;
        SetRunTiming(working, broken.WithTiming(RunTimingStep.EncounterPaidWindow,
            broken.CurrentIceIndex, broken.CurrentIceId, broken.Successful, broken.Unsuccessful, broken.EndReason,
            new RunPriorityState(priority.Version, RunPriorityWindowKind.EncounterPaid, PlayerSide.Runner, 0,
                priority.CorpMayRezApproachedIce, priority.CorpMayRezNonIce), broken.Encounter, broken.AccessState));
    }
}

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
        Derez(working, hostId);
    }

    /// <summary>Derezzes an installed rezzed Corp card: it turns facedown and stays installed.</summary>
    private static void Derez(WorkingState working, CardInstanceId cardId)
    {
        CardInstanceState card = GetCard(working, cardId);
        if (!card.Rezzed || card.OwnerSide != PlayerSide.Corp || !IsInstalledCard(working, cardId))
        {
            return;
        }

        ReplaceCard(working, new CardInstanceState(card.Id, card.DefinitionId, card.ZoneId,
            card.OwnerSide, false, false, card.AdvancementCounters, card.HostCardId));
        Emit(working, EngineEventKinds.CardDerezzed, new CardDerezzedEventPayload(cardId));
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
        && (run.Encounter?.Subroutines.Any(subroutine => !subroutine.Broken && !subroutine.Resolved) ?? false)
        && BreakRestrictionAllows(working, card, run);

    /// <summary>
    /// EL-BREAK-RESTRICTION: ICE whose subroutines only a breaker of one subtype may break (Semak-samun's fracter) can't be
    /// broken with a hosted program's counters (Botulus) unless that program has the subtype.
    /// </summary>
    private static bool BreakRestrictionAllows(WorkingState working, CardInstanceState card, RunState run) =>
        run.Encounter is not { } encounter
        || !working.BehaviorCatalog.TryGetIce(GetCard(working, encounter.IceId).DefinitionId, out IceBehaviorSpec ice)
        || ice.BreakableOnlyBySubtype is not { } required
        || CardSubtypes(working.RuntimeCardCatalog, card.DefinitionId).Contains(required, StringComparer.Ordinal);

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

    /// <summary>
    /// EL-HOSTED-FACEUP: hosts a card faceup on a host without installing it
    /// (Bling, Madani, Detente). Hosted cards stay in their owner's hosted
    /// zone, name their host, and are trashed when the host leaves play.
    /// </summary>
    private static void HostFaceUp(WorkingState working, CardInstanceId cardId, CardInstanceId hostId)
    {
        CardInstanceState card = GetCard(working, cardId);
        ZoneState source = ZoneForCard(working, cardId);
        ZoneState hosted = GetBehaviorZone(working, BehaviorZone.HostedFaceUp, card.OwnerSide);
        if (source.Id != hosted.Id)
        {
            MoveCard(working, card, source, hosted);
        }

        CardInstanceState moved = GetCard(working, cardId);
        ReplaceCard(working, new CardInstanceState(moved.Id, moved.DefinitionId, moved.ZoneId, moved.OwnerSide,
            faceUp: true, rezzed: false, moved.AdvancementCounters, hostId));
    }

    /// <summary>EL-ICE-GAINS-SUBTYPE: a card installed on this ICE gives it every ICE subtype (Chromatophores).</summary>
    internal static bool IceGainsEveryType(IEnumerable<CardInstanceState?> cards, IBehaviorCatalog catalog, CardInstanceId iceId) =>
        cards.Any(card => card is not null
            && card.HostCardId == iceId
            && catalog.TryGet(card.DefinitionId, out CardBehaviorDefinition? definition)
            && definition.Hosting?.HostGainsEveryIceType == true);

    /// <summary>A card's printed subtypes, lower-cased by the runtime catalog; none without one.</summary>
    internal static IReadOnlyCollection<string> CardSubtypes(IRuntimeCardCatalog? runtime, CardDefinitionId definitionId) =>
        runtime is not null && runtime.TryGet(definitionId, out CardRuntimeSpec? spec) ? spec.Subtypes.ToArray() : [];

    /// <summary>EL-COPY-OF-ACCESSED: another card with this card's definition is faceup in Archives.</summary>
    internal static bool IsCopyOfFaceUpArchivesCard(
        IEnumerable<CardInstanceState?> cards,
        IEnumerable<ZoneState?> zones,
        CardInstanceState card)
    {
        ZoneState? archives = zones.FirstOrDefault(zone => zone?.Kind == ZoneKind.CorpDiscard && zone.ServerId is null);
        return archives is not null
            && cards.Any(candidate => candidate is not null
                && candidate.Id != card.Id
                && candidate.FaceUp
                && candidate.DefinitionId == card.DefinitionId
                && archives.CardIds.Contains(candidate.Id));
    }

    /// <summary>EL-MOD-HOST: an installed Runner icebreaker without the AI subtype.</summary>
    private static bool IsModHost(WorkingState working, CardInstanceId hostId) =>
        IsModHost(working.Cards, working.Zones, working.BehaviorCatalog, working.RuntimeCardCatalog, hostId);

    internal static bool IsModHost(
        IEnumerable<CardInstanceState?> cards,
        IEnumerable<ZoneState?> zones,
        IBehaviorCatalog catalog,
        IRuntimeCardCatalog? runtime,
        CardInstanceId hostId) =>
        cards.FirstOrDefault(card => card?.Id == hostId) is { OwnerSide: PlayerSide.Runner } host
        && zones.FirstOrDefault(zone => zone?.Id == host.ZoneId)?.Kind == ZoneKind.RunnerRig
        && catalog.TryGetIcebreaker(host.DefinitionId, out _)
        && !CardSubtypes(runtime, host.DefinitionId).Contains("ai", StringComparer.Ordinal);

    /// <summary>EL-STRENGTH-PERSISTENCE: a mod on this breaker makes its pumped strength last the run.</summary>
    private static bool BreakerPumpsLastTheRun(WorkingState working, CardInstanceId breakerId) =>
        working.Cards.Any(card => card.HostCardId == breakerId
            && working.BehaviorCatalog.TryGet(card.DefinitionId, out CardBehaviorDefinition? definition)
            && definition.HostIcebreakerStrength > 0);

    internal static bool IsHostedFaceUpZone(ZoneKind kind) => kind is ZoneKind.RunnerHostedFaceUp or ZoneKind.CorpHostedFaceUp;

    /// <summary>
    /// EL-HOSTED-FACEUP: a Runner card hosted faceup on an installed card that
    /// lets the Runner play or install its hosted cards as if they were in the
    /// grip (Bling).
    /// </summary>
    private static bool IsGripLikeHosted(WorkingState working, CardInstanceState card)
    {
        if (card.OwnerSide != PlayerSide.Runner
            || card.HostCardId is not { } hostId
            || working.Zones.FirstOrDefault(zone => zone.Id == card.ZoneId)?.Kind != ZoneKind.RunnerHostedFaceUp)
        {
            return false;
        }

        CardInstanceState? host = working.Cards.FirstOrDefault(candidate => candidate.Id == hostId);
        return host is not null
            && working.Zones.FirstOrDefault(zone => zone.Id == host.ZoneId)?.Kind == ZoneKind.RunnerRig
            && working.BehaviorCatalog.TryGet(host.DefinitionId, out CardBehaviorDefinition? definition)
            && definition.HostedCardsPlayAsGrip;
    }

    private static CardInstanceId[] GripLikeHostedCardIds(WorkingState working)
    {
        ZoneState? hosted = working.Zones.FirstOrDefault(zone => zone.Kind == ZoneKind.RunnerHostedFaceUp && zone.ServerId is null);
        return hosted is null
            ? []
            : hosted.CardIds
                .Where(cardId => IsGripLikeHosted(working, GetCard(working, cardId)))
                .OrderBy(id => id.Value, StringComparer.Ordinal)
                .ToArray();
    }
}

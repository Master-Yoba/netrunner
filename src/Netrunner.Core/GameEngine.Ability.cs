using System.Collections.ObjectModel;

namespace Netrunner.Core;

/// <summary>
/// Materialization-only portion of the Slice 4.3 ability reducer.  The
/// action projection intentionally has no allocator side effects: reducers
/// and invariant validation can ask for the same candidate set and compare
/// the complete immutable payload.
/// </summary>
public static partial class GameEngine
{
    // Keep additional-cost materialization deterministic and bounded. The
    // cap is deliberately small enough to keep an observation responsive,
    // while still covering ordinary Gateway grip sizes and future costs.
    private const int MaxAdditionalCostCombinations = 256;

    // A result cap alone is not enough: overlapping additional costs can
    // enumerate a large number of rejected branches without ever producing
    // more than the result cap. Count both recursion nodes and candidate
    // selections so materialization fails closed before that search becomes
    // unbounded.
    private const int MaxAdditionalCostExplorations = 4096;

    /// <summary>
    /// Re-materializes every legal behavior-owned ability payload for the
    /// current formal turn/run window. The returned order is the physical
    /// zone order, card order within that zone, and declaration order within
    /// the card's behavior definition.
    /// </summary>
    internal static IReadOnlyList<AbilityActionParameters> MaterializeAbilityActionParameters(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        return new ReadOnlyCollection<AbilityActionParameters>(
            MaterializeAbilityActionParameters(WorkingState.From(state, behaviorCatalog, runtimeCardCatalog)).ToArray());
    }

    private static List<AbilityActionParameters> MaterializeAbilityActionParameters(WorkingState working)
    {
        if (!TryGetAbilityWindow(working, out AbilityWindow window))
        {
            return [];
        }

        TurnFactsState? activeFacts = CurrentAbilityFacts(working);
        if (activeFacts is null)
        {
            return [];
        }

        var materialized = new List<AbilityActionParameters>();
        foreach ((CardInstanceState source, ZoneState sourceZone) in EnumerateAbilitySources(working))
        {
            bool inRunnerScoreArea = sourceZone.Kind == ZoneKind.RunnerScore;
            bool inArchives = sourceZone.Kind == ZoneKind.CorpDiscard;
            if (source.OwnerSide != window.ControllerSide
                || sourceZone.OwnerSide != source.OwnerSide && !inRunnerScoreArea)
            {
                continue;
            }

            if (!working.BehaviorCatalog.TryGet(source.DefinitionId, out CardBehaviorDefinition? definition)
                || definition.Controller != source.OwnerSide)
            {
                continue;
            }

            string definitionFingerprint;
            try
            {
                definitionFingerprint = working.BehaviorCatalog.GetDefinitionFingerprint(definition.DefinitionId);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                // A catalog that cannot prove the definition fingerprint
                // cannot issue a replayable action.
                continue;
            }

            foreach (BehaviorPaidAbility ability in definition.PaidAbilities)
            {
                if (inRunnerScoreArea && !ability.UsableFromRunnerScoreArea
                    || inArchives && !ability.UsableFromArchives)
                {
                    continue;
                }

                if (!AbilityWindowMatches(window, ability)
                    || !AbilityConditionsMatch(working, source, window.ControllerId, ability.Conditions)
                    || !CanAffordAbility(working, source, window.ControllerId, ability))
                {
                    continue;
                }

                IReadOnlyList<CardInstanceId> targetCardIds;
                try
                {
                    targetCardIds = MaterializeAbilityTargets(working, ability, window);
                }
                catch (ResolutionFailureException)
                {
                    continue;
                }

                // A declaration with a target that this materializer cannot
                // prove must not silently become a target-less action.  This
                // keeps the executable vocabulary fail-closed while allowing
                // legacy no-target abilities to retain their empty snapshot.
                if (ability.Targets.Count > 0 && targetCardIds.Count == 0)
                {
                    continue;
                }

                List<IReadOnlyList<AbilityAdditionalCostSelection>> additionalSelections;
                try
                {
                    additionalSelections = MaterializeAdditionalCostSelections(working, ability);
                }
                catch (ResolutionFailureException)
                {
                    continue;
                }

                if (additionalSelections.Count == 0)
                {
                    continue;
                }

                if (BehaviorRunRules.TryGetFinalStartRun(ability, out BehaviorRunOperand startRun)
                    && !CanStartRunAbility(working, source, definition, startRun, out _))
                {
                    continue;
                }

                int uses = ability.UsageLimit == BehaviorAbilityUsageLimit.OncePerTurn
                    && activeFacts.HasUsedAbility(source.Id, ability.Key)
                    ? 1
                    : 0;
                if (ability.UsageLimit == BehaviorAbilityUsageLimit.OncePerTurn && uses != 0)
                {
                    continue;
                }

                foreach (IReadOnlyList<AbilityAdditionalCostSelection> selections in additionalSelections)
                {
                    materialized.Add(new AbilityActionParameters(
                        source.Id,
                        source.DefinitionId,
                        ability.Key,
                        ability.Timing,
                        ability.Activation,
                        ability.Costs,
                        targetCardIds,
                        new AbilityUsageSnapshot(ability.UsageLimit, uses),
                        working.BehaviorCatalog.Fingerprint,
                        definitionFingerprint,
                        sourceZone.Id,
                        sourceZone.Kind,
                        selections));
                }
            }
        }

        return materialized;
    }

    /// <summary>
    /// Tests the authenticated behavior declaration behind one already
    /// materialized access ability.  The no-steal/no-trash run restriction
    /// filters only declarations that can actually trash a Corp card (or pay
    /// a typed Corp-card trash cost); it does not blanket-suppress unrelated
    /// Runner abilities such as credit gain.  Unknown declarations fail
    /// closed because their effect vocabulary cannot be authenticated.
    /// </summary>
    internal static bool IsAbilityBlockedByRunRestriction(
        IBehaviorCatalog behaviorCatalog,
        AbilityActionParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        ArgumentNullException.ThrowIfNull(parameters);

        if (!behaviorCatalog.TryGet(parameters.DefinitionId, out CardBehaviorDefinition? definition))
        {
            return true;
        }

        BehaviorPaidAbility? declaration = definition.PaidAbilities.SingleOrDefault(ability =>
            string.Equals(ability.Key, parameters.AbilityKey, StringComparison.Ordinal));
        return declaration is null || IsAbilityBlockedByRunRestriction(declaration);
    }

    internal static bool IsAbilityBlockedByRunRestriction(BehaviorPaidAbility ability)
    {
        ArgumentNullException.ThrowIfNull(ability);
        if (ability.Costs.Any(static cost =>
                cost.Kind == BehaviorCostKind.Additional
                && cost.AdditionalKind == BehaviorAdditionalCostKind.TrashCards
                && cost.AdditionalTarget?.Filter?.OwnerSide == PlayerSide.Corp))
        {
            return true;
        }

        return InstructionsCanTrashCorpCard(ability.Instructions, ability.Targets);
    }

    private static bool InstructionsCanTrashCorpCard(
        IReadOnlyList<BehaviorInstruction> instructions,
        IReadOnlyList<BehaviorTarget> candidateTargets)
    {
        foreach (BehaviorInstruction instruction in instructions)
        {
            if (instruction.Kind == BehaviorInstructionKind.TrashCard
                && TargetCanBeCorpCard(instruction.Target, candidateTargets))
            {
                return true;
            }

            if (instruction.Choice is { } choice)
            {
                IReadOnlyList<BehaviorTarget> nestedTargets = choice.Target is { } choiceTarget
                    ? candidateTargets.Append(choiceTarget).ToArray()
                    : candidateTargets;
                foreach (BehaviorChoiceOption option in choice.Options)
                {
                    if (InstructionsCanTrashCorpCard(option.Instructions, nestedTargets))
                    {
                        return true;
                    }
                }
            }

            if (instruction.Branch is { } branch
                && (InstructionsCanTrashCorpCard(branch.WhenTrue, candidateTargets)
                    || InstructionsCanTrashCorpCard(branch.WhenFalse, candidateTargets)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TargetCanBeCorpCard(
        BehaviorTarget? target,
        IReadOnlyList<BehaviorTarget> candidateTargets)
    {
        if (target is null)
        {
            return false;
        }

        if (target.Kind == BehaviorTargetKind.AccessedCard)
        {
            return true;
        }

        if (target.Kind is BehaviorTargetKind.ZoneCard or BehaviorTargetKind.ZoneCards)
        {
            return target.Filter?.OwnerSide == PlayerSide.Corp;
        }

        // A ChosenCard is safe to classify only when its enclosing
        // declaration has an explicit Corp-owned candidate descriptor. Do
        // not infer ownership from selector/key strings.
        return target.Kind == BehaviorTargetKind.ChosenCard
            && candidateTargets.Any(candidate =>
                candidate.Kind is BehaviorTargetKind.ZoneCard or BehaviorTargetKind.ZoneCards
                && candidate.Filter?.OwnerSide == PlayerSide.Corp);
    }

    private static IReadOnlyList<CardInstanceId> MaterializeAbilityTargets(
        WorkingState working,
        BehaviorPaidAbility ability,
        AbilityWindow window)
    {
        if (ability.Targets.Count == 0)
        {
            return [];
        }

        // AccessedCard is the first target descriptor whose identity depends
        // on a live run. It is intentionally the only target materialized by
        // this checkpoint; all other target vocabularies remain fail-closed.
        if (ability.Targets.Count != 1
            || ability.Targets[0].Kind != BehaviorTargetKind.AccessedCard
            || window.RunTiming != BehaviorAbilityTiming.BreachAccessPaidWindow
            || working.RunState is not { Step: RunTimingStep.BreachAccessAbility } run
            || !TryGetCurrentAccessCard(working, run, out CardInstanceState? card)
            || !BehaviorCardMatches(working, card!.Id, ability.Targets[0].Filter))
        {
            return [];
        }

        return [card!.Id];
    }

    /// <summary>
    /// Resolves the one card that is currently being accessed and proves it
    /// still occupies the attacked server's authoritative source zone. This
    /// shared guard prevents a moved/replaced card from being retargeted by a
    /// resumed access ability or by automatic agenda progression.
    /// </summary>
    private static bool TryGetCurrentAccessCard(
        WorkingState working,
        RunState run,
        out CardInstanceState? card,
        bool requireTrashableSource = true)
    {
        card = null;
        if (run.Step != RunTimingStep.BreachAccessAbility
            || run.ChosenAccessCards.Count == 0)
        {
            return false;
        }

        CardInstanceId accessed = run.ChosenAccessCards[^1];
        if (!run.AccessedCards.Contains(accessed))
        {
            return false;
        }

        CardInstanceState? candidate = working.Cards.FirstOrDefault(value => value.Id == accessed);
        if (candidate is null || candidate.OwnerSide != PlayerSide.Corp)
        {
            return false;
        }

        ZoneState? zone = working.Zones.FirstOrDefault(value =>
            value.Id == candidate.ZoneId && value.CardIds.Contains(candidate.Id));
        ServerState? server = working.Servers.FirstOrDefault(value => value.Id == run.AttackedServer);
        if (zone is null || server is null || zone.ServerId is not null && zone.ServerId != server.Id)
        {
            return false;
        }

        // A central breach can access both its hidden zone and cards installed
        // in that server's root (for example a central upgrade). Archives is
        // recognized for automatic agenda stealing but excluded below when a
        // caller requires a source from which the card can still be trashed.
        // Score, RFG, another server root, and every unrelated zone fail this
        // exact server/source relation.
        bool inAccessSource = zone.OwnerSide == PlayerSide.Corp
            && (zone.Kind == ZoneKind.ServerRoot
                ? zone.Id == server.RootZoneId
                : server.Kind switch
                {
                    ServerKind.Headquarters => zone.Kind == ZoneKind.CorpHand
                        && zone.ServerId is null,
                    ServerKind.RAndD => zone.Kind == ZoneKind.CorpDeck
                        && zone.ServerId is null,
                    ServerKind.Archives => zone.Kind == ZoneKind.CorpDiscard
                        && zone.ServerId is null,
                    _ => false,
                });
        if (!inAccessSource)
        {
            return false;
        }

        if (requireTrashableSource && zone.Kind == ZoneKind.CorpDiscard)
        {
            return false;
        }

        card = candidate;
        return true;
    }

    private static bool AbilityTargetSnapshotMatches(
        WorkingState working,
        AbilityFrame frame,
        BehaviorPaidAbility declaration,
        bool requireLiveSource)
    {
        if (declaration.Targets.Count == 0)
        {
            return frame.TargetCardIds.Count == 0;
        }

        if (declaration.Targets.Count != 1
            || declaration.Targets[0].Kind != BehaviorTargetKind.AccessedCard
            || frame.TargetCardIds.Count != 1
            || working.RunState is not { } run
            || run.Step != RunTimingStep.BreachAccessAbility
            || run.ChosenAccessCards.Count == 0
            || run.ChosenAccessCards[^1] != frame.TargetCardIds[0]
            || !run.AccessedCards.Contains(frame.TargetCardIds[0]))
        {
            return false;
        }

        return !requireLiveSource
            || TryGetCurrentAccessCard(working, run, out CardInstanceState? card)
                && card!.Id == frame.TargetCardIds[0]
                && BehaviorCardMatches(working, card.Id, declaration.Targets[0].Filter);
    }

    private static bool AdditionalCostSelectionsMatchDeclaration(
        IReadOnlyList<AbilityAdditionalCostSelection>? selections,
        IReadOnlyList<BehaviorCost> costs)
    {
        AbilityAdditionalCostSelection[] actual = (selections ?? []).ToArray();
        (BehaviorCost Cost, int Index)[] additional = costs
            .Select((cost, index) => (Cost: cost, Index: index))
            .Where(value => value.Cost.Kind == BehaviorCostKind.Additional)
            .ToArray();
        if (additional.Length == 0)
        {
            return actual.Length == 0;
        }

        if (additional.Any(value => value.Cost.AdditionalKind is null)
            || actual.Length != additional.Length)
        {
            return false;
        }

        foreach ((BehaviorCost cost, int index) in additional)
        {
            AbilityAdditionalCostSelection? selection = actual.SingleOrDefault(
                value => value.CostIndex == index);
            if (selection is null
                || !string.Equals(selection.CostKey, cost.AdditionalKey, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static List<IReadOnlyList<AbilityAdditionalCostSelection>>
        MaterializeAdditionalCostSelections(
            WorkingState working,
            BehaviorPaidAbility ability)
    {
        if (!ability.Costs.Any(static cost => cost.Kind == BehaviorCostKind.Additional))
        {
            return [new ReadOnlyCollection<AbilityAdditionalCostSelection>([])];
        }

        var candidateSets = new List<(int CostIndex, BehaviorCost Cost, CardInstanceId[] Candidates)>();
        foreach ((BehaviorCost cost, int costIndex) in ability.Costs
            .Select((cost, index) => (cost, index))
            .Where(value => value.cost.Kind == BehaviorCostKind.Additional))
        {
            if (cost.AdditionalTarget is not
                {
                    Kind: BehaviorTargetKind.ZoneCards,
                    Zone: { } sourceZone,
                    Filter: not null,
                    Reveal: false,
                }
                || cost.AdditionalKind != BehaviorAdditionalCostKind.TrashCards
                || cost.AdditionalTarget.Filter.OwnerSide is not { } ownerSide)
            {
                return [];
            }

            if (!TryGetExistingBehaviorZone(working, sourceZone, ownerSide, out ZoneState? source)
                || source is null
                || !BehaviorZoneMatches(sourceZone, source.Kind, ownerSide))
            {
                return [];
            }
            CardInstanceId[] candidates = source.CardIds
                .Where(cardId =>
                {
                    CardInstanceState card = GetCard(working, cardId);
                    return card.OwnerSide == ownerSide
                        && BehaviorCardMatches(working, cardId, cost.AdditionalTarget.Filter);
                })
                .ToArray();
            if (candidates.Length < cost.Amount)
            {
                return [];
            }

            candidateSets.Add((costIndex, cost, candidates));
        }

        var combinations = new List<IReadOnlyList<AbilityAdditionalCostSelection>>();
        var selected = new List<AbilityAdditionalCostSelection>();
        var used = new HashSet<CardInstanceId>();
        bool exceeded = false;
        int explorations = 0;

        bool SpendExploration()
        {
            if (explorations >= MaxAdditionalCostExplorations)
            {
                exceeded = true;
                return false;
            }

            explorations++;
            return true;
        }

        void AppendCost(int ordinal)
        {
            if (exceeded || !SpendExploration())
            {
                return;
            }

            if (ordinal == candidateSets.Count)
            {
                combinations.Add(new ReadOnlyCollection<AbilityAdditionalCostSelection>(selected.ToArray()));
                if (combinations.Count > MaxAdditionalCostCombinations)
                {
                    exceeded = true;
                }

                return;
            }

            (int costIndex, BehaviorCost cost, CardInstanceId[] candidates) = candidateSets[ordinal];
            foreach (CardInstanceId[] pair in EnumerateExactSelections(candidates, cost.Amount))
            {
                if (!SpendExploration())
                {
                    return;
                }

                if (pair.Any(used.Contains))
                {
                    continue;
                }

                foreach (CardInstanceId cardId in pair)
                {
                    used.Add(cardId);
                }

                selected.Add(new AbilityAdditionalCostSelection(costIndex, cost.AdditionalKey!, pair));
                AppendCost(ordinal + 1);
                selected.RemoveAt(selected.Count - 1);
                foreach (CardInstanceId cardId in pair)
                {
                    used.Remove(cardId);
                }

                if (exceeded)
                {
                    return;
                }
            }
        }

        AppendCost(0);
        if (exceeded)
        {
            return [];
        }

        return combinations;
    }

    private static IEnumerable<CardInstanceId[]> EnumerateExactSelections(
        CardInstanceId[] candidates,
        int count)
    {
        if (count <= 0 || count > candidates.Length)
        {
            yield break;
        }

        var selection = new CardInstanceId[count];
        IEnumerable<CardInstanceId[]> Visit(int start, int depth)
        {
            if (depth == count)
            {
                yield return selection.ToArray();
                yield break;
            }

            int remaining = count - depth;
            for (int index = start; index <= candidates.Length - remaining; index++)
            {
                selection[depth] = candidates[index];
                foreach (CardInstanceId[] result in Visit(index + 1, depth + 1))
                {
                    yield return result;
                }
            }
        }

        foreach (CardInstanceId[] result in Visit(0, 0))
        {
            yield return result;
        }
    }

    private static bool CanStartRunAbility(
        WorkingState working,
        CardInstanceState source,
        CardBehaviorDefinition definition,
        BehaviorRunOperand operand,
        out ServerState? server)
    {
        server = null;
        if (definition.Controller != PlayerSide.Runner
            || source.OwnerSide != PlayerSide.Runner
            || !source.FaceUp
            || working.RunState is not null
            || !EnumerateAbilitySources(working).Any(candidate =>
                candidate.Card.Id == source.Id
                && candidate.Zone.Kind == ZoneKind.RunnerRig))
        {
            return false;
        }

        ServerState[] matchingServers = working.Servers
            .Where(candidate => candidate.Kind == operand.ServerKind)
            .ToArray();
        if (matchingServers.Length != 1)
        {
            return false;
        }

        ServerState selectedServer = matchingServers[0];
        server = selectedServer;
        if (!working.Zones.Any(zone => zone.Id == selectedServer.IceZoneId))
        {
            server = null;
            return false;
        }

        if (operand.RequireNotRunThisTurn)
        {
            TurnFactsState? facts = CurrentAbilityFacts(working);
            if (facts is null || facts.AttemptedRunServerIds.Contains(selectedServer.Id))
            {
                server = null;
                return false;
            }
        }

        return true;
    }

    private readonly record struct AbilityWindow(
        PlayerId ControllerId,
        PlayerSide ControllerSide,
        BehaviorAbilityActivation Activation,
        BehaviorAbilityTiming? RunTiming,
        bool IsTurnWindow);

    private static bool TryGetAbilityWindow(WorkingState working, out AbilityWindow window)
    {
        window = default;
        if (working.ResolutionStack.Count == 0)
        {
            return false;
        }

        ResolutionFrame top = working.ResolutionStack[^1];
        if (top is TurnFrame turn
            && working.RunState is null
            && working.Turn.ActiveSide == turn.ActiveSide
            && working.Turn.TurnNumber == turn.TurnNumber
            && working.Turn.Phase == (turn.ActiveSide == PlayerSide.Corp ? TurnPhase.Corp : TurnPhase.Runner))
        {
            if (turn.Stage == TurnFrameStage.AwaitingAction)
            {
                if (working.ResolutionStack.Count != 1
                    || GetPlayer(working, turn.ActiveSide).Clicks <= 0
                    || HasOccupiedTransientPlayArea(working))
                {
                    return false;
                }

                window = new AbilityWindow(
                    GetPlayer(working, turn.ActiveSide).Id,
                    turn.ActiveSide,
                    BehaviorAbilityActivation.ClickAction,
                    null,
                    IsTurnWindow: true);
                return PendingOwnerMatches(working, window.ControllerId);
            }

            if (turn.Stage is (TurnFrameStage.BeforeTurnPriority
                or TurnFrameStage.ActionPriority
                or TurnFrameStage.DiscardPriority)
                && turn.AllowsPaidAbilities
                && turn.PriorityHolder.HasValue
                && working.ResolutionStack.Count == 1
                && !HasOccupiedTransientPlayArea(working))
            {
                PlayerState holder = GetPlayer(working, turn.PriorityHolder.Value);
                window = new AbilityWindow(
                    holder.Id,
                    holder.Side,
                    BehaviorAbilityActivation.PaidAbility,
                    null,
                    IsTurnWindow: true);
                return PendingOwnerMatches(working, window.ControllerId);
            }

            return false;
        }

        if (working.RunState is { } run
            && top is RunTimingFrame runFrame
            && runFrame.RunId == run.Id
            && runFrame.Step == run.Step
            && run.Step == RunTimingStep.BreachAccessAbility
            && run.Priority is null)
        {
            window = new AbilityWindow(
                GetPlayer(working, PlayerSide.Runner).Id,
                PlayerSide.Runner,
                BehaviorAbilityActivation.PaidAbility,
                BehaviorAbilityTiming.BreachAccessPaidWindow,
                IsTurnWindow: false);
            return PendingOwnerMatches(working, window.ControllerId);
        }

        if (working.RunState is { } runWithPriority
            && runWithPriority.Priority is { } priority
            && top is RunTimingFrame priorityRunFrame
            && priorityRunFrame.RunId == runWithPriority.Id
            && priorityRunFrame.Step == runWithPriority.Step
            && IsPaidWindow(runWithPriority.Step))
        {
            PlayerState holder = GetPlayer(working, priority.PriorityHolder);
            window = new AbilityWindow(
                holder.Id,
                holder.Side,
                BehaviorAbilityActivation.PaidAbility,
                RunTimingFor(runWithPriority.Step),
                IsTurnWindow: false);
            return PendingOwnerMatches(working, window.ControllerId);
        }

        return false;
    }

    private static bool PendingOwnerMatches(WorkingState working, PlayerId controllerId) =>
        working.PendingDecision is null || working.PendingDecision.Owner == controllerId;

    private static BehaviorAbilityTiming? RunTimingFor(RunTimingStep step) => step switch
    {
        RunTimingStep.InitiationPaidWindow => BehaviorAbilityTiming.InitiationPaidWindow,
        RunTimingStep.ApproachPaidWindow => BehaviorAbilityTiming.ApproachPaidWindow,
        RunTimingStep.EncounterPaidWindow => BehaviorAbilityTiming.EncounterPaidWindow,
        RunTimingStep.MovementPaidWindow => BehaviorAbilityTiming.MovementPaidWindow,
        RunTimingStep.AfterMovePaidWindow => BehaviorAbilityTiming.AfterMovePaidWindow,
        RunTimingStep.BreachAccessAbility => BehaviorAbilityTiming.BreachAccessPaidWindow,
        _ => null,
    };

    private static bool AbilityWindowMatches(AbilityWindow window, BehaviorPaidAbility ability)
    {
        if (ability.Activation != window.Activation)
        {
            return false;
        }

        if (window.Activation == BehaviorAbilityActivation.ClickAction)
        {
            return ability.Timing == BehaviorAbilityTiming.ActionPhase;
        }

        if (window.IsTurnWindow)
        {
            return ability.Timing == BehaviorAbilityTiming.AnyPaidWindow;
        }

        return ability.Timing == BehaviorAbilityTiming.AnyPaidWindow
            || ability.Timing == window.RunTiming;
    }

    private static IEnumerable<(CardInstanceState Card, ZoneState Zone)> EnumerateAbilitySources(WorkingState working)
    {
        // The first card with each ID, as a scan of the cards finds it, built once a source zone is reached (S5.0: a
        // scan per card in Archives and the score areas).
        Dictionary<CardInstanceId, CardInstanceState>? cardsById = null;
        foreach (ZoneState zone in working.Zones)
        {
            bool runnerRig = zone.Kind == ZoneKind.RunnerRig
                && zone.OwnerSide == PlayerSide.Runner
                && zone.ServerId is null
                && working.Zones.Count(candidate => candidate.Kind == ZoneKind.RunnerRig
                    && candidate.OwnerSide == PlayerSide.Runner
                    && candidate.ServerId is null) == 1;

            ServerState? server = zone.Kind switch
            {
                ZoneKind.ServerRoot or ZoneKind.ServerIce => working.Servers.SingleOrDefault(candidate =>
                    candidate.Id == zone.ServerId
                    && zone.OwnerSide == PlayerSide.Corp
                    && ((zone.Kind == ZoneKind.ServerRoot && candidate.RootZoneId == zone.Id)
                        || (zone.Kind == ZoneKind.ServerIce && candidate.IceZoneId == zone.Id))),
                _ => null,
            };

            bool corpServer = server is not null;
            bool identityOrScore = IsIdentityOrScoreAbilityZone(zone);
            if (!runnerRig && !corpServer && !identityOrScore)
            {
                continue;
            }

            if (cardsById is null && zone.CardIds.Count > 0)
            {
                cardsById = new Dictionary<CardInstanceId, CardInstanceState>(working.Cards.Count);
                foreach (CardInstanceState candidate in working.Cards)
                {
                    cardsById.TryAdd(candidate.Id, candidate);
                }
            }

            foreach (CardInstanceId cardId in zone.CardIds)
            {
                if (!cardsById!.TryGetValue(cardId, out CardInstanceState? card) || card.ZoneId != zone.Id)
                {
                    continue;
                }

                if (runnerRig
                    && card.OwnerSide == PlayerSide.Runner
                    && card.FaceUp)
                {
                    yield return (card, zone);
                }
                else if (corpServer
                    && card.OwnerSide == PlayerSide.Corp
                    && card.FaceUp
                    && card.Rezzed)
                {
                    yield return (card, zone);
                }
                else if (identityOrScore && IsIdentityOrScoreAbilitySource(card, zone))
                {
                    yield return (card, zone);
                }
            }
        }
    }

    /// <summary>Identity zones and score areas, whose faceup cards may have paid abilities (Elevation).</summary>
    internal static bool IsIdentityOrScoreAbilityZone(ZoneState zone) =>
        zone.ServerId is null
        && zone.Kind is ZoneKind.CorpIdentity or ZoneKind.RunnerIdentity or ZoneKind.CorpScore or ZoneKind.RunnerScore
            or ZoneKind.CorpDiscard;

    /// <summary>
    /// A faceup identity in its owner's identity zone, or a Corp agenda in
    /// either score area (an agenda's ability in the Runner's score area also
    /// needs <see cref="BehaviorPaidAbility.UsableFromRunnerScoreArea"/>).
    /// </summary>
    internal static bool IsIdentityOrScoreAbilitySource(CardInstanceState card, ZoneState zone) =>
        zone.ServerId is null
        && zone.Kind == ZoneKind.CorpDiscard
        && card.OwnerSide == PlayerSide.Corp
        && zone.OwnerSide == PlayerSide.Corp
        || card.FaceUp
        && zone.ServerId is null
        && zone.Kind switch
        {
            ZoneKind.CorpIdentity => card.OwnerSide == PlayerSide.Corp && zone.OwnerSide == PlayerSide.Corp,
            ZoneKind.RunnerIdentity => card.OwnerSide == PlayerSide.Runner && zone.OwnerSide == PlayerSide.Runner,
            ZoneKind.CorpScore or ZoneKind.RunnerScore => card.OwnerSide == PlayerSide.Corp,
            _ => false,
        };

    private static TurnFactsState? CurrentAbilityFacts(WorkingState working)
    {
        PlayerSide activeSide = working.Turn.ActiveSide;
        TurnFactsState? facts = working.Turn.History?.ThisTurn(activeSide);
        return facts is not null
            && facts.Side == activeSide
            && facts.TurnNumber == working.Turn.TurnNumber
            ? facts
            : null;
    }

    private static bool TryGetExistingBehaviorZone(
        WorkingState working,
        BehaviorZone zone,
        PlayerSide? ownerSide,
        out ZoneState? existing)
    {
        existing = null;
        PlayerSide side = ownerSide ?? PlayerSide.Corp;
        ZoneKind? kind = zone switch
        {
            BehaviorZone.Deck => side == PlayerSide.Corp ? ZoneKind.CorpDeck : ZoneKind.RunnerStack,
            BehaviorZone.Headquarters => ZoneKind.CorpHand,
            BehaviorZone.Archives => ZoneKind.CorpDiscard,
            BehaviorZone.Grip => ZoneKind.RunnerGrip,
            BehaviorZone.Stack => ZoneKind.RunnerStack,
            BehaviorZone.Heap => ZoneKind.RunnerHeap,
            BehaviorZone.Rig => ZoneKind.RunnerRig,
            BehaviorZone.PlayArea => ZoneKind.RunnerPlayArea,
            BehaviorZone.ScoreArea => side == PlayerSide.Corp ? ZoneKind.CorpScore : ZoneKind.RunnerScore,
            BehaviorZone.RemovedFromGame => side == PlayerSide.Corp
                ? ZoneKind.CorpRemovedFromGame
                : ZoneKind.RunnerRemovedFromGame,
            BehaviorZone.HostedFaceUp => side == PlayerSide.Corp
                ? ZoneKind.CorpHostedFaceUp
                : ZoneKind.RunnerHostedFaceUp,
            BehaviorZone.ServerIce or BehaviorZone.ServerRoot => null,
            _ => null,
        };

        if (zone == BehaviorZone.ServerRoot)
        {
            existing = working.Servers
                .Where(server => server.Kind == ServerKind.Remote && server.Exists)
                .OrderBy(server => server.Id.Value, StringComparer.Ordinal)
                .Select(server => working.Zones.FirstOrDefault(zoneState => zoneState.Id == server.RootZoneId))
                .FirstOrDefault(zoneState => zoneState is not null);
            return existing is not null;
        }

        if (zone == BehaviorZone.ServerIce)
        {
            existing = working.Servers
                .Where(server => server.Kind == ServerKind.Remote && server.Exists)
                .OrderBy(server => server.Id.Value, StringComparer.Ordinal)
                .Select(server => working.Zones.FirstOrDefault(zoneState => zoneState.Id == server.IceZoneId))
                .FirstOrDefault(zoneState => zoneState is not null);
            return existing is not null;
        }

        if (kind is not { } expectedKind)
        {
            return false;
        }

        existing = working.Zones.FirstOrDefault(zoneState =>
            zoneState.Kind == expectedKind && zoneState.ServerId is null);
        return existing is not null;
    }

    private static bool AbilityConditionsMatch(
        WorkingState working,
        CardInstanceState source,
        PlayerId controllerId,
        IReadOnlyList<BehaviorCondition> conditions)
    {
        foreach (BehaviorCondition condition in conditions)
        {
            if (!AbilityConditionMatches(working, source, controllerId, condition))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AbilityConditionMatches(
        WorkingState working,
        CardInstanceState source,
        PlayerId controllerId,
        BehaviorCondition condition)
    {
        // The legacy condition contract permits an omitted numeric value for
        // compatibility. It is not executable for an ability materializer.
        if (condition.Kind is (BehaviorConditionKind.CounterAtLeast or BehaviorConditionKind.FactEquals)
            && (!condition.Value.HasValue || condition.Value.Value < 0))
        {
            return false;
        }

        PlayerState controller = GetPlayer(working, controllerId);
        return condition.Kind switch
        {
            BehaviorConditionKind.Tagged => AbilityTagsForCondition(working, controllerId, condition) > 0,
            BehaviorConditionKind.NotTagged => AbilityTagsForCondition(working, controllerId, condition) <= 0,
            BehaviorConditionKind.ClicksRemainingAtLeast => controller.Clicks >= condition.Value.GetValueOrDefault(),
            BehaviorConditionKind.Installed => IsInstalledCard(working, source.Id),
            BehaviorConditionKind.InstalledThisTurn => CurrentAbilityFacts(working)?.InstalledCardIds.Contains(source.Id) == true,
            BehaviorConditionKind.SuccessfulRun => working.RunState?.Successful == true,
            BehaviorConditionKind.SuccessfulRunDuringRunnerLastTurn => working.Turn.History?.RunnerThisTurn?.SuccessfulRun == true,
            BehaviorConditionKind.SuccessfulRunThisTurn => CurrentAbilityFacts(working)?.SuccessfulRun == true,
            BehaviorConditionKind.SourceController => source.OwnerSide == controller.Side,
            BehaviorConditionKind.Encountering => working.RunState is
            {
                Step: RunTimingStep.Encounter or RunTimingStep.EncounterPaidWindow,
                Encounter: { Status: RunEncounterStatus.Initialized },
            },
            BehaviorConditionKind.CounterAtLeast => CurrentCounter(working, source.Id, condition.Fact!) >= condition.Value.GetValueOrDefault(),
            BehaviorConditionKind.FactEquals => AbilityFactEquals(working, source, controllerId, condition),
            // A paid ability has no search continuation; a forged declaration
            // depending on one therefore fails closed.
            BehaviorConditionKind.FoundCard => false,
            BehaviorConditionKind.SourceHostsAtLeast or BehaviorConditionKind.RezzedInstalledCopy
                => OptionConditionMatches(working, controllerId, source.Id, condition),
            _ => SharedConditionMatches(working, controllerId, condition) ?? false,
        };
    }

    private static int AbilityTagsForCondition(
        WorkingState working,
        PlayerId controllerId,
        BehaviorCondition condition)
    {
        PlayerId subject = condition.SubjectSide is { } side
            ? GetPlayer(working, side).Id
            : controllerId;
        return working.RulesState?.Players
            .SingleOrDefault(value => value.PlayerId == subject)?.Tags ?? 0;
    }

    private static bool AbilityFactEquals(
        WorkingState working,
        CardInstanceState source,
        PlayerId controllerId,
        BehaviorCondition condition)
    {
        int? actual = condition.Fact switch
        {
            "credits" => GetPlayer(working, controllerId).Credits,
            "clicks" => GetPlayer(working, controllerId).Clicks,
            "tags" => AbilityTagsForCondition(working, controllerId, condition),
            "advancement" => source.AdvancementCounters,
            _ => null,
        };
        return actual.HasValue && actual.Value == condition.Value;
    }

    private static bool CanAffordAbility(
        WorkingState working,
        CardInstanceState source,
        PlayerId controllerId,
        BehaviorPaidAbility ability)
    {
        if (CurrentAbilityFacts(working) is null)
        {
            return false;
        }

        try
        {
            long credits = 0;
            long clicks = 0;
            long tags = 0;
            long hostedCredits = 0;
            var counters = new Dictionary<string, long>(StringComparer.Ordinal);
            var restrictedCredits = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (BehaviorCost cost in ability.Costs)
            {
                switch (cost.Kind)
                {
                    case BehaviorCostKind.Credits when cost.CreditSourceSubtype is { } subtype:
                        restrictedCredits.TryGetValue(subtype, out long restrictedCost);
                        restrictedCredits[subtype] = checked(restrictedCost + cost.Amount);
                        break;
                    case BehaviorCostKind.Credits:
                        credits = checked(credits + cost.Amount);
                        break;
                    case BehaviorCostKind.Clicks:
                    case BehaviorCostKind.LoseClick:
                        clicks = checked(clicks + cost.Amount);
                        break;
                    case BehaviorCostKind.Tags:
                        tags = checked(tags + cost.Amount);
                        break;
                    case BehaviorCostKind.HostedCredits:
                        hostedCredits = checked(hostedCredits + cost.Amount);
                        break;
                    case BehaviorCostKind.Counter:
                        string counterName = cost.CounterName!;
                        counters.TryGetValue(counterName, out long counterCost);
                        counters[counterName] = checked(counterCost + cost.Amount);
                        break;
                    case BehaviorCostKind.TrashSelf:
                        break;
                    case BehaviorCostKind.RemoveSelfFromGame:
                        break;
                    case BehaviorCostKind.Additional:
                        // Structured additional costs are validated against
                        // their frozen card selections before payment. The
                        // affordability pass only rejects an untyped or
                        // unknown operation; it does not infer semantics
                        // from AdditionalKey.
                        if (cost.AdditionalKind != BehaviorAdditionalCostKind.TrashCards
                            || cost.AdditionalTarget is null)
                        {
                            return false;
                        }

                        break;
                    default:
                        return false;
                }
            }

            PlayerState controller = GetPlayer(working, controllerId);
            int availableTags = working.RulesState?.Players
                .SingleOrDefault(value => value.PlayerId == controllerId)?.Tags ?? 0;
            if (hostedCredits > 0)
            {
                CardCounterState? hostedPool = working.RulesState?.Counters.SingleOrDefault(counter =>
                    counter.CardId == source.Id
                    && counter.Purpose == CardCounterPurpose.HostedCredit);
                if (hostedPool is null
                    || !CardCounterRules.CanSpend(hostedPool, HostedCreditSpendRestriction.AbilityCost)
                    || hostedCredits > hostedPool.Count)
                {
                    return false;
                }
            }

            // VP-STEALTH-CREDITS: source-restricted credits are checked on their own. An ability with both kinds of credit
            // cost would check them against overlapping sources; no card has one.
            return credits <= AvailableCreditsForPayment(working, controllerId)
                && restrictedCredits.All(pair => pair.Value <= SourceRestrictedCredits(working, controllerId, pair.Key))
                && clicks <= controller.Clicks
                && tags <= availableTags
                && counters.All(pair => CurrentAbilityCounter(working, source.Id, pair.Key) >= pair.Value);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// Consumes an engine-issued UseAbility action. The payload is compared
    /// against the same pure materializer used for the pending decision, then
    /// converted into a serialized lifecycle cursor. No cost is paid here;
    /// payment is an explicit reducer step so a checkpoint can be persisted
    /// between authorization and execution.
    /// </summary>
    private static void ConsumeAbilityAction(
        WorkingState working,
        AbilityActionParameters submitted,
        PlayerId? decisionOwner)
    {
        if (decisionOwner is null)
        {
            throw new ResolutionFailureException("An ability action requires a decision owner.");
        }

        IReadOnlyList<AbilityActionParameters> materialized =
            MaterializeAbilityActionParameters(working);
        AbilityActionParameters? expected = materialized.FirstOrDefault(candidate =>
            AbilityParametersMatch(candidate, submitted));
        if (expected is null)
        {
            throw new ResolutionFailureException(
                "The submitted ability action is not an exact current materialization.");
        }

        CardInstanceState source = GetCard(working, submitted.SourceCardId);
        PlayerState controller = GetPlayer(working, decisionOwner.Value);
        if (controller.Side != source.OwnerSide)
        {
            throw new ResolutionFailureException(
                "An ability controller must be the pending decision owner and source-card owner.");
        }

        if (working.ResolutionStack.Count == 0)
        {
            throw new ResolutionFailureException("An ability action requires an active turn or run parent frame.");
        }

        ResolutionFrame parent = working.ResolutionStack[^1];
        ValidateAbilityParent(working, parent, submitted, controller.Id);
        string parentIdentity = BehaviorFrameIdentity.Compute(parent);

        working.ResolutionStack.Add(new AbilityFrame(
            1,
            ResolutionBehaviorIds.Ability,
            expected.DefinitionId,
            expected.SourceCardId,
            expected.AbilityKey,
            controller.Id,
            expected.Timing,
            expected.Activation,
            expected.Costs,
            expected.TargetCardIds,
            expected.Usage,
            expected.CatalogFingerprint,
            expected.DefinitionFingerprint,
            expected.SourceZoneId,
            expected.SourceZoneKind,
            AbilityLifecycleStage.PayCosts,
            parentIdentity,
            expected.AdditionalCostSelections));
    }

    /// <summary>Advances the serialized ability lifecycle one boundary.</summary>
    private static void StepAbility(WorkingState working, AbilityFrame frame)
    {
        ValidateAbilityFrameRuntime(working, frame);
        switch (frame.Stage)
        {
            case AbilityLifecycleStage.PayCosts:
                CardBehaviorDefinition definition = GetAbilityDefinition(working, frame);
                BehaviorPaidAbility declaration = GetAbilityDeclaration(definition, frame.AbilityKey);
                CardInstanceState source = GetCard(working, frame.SourceCardId);
                PreflightAbilityPayment(working, frame, declaration, source);
                PayAbilityCosts(working, frame);
                MarkAbilityUsage(working, frame);
                ReplaceTopFrame(working, frame.With(AbilityLifecycleStage.CostCheckpoint));
                return;
            case AbilityLifecycleStage.CostCheckpoint:
                ReplaceTopFrame(working, frame.With(AbilityLifecycleStage.Execute));
                return;
            case AbilityLifecycleStage.Execute:
                PushAbilityBehaviorChild(working, frame);
                return;
            case AbilityLifecycleStage.AwaitingChild:
                ReplaceTopFrame(working, frame.With(AbilityLifecycleStage.Complete));
                return;
            case AbilityLifecycleStage.Complete:
                CompleteAbility(working, frame);
                return;
            default:
                throw new ResolutionFailureException($"Ability stage '{frame.Stage}' is not executable.");
        }
    }

    private static CardBehaviorDefinition GetAbilityDefinition(WorkingState working, AbilityFrame frame)
    {
        if (!working.BehaviorCatalog.TryGet(frame.DefinitionId, out CardBehaviorDefinition? definition))
        {
            throw new ResolutionFailureException(
                $"Ability definition '{frame.DefinitionId}' is unavailable.");
        }

        return definition;
    }

    private static BehaviorPaidAbility GetAbilityDeclaration(
        CardBehaviorDefinition definition,
        string abilityKey) => definition.PaidAbilities
            .SingleOrDefault(candidate => string.Equals(candidate.Key, abilityKey, StringComparison.Ordinal))
            ?? throw new ResolutionFailureException(
                $"Ability key '{abilityKey}' is not declared by definition '{definition.DefinitionId}'.");

    private static void ValidateAbilityParent(
        WorkingState working,
        ResolutionFrame parent,
        AbilityActionParameters parameters,
        PlayerId controllerId)
    {
        PlayerState controller = GetPlayer(working, controllerId);
        switch (parent)
        {
            case TurnFrame turn:
                if (working.RunState is not null
                    || working.Turn.ActiveSide != turn.ActiveSide
                    || working.Turn.TurnNumber != turn.TurnNumber)
                {
                    throw new ResolutionFailureException("Ability parent turn does not match the active turn.");
                }

                if (parameters.Activation == BehaviorAbilityActivation.ClickAction)
                {
                    if (turn.ActiveSide != controller.Side
                        || turn.Stage != TurnFrameStage.AwaitingAction
                        || turn.PriorityHolder.HasValue
                        || turn.PriorityWindow.HasValue)
                    {
                        throw new ResolutionFailureException(
                            "A click ability requires the active turn's AwaitingAction frame.");
                    }

                    return;
                }

                if (turn.Stage is not (TurnFrameStage.BeforeTurnPriority
                    or TurnFrameStage.ActionPriority
                    or TurnFrameStage.DiscardPriority)
                    || !turn.AllowsPaidAbilities
                    || turn.PriorityHolder != controller.Side
                    || !turn.PriorityWindow.HasValue)
                {
                    throw new ResolutionFailureException(
                        "A paid ability requires the controller's active turn priority window.");
                }

                return;
            case RunTimingFrame runFrame:
                RunState run = working.RunState
                    ?? throw new ResolutionFailureException("A run ability requires live run state.");
                if (run.Step == RunTimingStep.BreachAccessAbility)
                {
                    if (parameters.Activation != BehaviorAbilityActivation.PaidAbility
                        || parameters.Timing is not (BehaviorAbilityTiming.BreachAccessPaidWindow
                            or BehaviorAbilityTiming.AnyPaidWindow)
                        || runFrame.RunId != run.Id
                        || runFrame.Step != run.Step
                        || run.Priority is not null
                        || controller.Side != PlayerSide.Runner)
                    {
                        throw new ResolutionFailureException(
                            "An access ability requires the Runner's priority-free breach-access window.");
                    }

                    return;
                }

                BehaviorAbilityTiming? runTiming = RunTimingFor(run.Step);
                if (parameters.Activation != BehaviorAbilityActivation.PaidAbility
                    || runFrame.RunId != run.Id
                    || runFrame.Step != run.Step
                    || !IsPaidWindow(run.Step)
                    || run.Priority is null
                    || GetPlayer(working, run.Priority.PriorityHolder).Id != controllerId
                    || runTiming is null
                    || (parameters.Timing != BehaviorAbilityTiming.AnyPaidWindow
                        && parameters.Timing != runTiming.Value))
                {
                    throw new ResolutionFailureException(
                        "A run ability parent does not match the active paid window.");
                }

                return;
            default:
                throw new ResolutionFailureException(
                    "Ability parent must be the exact top TurnFrame or RunTimingFrame.");
        }
    }

    private static void ValidateAbilityFrameRuntime(WorkingState working, AbilityFrame frame)
    {
        if (working.ResolutionStack.Count < 2
            || !ReferenceEquals(working.ResolutionStack[^1], frame))
        {
            throw new ResolutionFailureException("An ability frame must be the active top resolution frame.");
        }

        CardBehaviorDefinition definition = GetAbilityDefinition(working, frame);
        BehaviorPaidAbility declaration = GetAbilityDeclaration(definition, frame.AbilityKey);
        CardInstanceState source = GetCard(working, frame.SourceCardId);

        if (declaration.Instructions.Any(static instruction =>
                BehaviorEncounterStrengthContracts.IsStrengthInstruction(instruction.Kind))
            && !BehaviorEncounterStrengthContracts.TryGetExecutableInstruction(
                definition,
                declaration.Key,
                out _,
                out _))
        {
            throw new ResolutionFailureException(
                "Encountered-ICE strength instructions must be the exact single instruction of a Runner encounter-paid ability.");
        }

        if (BehaviorRunRules.TryGetFinalStartRun(declaration, out BehaviorRunOperand startRun)
            && !CanStartRunAbility(working, source, definition, startRun, out _))
        {
            throw new ResolutionFailureException(
                "The StartRun ability is no longer legal for its source, server topology, or turn facts.");
        }

        ZoneState sourceZone = working.Zones.SingleOrDefault(zone => zone.Id == frame.SourceZoneId)
            ?? throw new ResolutionFailureException("Ability source zone no longer exists.");
        if (sourceZone.Kind != frame.SourceZoneKind)
        {
            throw new ResolutionFailureException("Ability source zone kind no longer matches the captured snapshot.");
        }

        if (source.DefinitionId != frame.DefinitionId
            || source.OwnerSide != GetPlayer(working, frame.Controller).Side)
        {
            throw new ResolutionFailureException("Ability source card no longer matches its captured definition or owner.");
        }

        if (!string.Equals(frame.CatalogFingerprint, working.BehaviorCatalog.Fingerprint, StringComparison.Ordinal)
            || !string.Equals(
                frame.DefinitionFingerprint,
                working.BehaviorCatalog.GetDefinitionFingerprint(frame.DefinitionId),
                StringComparison.Ordinal))
        {
            throw new ResolutionFailureException("Ability frame catalog provenance does not match the injected catalog.");
        }

        bool requireLiveAccessTarget = frame.Stage is
            AbilityLifecycleStage.PayCosts
                or AbilityLifecycleStage.CostCheckpoint
                or AbilityLifecycleStage.Execute;
        if (frame.Timing != declaration.Timing
            || frame.Activation != declaration.Activation
            || frame.Usage.Limit != declaration.UsageLimit
            || !frame.Costs.SequenceEqual(declaration.Costs)
            || !AbilityTargetSnapshotMatches(working, frame, declaration, requireLiveAccessTarget)
            || !AdditionalCostSelectionsMatchDeclaration(frame.AdditionalCostSelections, declaration.Costs))
        {
            throw new ResolutionFailureException("Ability frame no longer matches the declared executable shape.");
        }

        if (declaration.Targets.Any(static target => target.Kind != BehaviorTargetKind.AccessedCard)
            || declaration.Targets.Count > 1)
        {
            throw new ResolutionFailureException("This ability contains an unsupported target.");
        }

        if (frame.Stage == AbilityLifecycleStage.PayCosts
            && declaration.Costs.Any(static cost => cost.Kind == BehaviorCostKind.Additional))
        {
            ValidateAdditionalCostSelections(working, frame, declaration);
        }

        ResolutionFrame parent = working.ResolutionStack[^2];
        if (frame.ParentFrameIdentity is null
            || !string.Equals(
                frame.ParentFrameIdentity,
                BehaviorFrameIdentity.Compute(parent),
                StringComparison.Ordinal))
        {
            throw new ResolutionFailureException("Ability frame parent identity no longer matches the exact parent.");
        }

        var parentParameters = new AbilityActionParameters(
            frame.SourceCardId,
            frame.DefinitionId,
            frame.AbilityKey,
            frame.Timing,
            frame.Activation,
            frame.Costs,
            frame.TargetCardIds,
            frame.Usage,
            frame.CatalogFingerprint,
            frame.DefinitionFingerprint,
            frame.SourceZoneId,
            frame.SourceZoneKind,
            frame.AdditionalCostSelections);
        ValidateAbilityParent(working, parent, parentParameters, frame.Controller);

        bool paid = frame.Stage != AbilityLifecycleStage.PayCosts;
        if (!paid && source.ZoneId != frame.SourceZoneId)
        {
            throw new ResolutionFailureException("Ability source card moved before its costs were paid.");
        }

        bool selfTrash = declaration.Costs.Any(static cost => cost.Kind == BehaviorCostKind.TrashSelf);
        bool selfRemove = declaration.Costs.Any(static cost => cost.Kind == BehaviorCostKind.RemoveSelfFromGame);
        if (source.ZoneId != frame.SourceZoneId)
        {
            ZoneState currentSourceZone = working.Zones.SingleOrDefault(zone => zone.Id == source.ZoneId)
                ?? throw new ResolutionFailureException("Ability source card current zone no longer exists.");
            bool validLifecycleZone = (selfTrash && IsAbilityTrashZone(currentSourceZone, source))
                || (selfRemove && IsAbilityRemoveZone(currentSourceZone, source))
                || AbilityLifecycleRules.IsEmptyPoolSelfTrash(definition, declaration, frame,
                    source, currentSourceZone, working.RulesState, working.Turn)
                || AbilityLifecycleRules.IsSelfPlay(declaration, frame, source, currentSourceZone);
            if (!paid || !validLifecycleZone || (selfTrash && selfRemove))
            {
                throw new ResolutionFailureException("Ability source card moved outside its permitted lifecycle.");
            }
        }

        TurnFactsState? facts = CurrentAbilityFacts(working);
        if (facts is null)
        {
            throw new ResolutionFailureException("Ability lifecycle requires current turn facts.");
        }

        bool used = facts.HasUsedAbility(frame.SourceCardId, frame.AbilityKey);
        if (frame.Usage.Limit == BehaviorAbilityUsageLimit.OncePerTurn)
        {
            // UsesThisTurn is the exact action-issued snapshot.  It remains
            // zero for the whole lifecycle; the turn-facts ledger records the
            // successful payment separately and is what changes after the
            // bundle is accepted.
            if ((!paid && used) || (paid && !used) || frame.Usage.UsesThisTurn != 0)
            {
                throw new ResolutionFailureException("Ability usage snapshot does not match the current turn facts.");
            }
        }
        else if (frame.Usage.UsesThisTurn != 0)
        {
            throw new ResolutionFailureException("Unlimited abilities must retain a zero usage count.");
        }
    }

    private static bool IsAbilityTrashZone(ZoneState zone, CardInstanceState source) =>
        source.OwnerSide == PlayerSide.Corp
            ? zone.Kind == ZoneKind.CorpDiscard
            : zone.Kind == ZoneKind.RunnerHeap;

    private static bool IsAbilityRemoveZone(ZoneState zone, CardInstanceState source) =>
        source.OwnerSide == PlayerSide.Corp
            ? zone.Kind == ZoneKind.CorpRemovedFromGame
            : zone.Kind == ZoneKind.RunnerRemovedFromGame;

    private static void PreflightAbilityPayment(
        WorkingState working,
        AbilityFrame frame,
        BehaviorPaidAbility declaration,
        CardInstanceState source)
    {
        if (frame.Stage != AbilityLifecycleStage.PayCosts
            || source.ZoneId != frame.SourceZoneId
            || !IsCurrentlyActiveAbilitySource(working, source, frame.SourceZoneId)
            || !CanAffordAbility(working, source, frame.Controller, declaration))
        {
            throw new ResolutionFailureException("Ability costs or source state are no longer affordable.");
        }

        if (declaration.Targets.Any(static target => target.Kind != BehaviorTargetKind.AccessedCard)
            || declaration.Targets.Count > 1)
        {
            throw new ResolutionFailureException("This ability contains an unsupported target.");
        }

        if (declaration.Costs.Any(static cost => cost.Kind == BehaviorCostKind.Additional))
        {
            // Validate every selected card before the first ordinary cost is
            // mutated. Apply works on an isolated copy, but this explicit
            // preflight is what makes a stale/forged bundle fail atomically at
            // the payment boundary as well as at action materialization.
            ValidateAdditionalCostSelections(working, frame, declaration);
        }

        if (declaration.Targets.Count > 0
            && (declaration.Targets.Count != 1
                || declaration.Targets[0].Kind != BehaviorTargetKind.AccessedCard
                || frame.TargetCardIds.Count != 1
                || working.RunState is not { } run
                || !TryGetCurrentAccessCard(working, run, out CardInstanceState? accessed)
                || frame.TargetCardIds[0] != accessed!.Id))
        {
            throw new ResolutionFailureException("The ability target no longer matches the current accessed card.");
        }
    }

    private static void ValidateAdditionalCostSelections(
        WorkingState working,
        AbilityFrame frame,
        BehaviorPaidAbility declaration)
    {
        if (!AdditionalCostSelectionsMatchDeclaration(frame.AdditionalCostSelections, declaration.Costs))
        {
            throw new ResolutionFailureException(
                "The ability additional-cost selections no longer match the declared cost bundle.");
        }

        AbilityAdditionalCostSelection[] selections = frame.AdditionalCostSelections!.ToArray();
        var globallySelected = new HashSet<CardInstanceId>();
        foreach ((BehaviorCost cost, int costIndex) in declaration.Costs
            .Select((cost, index) => (cost, index))
            .Where(value => value.cost.Kind == BehaviorCostKind.Additional))
        {
            if (cost.AdditionalKind != BehaviorAdditionalCostKind.TrashCards
                || cost.AdditionalTarget is not
                {
                    Kind: BehaviorTargetKind.ZoneCards,
                    Zone: { } sourceZone,
                    Filter: not null,
                    Reveal: false,
                }
                || cost.AdditionalTarget.Filter.OwnerSide is not { } ownerSide)
            {
                throw new ResolutionFailureException(
                    "The ability contains an unsupported structured additional-cost descriptor.");
            }

            AbilityAdditionalCostSelection selection = selections.Single(value => value.CostIndex == costIndex);
            if (selection.CardIds.Count != cost.Amount
                || selection.CardIds.Distinct().Count() != selection.CardIds.Count)
            {
                throw new ResolutionFailureException(
                    "An additional-cost selection has the wrong cardinality or duplicate cards.");
            }

            ZoneState source = GetBehaviorZone(working, sourceZone, ownerSide);
            if (!BehaviorZoneMatches(sourceZone, source.Kind, ownerSide))
            {
                throw new ResolutionFailureException(
                    "The additional-cost target zone does not match its explicit owner side.");
            }
            BehaviorTarget additionalTarget = cost.AdditionalTarget!;
            BehaviorCardFilter filter = additionalTarget.Filter!;
            foreach (CardInstanceId cardId in selection.CardIds)
            {
                if (!globallySelected.Add(cardId))
                {
                    throw new ResolutionFailureException(
                        "Additional-cost selections may not reuse a physical card.");
                }

                CardInstanceState card = GetCard(working, cardId);
                if (card.OwnerSide != ownerSide
                    || card.ZoneId != source.Id
                    || !source.CardIds.Contains(cardId)
                    || !BehaviorCardMatches(working, cardId, filter))
                {
                    throw new ResolutionFailureException(
                        $"Additional-cost card '{cardId}' is stale or no longer occupies its declared source zone.");
                }
            }
        }
    }

    private static bool IsCurrentlyActiveAbilitySource(
        WorkingState working,
        CardInstanceState source,
        ZoneId sourceZoneId) => EnumerateAbilitySources(working)
            .Any(candidate => candidate.Card.Id == source.Id && candidate.Zone.Id == sourceZoneId);

    private static bool AbilityRetainsSourceCountersThroughChild(
        WorkingState working,
        AbilityFrame frame)
    {
        if (!frame.Costs.Any(static cost =>
                cost.Kind is BehaviorCostKind.TrashSelf or BehaviorCostKind.RemoveSelfFromGame)
            || !working.BehaviorCatalog.TryGet(frame.DefinitionId, out CardBehaviorDefinition? definition))
        {
            return false;
        }

        BehaviorPaidAbility declaration = GetAbilityDeclaration(definition, frame.AbilityKey);
        return ContainsDynamicAmount(declaration.Instructions);
    }

    private static void PayAbilityCosts(WorkingState working, AbilityFrame frame)
    {
        CardInstanceState source = GetCard(working, frame.SourceCardId);
        bool deferredCounterCleanup = false;
        for (int costIndex = 0; costIndex < frame.Costs.Count; costIndex++)
        {
            BehaviorCost cost = frame.Costs[costIndex];
            switch (cost.Kind)
            {
                case BehaviorCostKind.Credits when cost.CreditSourceSubtype is { } subtype:
                    PaySourceRestrictedCredits(working, frame.Controller, cost.Amount, subtype);
                    break;
                case BehaviorCostKind.Credits:
                    PayCreditCost(working, frame.Controller, cost.Amount);
                    break;
                case BehaviorCostKind.Clicks:
                    for (int click = 0; click < cost.Amount; click++)
                    {
                        SpendClick(working, frame.Controller);
                    }

                    break;
                case BehaviorCostKind.LoseClick:
                    ApplyClickDelta(working, frame.Controller, checked(-cost.Amount));
                    break;
                case BehaviorCostKind.Tags:
                    ApplyTags(working, frame.Controller, checked(-cost.Amount));
                    break;
                case BehaviorCostKind.HostedCredits:
                    ChangeHostedCreditPool(
                        working,
                        frame.SourceCardId,
                        HostedCreditPoolOperation.Spend,
                        cost.Amount,
                        HostedCreditSpendRestriction.AbilityCost,
                        recipient: null);
                    break;
                case BehaviorCostKind.Counter:
                    if (!StableIdentifier.TryValidate(cost.CounterName))
                    {
                        throw new ResolutionFailureException("Ability counter cost has no stable counter name.");
                    }

                    AdjustCounter(
                        working,
                        frame.SourceCardId,
                        cost.CounterName!,
                        checked(-cost.Amount));
                    break;
                case BehaviorCostKind.TrashSelf:
                    MoveBehaviorCardToTrash(working, source);
                    source = GetCard(working, frame.SourceCardId);
                    deferredCounterCleanup = true;
                    break;
                case BehaviorCostKind.RemoveSelfFromGame:
                    MoveBehaviorCardToRemovedFromGame(working, source);
                    source = GetCard(working, frame.SourceCardId);
                    deferredCounterCleanup = true;
                    break;
                case BehaviorCostKind.Additional:
                    PayAdditionalCost(working, frame, cost, costIndex);
                    break;
                default:
                    throw new ResolutionFailureException($"Ability cost '{cost.Kind}' is not executable.");
            }
        }

        if (deferredCounterCleanup
            && !AbilityRetainsSourceCountersThroughChild(working, frame))
        {
            ClearCardCounters(working, frame.SourceCardId);
        }
    }

    private static void PayAdditionalCost(
        WorkingState working,
        AbilityFrame frame,
        BehaviorCost cost,
        int costIndex)
    {
        if (cost.AdditionalKind != BehaviorAdditionalCostKind.TrashCards
            || cost.AdditionalTarget is not
            {
                Kind: BehaviorTargetKind.ZoneCards,
                Zone: { } sourceZone,
                Filter: not null,
                Reveal: false,
            }
            || cost.AdditionalTarget.Filter.OwnerSide is not { } ownerSide
            || frame.AdditionalCostSelections is null)
        {
            throw new ResolutionFailureException(
                "An additional ability cost has no executable typed card-trash operation.");
        }

        if (costIndex < 0 || costIndex >= frame.Costs.Count)
        {
            throw new ResolutionFailureException("The additional ability cost is not present in its declaration.");
        }

        AbilityAdditionalCostSelection selection = frame.AdditionalCostSelections
            .SingleOrDefault(value => value.CostIndex == costIndex)
            ?? throw new ResolutionFailureException("The additional ability cost selection is missing.");
        ZoneState sourceZoneState = GetBehaviorZone(working, sourceZone, ownerSide);
        if (!BehaviorZoneMatches(sourceZone, sourceZoneState.Kind, ownerSide))
        {
            throw new ResolutionFailureException(
                "The additional ability cost source zone does not match its explicit owner.");
        }

        BehaviorCardFilter filter = cost.AdditionalTarget.Filter;
        foreach (CardInstanceId cardId in selection.CardIds)
        {
            CardInstanceState card = GetCard(working, cardId);
            if (card.OwnerSide != ownerSide
                || card.ZoneId != sourceZoneState.Id
                || !sourceZoneState.CardIds.Contains(cardId)
                || !BehaviorCardMatches(working, cardId, filter))
            {
                // This is a defensive second check. The complete bundle was
                // preflight before any payment mutation; keeping the guard at
                // the operation boundary also protects future callers.
                throw new ResolutionFailureException(
                    $"Additional-cost card '{cardId}' moved before payment.");
            }

            // TrashCard returns a replacement state for the destination
            // zone. Refresh it for every selected card so a second movement
            // cannot be based on the first iteration's stale zone snapshot.
            ZoneState destination = GetBehaviorZone(
                working,
                ownerSide == PlayerSide.Corp ? BehaviorZone.Archives : BehaviorZone.Heap,
                ownerSide);
            TrashCard(working, card, sourceZoneState, destination);
            CardInstanceState trashed = GetCard(working, cardId);
            ReplaceCard(working, new CardInstanceState(
                trashed.Id,
                trashed.DefinitionId,
                trashed.ZoneId,
                trashed.OwnerSide,
                faceUp: true,
                rezzed: false,
                trashed.AdvancementCounters));
            sourceZoneState = GetBehaviorZone(working, sourceZone, ownerSide);
        }
    }

    private static void MarkAbilityUsage(WorkingState working, AbilityFrame frame)
    {
        if (frame.Usage.Limit != BehaviorAbilityUsageLimit.OncePerTurn)
        {
            return;
        }

        if (CurrentAbilityFacts(working) is null)
        {
            throw new ResolutionFailureException("Ability usage requires current active turn facts.");
        }

        PlayerSide side = working.Turn.ActiveSide;
        TurnHistoryState history = working.Turn.History
            ?? throw new ResolutionFailureException("Ability usage requires current turn facts.");
        working.Turn = new TurnState(
            working.Turn.ActiveSide,
            working.Turn.Phase,
            working.Turn.TurnNumber,
            working.Turn.Step,
            history.MarkAbilityUsed(side, frame.SourceCardId, frame.AbilityKey));
    }

    private static void PushAbilityBehaviorChild(WorkingState working, AbilityFrame frame)
    {
        CardBehaviorDefinition definition = GetAbilityDefinition(working, frame);
        BehaviorPaidAbility declaration = GetAbilityDeclaration(definition, frame.AbilityKey);
        AbilityFrame awaitingChild = frame.With(AbilityLifecycleStage.AwaitingChild);
        string parentIdentity = BehaviorFrameIdentity.Compute(awaitingChild);
        BehaviorResolutionFrame child = new BehaviorResolutionFrame(
            definition,
            new BehaviorInvocationContext(frame.SourceCardId, frame.Controller, frame.Controller),
            working.BehaviorCatalog.Fingerprint,
            working.BehaviorCatalog.GetDefinitionFingerprint(frame.DefinitionId),
            working.Allocator.NextBehaviorFrameId(),
            frame.SourceZoneId,
            frame.SourceZoneKind,
            frame.BehaviorId,
            parentIdentity,
            abilityKey: frame.AbilityKey,
            rootInstructions: declaration.Instructions,
            instructions: declaration.Instructions);
        child = child.With(stage: BehaviorResolutionStage.Execute);

        ReplaceTopFrame(working, awaitingChild);
        working.ResolutionStack.Add(child);
    }

    private static void CompleteAbility(WorkingState working, AbilityFrame frame)
    {
        if (working.ResolutionStack.Count < 2)
        {
            throw new ResolutionFailureException("An ability completion requires its parent frame.");
        }

        if (AbilityRetainsSourceCountersThroughChild(working, frame))
        {
            ClearCardCounters(working, frame.SourceCardId);
        }

        ResolutionFrame parent = working.ResolutionStack[^2];
        working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
        switch (parent)
        {
            case TurnFrame turn when frame.Activation == BehaviorAbilityActivation.ClickAction:
                if (turn.Stage != TurnFrameStage.AwaitingAction)
                {
                    throw new ResolutionFailureException("A click ability parent changed before completion.");
                }

                working.ResolutionStack[^1] = new TurnFrame(
                    turn.Version,
                    turn.BehaviorId,
                    turn.ActiveSide,
                    turn.TurnNumber,
                    TurnFrameStage.ActionPriority,
                    TurnWindow(turn.ActiveSide),
                    turn.ActiveSide,
                    0,
                    turn.ForcedActionEnd);
                return;
            case TurnFrame turn when frame.Activation == BehaviorAbilityActivation.PaidAbility:
                if (turn.PriorityHolder is null || !turn.PriorityWindow.HasValue)
                {
                    throw new ResolutionFailureException("A paid turn ability parent lost its priority context.");
                }

                working.ResolutionStack[^1] = new TurnFrame(
                    turn.Version,
                    turn.BehaviorId,
                    turn.ActiveSide,
                    turn.TurnNumber,
                    turn.Stage,
                    turn.PriorityWindow,
                    turn.PriorityHolder,
                    0,
                    turn.ForcedActionEnd);
                return;
            case RunTimingFrame runFrame when frame.Activation == BehaviorAbilityActivation.PaidAbility:
                RunState run = working.RunState
                    ?? throw new ResolutionFailureException("A run ability parent lost its live run state.");
                if (run.Id != runFrame.RunId || run.Step != runFrame.Step)
                {
                    throw new ResolutionFailureException("A run ability parent changed before completion.");
                }

                if (run.Step == RunTimingStep.BreachAccessAbility)
                {
                    if (run.Priority is not null)
                    {
                        throw new ResolutionFailureException(
                            "A breach-access ability must restore a priority-free run timing frame.");
                    }

                    working.ResolutionStack[^1] = new RunTimingFrame(
                        runFrame.Version,
                        runFrame.BehaviorId,
                        runFrame.RunId,
                        runFrame.Step);
                    YieldBreachAccessDecision(working, run);
                    return;
                }

                if (run.Priority is null)
                {
                    throw new ResolutionFailureException("A run ability parent lost its priority context.");
                }

                RunPriorityState priority = run.Priority;
                working.RunState = run.WithPriority(new RunPriorityState(
                    priority.Version,
                    priority.Window,
                    priority.PriorityHolder,
                    0,
                    priority.CorpMayRezApproachedIce,
                    priority.CorpMayRezNonIce));
                working.ResolutionStack[^1] = new RunTimingFrame(
                    runFrame.Version,
                    runFrame.BehaviorId,
                    runFrame.RunId,
                    runFrame.Step);
                return;
            default:
                throw new ResolutionFailureException("An ability parent frame is not a matching paid window.");
        }
    }

    private static bool AbilityParametersMatch(
        AbilityActionParameters actual,
        AbilityActionParameters expected) =>
        actual.SourceCardId == expected.SourceCardId
        && actual.DefinitionId == expected.DefinitionId
        && string.Equals(actual.AbilityKey, expected.AbilityKey, StringComparison.Ordinal)
        && actual.Timing == expected.Timing
        && actual.Activation == expected.Activation
        && actual.Costs.SequenceEqual(expected.Costs)
        && actual.TargetCardIds.SequenceEqual(expected.TargetCardIds)
        && AdditionalCostSelectionsEqual(actual.AdditionalCostSelections, expected.AdditionalCostSelections)
        && actual.Usage.Equals(expected.Usage)
        && string.Equals(actual.CatalogFingerprint, expected.CatalogFingerprint, StringComparison.Ordinal)
        && string.Equals(actual.DefinitionFingerprint, expected.DefinitionFingerprint, StringComparison.Ordinal)
        && actual.SourceZoneId == expected.SourceZoneId
        && actual.SourceZoneKind == expected.SourceZoneKind;

    private static bool AbilityFramesMatch(
        AbilityFrame actual,
        AbilityFrame expected) =>
        actual.Version == expected.Version
        && string.Equals(actual.BehaviorId, expected.BehaviorId, StringComparison.Ordinal)
        && actual.DefinitionId == expected.DefinitionId
        && actual.SourceCardId == expected.SourceCardId
        && string.Equals(actual.AbilityKey, expected.AbilityKey, StringComparison.Ordinal)
        && actual.Controller == expected.Controller
        && actual.Timing == expected.Timing
        && actual.Activation == expected.Activation
        && actual.Costs.SequenceEqual(expected.Costs)
        && actual.TargetCardIds.SequenceEqual(expected.TargetCardIds)
        && AdditionalCostSelectionsEqual(actual.AdditionalCostSelections, expected.AdditionalCostSelections)
        && actual.Usage.Equals(expected.Usage)
        && string.Equals(actual.CatalogFingerprint, expected.CatalogFingerprint, StringComparison.Ordinal)
        && string.Equals(actual.DefinitionFingerprint, expected.DefinitionFingerprint, StringComparison.Ordinal)
        && actual.SourceZoneId == expected.SourceZoneId
        && actual.SourceZoneKind == expected.SourceZoneKind
        && actual.Stage == expected.Stage
        && string.Equals(actual.ParentFrameIdentity, expected.ParentFrameIdentity, StringComparison.Ordinal);

    private static void AddAbilityActions(
        WorkingState working,
        List<LegalAction> actions,
        bool suppressCorpCardTrash = false)
    {
        foreach (AbilityActionParameters parameters in MaterializeAbilityActionParameters(working))
        {
            if (suppressCorpCardTrash
                && IsAbilityBlockedByRunRestriction(working.BehaviorCatalog, parameters))
            {
                continue;
            }

            string description = $"Use ability {parameters.SourceCardId.Value}/{parameters.AbilityKey}";
            if (parameters.AdditionalCostSelections is { Count: > 0 } selections)
            {
                // The card choices are part of the issued payload, so make
                // otherwise-identical combinations distinguishable to the
                // decision owner.  Cost and card order are declaration and
                // physical-zone order respectively and therefore stable.
                string selectedCards = string.Join(
                    "; ",
                    selections
                        .OrderBy(selection => selection.CostIndex)
                        .Select(selection =>
                            $"{selection.CostKey}: {string.Join(',', selection.CardIds.Select(cardId => cardId.Value))}"));
                description += $" (additional cost {selectedCards})";
            }

            actions.Add(new LegalAction(
                working.Allocator.NextActionId(),
                ActionKind.UseAbility,
                description,
                parameters));
        }
    }

    private static bool AdditionalCostSelectionsEqual(
        IReadOnlyList<AbilityAdditionalCostSelection>? actual,
        IReadOnlyList<AbilityAdditionalCostSelection>? expected)
    {
        AbilityAdditionalCostSelection[] left = (actual ?? []).ToArray();
        AbilityAdditionalCostSelection[] right = (expected ?? []).ToArray();
        return left.Length == right.Length
            && left.Zip(right).All(pair =>
                pair.First.CostIndex == pair.Second.CostIndex
                && string.Equals(pair.First.CostKey, pair.Second.CostKey, StringComparison.Ordinal)
                && pair.First.CardIds.SequenceEqual(pair.Second.CardIds));
    }
}

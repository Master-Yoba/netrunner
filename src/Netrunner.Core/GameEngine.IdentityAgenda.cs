namespace Netrunner.Core;

/// <summary>Composable identity and agenda timing rules; no card identifiers are interpreted here.</summary>
public static partial class GameEngine
{
    private const string ScoreProhibitionKey = "score-prohibited";

    private static void QueueIdentityOccurrence(WorkingState working, OccurrenceKind kind,
        PlayerSide side, CardInstanceId? subject = null)
    {
        if (!HasActiveCatalogTrigger(working, kind)) return;
        PlayerId controller = GetPlayer(working, side).Id;
        working.ResolutionStack.Add(new EffectResolutionFrame(controller, new CostBundle(),
            new EffectBundle(new GameplayOccurrenceEffect(kind, controller, subject))));
    }

    private static bool CatalogIdentityTriggerMatches(WorkingState working, CardInstanceState source,
        BehaviorTrigger trigger, OccurrenceSnapshot occurrence)
    {
        if (trigger.OccurrenceControllerSide is { } side
            && GetPlayer(working, occurrence.Controller).Side != side) return false;
        TurnFactsState? facts = working.Turn.History?.ThisTurn(working.Turn.ActiveSide);
        if (trigger.OncePerTurn && facts?.HasUsedAbility(source.Id, trigger.Key) == true) return false;
        return trigger.Condition switch
        {
            null => true,
            BehaviorTriggerCondition.FacedownArchives => working.Cards.Any(card => !card.FaceUp
                && working.Zones.Any(zone => zone.Id == card.ZoneId && zone.Kind == ZoneKind.CorpDiscard)),
            BehaviorTriggerCondition.FirstRunnerTag => occurrence.Effect is TagAdjustmentEffect { Delta: > 0 } tag
                && tag.Target == GetPlayer(working, PlayerSide.Runner).Id
                && (facts is null || facts.TagTurnChanges.Count(change => change.Delta > 0
                    && change.AffectedPlayerId == tag.Target) == 1),
            BehaviorTriggerCondition.HqOrRdRun => occurrence.Effect is GameplayOccurrenceEffect { ServerId: { } server, RunContext.AccessOccurrenceCount: > 0 }
                && working.Servers.Any(value => value.Id == server
                    && value.Kind is ServerKind.Headquarters or ServerKind.RAndD),
            _ => false,
        };
    }

    private static void MarkCatalogTriggerUsed(WorkingState working, CardInstanceState source, BehaviorTrigger trigger)
    {
        if (!trigger.OncePerTurn || working.Turn.History?.ThisTurn(working.Turn.ActiveSide) is null) return;
        TurnState turn = working.Turn;
        ReplaceTurn(working, turn.ActiveSide, turn.Phase, turn.TurnNumber, turn.Step,
            turn.History.MarkAbilityUsed(turn.ActiveSide, source.Id, trigger.Key));
    }

    private static bool IsScoringProhibited(WorkingState working) =>
        working.Turn.History?.ThisTurn(working.Turn.ActiveSide)?.UsedAbilityKeys
            .Any(key => key.AbilityKey == ScoreProhibitionKey) == true;

    private static void MarkScoreProhibition(WorkingState working, CardInstanceId source)
    {
        TurnState turn = working.Turn;
        if (turn.History?.ThisTurn(turn.ActiveSide) is not null)
            ReplaceTurn(working, turn.ActiveSide, turn.Phase, turn.TurnNumber, turn.Step,
                turn.History.MarkAbilityUsed(turn.ActiveSide, source, ScoreProhibitionKey));
    }

    private static void ResolveBehaviorRezIgnoringCosts(WorkingState working, BehaviorResolutionFrame frame,
        BehaviorInstruction instruction)
    {
        IReadOnlyList<CardInstanceId> selected = ResolveBehaviorCards(working, frame, instruction.Target);
        AdvanceBehaviorCursor(working, frame);
        if (selected.Count == 0) return;
        if (selected.Count != 1) throw new ResolutionFailureException("A rez instruction requires one selected ICE.");
        CardInstanceState card = GetCard(working, selected[0]);
        ZoneState zone = ZoneForCard(working, card.Id);
        if (zone.Kind != ZoneKind.ServerIce || card.OwnerSide != PlayerSide.Corp
            || working.RuntimeCardCatalog?.GetSpec(card.DefinitionId).CardClass != RuntimeCardClass.Ice)
            throw new ResolutionFailureException("The selected rez target must be installed Corp ICE.");
        if (card.Rezzed) return;
        ReplaceCard(working, new CardInstanceState(card.Id, card.DefinitionId, card.ZoneId, card.OwnerSide,
            faceUp: true, rezzed: true, card.AdvancementCounters));
        Emit(working, EngineEventKinds.CardRezzed, new CardRezzedEventPayload(card.Id, 0));
        TrashCorpUniqueRezConflicts(working, card.Id);
        QueueCardRezzedOccurrence(working, GetPlayer(working, PlayerSide.Corp).Id, card.Id, zone.ServerId);
    }
    private static void ResolveSearchedDeckShuffle(WorkingState working, BehaviorResolutionFrame frame,
        BehaviorInstruction instruction)
    {
        CardBehaviorDefinition definition = GetBehaviorDefinition(working, frame);
        IReadOnlyList<BehaviorInstruction> root = frame.TriggerKey is { } trigger
            ? GetTriggerDeclaration(definition, trigger).Instructions
            : frame.AbilityKey is { } ability ? GetAbilityDeclaration(definition, ability).Instructions : definition.Instructions;
        List<BehaviorInstruction> replay = root.ToList();
        BehaviorInstruction? search = null;
        foreach (BehaviorContinuationStep step in frame.Continuation)
        {
            if (step.InstructionCursor < 0 || step.InstructionCursor >= replay.Count)
                throw new ResolutionFailureException("Search shuffle has an unreachable continuation.");
            if (step.Kind == BehaviorContinuationKind.ChoiceSelection)
                search = replay[step.InstructionCursor].Kind == BehaviorInstructionKind.Search ? replay[step.InstructionCursor] : null;
            if (!BehaviorContinuation.TryApply(replay, step, out _))
                throw new ResolutionFailureException("Search shuffle has an unreachable continuation.");
        }
        if (search?.SourceZone != instruction.SourceZone || search?.Target is null)
            throw new ResolutionFailureException("Search shuffle must follow a search of its named deck.");
        PlayerSide owner = search.Target.Filter?.OwnerSide ?? GetPlayer(working, frame.Controller).Side;
        // The found card remains in BehaviorSetAside. Shuffle only the remaining
        // deck, preserving its binding through reveal and the final transfer.
        ShuffleProvenEmptyZone(working, frame, instruction.SourceZone!.Value, owner);
    }

}

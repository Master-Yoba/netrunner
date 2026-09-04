namespace Netrunner.Core;

/// <summary>Resolution, advancement, and agenda-scoring reducers.</summary>
public static partial class GameEngine
{
    private static void Advance(WorkingState working, int maxResolutionSteps)
    {
        int steps = 0;
        while (working.PendingDecision is null && working.ResolutionStack.Count > 0)
        {
            if (steps >= maxResolutionSteps)
            {
                // Ability payment commits at an explicit serialized
                // checkpoint. Permit a deliberately tiny budget to return
                // that checkpoint so a caller can persist and resume it;
                // every other incomplete resolution remains bounded-failure
                // compatible with the existing reducer contract.
                if (working.ResolutionStack[^1] is AbilityFrame
                    {
                        Stage: AbilityLifecycleStage.CostCheckpoint,
                    })
                {
                    break;
                }

                throw new ResolutionStepBudgetExceededException(maxResolutionSteps);
            }

            steps++;
            ResolutionFrame frame = working.ResolutionStack[^1]
                ?? throw new ResolutionFailureException("The resolution stack contained a null frame.");

            IReadOnlyList<StateInvariantViolation> frameViolations = ResolutionFrameValidator.Collect(frame);
            if (frameViolations.Count > 0)
            {
                throw new ResolutionFailureException(FormatViolations(frameViolations));
            }

            switch (frame)
            {
                case ChooseNumberThenAdjustCreditsFrame chooseThenAdjust:
                    StepChooseNumberThenAdjustCredits(working, chooseThenAdjust);
                    break;
                case ChooseNumberFrame chooseNumber:
                    StepChooseNumber(working, chooseNumber);
                    break;
                case ChooseCardFrame chooseCard:
                    StepChooseCard(working, chooseCard);
                    break;
                case AdjustCreditsFrame adjustCredits:
                    working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
                    ApplyCreditDelta(working, adjustCredits.Target, adjustCredits.Delta);
                    break;
                case ToyTurnFrame toyTurn:
                    StepToyTurn(working, toyTurn);
                    break;
                case TurnFrame turn:
                    StepTurn(working, turn);
                    break;
                case TurnDiscardFrame turnDiscard:
                    StepTurnDiscard(working, turnDiscard);
                    break;
                case InstallFrame install:
                    StepInstall(working, install);
                    break;
                case CardPlayFrame cardPlay:
                    StepCardPlay(working, cardPlay);
                    break;
                case RunTimingFrame runTiming:
                    StepRunTiming(working, runTiming);
                    break;
                case IceSubroutineFrame iceSubroutine:
                    StepIceSubroutineFrame(working, iceSubroutine);
                    break;
                case EffectResolutionFrame effectResolution:
                    StepEffectResolution(working, effectResolution);
                    break;
                case EffectCheckpointFrame effectCheckpoint:
                    StepEffectCheckpoint(working, effectCheckpoint);
                    break;
                case MemoryCleanupFrame memoryCleanup:
                    StepMemoryCleanup(working, memoryCleanup);
                    break;
                case BehaviorResolutionFrame behavior:
                    StepBehaviorResolution(working, behavior);
                    break;
                case AbilityFrame ability:
                    StepAbility(working, ability);
                    break;
                default:
                    throw new ResolutionFailureException(
                        $"No resolution behavior is registered for frame type '{frame.GetType().Name}'.");
            }
        }
    }

    private static void AdvanceToyAgenda(WorkingState working, CardInstanceId cardId)
    {
        CardInstanceState card = GetCard(working, cardId);
        ToyCardBehavior behavior = ToyCardBehaviorCatalog.Get(card.DefinitionId);
        ZoneState remoteRoot = GetRemoteRoot(working);
        PlayerState corp = GetPlayer(working, PlayerSide.Corp);
        if (corp.Side != PlayerSide.Corp
            || card.OwnerSide != PlayerSide.Corp
            || card.ZoneId != remoteRoot.Id
            || behavior.Kind != ToyCardKind.CorpAgenda
            || card.AdvancementCounters >= behavior.AdvancementRequirement)
        {
            throw new ResolutionFailureException($"Card '{cardId}' is not an installed Corp agenda.");
        }

        if (corp.Credits < 1)
        {
            throw new ResolutionFailureException("Advancing an agenda requires one credit.");
        }

        int counters = checked(card.AdvancementCounters + 1);
        ReplaceCard(working, new CardInstanceState(
            card.Id,
            card.DefinitionId,
            card.ZoneId,
            card.OwnerSide,
            card.FaceUp,
            card.Rezzed,
            counters));
        ApplyCreditDelta(working, corp.Id, -1);
        Emit(working, EngineEventKinds.AgendaAdvanced, new AgendaAdvancedEventPayload(
            cardId,
            counters,
            behavior.AdvancementRequirement));
    }

    private static void ScoreToyAgenda(WorkingState working, CardInstanceId cardId)
    {
        CardInstanceState card = GetCard(working, cardId);
        ToyCardBehavior behavior = ToyCardBehaviorCatalog.Get(card.DefinitionId);
        ZoneState remoteRoot = GetRemoteRoot(working);
        if (card.OwnerSide != PlayerSide.Corp
            || card.ZoneId != remoteRoot.Id
            || behavior.Kind != ToyCardKind.CorpAgenda
            || card.AdvancementCounters < behavior.AdvancementRequirement)
        {
            throw new ResolutionFailureException($"Card '{cardId}' is not a completed installed Corp agenda.");
        }

        ZoneState score = GetZone(working, ZoneKind.CorpScore);
        MoveCard(working, card, remoteRoot, score);
        ReplaceCard(working, new CardInstanceState(
            card.Id,
            card.DefinitionId,
            score.Id,
            PlayerSide.Corp,
            true,
            card.Rezzed,
            card.AdvancementCounters));
        AddAgendaPoints(working, PlayerSide.Corp, behavior.AgendaPoints);
        Emit(working, EngineEventKinds.AgendaScored, new AgendaScoredEventPayload(
            PlayerSide.Corp,
            cardId,
            behavior.AgendaPoints));
        if (GetPlayer(working, PlayerSide.Corp).AgendaPoints >= ToyRules.AgendaPointsToWin)
        {
            CompleteToyGame(working, PlayerSide.Corp, GameWinReason.AgendaPoints);
        }
    }

    private static void StealToyAgenda(WorkingState working, CardInstanceId cardId)
    {
        CardInstanceState card = GetCard(working, cardId);
        ToyCardBehavior behavior = ToyCardBehaviorCatalog.Get(card.DefinitionId);
        ZoneState remoteRoot = GetRemoteRoot(working);
        if (card.OwnerSide != PlayerSide.Corp
            || card.ZoneId != remoteRoot.Id
            || behavior.Kind != ToyCardKind.CorpAgenda)
        {
            throw new ResolutionFailureException($"Card '{cardId}' is not an installed agenda available to steal.");
        }

        ZoneState score = GetZone(working, ZoneKind.RunnerScore);
        MoveCard(working, card, remoteRoot, score);
        ReplaceCard(working, new CardInstanceState(
            card.Id,
            card.DefinitionId,
            score.Id,
            PlayerSide.Corp,
            true,
            card.Rezzed,
            card.AdvancementCounters));
        AddAgendaPoints(working, PlayerSide.Runner, behavior.AgendaPoints);
        Emit(working, EngineEventKinds.AgendaStolen, new AgendaStolenEventPayload(
            PlayerSide.Runner,
            cardId,
            behavior.AgendaPoints));
        if (GetPlayer(working, PlayerSide.Runner).AgendaPoints >= ToyRules.AgendaPointsToWin)
        {
            CompleteToyGame(working, PlayerSide.Runner, GameWinReason.AgendaPoints);
        }
    }

    private static void AddAgendaPoints(WorkingState working, PlayerSide side, int points)
    {
        PlayerState player = GetPlayer(working, side);
        ReplacePlayer(working, new PlayerState(
            player.Id,
            player.Side,
            player.Credits,
            player.Clicks,
            checked(player.AgendaPoints + points)));
    }
}

namespace Netrunner.Core;

public static partial class GameEngine
{
    private static void YieldIceSubroutineDamage(
        WorkingState working, RunState run, CardInstanceState ice,
        RunSubroutineState subroutine, DamageSubroutineSpec damage)
    {
        ZoneState iceZone = GetRunIceZone(working, run);
        var frame = new IceSubroutineFrame(
            run.Id, run.Encounter!.Id, ice.Id, ice.DefinitionId,
            subroutine.Id, subroutine.Ordinal, run.AttackedServer,
            iceZone.Id, iceZone.CardIds, working.BehaviorCatalog.Fingerprint, []);
        working.ResolutionStack[^1] = frame.WithStage(IceSubroutineFrameStage.AwaitingDamage);
        working.ResolutionStack.Add(new EffectResolutionFrame(
            GetPlayer(working, PlayerSide.Corp).Id, new CostBundle(),
            new EffectBundle(new DamageEffect(GetPlayer(working, PlayerSide.Runner).Id, damage.Kind, damage.Amount))));
    }

    // Only the exact damage instruction directly nested under this ICE cursor
    // may record its victim. Damage caused by a reaction has a different parent
    // chain, so cannot overwrite Diviner's printed-cost provenance.
    private static void RecordIceDamageVictim(WorkingState working, CardInstanceId victim)
    {
        int count = working.ResolutionStack.Count;
        if (count >= 3
            && working.ResolutionStack[count - 1] is EffectCheckpointFrame
            && working.ResolutionStack[count - 2] is EffectResolutionFrame
            && working.ResolutionStack[count - 3] is IceSubroutineFrame
            { Stage: IceSubroutineFrameStage.AwaitingDamage } frame)
        {
            working.ResolutionStack[count - 3] = frame.WithDamageResult(victim);
        }
    }

    private static int? DivinerPrintedCost(WorkingState working)
    {
        if (working.ResolutionStack.Count < 3
            || working.ResolutionStack[^3] is not IceSubroutineFrame
            {
                Stage: IceSubroutineFrameStage.AwaitingDamage,
                DamageTrashedCardId: { } victim
            } frame
            || !working.BehaviorCatalog.TryGetIce(frame.IceDefinitionId, out IceBehaviorSpec? spec)
            || working.RunState?.EncounterSubroutineSpec(working.BehaviorCatalog, spec, frame.EncounterId, frame.Ordinal) is not DamageSubroutineSpec
            { FollowUp: DamageSubroutineFollowUp.EndRunIfTrashedCostOdd }
            || working.RuntimeCardCatalog is null
            || !working.RuntimeCardCatalog.TryGet(GetCard(working, victim).DefinitionId, out CardRuntimeSpec? metadata)) return null;
        return metadata.PlayCost ?? metadata.InstallCost;
    }

    private static void ResumeIceSubroutineDamage(WorkingState working, IceSubroutineFrame frame)
    {
        RunState run = working.RunState ?? throw new ResolutionFailureException("Damage requires its live encounter.");
        CardInstanceState ice = GetCard(working, frame.IceId);
        if (!working.BehaviorCatalog.TryGetIce(ice.DefinitionId, out IceBehaviorSpec? spec)
            || run.EncounterSubroutineSpec(working.BehaviorCatalog, spec, frame.EncounterId, frame.Ordinal) is not DamageSubroutineSpec damage)
            throw new ResolutionFailureException("Damage continuation no longer matches its printed subroutine.");
        RunSubroutineState subroutine = run.Encounter!.Subroutines.Single(value => value.Id == frame.SubroutineId);
        if (damage.FollowUp == DamageSubroutineFollowUp.OfferJackOut)
        {
            var choices = MaterializeIceSubroutineChoices(working, run, ice, subroutine, damage, frame.FrameIdentity);
            working.ResolutionStack[^1] = frame.WithChoices(choices.Choices);
            return;
        }

        if (damage.FollowUp == DamageSubroutineFollowUp.EndRunIfTrashedCostOdd
            && frame.DamageTrashedCardId is { } victim)
        {
            CardInstanceState card = GetCard(working, victim);
            if (working.RuntimeCardCatalog is null
                || !working.RuntimeCardCatalog.TryGet(card.DefinitionId, out CardRuntimeSpec? runtime))
                throw new ResolutionFailureException("Conditional damage requires authenticated printed card costs.");
            int? cost = runtime.PlayCost ?? runtime.InstallCost;
            if (cost is { } printed && printed % 2 == 1)
            {
                FinishIceSubroutineRun(working, run, ice, subroutine, RunEndReason.EndedByRule);
                return;
            }
        }

        // VP-ICE-GAINS-SUBROUTINE: "The Runner draws 1 card" after the damage (Stick and Poke's subroutine).
        if (damage.FollowUp == DamageSubroutineFollowUp.RunnerDrawsOne && working.Outcome.Kind != GameOutcomeKind.Won)
        {
            TryDrawCard(working, PlayerSide.Runner);
        }

        ResolveIceSubroutineWithoutChoice(working, run, ice, subroutine);
        working.ResolutionStack[^1] = new RunTimingFrame(run.Id, RunTimingStep.Encounter);
    }

    private static void FinishIceSubroutineRun(
        WorkingState working, RunState run, CardInstanceState ice,
        RunSubroutineState subroutine, RunEndReason reason)
    {
        if (reason == RunEndReason.JackedOut)
        {
            Emit(working, EngineEventKinds.IceSubroutineJackOut,
                new IceSubroutineJackOutEventPayload(run.Id, run.Encounter!.Id, ice.Id, subroutine.Id));
        }
        else if (PreventsRunEnd(working, run))
        {
            // EL-PREVENT-END-RUN: the run goes on past this subroutine.
            ResolveIceSubroutineWithoutChoice(working, working.RunState!, ice, subroutine);
            working.ResolutionStack[^1] = new RunTimingFrame(run.Id, RunTimingStep.Encounter);
            return;
        }

        ResolveIceSubroutineWithoutChoice(working, run, ice, subroutine);
        run = working.RunState!;
        RunEncounterState encounter = run.Encounter!;
        var completed = new RunEncounterState(encounter.Version, encounter.Id, encounter.IceId,
            RunEncounterStatus.Complete, encounter.Subroutines);
        run = run.WithLingeringEffects(run.LingeringEffects
            .Where(effect => effect.Expiry != RunLingeringEffectExpiry.Encounter).ToArray());
        Emit(working, EngineEventKinds.EncounterCompleted, new EncounterCompletedEventPayload(run.Id, encounter.Id));
        working.ResolutionStack[^1] = new RunTimingFrame(run.Id, RunTimingStep.Encounter);
        SetRunTiming(working, run.WithTiming(RunTimingStep.RunEnd, run.CurrentIceIndex,
            run.CurrentIceId, false, true, reason, null, completed, run.AccessState));
    }
}

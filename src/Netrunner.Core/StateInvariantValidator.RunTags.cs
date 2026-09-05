namespace Netrunner.Core;

public static partial class StateInvariantValidator
{
    private static bool IsExpectedRunTagEffectFrame(GameState state, EffectResolutionFrame frame,
        RunState run, IBehaviorCatalog catalog)
    {
        if (run.Step != RunTimingStep.Encounter
            || run.Encounter is not { Status: RunEncounterStatus.Initialized } encounter
            || frame.Controller != state.Players.Single(player => player.Side == PlayerSide.Corp).Id
            || frame.Effects.Instructions.Count != 1
            || frame.Effects.Instructions[0] is not TagAdjustmentEffect tag
            || tag.Target != state.Players.Single(player => player.Side == PlayerSide.Runner).Id
            || run.ResolvedSubroutineLedger is not { Count: > 0 } ledger
            || ledger[^1] is not { } entry
            || entry.EncounterId != encounter.Id || entry.IceId != encounter.IceId
            || !catalog.TryGetIce(entry.IceDefinitionId, out IceBehaviorSpec? ice)
            || entry.Ordinal < 0 || entry.Ordinal >= ice.Subroutines.Count
            || !encounter.Subroutines.Any(subroutine => subroutine.Id == entry.SubroutineId
                && subroutine.Resolved && !subroutine.Broken)) return false;
        return ice.Subroutines[entry.Ordinal] switch
        {
            GiveTagsSubroutineSpec printed => tag.Delta == printed.Amount,
            PayCreditsOrTagSubroutineSpec printed => tag.Delta == printed.TagAmount,
            _ => false,
        };
    }
}

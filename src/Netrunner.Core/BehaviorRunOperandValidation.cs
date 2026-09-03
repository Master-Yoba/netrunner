namespace Netrunner.Core;

internal static class BehaviorRunOperandValidation
{
    internal static void RequireNoStartRun(
        IReadOnlyList<BehaviorInstruction> instructions,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        if (instructions.Any(ContainsAnyStartRun))
        {
            throw new ArgumentException(
                "StartRun is valid only as the single final instruction of an action-phase click ability.",
                parameterName);
        }
    }

    internal static void RequireFinalStartRun(
        IReadOnlyList<BehaviorInstruction> instructions,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(instructions);

        int firstStartRun = -1;
        for (int index = 0; index < instructions.Count; index++)
        {
            BehaviorInstruction instruction = instructions[index];
            if (instruction.Kind != BehaviorInstructionKind.StartRun)
            {
                if (ContainsNestedStartRun(instruction))
                {
                    throw new ArgumentException(
                        "A StartRun instruction must be the single final instruction in its root program.",
                        parameterName);
                }

                continue;
            }

            if (firstStartRun >= 0 || index != instructions.Count - 1)
            {
                throw new ArgumentException(
                    "A StartRun instruction must be the single final instruction in its root program.",
                    parameterName);
            }

            firstStartRun = index;
        }
    }

    private static bool ContainsNestedStartRun(BehaviorInstruction instruction)
    {
        if (instruction.Choice is { } choice
            && choice.Options.Any(option => option.Instructions.Any(ContainsAnyStartRun)))
        {
            return true;
        }

        return instruction.Branch is { } branch
            && (branch.WhenTrue.Any(ContainsAnyStartRun)
                || branch.WhenFalse.Any(ContainsAnyStartRun));
    }

    private static bool ContainsAnyStartRun(BehaviorInstruction instruction) =>
        instruction.Kind == BehaviorInstructionKind.StartRun
        || ContainsNestedStartRun(instruction);
}

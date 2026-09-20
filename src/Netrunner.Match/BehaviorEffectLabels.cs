using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>Describes declarations, never predicts outcomes or evaluates game state.</summary>
internal static class BehaviorEffectLabels
{
    internal static string? Describe(IReadOnlyList<BehaviorInstruction> instructions, PlayerSide controller)
    {
        string?[] parts = instructions.Select(instruction => Describe(instruction, controller)).ToArray();
        return parts.Length == 0 || parts.Any(part => part is null) ? null
            : string.Join("; then ", parts.Where(part => part!.Length > 0));
    }

    private static string? Describe(BehaviorInstruction instruction, PlayerSide controller)
    {
        string recipient = instruction.Recipient switch
        {
            BehaviorPlayerSelector.Corp => "Corp",
            BehaviorPlayerSelector.Runner => "Runner",
            BehaviorPlayerSelector.ActingPlayer => "Acting player",
            _ => controller.ToString(),
        };
        return instruction.Kind switch
        {
            BehaviorInstructionKind.NoOp => "",
            BehaviorInstructionKind.GainCredits when instruction.AmountOperand is { } amount =>
                $"{recipient} gains {amount.Multiplier} credits per {amount.SourceCounterName} counter",
            BehaviorInstructionKind.GainCredits => $"{recipient} gains {Count(instruction.Amount, "credit")}",
            BehaviorInstructionKind.Draw => $"{recipient} draws {Count(instruction.Amount, "card")}",
            BehaviorInstructionKind.AddTags => $"{recipient} takes {Count(instruction.Amount, "tag")}",
            BehaviorInstructionKind.PlaceHostedCredits => $"Place {instruction.Amount} hosted credits",
            BehaviorInstructionKind.TakeHostedCredits => $"Take {instruction.Amount} hosted credits",
            BehaviorInstructionKind.TakeUpToHostedCredits => $"Take up to {instruction.Amount} hosted credits",
            BehaviorInstructionKind.TakeAllHostedCredits => "Take all hosted credits",
            BehaviorInstructionKind.StartRun when instruction.RunOperand is { } run => $"Run {Server(run.ServerKind)}",
            BehaviorInstructionKind.ModifyEncounteredIceStrength => $"Encountered ICE strength {instruction.Amount:+#;-#;0}",
            BehaviorInstructionKind.TrashCard => "Trash the chosen card",
            BehaviorInstructionKind.TrashSelf => "Trash this card",
            BehaviorInstructionKind.InstallCard => "Install the chosen card",
            BehaviorInstructionKind.AddToHand => "Add the chosen card to hand",
            BehaviorInstructionKind.Choose when instruction.Choice?.Mode == BehaviorChoiceMode.OrderSelection => "Choose the order of effects",
            BehaviorInstructionKind.Choose when instruction.Choice is { Mode: BehaviorChoiceMode.TargetSelection } choice =>
                choice.Cardinality.Kind == BehaviorChoiceCardinalityKind.UpTo ? $"Choose up to {choice.Cardinality.Count} cards"
                    : choice.Cardinality.Kind == BehaviorChoiceCardinalityKind.Exactly ? $"Choose {choice.Cardinality.Count} cards" : "Choose cards",
            BehaviorInstructionKind.ShuffleIntoDeck => $"Shuffle chosen cards into {(controller == PlayerSide.Corp ? "R&D" : "Stack")}",
            _ => null,
        };
    }

    private static string Count(int amount, string noun) => $"{amount} {noun}{(amount == 1 ? "" : "s")}";

    private static string Server(ServerKind kind) => kind switch
    {
        ServerKind.Headquarters => "HQ",
        ServerKind.RAndD => "R&D",
        ServerKind.Archives => "Archives",
        _ => "a server",
    };
}

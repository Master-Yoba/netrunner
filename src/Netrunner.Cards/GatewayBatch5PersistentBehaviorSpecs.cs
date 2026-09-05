using Netrunner.Core;

namespace Netrunner.Cards;

public static class GatewayBatch5PersistentBehaviorSpecs
{
    public static CardBehaviorDefinition AmazeAmusements { get; } = new(
        GatewayBatch5CardCodes.AmazeAmusements, PlayerSide.Corp,
        triggers:
        [
            new BehaviorTrigger("amaze-run-end-tags", OccurrenceKind.RunEnded, TriggerMode.Mandatory,
                BehaviorTriggerScope.Any, null,
                [new BehaviorInstruction(BehaviorInstructionKind.AddTags, amount: 2,
                    recipient: BehaviorPlayerSelector.Runner)],
                requireSourceOnAttackedServer: true, persistent: true, requireAgendaStolenDuringRun: true),
        ]);
}

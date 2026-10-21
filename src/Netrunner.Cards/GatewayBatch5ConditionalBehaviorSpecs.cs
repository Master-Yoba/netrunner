using System.Collections.ObjectModel;

using Netrunner.Core;

namespace Netrunner.Cards;

public static class GatewayBatch5ConditionalBehaviorSpecs
{
    public static IReadOnlyList<CardBehaviorDefinition> GenericSpecs { get; } = new ReadOnlyCollection<CardBehaviorDefinition>(
    [
        new(GatewayBatch5CardCodes.Ping, PlayerSide.Corp, triggers:
        [
            new BehaviorTrigger("ping-rez-tag", OccurrenceKind.CardRezzed, TriggerMode.Mandatory,
                BehaviorTriggerScope.Self, null,
                [new BehaviorInstruction(BehaviorInstructionKind.AddTags,
                    recipient: BehaviorPlayerSelector.Runner, amount: 1)],
                requireSourceOnAttackedServer: true),
        ]),
        new(GatewayBatch5CardCodes.Pharos, PlayerSide.Corp, canBeAdvanced: true),
    ]);
}

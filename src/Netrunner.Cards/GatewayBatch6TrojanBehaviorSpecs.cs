using System.Collections.ObjectModel;

using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>Gateway's ICE-hosted viruses, authored from the vendor snapshot.</summary>
public static class GatewayBatch6TrojanBehaviorSpecs
{
    public static IReadOnlyList<CardBehaviorDefinition> GenericSpecs { get; } =
        new ReadOnlyCollection<CardBehaviorDefinition>(
        [
            Create("30004", "botulus", new HostingBehaviorSpec(breakHostSubroutine: true)),
            Create("30017", "tranquilizer", new HostingBehaviorSpec(derezAtVirusCounters: 3)),
        ]);

    public static IReadOnlyList<CardDefinitionId> AssignedDefinitionIds { get; } =
        new ReadOnlyCollection<CardDefinitionId>(GenericSpecs.Select(spec => spec.DefinitionId).ToArray());

    private static CardBehaviorDefinition Create(string code, string key, HostingBehaviorSpec hosting) => new(
        new CardDefinitionId(code), PlayerSide.Runner,
        triggers:
        [
            new BehaviorTrigger($"{key}-install", OccurrenceKind.CardInstalled, TriggerMode.Mandatory,
                BehaviorTriggerScope.Self, null,
                [new BehaviorInstruction(BehaviorInstructionKind.AddCounter, amount: 1, argument: "virus")]),
            new BehaviorTrigger($"{key}-turn-begin", OccurrenceKind.TurnBegin, TriggerMode.Mandatory,
                BehaviorTriggerScope.Controller, null,
                [new BehaviorInstruction(BehaviorInstructionKind.AddCounter, amount: 1, argument: "virus")]),
        ], hosting: hosting);
}

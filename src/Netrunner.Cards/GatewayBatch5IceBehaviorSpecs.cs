using System.Collections.ObjectModel;

using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>Printed ICE instructions for Slice 4.6.</summary>
public static class GatewayBatch5IceBehaviorSpecs
{
    public static IReadOnlyList<IceBehaviorSpec> IceSpecs { get; } = new ReadOnlyCollection<IceBehaviorSpec>(
    [
        new(GatewayBatch5CardCodes.Diviner, 3, IceType.CodeGate, 2,
            [new DamageSubroutineSpec(DamageKind.Net, 1, DamageSubroutineFollowUp.EndRunIfTrashedCostOdd)], []),
        new(GatewayBatch5CardCodes.Karuna, 3, IceType.Sentry, 4,
            [new DamageSubroutineSpec(DamageKind.Net, 2, DamageSubroutineFollowUp.OfferJackOut), new DamageSubroutineSpec(DamageKind.Net, 2)], []),
        new(GatewayBatch5CardCodes.Ping, 1, IceType.Barrier, 2, [new EndRunSubroutineSpec()], []),
        new(GatewayBatch5CardCodes.Ballista, 4, IceType.Sentry, 5,
            [new TrashInstalledRunnerCardSubroutineSpec(RuntimeCardClass.Program, endRunAlternative: true)], []),
        new(GatewayBatch5CardCodes.Pharos, 5, IceType.Barrier, 7,
            [new GiveTagsSubroutineSpec(1), new EndRunSubroutineSpec(), new EndRunSubroutineSpec()], [],
            valueModifiers: [BehaviorValueModifier.CounterThreshold("advancement", 3, 5, "pharos-advancement-strength")]),
        new(GatewayBatch5CardCodes.Tithe, 1, IceType.Sentry, 1,
            [new DamageSubroutineSpec(DamageKind.Net, 1), new GainCreditsSubroutineSpec(PlayerSide.Corp, 1)], []),
    ]);
}

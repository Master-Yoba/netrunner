using System.Collections.ObjectModel;

using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// Stable typed IDs for the seven representative System Gateway cards that
/// anchor the M2 card-behavior work.
/// </summary>
public static class GatewayCardCodes
{
    public static readonly CardDefinitionId Palisade = new("30072");
    public static readonly CardDefinitionId Whitespace = new("30074");
    public static readonly CardDefinitionId Cleaver = new("30006");
    public static readonly CardDefinitionId Buzzsaw = new("30005");
    public static readonly CardDefinitionId Overclock = new("30029");
    public static readonly CardDefinitionId OffworldOffice = new("30067");
    public static readonly CardDefinitionId NicoCampaign = new("30037");
}

/// <summary>
/// Compiled ICE behavior specs for the two representative System Gateway ICE
/// cards. Values are hand-copied from the official printed card text; no
/// text parsing happens here.
/// </summary>
public static class GatewayIceBehaviorSpecs
{
    private static readonly IceBehaviorSpec Palisade = new(
        GatewayCardCodes.Palisade,
        strength: 2,
        iceType: IceType.Barrier,
        rezCost: 3,
        subroutines: new IceSubroutineSpec[] { new EndRunSubroutineSpec() },
        strengthModifiers: new IceStrengthModifierSpec[]
        {
            new IceStrengthModifierSpec(2, IceStrengthModifierCondition.ProtectingRemoteServer),
        });

    private static readonly IceBehaviorSpec Whitespace = new(
        GatewayCardCodes.Whitespace,
        strength: 0,
        iceType: IceType.CodeGate,
        rezCost: 2,
        subroutines: new IceSubroutineSpec[]
        {
            new LoseCreditsSubroutineSpec(PlayerSide.Runner, 3),
            new EndRunIfCreditsAtMostSubroutineSpec(PlayerSide.Runner, 6),
        },
        strengthModifiers: Array.Empty<IceStrengthModifierSpec>());

    /// <summary>The exactly two compiled ICE specs, in card order.</summary>
    public static IReadOnlyList<IceBehaviorSpec> IceSpecs { get; } =
        new ReadOnlyCollection<IceBehaviorSpec>(new[] { Palisade, Whitespace });

    /// <summary>Registers <see cref="IceSpecs"/> into the shared catalog (idempotent).</summary>
    public static void RegisterAll() => CardBehaviorSpecCatalog.RegisterIce(IceSpecs);
}

using System.Collections.ObjectModel;

using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// Compiled icebreaker behavior for Mayfly (30032). The run-end cleanup is a
/// typed declaration only: Core arms it after a successful interface break,
/// and never infers the card identity from this source file at runtime.
/// </summary>
public static class GatewayBatch4IcebreakerBehaviorSpecs
{
    public const string MayflyRunEndCleanupBehaviorId = "mayfly-run-end-trash";

    private static readonly IcebreakerBehaviorSpec Mayfly = new(
        GatewayBatch4CardCodes.Mayfly,
        baseStrength: 1,
        interfaceType: IceType.Any,
        breakCost: 1,
        breakLimit: 1,
        pumpCost: 1,
        pumpStrength: 1,
        runEndCleanup: new IcebreakerRunEndCleanupSpec(MayflyRunEndCleanupBehaviorId));

    /// <summary>The single Batch 4 icebreaker, in printed-code order.</summary>
    public static IReadOnlyList<IcebreakerBehaviorSpec> IcebreakerSpecs { get; } =
        new ReadOnlyCollection<IcebreakerBehaviorSpec>([Mayfly]);
}

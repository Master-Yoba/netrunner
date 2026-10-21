using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// Stable definition IDs for the twelve cards owned by Slice 4.2 Batch 1.
/// The IDs are the only metadata these behavior registrations depend on;
/// card titles and rules text remain in the authenticated card-data pipeline.
/// </summary>
public static class GatewayBatch1CardCodes
{
    public static readonly CardDefinitionId WildcatStrike = new("30002");
    public static readonly CardDefinitionId MutualFavor = new("30011");
    public static readonly CardDefinitionId CreativeCommission = new("30020");
    public static readonly CardDefinitionId VRcation = new("30021");
    public static readonly CardDefinitionId SureGamble = new("30030");
    public static readonly CardDefinitionId T400MemoryDiamond = new("30031");
    public static readonly CardDefinitionId SeamlessLaunch = new("30040");
    public static readonly CardDefinitionId Sprint = new("30041");
    public static readonly CardDefinitionId HanseiReview = new("30048");
    public static readonly CardDefinitionId PredictivePlanogram = new("30056");
    public static readonly CardDefinitionId GovernmentSubsidy = new("30064");
    public static readonly CardDefinitionId HedgeFund = new("30075");
}

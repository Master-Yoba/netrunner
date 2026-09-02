namespace Netrunner.Core;

/// <summary>Rules constants used by the real CR turn/action shell.</summary>
public static class RealGameRules
{
    /// <summary>The normal agenda-point victory threshold.</summary>
    public const int AgendaPointsToWin = 7;

    /// <summary>The fixed click cost for removing one standard Runner tag.</summary>
    public const int RemoveTagClickCost = 1;

    /// <summary>The fixed credit cost for removing one standard Runner tag.</summary>
    public const int RemoveTagCreditCost = 2;

    /// <summary>The fixed click cost for trashing one tagged Runner resource.</summary>
    public const int TrashTaggedResourceClickCost = 1;

    /// <summary>The fixed credit cost for trashing one tagged Runner resource.</summary>
    public const int TrashTaggedResourceCreditCost = 2;

    /// <summary>The fixed click cost for purging all virus counters.</summary>
    public const int PurgeVirusCountersClickCost = 3;
}

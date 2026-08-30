using System.Collections.ObjectModel;

using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// Compiled agenda behavior specs for the representative System Gateway
/// agenda. Values are hand-copied from the official printed card text; no
/// text parsing happens here.
/// </summary>
public static class GatewayAgendaBehaviorSpecs
{
    private static readonly AgendaBehaviorSpec OffworldOffice = new(
        GatewayCardCodes.OffworldOffice,
        agendaPoints: 2);

    /// <summary>The exactly one compiled agenda spec, in card order.</summary>
    public static IReadOnlyList<AgendaBehaviorSpec> AgendaSpecs { get; } =
        new ReadOnlyCollection<AgendaBehaviorSpec>(new[] { OffworldOffice });

    /// <summary>Registers <see cref="AgendaSpecs"/> into the shared catalog (idempotent).</summary>
    public static void RegisterAll() => CardBehaviorSpecCatalog.RegisterAgenda(AgendaSpecs);
}

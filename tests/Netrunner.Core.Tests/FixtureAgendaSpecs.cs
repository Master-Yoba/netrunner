using System.Runtime.CompilerServices;

namespace Netrunner.Core.Tests;

/// <summary>
/// Fixture agenda registered into the shared catalog at test assembly load,
/// so the observation fixtures can model a stolen agenda in RunnerScore — a
/// state the engine produces when a hidden-pool breach draws a registered
/// agenda and the Runner steals it (CR 7.1.6).
/// </summary>
internal static class FixtureAgendaSpecs
{
    public static readonly CardDefinitionId ScoredAgenda = new("fixture:agenda-scored");

    [ModuleInitializer]
    internal static void Initialize()
    {
        CardBehaviorSpecCatalog.RegisterAgenda(new AgendaBehaviorSpec(ScoredAgenda, agendaPoints: 1));
    }
}

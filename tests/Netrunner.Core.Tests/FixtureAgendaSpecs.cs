namespace Netrunner.Core.Tests;

/// <summary>
/// Immutable fixture agenda declarations used by behavior-bearing Core tests.
/// </summary>
internal static class FixtureAgendaSpecs
{
    public static readonly CardDefinitionId ScoredAgenda = new("fixture:agenda-scored");

    public static AgendaBehaviorSpec AgendaSpec { get; } = new(ScoredAgenda, agendaPoints: 1);

    public static IReadOnlyList<AgendaBehaviorSpec> AgendaSpecs { get; } = [AgendaSpec];
}

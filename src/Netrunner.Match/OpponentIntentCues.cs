using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>
/// Optional opponent intent cues (U6): short lines such as "Corp is advancing
/// a card in Remote 1", derived only from the public board in the viewer's
/// view. They say what is visible, never what the opponent is thinking.
/// </summary>
public static class OpponentIntentCues
{
    /// <summary>Points that win the game.</summary>
    public const int WinningPoints = 7;

    /// <param name="serverName">The client's display name for a server, such as "Remote 1".</param>
    public static IReadOnlyList<string> From(MatchViewModel view, Func<ServerViewModel, string> serverName)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(serverName);
        if (view.Outcome.IsTerminal) return [];
        List<string> cues = [];
        PlayerViewModel opponent = view.Viewer == PlayerSide.Corp ? view.Runner : view.Corp;
        string name = opponent.Side == PlayerSide.Corp ? "Corp" : "Runner";
        if (opponent.Side == PlayerSide.Corp)
            foreach ((ServerViewModel server, int counters) in view.Servers
                .Where(server => server.Kind == ServerKind.Remote)
                .Select(server => (server, counters: server.RootKeys.Sum(key => key is not null && view.AdvancementByKey.TryGetValue(key, out int count) ? count : 0)))
                .Where(entry => entry.counters > 0)
                .OrderByDescending(entry => entry.counters)
                .Take(2))
                cues.Add($"Corp is advancing a card in {serverName(server)} · {counters} {(counters == 1 ? "counter" : "counters")}");
        int missing = WinningPoints - opponent.AgendaPoints;
        if (missing is > 0 and <= 2)
            cues.Add($"{name} is {missing} {(missing == 1 ? "point" : "points")} from winning");
        return cues;
    }
}

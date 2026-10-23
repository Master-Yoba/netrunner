using System.Text;
using System.Text.RegularExpressions;
using Netrunner.Match.Review;

namespace Netrunner.Match.Llm;

/// <summary>
/// The rules a model player needs without asking: a primer for the system prompt, the same on every request so
/// the server reuses it from its prompt cache, and the comprehensive rules for mechanics a decision shows.
/// </summary>
/// <remarks>
/// Before the primer, the opponent looked up a card or a rule on every decision, mostly cards already in its
/// prompt and basics such as how to steal an agenda, and each lookup cost another round of thinking (15–40 s on
/// a local 27B model).
/// </remarks>
public static partial class LlmRulesPrimer
{
    /// <summary>A summary of the comprehensive rules (<c>data/vendor/rules</c>) for the system prompt.</summary>
    public const string Text = """
        Rules summary (the engine enforces the rules: every option you are given is legal).
        Turns
        - The Corp has 3 clicks a turn and must draw 1 card from R&D as its turn begins. The Runner has 4 clicks and no
          mandatory draw.
        - Basic actions cost 1 click. Corp: gain 1 credit; draw 1 card; install an agenda, asset, upgrade or ice; play
          an operation; advance an installed card (and 1 credit); trash a resource while the Runner is tagged (and 2
          credits); purge virus counters (3 clicks). Runner: gain 1 credit; draw 1 card; install a program, resource
          or hardware; play an event; run a server; remove a tag (and 2 credits).
        - Each player discards down to their maximum hand size (5 unless changed) at the end of their turn.
        Winning
        - 7 agenda points wins: the Corp scores agendas, the Runner steals them. The Corp also wins when the Runner is
          flatlined (takes more damage than the cards in the grip); the Runner also wins when the Corp must draw from
          an empty R&D.
        Corp cards
        - The Corp installs cards facedown (unrezzed). Ice protects a server, the newest outermost; installing ice
          costs 1 credit for each ice already protecting that server. Agendas, assets and upgrades go in a server's
          root, and a remote server's root holds at most one agenda or asset.
        - Rezzing turns a card faceup by paying its rez cost, without a click. Ice can be rezzed when the Runner
          approaches it; assets and upgrades whenever the Corp has priority. Agendas are never rezzed.
        - An agenda can be scored on the Corp's own turn, without a click, once its advancement counters reach its
          advancement requirement. Some assets and ice can be advanced too, so an advanced facedown card is not
          always an agenda.
        Runs
        - The Runner chooses a server and approaches its ice from the outermost in. Unrezzed ice is passed. Rezzed
          ice is encountered: each subroutine resolves unless the Runner breaks it. "End the run" ends the run with
          nothing accessed. After passing a piece of ice the Runner may jack out.
        - Icebreakers break subroutines of their ice type (fracter: barrier, decoder: code gate, killer: sentry; an
          AI breaks any type) only while their strength is at least the ice's strength. Raising strength usually
          costs credits and lasts for that encounter.
        - When the Runner passes all the ice, the run is successful and the Runner breaches the server.
        Access
        - HQ: 1 random card from the Corp's hand. R&D: the top card of R&D. Archives: every card in the Corp's discard
          pile, facedown cards turned faceup first. Remote server: every card in its root. Breaching HQ, R&D or
          Archives also accesses the cards in that server's root (its upgrades).
        - An accessed agenda is stolen, unless an additional cost to steal it is not paid. Any other accessed card with
          a trash cost can be trashed by paying that cost; otherwise it stays where it is.
        Damage, tags and the rest
        - Net and meat damage trash that many random cards from the grip. Core damage does too and also lowers the
          Runner's maximum hand size by 1 for each point.
        - While the Runner is tagged, the Corp can trash the Runner's resources, and some Corp cards punish tags.
        - For each bad publicity the Corp has, the Runner gets 1 credit to spend during each run.
        - Programs use memory units (4 by default). Virus counters stay on their cards until the Corp purges.
        - Trace [N]: the Corp spends credits on top of N, then the Runner spends credits on top of their link; the
          trace succeeds if the Corp's total is higher.
        - Card text symbols: [click] is a click, [subroutine] a subroutine, [trash] trashing the card itself as a cost,
          and [interrupt] an ability that changes another effect just before it happens.
        """;

    /// <summary>
    /// The rules for mechanics the primer leaves out, as rule numbers, attached when a decision's text shows the
    /// mechanic (a card in view or an option).
    /// </summary>
    private static readonly (Regex Shows, string[] Rules)[] Mechanics =
    [
        (TraceText(), ["10.8.2", "10.8.3", "10.8.4"]),
        (ExposeText(), ["1.21.4"]),
        (DerezText(), ["8.1.3"]),
        (LoadText(), ["10.9.1"]),
        (ChargeText(), ["10.10.1"]),
        (MarkText(), ["10.11.1", "10.11.2"]),
        (SabotageText(), ["10.12.1", "10.12.2"]),
        (RecurringText(), ["1.3.4"]),
        (ThreatText(), ["1.17.1"]),
        (ForfeitText(), ["8.2.5"]),
        (PersistentText(), ["9.12.5a", "9.12.5b"]),
        (InterruptText(), ["9.9.1", "9.9.1a"]),
    ];

    /// <summary>The comprehensive rules for the mechanics <paramref name="request"/> shows, or "" for none.</summary>
    public static string RulesFor(string request, RulesIndex? rules)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (rules is null) return "";
        var text = new StringBuilder();
        foreach ((Regex shows, string[] numbers) in Mechanics)
        {
            if (!shows.IsMatch(request)) continue;
            foreach (string number in numbers)
                if (rules.Rule(number) is { } rule) text.AppendLine(rule.Text.StartsWith(rule.Number, StringComparison.Ordinal) ? rule.Text : $"{rule.Number} {rule.Text}");
        }
        return text.Length == 0 ? "" : "\nRules for mechanics in view:\n" + text + "\n";
    }

    [GeneratedRegex(@"\btrace\b", RegexOptions.IgnoreCase)]
    private static partial Regex TraceText();

    [GeneratedRegex(@"\bexpos(e|es|ed)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ExposeText();

    [GeneratedRegex(@"\bderez", RegexOptions.IgnoreCase)]
    private static partial Regex DerezText();

    [GeneratedRegex(@"\bload(ed)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex LoadText();

    [GeneratedRegex(@"\bcharge\b", RegexOptions.IgnoreCase)]
    private static partial Regex ChargeText();

    [GeneratedRegex(@"\byour mark\b", RegexOptions.IgnoreCase)]
    private static partial Regex MarkText();

    [GeneratedRegex(@"\bsabotage\b", RegexOptions.IgnoreCase)]
    private static partial Regex SabotageText();

    [GeneratedRegex(@"\brecurring\b|\[recurring\]", RegexOptions.IgnoreCase)]
    private static partial Regex RecurringText();

    [GeneratedRegex(@"\bthreat\b", RegexOptions.IgnoreCase)]
    private static partial Regex ThreatText();

    [GeneratedRegex(@"\bforfeit", RegexOptions.IgnoreCase)]
    private static partial Regex ForfeitText();

    [GeneratedRegex(@"\bpersistent\b", RegexOptions.IgnoreCase)]
    private static partial Regex PersistentText();

    [GeneratedRegex(@"\[interrupt\]", RegexOptions.IgnoreCase)]
    private static partial Regex InterruptText();
}

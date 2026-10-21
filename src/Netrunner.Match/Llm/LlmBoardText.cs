using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Netrunner.Core;

namespace Netrunner.Match.Llm;

/// <summary>
/// The board as one side sees it, as text for the LLM opponent (Wave 4.5 P3). It is built from the
/// side's own <see cref="MatchViewModel"/>, which comes from its observation alone, so it holds nothing
/// that side couldn't see: face-down cards stay unnamed, the other hand is only counted.
/// </summary>
public static partial class LlmBoardText
{
    public static string Board(MatchViewModel view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var text = new StringBuilder();
        PlayerSide me = view.Viewer;
        text.AppendLine(CultureInfo.InvariantCulture, $"You are the {me}. Turn {view.Turn.TurnNumber?.ToString(CultureInfo.InvariantCulture) ?? "?"}, {view.Turn.ActiveSide?.ToString() ?? "?"} to act.");
        text.AppendLine(CultureInfo.InvariantCulture,
            $"Corp: {view.Corp.Credits} credits, {view.Corp.Clicks} clicks, {view.Corp.AgendaPoints} agenda points; HQ {Hand(view.CorpHand)}; R&D {view.CorpDeck.Count} cards; Archives {Cards(view.CorpDiscard)}{(view.CorpDiscardCount > view.CorpDiscard.Count ? $" and {view.CorpDiscardCount - view.CorpDiscard.Count} face down" : "")}.");
        text.AppendLine(CultureInfo.InvariantCulture,
            $"Runner: {view.Runner.Credits} credits, {view.Runner.Clicks} clicks, {view.Runner.AgendaPoints} agenda points, {view.Runner.Tags ?? 0} tags; grip {Hand(view.RunnerHand)}; stack {view.RunnerDeck.Count} cards; heap {Cards(view.RunnerHeap)}; rig {Cards(view.RunnerRig)}.");
        text.AppendLine("Servers (ICE outermost first):");
        int remote = 0;
        foreach (ServerViewModel server in view.Servers)
        {
            string name = server.Kind == ServerKind.Remote ? $"Remote {++remote}" : Name(server.Kind);
            if (server.Kind == ServerKind.Remote && server.Ice.Count == 0 && server.Root.Count == 0) continue;
            text.AppendLine(CultureInfo.InvariantCulture,
                $"  {name}: ICE [{string.Join(", ", server.Ice.Select(card => Installed(card, ice: true)))}] root [{string.Join(", ", server.Root.Select(card => Installed(card, ice: false)))}]");
        }
        text.AppendLine(CultureInfo.InvariantCulture, $"Score areas: Corp {Cards(view.CorpScore)}; Runner {Cards(view.RunnerScore)}.");
        return text.ToString();
    }

    /// <summary>What each card in view does, once per title: the side can read every card it sees.</summary>
    public static string Glossary(MatchViewModel view)
    {
        ArgumentNullException.ThrowIfNull(view);
        IEnumerable<CardViewModel> seen = view.Servers.SelectMany(server => server.Ice.Concat(server.Root)).OfType<CardViewModel>()
            .Concat(view.CorpHand.VisibleCards).Concat(view.RunnerHand.VisibleCards).Concat(view.RunnerRig)
            .Concat(view.CorpScore).Concat(view.RunnerScore).Concat(view.CorpDiscard).Concat(view.RunnerHeap);
        var text = new StringBuilder();
        foreach (CardViewModel card in seen.Where(card => card.IsKnown).DistinctBy(card => card.Title, StringComparer.Ordinal))
        {
            var facts = new List<string> { card.Type };
            if (card.Subtypes.Count > 0) facts.Add(string.Join(" ", card.Subtypes));
            if (card.Cost is { } cost) facts.Add($"cost {cost}");
            if (card.Strength is { } strength) facts.Add($"strength {strength}");
            if (card.AgendaPoints is { } points) facts.Add($"{points} points, advancement {card.AdvancementRequirement}");
            if (card.TrashCost is { } trash) facts.Add($"trash {trash}");
            if (card.MemoryCost is { } memory) facts.Add($"memory {memory}");
            text.AppendLine(CultureInfo.InvariantCulture, $"- {card.Title} ({string.Join(", ", facts)}): {Plain(card.RulesText)}");
        }
        return text.ToString();
    }

    /// <summary>Card text without markup: symbols as words, tags dropped.</summary>
    public static string Plain(string rulesText) =>
        Whitespace().Replace(Tag().Replace(Symbol().Replace(rulesText ?? "", match => match.Groups[1].Value switch
        {
            "credit" => " credits",
            "click" => "[click]",
            "subroutine" => "[subroutine]",
            "recurring-credit" => " recurring credits",
            "trash" => "[trash]",
            var other => $"[{other}]",
        }), " "), " ").Trim();

    private static string Installed(CardViewModel? card, bool ice)
    {
        if (card is null || !card.IsKnown) return Advanced(ice ? "unrezzed ICE" : "face-down card", card);
        return Advanced($"{card.Title}{(card.Rezzed ? "" : ice ? " (unrezzed)" : " (face down)")}", card);
    }

    private static string Advanced(string name, CardViewModel? card) =>
        card is { AdvancementCounters: > 0 } ? $"{name} +{card.AdvancementCounters} advancement" : name;

    private static string Hand(ZoneViewModel zone) =>
        zone.VisibleCards.Count > 0 ? $"[{string.Join(", ", zone.VisibleCards.Select(card => card.Title))}]" : $"{zone.Count} cards";

    private static string Cards(IReadOnlyList<CardViewModel> cards) =>
        cards.Count > 0 ? $"[{string.Join(", ", cards.Select(card => card.IsKnown ? card.Title : "face-down card"))}]" : "empty";

    private static string Name(ServerKind kind) => kind switch
    {
        ServerKind.Headquarters => "HQ",
        ServerKind.RAndD => "R&D",
        ServerKind.Archives => "Archives",
        _ => kind.ToString(),
    };

    [GeneratedRegex(@"<symbol:([a-z-]+)>")]
    private static partial Regex Symbol();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

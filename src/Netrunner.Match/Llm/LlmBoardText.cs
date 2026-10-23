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
            $"Identities: Corp {Identity(view.CorpIdentity, view.Corp.IdentitySide ?? view.SecretIdentitySide)}; Runner {Identity(view.RunnerIdentity, null)}.");
        text.AppendLine(CultureInfo.InvariantCulture,
            $"Corp: {view.Corp.Credits} credits, {view.Corp.Clicks} clicks, {view.Corp.AgendaPoints} agenda points{Extras(view.Corp, view.CorpHandSize)}; HQ {Hand(view.CorpHand)}; R&D {view.CorpDeck.Count} cards; Archives {Cards(view.CorpDiscard)}{(view.CorpDiscardCount > view.CorpDiscard.Count ? $" and {view.CorpDiscardCount - view.CorpDiscard.Count} face down" : "")}.");
        text.AppendLine(CultureInfo.InvariantCulture,
            $"Runner: {view.Runner.Credits} credits, {view.Runner.Clicks} clicks, {view.Runner.AgendaPoints} agenda points, {view.Runner.Tags ?? 0} tags{Extras(view.Runner, view.RunnerHandSize)}; grip {Hand(view.RunnerHand)}; stack {view.RunnerDeck.Count} cards; heap {Cards(view.RunnerHeap)}; rig {Rig(view)}.");
        text.AppendLine("Servers (ICE outermost first):");
        foreach ((ServerViewModel server, string name) in Named(view))
        {
            if (server.Kind == ServerKind.Remote && server.Ice.Count == 0 && server.Root.Count == 0) continue;
            string?[] ice = IceIds(server);
            text.AppendLine(CultureInfo.InvariantCulture,
                $"  {name}: ICE [{string.Join(", ", server.Ice.Select((card, index) => Installed(view, card, ice[index], ice: true)))}] root [{string.Join(", ", server.Root.Select(card => Installed(view, card, card?.Id, ice: false)))}]");
        }
        text.AppendLine(CultureInfo.InvariantCulture, $"Score areas: Corp {Scored(view.CorpScore)}; Runner {Scored(view.RunnerScore)}.");
        return text.ToString();
    }

    /// <summary>What each card in view does, once per title: the side can read every card it sees.</summary>
    public static string Glossary(MatchViewModel view)
    {
        ArgumentNullException.ThrowIfNull(view);
        IEnumerable<CardViewModel> seen = new[] { view.CorpIdentity, view.RunnerIdentity }.OfType<CardViewModel>()
            .Concat(view.Servers.SelectMany(server => server.Ice.Concat(server.Root)).OfType<CardViewModel>())
            .Concat(view.CorpHand.VisibleCards).Concat(view.RunnerHand.VisibleCards).Concat(view.RunnerRig).Concat(view.HostedFaceUp)
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

    /// <summary>
    /// The counters on a card other than advancement, such as "3 credits, 2 virus counters", or "". The prompt showed none,
    /// so the model guessed them from card text.
    /// </summary>
    public static string Counters(CardViewModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return string.Join(", ", card.TypedCounters.Where(counter => counter.Count > 0).Select(counter => counter.Purpose switch
        {
            CardCounterPurpose.HostedCredit => $"{counter.Count} {(counter.Count == 1 ? "credit" : "credits")}",
            CardCounterPurpose.Virus => $"{counter.Count} virus {(counter.Count == 1 ? "counter" : "counters")}",
            // A flip identity's marker, not a counter a card spends.
            CardCounterPurpose.Generic when counter.CounterName.Equals("flipped", StringComparison.OrdinalIgnoreCase) => "flipped",
            _ => $"{counter.Count} {counter.CounterName.ToLowerInvariant()} {(counter.Count == 1 ? "counter" : "counters")}",
        }));
    }

    /// <summary>
    /// Where each installed Corp card the viewer can name an ID for is: "Remote 1", or for ICE "HQ (ICE 1 of 2, outermost
    /// first)", unrezzed ICE included. Options name cards by it, so two copies of a card, or two face-down cards, don't
    /// read alike (Qwen advanced the unadvanced copy of an agenda as the one it had scored).
    /// </summary>
    public static Dictionary<string, string> Locations(MatchViewModel view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var locations = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((ServerViewModel server, string name) in Named(view))
        {
            foreach (CardViewModel card in server.Root.OfType<CardViewModel>()) locations.TryAdd(card.Id, name);
            string?[] ice = IceIds(server);
            for (int position = 0; position < ice.Length; position++)
            {
                if (ice[position] is { } id)
                    locations.TryAdd(id, ice.Length == 1 ? name : $"{name} (ICE {position + 1} of {ice.Length}, outermost first)");
            }
        }
        return locations;
    }

    /// <summary>The servers with the names the prompt uses: HQ, R&amp;D, Archives and Remote 1, 2... in order.</summary>
    private static IEnumerable<(ServerViewModel Server, string Name)> Named(MatchViewModel view)
    {
        int remote = 0;
        foreach (ServerViewModel server in view.Servers)
            yield return (server, server.Kind == ServerKind.Remote ? $"Remote {++remote}" : Name(server.Kind));
    }

    /// <summary>A server's ICE IDs, outermost first: a visible card's own, or an unrezzed one's from its position.</summary>
    private static string?[] IceIds(ServerViewModel server) =>
        [.. server.Ice.Select((card, position) => card?.Id
            // Core counts positions from the innermost ICE.
            ?? server.IcePositions.FirstOrDefault(ice => ice.Index == server.Ice.Count - 1 - position)?.CardId)];

    private static string Installed(MatchViewModel view, CardViewModel? card, string? id, bool ice)
    {
        if (card is null || !card.IsKnown) return Advanced(ice ? "unrezzed ICE" : "face-down card", card) + Hosting(view, id);
        string state = card.Rezzed ? "" : ice ? "unrezzed" : card.IsFaceUpInactive ? "faceup, inactive" : "face down";
        string details = string.Join(", ", new[] { state, Counters(card) }.Where(part => part.Length > 0));
        return Advanced($"{card.Title}{(details.Length > 0 ? $" ({details})" : "")}", card) + Hosting(view, id);
    }

    /// <summary>The cards a card hosts, such as a Trojan on a piece of ICE: " hosting [Botulus (1 virus counter)]".</summary>
    private static string Hosting(MatchViewModel view, string? host)
    {
        if (host is null) return "";
        string[] hosted = [.. view.RunnerRig.Concat(view.HostedFaceUp).Concat(view.HostedFaceDown).Where(card => card.HostCardId == host)
            .DistinctBy(card => card.Id).Select(card => card.IsKnown ? Described(card) : "face-down card")];
        return hosted.Length == 0 ? "" : $" hosting [{string.Join(", ", hosted)}]";
    }

    /// <summary>The Runner's installed cards not hosted on a Corp card (those show under their host).</summary>
    private static string Rig(MatchViewModel view)
    {
        Dictionary<string, string> corpCards = Locations(view);
        CardViewModel[] rig = [.. view.RunnerRig.Where(card => card.HostCardId is not { } host || !corpCards.ContainsKey(host))];
        return rig.Length > 0 ? $"[{string.Join(", ", rig.Select(card => Described(card) + Hosting(view, card.Id)))}]" : "empty";
    }

    private static string Described(CardViewModel card) =>
        Counters(card) is { Length: > 0 } counters ? $"{card.Title} ({counters})" : card.Title;

    private static string Identity(CardViewModel? card, string? side)
    {
        if (card is null) return "unknown";
        string details = string.Join(", ", new[] { side is null ? "" : $"showing {side}", Counters(card) }.Where(part => part.Length > 0));
        return details.Length > 0 ? $"{card.Title} ({details})" : card.Title;
    }

    /// <summary>The player facts beyond the usual: bad publicity, core damage, memory, and a hand size other than 5.</summary>
    private static string Extras(PlayerViewModel player, HandSizeViewModel hand)
    {
        var extras = new List<string>();
        if (player.BadPublicity is > 0) extras.Add($"{player.BadPublicity} bad publicity");
        if (player.CoreDamage is > 0) extras.Add($"{player.CoreDamage} core damage");
        if (player.UsedMemory is { } used && player.MemoryLimit is { } limit) extras.Add($"memory {used} of {limit} used");
        if (hand.MaximumHandSize is { } maximum and not 5) extras.Add($"maximum hand size {maximum}");
        return string.Concat(extras.Select(extra => ", " + extra));
    }

    /// <summary>
    /// A card's advancement counters; for an agenda the viewer can read, against its requirement even at none, so two
    /// copies of an agenda don't read alike (Qwen advanced the unadvanced copy as the one it had scored).
    /// </summary>
    private static string Advanced(string name, CardViewModel? card) => card switch
    {
        { IsKnown: true, AdvancementRequirement: { } requirement } => $"{name}, {card.AdvancementCounters} of {requirement} advancements",
        { AdvancementCounters: > 0 } => $"{name} +{card.AdvancementCounters} advancement",
        _ => name,
    };

    private static string Scored(IReadOnlyList<CardViewModel> cards) =>
        cards.Count > 0
            ? $"[{string.Join(", ", cards.Select(card => string.Join(", ", new[] { card.AgendaPoints is { } points ? $"{points} points" : "", Counters(card) }
                .Where(part => part.Length > 0)) is { Length: > 0 } details ? $"{card.Title} ({details})" : card.Title))}]"
            : "empty";

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

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Netrunner.Match.Review;

/// <summary>One comprehensive rule: its number, section and full text, sub-points included.</summary>
public sealed record RuleText(string Number, string Section, string Text);

/// <summary>
/// The vendored comprehensive rules (<c>data/vendor/rules</c>) for the LLM reviewer's
/// <c>rules_lookup</c> tool: a rule by its number, or the rules that best match a query.
/// </summary>
public sealed partial class RulesIndex
{
    private readonly Dictionary<string, RuleText> _rules;

    private RulesIndex(Dictionary<string, RuleText> rules) => _rules = rules;

    public int Count => _rules.Count;

    /// <summary>Reads <c>index.json</c> and the chapters it points at.</summary>
    public static RulesIndex Load(string rulesRoot)
    {
        JsonObject index = JsonNode.Parse(File.ReadAllText(Path.Combine(rulesRoot, "index.json")))?.AsObject()
            ?? throw new InvalidDataException("The rules index is empty.");
        var lines = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var rules = new Dictionary<string, RuleText>(StringComparer.Ordinal);
        foreach ((string number, JsonNode? entry) in index)
        {
            if (entry is null || (string?)entry["kind"] != "rule") continue;
            string file = (string)entry["file"]!;
            if (!lines.TryGetValue(file, out string[]? text)) lines[file] = text = File.ReadAllLines(Path.Combine(rulesRoot, file));
            int start = ((int?)entry["line"] ?? 1) - 1;
            if (start < 0 || start >= text.Length) continue;
            var paragraph = new List<string> { text[start] };
            for (int line = start + 1; line < text.Length; line++)
            {
                if (RuleStart().IsMatch(text[line]) || text[line].StartsWith('#')) break;
                if (!string.IsNullOrWhiteSpace(text[line])) paragraph.Add(text[line].Trim());
            }
            rules[number] = new RuleText(number, (string?)entry["section"] ?? "", string.Join("\n", paragraph).Replace("**", "", StringComparison.Ordinal));
        }
        return new RulesIndex(rules);
    }

    /// <summary>The rule with that number (such as "7.5.2"), if there is one.</summary>
    public RuleText? Rule(string number) => _rules.GetValueOrDefault(number.Trim().TrimEnd('.'));

    /// <summary>Rules whose number is the query, or which contain most of its words.</summary>
    public IReadOnlyList<RuleText> Search(string query, int limit = 5)
    {
        if (Rule(query) is { } exact) return [exact];
        string[] words = [.. Words().Matches(query.ToLowerInvariant()).Select(match => match.Value).Where(word => word.Length > 2).Distinct()];
        if (words.Length == 0) return [];
        return [.. _rules.Values
            .Select(rule => (rule, score: words.Count(word => rule.Text.Contains(word, StringComparison.OrdinalIgnoreCase))))
            .Where(pair => pair.score > 0)
            .OrderByDescending(pair => pair.score).ThenBy(pair => pair.rule.Text.Length)
            .Take(limit).Select(pair => pair.rule)];
    }

    [GeneratedRegex(@"^\*\*\d+(\.\d+)+\.\*\*")]
    private static partial Regex RuleStart();

    [GeneratedRegex(@"[a-z0-9']+")]
    private static partial Regex Words();
}

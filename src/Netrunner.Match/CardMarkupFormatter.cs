using System.Text;
using Netrunner.Cards;

namespace Netrunner.Match;

/// <summary>Raised when normalized card markup contains an unsupported tag.</summary>
public sealed class CardMarkupValidationException : FormatException
{
    public CardMarkupValidationException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Converts normalized card markup to safe, text-only presentation text. The
/// parser is intentionally allow-listed: tags with attributes, unknown tags,
/// and unknown symbols fail closed instead of being rendered as rich content.
/// </summary>
public static class CardMarkupFormatter
{
    private static readonly Dictionary<string, string> SymbolLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["advancement-counter"] = "[advancement counter]",
            ["agenda-point"] = "[agenda point]",
            ["bad-pub"] = "[bad publicity]",
            ["brain-damage"] = "[brain damage]",
            ["break"] = "[break]",
            ["card"] = "[card]",
            ["click"] = "[click]",
            ["click-cost"] = "[click cost]",
            ["credit"] = "[credit]",
            ["credit-cost"] = "[credit cost]",
            ["influence"] = "[influence]",
            ["interrupt"] = "[interrupt]",
            ["link"] = "[link]",
            ["meat-damage"] = "[meat damage]",
            ["memory"] = "[memory]",
            ["mu"] = "[MU]",
            ["net-damage"] = "[net damage]",
            ["power-counter"] = "[power counter]",
            ["recurring-credit"] = "[recurring credit]",
            ["rez"] = "[rez]",
            ["strength"] = "[strength]",
            ["subroutine"] = "[subroutine]",
            ["tag"] = "[tag]",
            ["trace"] = "[trace]",
            ["trash"] = "[trash]",
            ["trash-cost"] = "[trash cost]",
            ["virus-counter"] = "[virus counter]",
        };

    private static readonly HashSet<string> FormattingTags = new(
        new[] { "strong", "em", "ul", "li", "br" },
        StringComparer.Ordinal);

    /// <summary>
    /// Formats a normalized text string using explicit non-colour cues for
    /// symbols, emphasis, and list items. Text outside tags is copied exactly;
    /// it is never interpreted as Godot BBCode or HTML.
    /// </summary>
    public static string Format(string normalizedMarkup)
    {
        ArgumentNullException.ThrowIfNull(normalizedMarkup);
        var result = new StringBuilder(normalizedMarkup.Length);
        var openTags = new Stack<string>();
        int index = 0;
        while (index < normalizedMarkup.Length)
        {
            if (normalizedMarkup[index] != '<')
            {
                result.Append(normalizedMarkup[index++]);
                continue;
            }

            int close = normalizedMarkup.IndexOf('>', index + 1);
            if (close < 0)
            {
                throw new CardMarkupValidationException(
                    $"Card text contains an unterminated markup token at offset {index}.");
            }

            string token = normalizedMarkup[(index + 1)..close];
            if (token.StartsWith("symbol:", StringComparison.Ordinal))
            {
                string symbol = token["symbol:".Length..];
                if (!CardTextNormalizer.SupportedSymbols.Contains(symbol)
                    || !SymbolLabels.TryGetValue(symbol, out string? label))
                {
                    throw new CardMarkupValidationException(
                        $"Card text contains unsupported symbol '<symbol:{symbol}>'.");
                }

                result.Append(label);
            }
            else
            {
                bool closing = token.StartsWith('/');
                string tag = closing ? token[1..] : token;
                if (!FormattingTags.Contains(tag))
                {
                    throw new CardMarkupValidationException(
                        $"Card text contains unsupported markup '<{token}>'.");
                }

                if (closing)
                {
                    if (tag is "br")
                    {
                        throw new CardMarkupValidationException(
                            "Card text uses an invalid closing '<br>' tag.");
                    }

                    if (openTags.Count == 0
                        || !string.Equals(openTags.Pop(), tag, StringComparison.Ordinal))
                    {
                        throw new CardMarkupValidationException(
                            $"Card text contains a mismatched closing tag '</{tag}>'.");
                    }

                    if (tag is "li" or "ul"
                        && (result.Length == 0 || result[^1] != '\n'))
                    {
                        result.Append('\n');
                    }
                }
                else if (string.Equals(tag, "br", StringComparison.Ordinal))
                {
                    result.Append('\n');
                }
                else
                {
                    openTags.Push(tag);
                    if (string.Equals(tag, "ul", StringComparison.Ordinal)
                        && result.Length > 0
                        && result[^1] != '\n')
                    {
                        result.Append('\n');
                    }

                    if (string.Equals(tag, "li", StringComparison.Ordinal))
                    {
                        result.Append("• ");
                    }
                }
            }

            index = close + 1;
        }

        if (openTags.Count > 0)
        {
            throw new CardMarkupValidationException(
                $"Card text contains an unclosed '<{openTags.Peek()}>' tag.");
        }

        return result.ToString();
    }

    /// <summary>Alias that makes the plain-text output contract explicit.</summary>
    public static string ToPlainText(string normalizedMarkup) => Format(normalizedMarkup);
}

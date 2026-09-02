using System.Security.Cryptography;
using System.Text;

using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// Cards-owned composition root for the immutable printed runtime metadata
/// used by real-turn actions. Runtime classification is compiled from
/// normalized metadata only; executable behavior registration is a separate
/// catalog and is not consulted here.
/// </summary>
public static class GatewayRuntimeCardCatalog
{
    private const string VersionPrefix = "gateway-runtime-v1-";
    private const string RunnerConsoleKey = "runner-console";

    /// <summary>Builds a runtime catalog from one authenticated data snapshot.</summary>
    public static RuntimeCardCatalog Create(CardDataSet data)
    {
        ArgumentNullException.ThrowIfNull(data);

        // A CardDataSet is already detached from its source bytes, but the
        // composition boundary revalidates it before any runtime metadata is
        // exposed. A pack labelled as System Gateway must retain the pinned
        // official identity; synthetic/fixture packs remain opt-in fixtures.
        bool requireOfficialGateway = string.Equals(
            data.Manifest.PackCode,
            CardDataValidator.OfficialPackCode,
            StringComparison.Ordinal);
        CardDataValidator.Validate(data.Cards, data.Manifest, requireOfficialGateway);

        string version = VersionPrefix + data.Manifest.CommitSha;
        var builder = new RuntimeCardCatalogBuilder(version);
        foreach (NormalizedCardDefinition card in data.Cards)
        {
            builder.Add(Compile(card));
        }

        return builder.Build();
    }

    /// <summary>
    /// Composes the generated offline payload. Loading this snapshot performs
    /// no network access and does not consult the executable behavior catalog.
    /// </summary>
    public static RuntimeCardCatalog CreateEmbedded() =>
        Create(CardDataResource.LoadEmbedded());

    private static CardRuntimeSpec Compile(NormalizedCardDefinition card)
    {
        try
        {
            RuntimeCardClass cardClass = ParseCardClass(card);
            PlayerSide side = ParseSide(card);
            IReadOnlyList<string> subtypes = NormalizeSubtypes(card);

            ValidateFields(card, cardClass);

            bool console = cardClass == RuntimeCardClass.Hardware
                && subtypes.Any(subtype => string.Equals(
                    subtype,
                    "console",
                    StringComparison.Ordinal));

            string? uniquenessKey = card.Uniqueness
                ? UniqueTitleKey(card.Title)
                : null;
            string? consoleKey = console ? RunnerConsoleKey : null;

            return new CardRuntimeSpec(
                new CardDefinitionId(card.Code),
                side,
                cardClass,
                playCost: cardClass is RuntimeCardClass.Operation or RuntimeCardClass.Event
                    ? card.Cost
                    : null,
                installCost: cardClass is RuntimeCardClass.Program
                    or RuntimeCardClass.Hardware
                    or RuntimeCardClass.Resource
                    ? card.Cost
                    : null,
                rezCost: cardClass is RuntimeCardClass.Asset
                    or RuntimeCardClass.Upgrade
                    or RuntimeCardClass.Ice
                    ? card.Cost
                    : null,
                memoryUnits: cardClass == RuntimeCardClass.Program
                    ? card.MemoryCost!.Value
                    : 0,
                trashCost: cardClass is RuntimeCardClass.Asset or RuntimeCardClass.Upgrade
                    ? card.TrashCost
                    : null,
                agendaPoints: cardClass == RuntimeCardClass.Agenda
                    ? card.AgendaPoints!.Value
                    : 0,
                advancementRequirement: cardClass == RuntimeCardClass.Agenda
                    ? card.AdvancementRequirement!.Value
                    : 0,
                subtypes: subtypes,
                unique: card.Uniqueness,
                console: console,
                uniquenessKey: uniquenessKey,
                consoleKey: consoleKey);
        }
        catch (CardDataValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw InvalidCard(card, exception.Message, exception);
        }
    }

    private static RuntimeCardClass ParseCardClass(NormalizedCardDefinition card) =>
        card.Type switch
        {
            "identity" => RuntimeCardClass.Identity,
            "agenda" => RuntimeCardClass.Agenda,
            "asset" => RuntimeCardClass.Asset,
            "upgrade" => RuntimeCardClass.Upgrade,
            "ice" => RuntimeCardClass.Ice,
            "operation" => RuntimeCardClass.Operation,
            "event" => RuntimeCardClass.Event,
            "program" => RuntimeCardClass.Program,
            "hardware" => RuntimeCardClass.Hardware,
            "resource" => RuntimeCardClass.Resource,
            _ => throw InvalidCard(card, $"type '{card.Type}' is not a supported runtime class.", null),
        };

    private static PlayerSide ParseSide(NormalizedCardDefinition card) =>
        card.Side switch
        {
            "corp" => PlayerSide.Corp,
            "runner" => PlayerSide.Runner,
            _ => throw InvalidCard(card, $"side '{card.Side}' is not supported.", null),
        };

    private static List<string> NormalizeSubtypes(NormalizedCardDefinition card)
    {
        var normalized = new List<string>(card.Subtypes.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string subtype in card.Subtypes)
        {
            string key = NormalizeSubtype(card, subtype);
            if (!seen.Add(key))
            {
                throw InvalidCard(card, $"subtypes collide after normalization at '{key}'.", null);
            }

            normalized.Add(key);
        }

        return normalized;
    }

    private static string NormalizeSubtype(
        NormalizedCardDefinition card,
        string subtype)
    {
        if (string.IsNullOrEmpty(subtype))
        {
            throw InvalidCard(card, "subtype must not be empty.", null);
        }

        var builder = new StringBuilder(subtype.Length);
        bool previousWasSpace = false;
        foreach (char character in subtype)
        {
            if (character == ' ')
            {
                if (!previousWasSpace)
                {
                    builder.Append('-');
                }

                previousWasSpace = true;
                continue;
            }

            previousWasSpace = false;
            if (character is >= 'A' and <= 'Z')
            {
                builder.Append((char)(character + ('a' - 'A')));
            }
            else if (character is >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '-')
            {
                builder.Append(character);
            }
            else
            {
                throw InvalidCard(
                    card,
                    $"subtype '{subtype}' contains unsupported character '{character}'.",
                    null);
            }
        }

        if (builder.Length == 0)
        {
            throw InvalidCard(card, $"subtype '{subtype}' normalizes to an empty key.", null);
        }

        return builder.ToString();
    }

    private static void ValidateFields(
        NormalizedCardDefinition card,
        RuntimeCardClass cardClass)
    {
        bool requiresCost = cardClass is RuntimeCardClass.Asset
            or RuntimeCardClass.Upgrade
            or RuntimeCardClass.Ice
            or RuntimeCardClass.Operation
            or RuntimeCardClass.Event
            or RuntimeCardClass.Program
            or RuntimeCardClass.Hardware
            or RuntimeCardClass.Resource;
        if (requiresCost && card.Cost is null)
        {
            throw InvalidCard(card, "requires a printed cost.", null);
        }

        if (!requiresCost && card.Cost is not null)
        {
            throw InvalidCard(card, "must not carry a printed cost.", null);
        }

        if (cardClass == RuntimeCardClass.Agenda)
        {
            if (card.AgendaPoints is null)
            {
                throw InvalidCard(card, "requires agendaPoints.", null);
            }

            if (card.AdvancementRequirement is null)
            {
                throw InvalidCard(card, "requires advancementRequirement.", null);
            }

            if (card.AgendaPoints <= 0)
            {
                throw InvalidCard(card, "agendaPoints must be positive.", null);
            }

            if (card.AdvancementRequirement <= 0)
            {
                throw InvalidCard(card, "advancementRequirement must be positive.", null);
            }
        }
        else if (card.AgendaPoints is not null || card.AdvancementRequirement is not null)
        {
            throw InvalidCard(card, "must not carry agenda-only numeric fields.", null);
        }

        if (cardClass == RuntimeCardClass.Program)
        {
            if (card.MemoryCost is null)
            {
                throw InvalidCard(card, "requires memoryCost.", null);
            }

            if (card.MemoryCost < 0)
            {
                throw InvalidCard(card, "memoryCost must not be negative.", null);
            }
        }
        else if (card.MemoryCost is not null)
        {
            throw InvalidCard(card, "must not carry memoryCost.", null);
        }

        bool requiresTrashCost = cardClass is RuntimeCardClass.Asset or RuntimeCardClass.Upgrade;
        if (requiresTrashCost && card.TrashCost is null)
        {
            throw InvalidCard(card, "requires trashCost.", null);
        }

        if (!requiresTrashCost && card.TrashCost is not null)
        {
            throw InvalidCard(card, "must not carry trashCost.", null);
        }
    }

    private static string UniqueTitleKey(string title)
    {
        byte[] titleBytes = Encoding.UTF8.GetBytes(title);
        return "unique-title-sha256:" + Convert.ToHexString(SHA256.HashData(titleBytes)).ToLowerInvariant();
    }

    private static CardDataValidationException InvalidCard(
        NormalizedCardDefinition card,
        string reason,
        Exception? innerException)
    {
        string message = $"Card '{card.Code}' runtime metadata is invalid: {reason}";
        return innerException is null
            ? new CardDataValidationException(message)
            : new CardDataValidationException(message, innerException);
    }
}

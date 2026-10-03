using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// card-fingerprints. For change scoping (scripts/affected-pools.sh): every registered card's behavior fingerprint in
    /// the widest embedded pool, and the card codes each fuzz and ladder content's decks hold. Exit 0 on success,
    /// 2 without official content.
    /// </summary>
    private static int RunCardFingerprints(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length > 1)
        {
            error.WriteLine("card-fingerprints failed: it takes no options.");
            return 2;
        }

        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
        {
            error.WriteLine("card-fingerprints failed: it needs previously provisioned official content embedded at build time.");
            return 2;
        }

        CardPool pool = CardPools.GatewayElevation.IsEmbedded ? CardPools.GatewayElevation : CardPools.Gateway;
        BehaviorCatalog behavior = pool.CreateBehavior();
        SortedDictionary<string, string> cards = new(StringComparer.Ordinal);
        foreach (CardDefinitionId id in behavior.AllDefinitionIds)
            cards[id.Value] = behavior.GetCardBehaviorFingerprint(id);
        SortedDictionary<string, string[]> pools = new(StringComparer.Ordinal);
        foreach (string content in RulesFuzzRunner.ContentNames.Where(name => name != "developer"))
        {
            if (content != "official" && content != "gateway" && !CardPools.GatewayElevation.IsEmbedded) continue;
            var codes = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string contentId in RulesFuzzRunner.ContentIds(content))
            {
                FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(contentId, 1);
                foreach (DeckRequest deck in new[] { definition.Request.CorpDeck, definition.Request.RunnerDeck })
                {
                    codes.Add(deck.IdentityCode);
                    foreach (DeckCardEntry entry in deck.Cards) codes.Add(entry.Code);
                }
            }
            pools[content] = [.. codes];
        }

        output.WriteLine(EngineJson.Serialize(new { schemaVersion = 1, behaviorVersion = behavior.Version, cards, pools }));
        return 0;
    }
}

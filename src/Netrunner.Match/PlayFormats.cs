using System.Collections.Concurrent;
using Netrunner.Cards;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>
/// D-EL-8: everything a player's game in one format is built from: the format's
/// card data, behavior and runtime catalogs, card metadata and decks. The
/// Gateway format keeps the embedded Gateway payloads and presets, so its games,
/// saves and frozen pins are unchanged.
/// </summary>
public sealed class PlayFormatContent
{
    internal PlayFormatContent(CardFormat format, CardDataSet data, BehaviorCatalog behavior,
        OfficialPresetCatalog presets, IReadOnlyList<OfficialPreset> decks)
    {
        Format = format;
        Data = data;
        Behavior = behavior;
        Runtime = CardPool.CreateRuntime(data);
        Metadata = new CardCatalogMetadataResolver(data);
        Presets = presets;
        Decks = decks;
    }

    public CardFormat Format { get; }

    public string Id => Format.Id;

    public CardDataSet Data { get; }

    public BehaviorCatalog Behavior { get; }

    public RuntimeCardCatalog Runtime { get; }

    public ICardMetadataResolver Metadata { get; }

    /// <summary>Every preset a game in this format may name (the teaching decks included).</summary>
    public OfficialPresetCatalog Presets { get; }

    /// <summary>The format's own decks, as the menu offers them.</summary>
    public IReadOnlyList<OfficialPreset> Decks { get; }

    /// <summary>The match definition for a request in this format.</summary>
    public FullMatchDefinition Compose(MatchRequest request) =>
        FullMatchDefinition.FromRequest(request, Data, Behavior, Presets);

    /// <summary>A request for two of this format's presets.</summary>
    public MatchRequest ComposeOfficial(string corpPresetId, string runnerPresetId, ulong seed) =>
        MatchRequestComposer.ComposeOfficial(corpPresetId, runnerPresetId, Data, seed, Presets, behaviorCatalog: Behavior);
}

/// <summary>The formats a player can choose (D-EL-8), with their content loaded once.</summary>
public static class PlayFormats
{
    /// <summary>The formats in menu order.</summary>
    public static IReadOnlyList<string> Ids { get; } = [CardFormatIds.Gateway, CardFormatIds.GatewayElevation, CardFormatIds.NsgCoreSets];

    private static readonly ConcurrentDictionary<string, Lazy<PlayFormatContent>> Loaded = new(StringComparer.Ordinal);

    /// <summary>Whether this build embeds the format's whole card pool as official data.</summary>
    public static bool IsAvailable(string formatId)
    {
        ArgumentNullException.ThrowIfNull(formatId);
        return Ids.Contains(formatId, StringComparer.Ordinal)
            && string.Equals(CardDataResource.EmbeddedMode, CardDataResource.OfficialMode, StringComparison.Ordinal)
            && CardPools.ForFormat(CardFormatCatalog.Embedded().Get(formatId)).IsEmbedded;
    }

    /// <summary>The content of a format, loaded once.</summary>
    public static PlayFormatContent Get(string formatId)
    {
        ArgumentNullException.ThrowIfNull(formatId);
        if (!Ids.Contains(formatId, StringComparer.Ordinal))
            throw new ArgumentException($"'{formatId}' is not a playable format.", nameof(formatId));
        return Loaded.GetOrAdd(formatId, id => new Lazy<PlayFormatContent>(() => Load(id))).Value;
    }

    /// <summary>
    /// The format whose decks include a preset: NSG Core Sets for NSG's Core Sets decks,
    /// Gateway + Elevation for our Elevation decks, Gateway otherwise.
    /// </summary>
    public static string FormatOfPreset(string presetId) =>
        OfficialPresetCatalog.IsNsgCoreSetsDeck(presetId) ? CardFormatIds.NsgCoreSets
        : OfficialPresetCatalog.IsElevationDeck(presetId) ? CardFormatIds.GatewayElevation
        : CardFormatIds.Gateway;

    /// <summary>
    /// The format of a pairing: the non-Gateway format either deck belongs to, else Gateway. A Gateway + Elevation deck
    /// and an NSG Core Sets deck share no format's deck list, so they can't play each other.
    /// </summary>
    public static string FormatOfPresets(string corpPresetId, string runnerPresetId)
    {
        string corp = FormatOfPreset(corpPresetId);
        string runner = FormatOfPreset(runnerPresetId);
        if (corp != CardFormatIds.Gateway && runner != CardFormatIds.Gateway && corp != runner)
            throw new ArgumentException($"'{corpPresetId}' ({corp}) and '{runnerPresetId}' ({runner}) are decks of different formats; "
                + "choose two decks of one format.");
        return corp != CardFormatIds.Gateway ? corp : runner;
    }

    /// <summary>
    /// The format a match plays: its decks' format when it names presets, else the
    /// format its behavior catalog plays. NSG Core Sets and Gateway + Elevation share
    /// a pool and its behaviors, so only the decks tell them apart.
    /// </summary>
    public static string FormatOf(MatchRequest request, IBehaviorCatalog behavior)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CorpDeck.PresetId is { } corp && request.RunnerDeck.PresetId is { } runner)
        {
            string format = FormatOfPresets(corp, runner);
            if (format != CardFormatIds.Gateway) return format;
        }
        return FormatOfBehavior(behavior);
    }

    /// <summary>
    /// The format a behavior catalog plays: the one whose pool carries its version. On the
    /// Gateway + Elevation pool that is Gateway + Elevation; <see cref="FormatOf"/> tells NSG Core Sets apart by its decks.
    /// </summary>
    public static string FormatOfBehavior(IBehaviorCatalog behavior)
    {
        ArgumentNullException.ThrowIfNull(behavior);
        return behavior.Version == ElevationBehaviorCatalog.Version ? CardFormatIds.GatewayElevation : CardFormatIds.Gateway;
    }

    /// <summary>
    /// What a saved game of a format loads with; a save that records no format
    /// predates formats and is a System Gateway game.
    /// </summary>
    public static SoloSaveContent SaveContent(string? formatId)
    {
        string id = formatId ?? CardFormatIds.Gateway;
        if (!IsAvailable(id)) throw new InvalidOperationException($"This build can't play the '{id}' format.");
        PlayFormatContent content = Get(id);
        return new SoloSaveContent(content.Behavior, content.Runtime, content.Compose);
    }

    /// <summary>A preset from any playable format's catalog, for callers that only hold its ID.</summary>
    public static bool TryGetPreset(string? presetId, out OfficialPreset preset)
    {
        preset = null!;
        if (presetId is null) return false;
        if (OfficialPresetCatalog.Embedded().TryGet(presetId, out preset)) return true;
        if (OfficialPresetCatalog.IsNsgCoreSetsDeck(presetId)) return OfficialPresetCatalog.NsgCoreSets().TryGet(presetId, out preset);
        return OfficialPresetCatalog.IsElevationDeck(presetId) && OfficialPresetCatalog.GatewayElevation().TryGet(presetId, out preset);
    }

    private static PlayFormatContent Load(string formatId)
    {
        CardFormat format = CardFormatCatalog.Embedded().Get(formatId);
        CardPool pool = CardPools.ForFormat(format);
        if (formatId == CardFormatIds.Gateway)
        {
            OfficialPresetCatalog gateway = OfficialPresetCatalog.Embedded();
            return new(format, pool.LoadEmbeddedData(), GatewayBehaviorCatalog.Create(), gateway, gateway.Gateway);
        }
        bool nsg = formatId == CardFormatIds.NsgCoreSets;
        OfficialPresetCatalog presets = nsg ? OfficialPresetCatalog.NsgCoreSets() : OfficialPresetCatalog.GatewayElevation();
        CardDataSet data = pool.LoadEmbeddedData();
        presets.Validate(data);
        return new(format, data, pool.CreateBehavior(), presets, nsg ? presets.NsgCoreSetsDecks : presets.ElevationDecks);
    }
}

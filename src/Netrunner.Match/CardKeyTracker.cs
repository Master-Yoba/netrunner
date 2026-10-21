using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>
/// Gives every card a viewer can see or physically follow a presentation key.
/// A card the viewer can see keys by its instance ID, as in the view model. A
/// face-down installed card keys by an opaque <c>tracked:N</c> handle that
/// stays stable while the card stays installed and hidden, like a player
/// following "the face-down card in Remote 1". The handle is retired once the
/// card leaves play or is revealed, so it never links two separate hidden
/// periods of the same card. Cards in hidden zones get no key.
/// </summary>
public sealed class CardKeyTracker
{
    public const string TrackedPrefix = "tracked:";

    private Dictionary<CardInstanceId, string> _tracked = [];
    private int _nextHandle;

    /// <summary>Keys for <paramref name="state"/>; call once per published state, in order.</summary>
    public IReadOnlyDictionary<CardInstanceId, string> Update(GameState state, PlayerSide viewer)
    {
        ArgumentNullException.ThrowIfNull(state);
        var keys = new Dictionary<CardInstanceId, string>();
        var tracked = new Dictionary<CardInstanceId, string>();
        Dictionary<ZoneId, ZoneState> zones = state.Zones.ToDictionary(zone => zone.Id);
        IReadOnlySet<CardInstanceId> visible = ObservationBuilder.VisibleCardIds(state, viewer);
        foreach (CardInstanceState card in state.Cards)
        {
            if (visible.Contains(card.Id))
            {
                keys[card.Id] = card.Id.Value;
            }
            else if (zones.TryGetValue(card.ZoneId, out ZoneState? zone) && zone.Kind is ZoneKind.ServerRoot or ZoneKind.ServerIce)
            {
                string handle = _tracked.TryGetValue(card.Id, out string? existing) ? existing : $"{TrackedPrefix}{++_nextHandle}";
                tracked[card.Id] = handle;
                keys[card.Id] = handle;
            }
        }
        _tracked = tracked;
        return keys;
    }
}

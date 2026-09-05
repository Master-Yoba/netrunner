using System.Collections;
using System.Reflection;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>Fail-closed Corp diagnostics: R&D access never grants the Corp permission to look.</summary>
internal static class CorpEventVisibility
{
    public static IReadOnlyList<EngineEvent> Filter(IReadOnlyList<EngineEvent> events, GameState state) =>
        events.Where(value => Visible(value.Payload, state, 0)).ToArray();

    private static bool Visible(object? value, GameState state, int depth)
    {
        if (value is null)
        {
            return true;
        }
        if (depth > 12)
        {
            return false;
        }
        if (value is CardInstanceId id)
        {
            CardInstanceState? card = state.Cards.FirstOrDefault(candidate => candidate.Id == id);
            ZoneState? zone = state.Zones.FirstOrDefault(candidate => candidate.Id == card?.ZoneId);
            return card is not null && zone is not null && zone.Kind is not (ZoneKind.CorpDeck or ZoneKind.RunnerStack)
                && (card.OwnerSide == PlayerSide.Corp || card.FaceUp);
        }
        if (value is CardDefinitionId)
        {
            // Standalone definition references are not a proof of a public reveal.
            return false;
        }
        Type type = value.GetType();
        if (value is string || type.IsPrimitive || type.IsEnum || type.IsValueType)
        {
            return true;
        }
        if (value is IEnumerable items)
        {
            return items.Cast<object?>().All(item => Visible(item, state, depth + 1));
        }
        if (type.Namespace != typeof(GameState).Namespace)
        {
            return false;
        }
        try
        {
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.GetIndexParameters().Length == 0)
                .All(property => Visible(property.GetValue(value), state, depth + 1));
        }
        catch (Exception exception) when (exception is TargetInvocationException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }
}

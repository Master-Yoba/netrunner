using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

/// <summary>
/// Writes <see cref="BehaviorInstructionKind"/> with stable JSON names.
/// The enum has short aliases (<c>Trash = TrashCard</c>, ...), and which alias
/// the general string-enum converter writes depends on the enum's member
/// count, so adding instruction kinds silently changed saved-state and
/// evaluation-catalog bytes. The aliased values keep their historical names;
/// every name and alias still reads.
/// </summary>
internal sealed class BehaviorInstructionKindJsonConverter : JsonConverter<BehaviorInstructionKind>
{
    private static readonly FrozenDictionary<BehaviorInstructionKind, string> Names = BuildNames();

    private static readonly FrozenDictionary<string, BehaviorInstructionKind> Values = Enum.GetNames<BehaviorInstructionKind>()
        .ToFrozenDictionary(
            static name => JsonNamingPolicy.CamelCase.ConvertName(name),
            static name => Enum.Parse<BehaviorInstructionKind>(name),
            StringComparer.OrdinalIgnoreCase);

    public override BehaviorInstructionKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String
            || reader.GetString() is not { } name
            || !Values.TryGetValue(name, out BehaviorInstructionKind kind))
        {
            throw new JsonException($"'{reader.GetString()}' is not a behavior instruction kind.");
        }

        return kind;
    }

    public override void Write(Utf8JsonWriter writer, BehaviorInstructionKind value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(Names.TryGetValue(value, out string? name)
            ? name
            : throw new JsonException($"'{(int)value}' is not a behavior instruction kind."));
    }

    private static FrozenDictionary<BehaviorInstructionKind, string> BuildNames()
    {
        // The historical names of the aliased values, as the M4/M5 states and
        // pins recorded them.
        var historical = new Dictionary<BehaviorInstructionKind, string>
        {
            [BehaviorInstructionKind.TrashCard] = "trash",
            [BehaviorInstructionKind.AdvanceCard] = "advance",
            [BehaviorInstructionKind.InstallCard] = "installCard",
            [BehaviorInstructionKind.ShuffleIntoDeck] = "shuffleIntoDeck",
        };
        var names = new Dictionary<BehaviorInstructionKind, string>(historical);
        foreach (string name in Enum.GetNames<BehaviorInstructionKind>())
        {
            names.TryAdd(Enum.Parse<BehaviorInstructionKind>(name), JsonNamingPolicy.CamelCase.ConvertName(name));
        }

        return names.ToFrozenDictionary();
    }
}

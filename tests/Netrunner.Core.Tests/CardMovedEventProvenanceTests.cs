using System.Text.Json;
using Xunit;

namespace Netrunner.Core.Tests;

public sealed class CardMovedEventProvenanceTests
{
    [Fact]
    public void LegacyDirectCardMovedEventsKeepTheirCanonicalJsonShape()
    {
        EngineEvent legacy = new(
            1,
            EngineEventKinds.CardMoved,
            new CardMovedEventPayload(
                new CardInstanceId("card:legacy-move"),
                new ZoneId("zone:source"),
                new ZoneId("zone:target")));

        const string expected =
            "{\"version\":1,\"sequence\":1,\"kind\":\"card-moved\",\"payload\":{\"$type\":\"card-moved\",\"cardId\":\"card:legacy-move\",\"fromZoneId\":\"zone:source\",\"toZoneId\":\"zone:target\",\"version\":1}}";
        string json = EngineJson.Serialize(legacy);

        Assert.Equal(expected, json);
        Assert.DoesNotContain("fromZoneKind", json, StringComparison.Ordinal);
        Assert.DoesNotContain("toZoneKind", json, StringComparison.Ordinal);

        EngineEvent restored = EngineJson.Deserialize<EngineEvent>(json);
        CardMovedEventPayload payload = Assert.IsType<CardMovedEventPayload>(restored.Payload);
        Assert.Null(payload.FromZoneKind);
        Assert.Null(payload.ToZoneKind);
        Assert.Equal(json, EngineJson.Serialize(restored));
    }

    [Fact]
    public void TypedCardMovedEventsRoundTripAndFreezeTheirZoneProvenance()
    {
        EngineEvent typed = new(
            1,
            EngineEventKinds.CardMoved,
            new CardMovedEventPayload(
                new CardInstanceId("card:rfg-move"),
                new ZoneId("zone:rig"),
                new ZoneId("zone:rfg"),
                ZoneKind.RunnerRig,
                ZoneKind.RunnerRemovedFromGame));

        string json = EngineJson.Serialize(typed);
        Assert.Contains("\"fromZoneKind\":\"runnerRig\"", json, StringComparison.Ordinal);
        Assert.Contains("\"toZoneKind\":\"runnerRemovedFromGame\"", json, StringComparison.Ordinal);

        EngineEvent restored = EngineJson.Deserialize<EngineEvent>(json);
        CardMovedEventPayload restoredPayload = Assert.IsType<CardMovedEventPayload>(restored.Payload);
        Assert.Equal(ZoneKind.RunnerRig, restoredPayload.FromZoneKind);
        Assert.Equal(ZoneKind.RunnerRemovedFromGame, restoredPayload.ToZoneKind);

        EngineEvent frozen = Assert.Single(EngineEventValidator.FreezeBatch([typed]));
        CardMovedEventPayload frozenPayload = Assert.IsType<CardMovedEventPayload>(frozen.Payload);
        Assert.Equal(ZoneKind.RunnerRig, frozenPayload.FromZoneKind);
        Assert.Equal(ZoneKind.RunnerRemovedFromGame, frozenPayload.ToZoneKind);
    }

    [Fact]
    public void CardMovedProvenanceRejectsOneSidedAndUndefinedKinds()
    {
        Assert.Throws<ArgumentException>(() => new CardMovedEventPayload(
            1,
            new CardInstanceId("card:one-sided"),
            new ZoneId("zone:source"),
            new ZoneId("zone:target"),
            ZoneKind.RunnerRig,
            null));
        Assert.Throws<ArgumentException>(() => new CardMovedEventPayload(
            1,
            new CardInstanceId("card:one-sided"),
            new ZoneId("zone:source"),
            new ZoneId("zone:target"),
            null,
            ZoneKind.RunnerHeap));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CardMovedEventPayload(
            1,
            new CardInstanceId("card:unknown-kind"),
            new ZoneId("zone:source"),
            new ZoneId("zone:target"),
            (ZoneKind)999,
            ZoneKind.RunnerHeap));

        string validJson = EngineJson.Serialize(new EngineEvent(
            1,
            EngineEventKinds.CardMoved,
            new CardMovedEventPayload(
                new CardInstanceId("card:json-one-sided"),
                new ZoneId("zone:source"),
                new ZoneId("zone:target"),
                ZoneKind.RunnerRig,
                ZoneKind.RunnerHeap)));
        string oneSidedJson = validJson.Replace(
            "\"toZoneKind\":\"runnerHeap\"",
            "\"toZoneKind\":null",
            StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => EngineJson.Deserialize<EngineEvent>(oneSidedJson));
        EngineEvent valid = new(
            1,
            EngineEventKinds.CardMoved,
            new CardMovedEventPayload(
                new CardInstanceId("card:legacy-validator"),
                new ZoneId("zone:source"),
                new ZoneId("zone:target")));
        Assert.Empty(EngineEventValidator.Collect(valid));
    }
}

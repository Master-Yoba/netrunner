using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

public sealed class GatewayAgendaBehaviorSpecTests
{
    [Fact]
    public void OffworldOfficeSpecMatchesTheOfficialCardText()
    {
        AgendaBehaviorSpec spec = GatewayAgendaBehaviorSpecs.AgendaSpecs.Single(
            static candidate => candidate.DefinitionId == GatewayCardCodes.OffworldOffice);

        Assert.Equal("30067", spec.DefinitionId.Value);
        Assert.Equal(2, spec.AgendaPoints);
    }

    [Fact]
    public void AgendaSpecsBindExactlyTheOneRepresentativeAgendaDefinition()
    {
        Assert.Equal(
            new[] { GatewayCardCodes.OffworldOffice },
            GatewayAgendaBehaviorSpecs.AgendaSpecs
                .Select(static spec => spec.DefinitionId)
                .ToArray());
    }

    [Fact]
    public void RegisterAllIsIdempotentAndRetrievalSucceeds()
    {
        GatewayAgendaBehaviorSpecs.RegisterAll();
        GatewayAgendaBehaviorSpecs.RegisterAll();

        Assert.True(CardBehaviorSpecCatalog.TryGetAgenda(GatewayCardCodes.OffworldOffice, out AgendaBehaviorSpec? spec));
        Assert.Equal(GatewayAgendaBehaviorSpecs.AgendaSpecs[0], spec);
        Assert.Equal(
            GatewayAgendaBehaviorSpecs.AgendaSpecs[0],
            CardBehaviorSpecCatalog.GetAgenda(GatewayCardCodes.OffworldOffice));
    }

    [Fact]
    public void GetAgendaFailsClosedForUnregisteredDefinitions()
    {
        CardDefinitionId unregistered = new("30001");

        Assert.False(CardBehaviorSpecCatalog.TryGetAgenda(unregistered, out _));
        Assert.Throws<ArgumentException>(() => CardBehaviorSpecCatalog.GetAgenda(unregistered));
    }

    [Fact]
    public void RegisteringADifferentSpecForTheSameDefinitionThrows()
    {
        AgendaBehaviorSpec canonical = GatewayAgendaBehaviorSpecs.AgendaSpecs[0];
        Assert.Equal(GatewayCardCodes.OffworldOffice, canonical.DefinitionId);
        CardBehaviorSpecCatalog.RegisterAgenda(canonical);

        AgendaBehaviorSpec conflicting = new(canonical.DefinitionId, agendaPoints: canonical.AgendaPoints + 1);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => CardBehaviorSpecCatalog.RegisterAgenda(conflicting));
        Assert.Contains(canonical.DefinitionId.Value, exception.Message);
        Assert.Equal(canonical, CardBehaviorSpecCatalog.GetAgenda(canonical.DefinitionId));
    }
}

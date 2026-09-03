using Xunit;

namespace Netrunner.Core.Tests;

public sealed class CardCounterStateTests
{
    private static readonly CardInstanceId CardId = new("card:counter-purpose");

    [Fact]
    public void LegacyAndTypedConstructorsPreserveCounterPurpose()
    {
        CardCounterState generic = new(CardId, "power", 2);
        CardCounterState virus = new(CardId, "virus", 3);
        CardCounterState hosted = CardCounterState.CreateHostedCredit(
            CardId,
            4);
        CardCounterState hostedViaConstructor = new(CardId, 4);

        Assert.Equal(CardCounterPurpose.Generic, generic.Purpose);
        Assert.Null(generic.SpendRestriction);
        Assert.Equal(CardCounterPurpose.Virus, virus.Purpose);
        Assert.Null(virus.SpendRestriction);
        Assert.Equal("credit", hosted.CounterName);
        Assert.Equal(CardCounterPurpose.HostedCredit, hosted.Purpose);
        Assert.Equal(HostedCreditSpendRestriction.CardEffectOnly, hosted.SpendRestriction);
        Assert.Equal(CardCounterPurpose.HostedCredit, hostedViaConstructor.Purpose);
        Assert.Equal(HostedCreditSpendRestriction.CardEffectOnly, hostedViaConstructor.SpendRestriction);

        CardCounterState copied = new(
            hosted.CardId,
            hosted.CounterName,
            hosted.Count,
            hosted.Purpose,
            hosted.SpendRestriction);
        Assert.Equal(hosted.Purpose, copied.Purpose);
        Assert.Equal(hosted.SpendRestriction, copied.SpendRestriction);
    }

    [Fact]
    public void ReservedNamesAndInvalidPurposeCombinationsFailClosed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CardCounterState(CardId, "power", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CardCounterState(CardId, "power", -1));
        Assert.Throws<ArgumentException>(() => new CardCounterState(CardId, "credit", 1));
        Assert.Throws<ArgumentException>(() => new CardCounterState(
            CardId,
            "virus",
            1,
            CardCounterPurpose.Generic,
            HostedCreditSpendRestriction.AnyCost));
        Assert.Throws<ArgumentException>(() => new CardCounterState(
            CardId,
            "virus",
            1,
            CardCounterPurpose.Generic,
            spendRestriction: null));
        Assert.Throws<ArgumentException>(() => new CardCounterState(
            CardId,
            "virus",
            1,
            CardCounterPurpose.Virus,
            HostedCreditSpendRestriction.AnyCost));
        Assert.Throws<ArgumentException>(() => new CardCounterState(
            CardId,
            "power",
            1,
            CardCounterPurpose.Generic,
            HostedCreditSpendRestriction.AnyCost));
        Assert.Throws<ArgumentException>(() => new CardCounterState(
            CardId,
            "credit",
            1,
            CardCounterPurpose.HostedCredit,
            spendRestriction: null));
        Assert.Throws<ArgumentException>(() => new CardCounterState(
            CardId,
            "power",
            1,
            CardCounterPurpose.HostedCredit,
            HostedCreditSpendRestriction.RunCost));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CardCounterState(
            CardId,
            "power",
            1,
            (CardCounterPurpose)99,
            spendRestriction: null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CardCounterState(
            CardId,
            1,
            (HostedCreditSpendRestriction)99));
    }

    [Fact]
    public void TypedCounterJsonRoundTripsExactly()
    {
        CardCounterState source = new(
            CardId,
            "credit",
            5,
            CardCounterPurpose.HostedCredit,
            HostedCreditSpendRestriction.RunCost);

        string json = EngineJson.Serialize(source);
        CardCounterState restored = EngineJson.Deserialize<CardCounterState>(json);

        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(source.CardId, restored.CardId);
        Assert.Equal(source.CounterName, restored.CounterName);
        Assert.Equal(source.Count, restored.Count);
        Assert.Equal(source.Purpose, restored.Purpose);
        Assert.Equal(source.SpendRestriction, restored.SpendRestriction);
    }

    [Fact]
    public void CounterTransitionsRespectPurposeAndBounds()
    {
        CardCounterState generic = new(CardId, "power", 2);
        CardCounterState virus = new(CardId, "virus", 2);
        CardCounterState hostedAbility = CardCounterState.CreateHostedCredit(
            CardId,
            3,
            HostedCreditSpendRestriction.AbilityCost);
        CardCounterState hostedAny = CardCounterState.CreateHostedCredit(
            CardId,
            3,
            HostedCreditSpendRestriction.AnyCost);

        Assert.Equal(2, CardCounterRules.Load(null, 2));
        Assert.Equal(5, CardCounterRules.Load(hostedAbility, 2));
        Assert.Throws<InvalidOperationException>(() => CardCounterRules.Load(generic, 1));
        Assert.Throws<InvalidOperationException>(() => CardCounterRules.Load(virus, 1));
        Assert.Equal(4, CardCounterRules.Place(generic, 2));
        Assert.Throws<OverflowException>(() => CardCounterRules.Place(generic, int.MaxValue));
        Assert.Equal(0, CardCounterRules.Take(generic, 2));
        Assert.Throws<InvalidOperationException>(() => CardCounterRules.Take(generic, 3));
        Assert.Throws<OverflowException>(() => CardCounterRules.Load(
            new CardCounterState(CardId, "credit", int.MaxValue, CardCounterPurpose.HostedCredit, HostedCreditSpendRestriction.AnyCost),
            1));

        Assert.False(CardCounterRules.CanSpend(generic, HostedCreditSpendRestriction.AbilityCost));
        Assert.False(CardCounterRules.CanSpend(virus, HostedCreditSpendRestriction.RunCost));
        Assert.False(CardCounterRules.CanSpend(hostedAbility, HostedCreditSpendRestriction.CardEffectOnly));
        Assert.False(CardCounterRules.CanSpend(hostedAbility, HostedCreditSpendRestriction.RunCost));
        Assert.True(CardCounterRules.CanSpend(hostedAbility, HostedCreditSpendRestriction.AbilityCost));
        Assert.True(CardCounterRules.CanSpend(hostedAny, HostedCreditSpendRestriction.AbilityCost));
        Assert.True(CardCounterRules.CanSpend(hostedAny, HostedCreditSpendRestriction.RunCost));
        Assert.False(CardCounterRules.CanSpend(hostedAny, HostedCreditSpendRestriction.AnyCost));
        Assert.Equal(1, CardCounterRules.Spend(hostedAbility, 2, HostedCreditSpendRestriction.AbilityCost));
        Assert.Throws<InvalidOperationException>(() => CardCounterRules.Spend(
            hostedAbility,
            1,
            HostedCreditSpendRestriction.RunCost));
        Assert.Throws<InvalidOperationException>(() => CardCounterRules.Spend(
            generic,
            1,
            HostedCreditSpendRestriction.AbilityCost));
    }
}

using Tally.Domain;

namespace Tally.UnitTests;

public sealed class MoneyTests
{
    [Theory]
    [InlineData(12.34, "USD", 1234)]
    [InlineData(0.01, "usd", 1)]
    [InlineData(1500, "JPY", 1500)]
    public void Create_stores_amount_in_minor_units(decimal amount, string currency, long expectedMinor)
    {
        var result = Money.Create(amount, currency);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedMinor, result.Value.MinorUnits);
        Assert.Equal(currency.ToUpperInvariant(), result.Value.Currency);
        Assert.Equal(amount, result.Value.Amount);
    }

    [Theory]
    [InlineData(0, "USD", "money.not_positive")]
    [InlineData(-5, "USD", "money.not_positive")]
    [InlineData(10, "XYZ", "money.currency_unsupported")]
    [InlineData(10.001, "USD", "money.too_precise")]
    [InlineData(10.5, "JPY", "money.too_precise")]
    [InlineData(100000.01, "USD", "money.too_large")]
    public void Create_rejects_invalid_amounts(decimal amount, string currency, string expectedCode)
    {
        var result = Money.Create(amount, currency);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedCode, result.Error!.Code);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Equal_amounts_are_equal_values()
    {
        Assert.Equal(Money.Create(9.99m, "EUR").Value, Money.Create(9.99m, "eur").Value);
    }

    [Fact]
    public void ToString_uses_currency_precision()
    {
        Assert.Equal("7.50 GBP", Money.Create(7.5m, "GBP").Value.ToString());
        Assert.Equal("300 JPY", Money.Create(300m, "JPY").Value.ToString());
    }
}

namespace Tally.Domain;

/// <summary>
/// An amount in a single currency, stored as an integer count of the currency's minor unit
/// (cents for USD). Integers avoid rounding drift and aggregate the same way on every database.
/// </summary>
public sealed record Money
{
    private static readonly Dictionary<string, int> MinorUnitDigits = new(StringComparer.Ordinal)
    {
        ["USD"] = 2,
        ["EUR"] = 2,
        ["GBP"] = 2,
        ["CAD"] = 2,
        ["AUD"] = 2,
        ["JPY"] = 0,
    };

    public const long MaxMinorUnits = 100_000_00; // 100,000.00 in a two-digit currency

    // Used by EF Core when materializing; parameter names match the properties.
    private Money(long minorUnits, string currency)
    {
        MinorUnits = minorUnits;
        Currency = currency;
    }

    public long MinorUnits { get; }
    public string Currency { get; }

    public static IReadOnlyCollection<string> SupportedCurrencies => MinorUnitDigits.Keys;

    public decimal Amount => ToAmount(MinorUnits, Currency);

    /// <summary>Converts a stored minor-unit total (for example from a SUM query) back to a decimal amount.</summary>
    public static decimal ToAmount(long minorUnits, string currency) => minorUnits / Scale(currency);

    public static Result<Money> Create(decimal amount, string currency)
    {
        var code = (currency ?? string.Empty).Trim().ToUpperInvariant();
        if (!MinorUnitDigits.ContainsKey(code))
        {
            return Error.Validation("money.currency_unsupported", $"Currency '{currency}' is not supported.");
        }

        if (amount <= 0)
        {
            return Error.Validation("money.not_positive", "Amount must be greater than zero.");
        }

        var minor = amount * Scale(code);
        if (minor != decimal.Truncate(minor))
        {
            return Error.Validation(
                "money.too_precise",
                $"{code} amounts can have at most {MinorUnitDigits[code]} decimal places.");
        }

        if (minor > MaxMinorUnits)
        {
            return Error.Validation("money.too_large", "Amount is above the single-claim limit.");
        }

        return new Money((long)minor, code);
    }

    public override string ToString() =>
        $"{Amount.ToString($"F{MinorUnitDigits[Currency]}", System.Globalization.CultureInfo.InvariantCulture)} {Currency}";

    private static decimal Scale(string currency) => (decimal)Math.Pow(10, MinorUnitDigits[currency]);
}

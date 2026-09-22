using System.Globalization;

namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// A value that a payload filter compares with: text, a number or a boolean. Values of different kinds are never
/// equal, so the number 1 is not the text "1".
/// </summary>
public readonly record struct PayloadValue
{
    private PayloadValue(PayloadValueKind kind, string? text, decimal number, bool boolean)
    {
        Kind = kind;
        Text = text;
        Number = number;
        Boolean = boolean;
    }

    public PayloadValueKind Kind { get; }

    public string? Text { get; }

    public decimal Number { get; }

    public bool Boolean { get; }

    public static PayloadValue From(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new PayloadValue(PayloadValueKind.Text, text, 0, false);
    }

    // Equal numbers are written alike, so 1.0 is kept as 1: dividing by one leaves a decimal without trailing zeros.
    public static PayloadValue From(decimal number) =>
        new(PayloadValueKind.Number, null, number / 1.0000000000000000000000000000m, false);

    public static PayloadValue From(bool boolean) => new(PayloadValueKind.Boolean, null, 0, boolean);

    public override string ToString()
    {
        return Kind switch
        {
            PayloadValueKind.Text => $"text:{Text}",
            PayloadValueKind.Number => $"number:{Number.ToString(CultureInfo.InvariantCulture)}",
            _ => Boolean ? "boolean:true" : "boolean:false"
        };
    }
}
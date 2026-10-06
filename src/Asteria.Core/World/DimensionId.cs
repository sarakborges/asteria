namespace Asteria.Core.World;

public readonly record struct DimensionId
{
    public static readonly DimensionId Overworld =
        new("asteria:overworld");

    public DimensionId(string value)
    {
        Value =
            Validate(value);
    }

    public string Value { get; }

    public override string ToString() =>
        Value;

    public static implicit operator string(
        DimensionId value) =>
        value.Value;

    private static string Validate(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value != value.Trim())
        {
            throw new ArgumentException(
                "Dimension id must be a trimmed namespaced id.",
                nameof(value));
        }

        var split =
            value.Split(':');
        if (split.Length != 2 ||
            split[0].Length == 0 ||
            split[1].Length == 0 ||
            value.Contains(
                "..",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Invalid dimension id: {value}",
                nameof(value));
        }

        foreach (var character in value)
        {
            var valid =
                character is >= 'a' and <= 'z' ||
                character is >= '0' and <= '9' ||
                character is ':' or '_' or '-' or '.';

            if (!valid)
            {
                throw new ArgumentException(
                    $"Invalid dimension id character '{character}' in {value}.",
                    nameof(value));
            }
        }

        return value;
    }
}

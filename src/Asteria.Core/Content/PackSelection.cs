namespace Asteria.Core.Content;

public readonly record struct PackSelection
{
    public const string DefaultPackName = "default";

    public PackSelection(
        string resourcePack,
        string dataPack)
    {
        ResourcePack =
            ValidatePackName(
                resourcePack,
                nameof(resourcePack));
        DataPack =
            ValidatePackName(
                dataPack,
                nameof(dataPack));
    }

    public string ResourcePack { get; }

    public string DataPack { get; }

    public static PackSelection Default =>
        new(
            DefaultPackName,
            DefaultPackName);

    private static string ValidatePackName(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Pack name cannot be empty.",
                parameterName);
        }

        for (var index = 0;
             index < value.Length;
             index++)
        {
            var character =
                value[index];
            var valid =
                character is >= 'a' and <= 'z' ||
                character is >= '0' and <= '9' ||
                character is '.' or '_' or '-';

            if (!valid)
            {
                throw new ArgumentException(
                    "Pack names must use lowercase ASCII letters, digits, '.', '_' or '-'.",
                    parameterName);
            }
        }

        if (value is "." or "..")
        {
            throw new ArgumentException(
                "Pack name cannot be a relative path segment.",
                parameterName);
        }

        return value;
    }
}

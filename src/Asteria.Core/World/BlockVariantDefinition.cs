namespace Asteria.Core.World;

public sealed class BlockVariantDefinition
{
    public BlockVariantDefinition(string family, string key)
    {
        if (string.IsNullOrWhiteSpace(family) || family != family.Trim() || !family.Contains(':'))
        {
            throw new ArgumentException("Variant family must be a namespaced id.", nameof(family));
        }

        if (string.IsNullOrWhiteSpace(key) || key != key.Trim() || key.Contains(':'))
        {
            throw new ArgumentException("Variant key must be a non-empty local key.", nameof(key));
        }

        Family = family;
        Key = key;
    }

    public string Family { get; }

    public string Key { get; }
}

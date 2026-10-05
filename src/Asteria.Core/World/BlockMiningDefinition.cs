namespace Asteria.Core.World;

public sealed class BlockMiningDefinition
{
    public BlockMiningDefinition(
        float hardness = 1f,
        IEnumerable<string>? requiredTools = null,
        IEnumerable<string>? preferredTools = null)
    {
        if (!float.IsFinite(hardness) || hardness < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(hardness), "Mining hardness must be finite and non-negative.");
        }

        Hardness = hardness;
        RequiredTools = ValidateTags(requiredTools, nameof(requiredTools));
        PreferredTools = ValidateTags(preferredTools, nameof(preferredTools));
    }

    public float Hardness { get; }
    public IReadOnlyList<string> RequiredTools { get; }
    public IReadOnlyList<string> PreferredTools { get; }

    private static IReadOnlyList<string> ValidateTags(IEnumerable<string>? values, string parameterName)
    {
        if (values is null)
        {
            return Array.Empty<string>();
        }

        var result = values.ToArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in result)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
            {
                throw new ArgumentException("Mining tool tags must be non-empty and trimmed.", parameterName);
            }

            if (!seen.Add(value))
            {
                throw new ArgumentException($"Duplicate mining tool tag: {value}", parameterName);
            }
        }

        return result;
    }
}

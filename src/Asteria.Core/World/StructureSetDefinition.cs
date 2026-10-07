namespace Asteria.Core.World;

public readonly record struct StructureSetCountDefinition
{
    public const int MaximumCount = 128;

    public StructureSetCountDefinition(
        int minimum = 1,
        int maximum = 1)
    {
        if (minimum < 0 ||
            maximum < minimum ||
            maximum > MaximumCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximum),
                $"Structure-set count must satisfy 0 <= min <= max <= {MaximumCount}.");
        }

        Minimum = minimum;
        Maximum = maximum;
    }

    public int Minimum { get; }

    public int Maximum { get; }
}

public sealed class StructureSetElementPlacementDefinition
{
    public const int MaximumDistanceLimit = 4_096;
    public const int MaximumAttempts = 2_048;

    public StructureSetElementPlacementDefinition(
        string relativeTo = "origin",
        int minimumDistance = 0,
        int maximumDistance = 0,
        int minimumSeparation = 0,
        int attempts = 24,
        bool allowOverlap = false)
    {
        ValidateReference(
            relativeTo);

        if (minimumDistance < 0 ||
            maximumDistance < minimumDistance ||
            maximumDistance >
                MaximumDistanceLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDistance),
                $"Structure-set distance must satisfy 0 <= min <= max <= {MaximumDistanceLimit}.");
        }

        if (minimumSeparation < 0 ||
            minimumSeparation >
                MaximumDistanceLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumSeparation),
                $"Structure-set separation must be within 0..{MaximumDistanceLimit}.");
        }

        if (attempts is < 1 or
            > MaximumAttempts)
        {
            throw new ArgumentOutOfRangeException(
                nameof(attempts),
                $"Structure-set attempts must be within 1..{MaximumAttempts}.");
        }

        RelativeTo = relativeTo;
        MinimumDistance = minimumDistance;
        MaximumDistance = maximumDistance;
        MinimumSeparation = minimumSeparation;
        Attempts = attempts;
        AllowOverlap = allowOverlap;
    }

    public string RelativeTo { get; }

    public int MinimumDistance { get; }

    public int MaximumDistance { get; }

    public int MinimumSeparation { get; }

    public int Attempts { get; }

    public bool AllowOverlap { get; }

    private static void ValidateReference(
        string reference)
    {
        if (string.IsNullOrWhiteSpace(
                reference) ||
            reference != reference.Trim())
        {
            throw new ArgumentException(
                "Structure-set relativeTo must be a non-empty trimmed identifier.",
                nameof(reference));
        }
    }
}

public sealed class StructureSetElementDefinition
{
    public StructureSetElementDefinition(
        string id,
        string structure,
        StructureSetCountDefinition? count = null,
        float chance = 1f,
        bool required = false,
        StructureSetElementPlacementDefinition? placement = null)
    {
        ValidateLocalId(
            id,
            nameof(id));
        StructureDefinition.ValidateId(
            structure);

        if (!float.IsFinite(
                chance) ||
            chance is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chance),
                "Structure-set element chance must be within 0..1.");
        }

        Id = id;
        Structure = structure;
        Count =
            count ??
            new StructureSetCountDefinition(
                minimum: 1,
                maximum: 1);
        Chance = chance;
        Required = required;
        Placement =
            placement ??
            new StructureSetElementPlacementDefinition();
    }

    public string Id { get; }

    public string Structure { get; }

    public StructureSetCountDefinition Count { get; }

    public float Chance { get; }

    public bool Required { get; }

    public StructureSetElementPlacementDefinition Placement { get; }

    internal static void ValidateLocalId(
        string id,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(
                id) ||
            id != id.Trim() ||
            id.Contains(
                ':',
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Structure-set local id must be non-empty, trimmed, and unnamespaced.",
                parameterName);
        }

        foreach (var character in id)
        {
            var valid =
                character is >= 'a' and <= 'z' ||
                character is >= '0' and <= '9' ||
                character is '/' or '_' or '-' or '.';

            if (!valid)
            {
                throw new ArgumentException(
                    $"Invalid structure-set local id character '{character}' in {id}.",
                    parameterName);
            }
        }

        if (id.Contains(
                "..",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Structure-set local id cannot contain '..': {id}",
                parameterName);
        }
    }
}

public sealed class StructureSetDefinition
{
    public const int MaximumElements = 128;

    public StructureSetDefinition(
        string id,
        int priority = 0,
        IEnumerable<string>? conflictGroups = null,
        bool reserveSpace = false,
        IEnumerable<StructureSetElementDefinition>? elements = null)
    {
        StructureDefinition.ValidateId(
            id);

        var authoredElements =
            elements?.ToArray() ??
            throw new ArgumentNullException(
                nameof(elements));

        if (authoredElements.Length is < 1 or
            > MaximumElements)
        {
            throw new ArgumentException(
                $"Structure set must contain 1..{MaximumElements} elements.",
                nameof(elements));
        }

        var knownElements =
            new HashSet<string>(
                StringComparer.Ordinal);

        foreach (var element in
                 authoredElements)
        {
            if (element is null)
            {
                throw new ArgumentException(
                    "Structure-set elements cannot contain null.",
                    nameof(elements));
            }

            if (!knownElements.Add(
                    element.Id))
            {
                throw new ArgumentException(
                    $"Duplicate structure-set element id: {element.Id}",
                    nameof(elements));
            }

            var relativeTo =
                element.Placement.RelativeTo;

            if (relativeTo is not
                    ("origin" or "any") &&
                !knownElements.Contains(
                    relativeTo))
            {
                throw new ArgumentException(
                    $"Structure-set element {element.Id} relativeTo must reference origin, any, or an earlier element; missing {relativeTo}.",
                    nameof(elements));
            }

            if (relativeTo ==
                element.Id)
            {
                throw new ArgumentException(
                    $"Structure-set element {element.Id} cannot be relative to itself.",
                    nameof(elements));
            }
        }

        var authoredGroups =
            conflictGroups?.ToArray() ??
            Array.Empty<string>();
        var uniqueGroups =
            new HashSet<string>(
                StringComparer.Ordinal);

        foreach (var group in
                 authoredGroups)
        {
            StructureSetElementDefinition.ValidateLocalId(
                group,
                nameof(conflictGroups));

            if (!uniqueGroups.Add(
                    group))
            {
                throw new ArgumentException(
                    $"Duplicate structure-set conflict group: {group}",
                    nameof(conflictGroups));
            }
        }

        Id = id;
        Priority = priority;
        ConflictGroups =
            Array.AsReadOnly(
                authoredGroups);
        ReserveSpace = reserveSpace;
        Elements =
            Array.AsReadOnly(
                authoredElements);
    }

    public string Id { get; }

    public int Priority { get; }

    public IReadOnlyList<string> ConflictGroups { get; }

    public bool ReserveSpace { get; }

    public IReadOnlyList<StructureSetElementDefinition> Elements { get; }
}

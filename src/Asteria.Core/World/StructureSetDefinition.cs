using System.Collections.ObjectModel;

namespace Asteria.Core.World;

public readonly record struct StructureSetCount
{
    public StructureSetCount(
        int min,
        int max)
    {
        if (min < 0 ||
            max < min)
        {
            throw new ArgumentOutOfRangeException(
                nameof(min),
                "StructureSet count must satisfy 0 <= min <= max.");
        }

        Min = min;
        Max = max;
    }

    public static StructureSetCount One { get; } =
        new(
            1,
            1);

    public int Min { get; }

    public int Max { get; }
}

public sealed class StructureSetElementPlacementDefinition
{
    public const int DefaultAttempts = 24;

    public StructureSetElementPlacementDefinition(
        string relativeTo = "origin",
        int minDistance = 0,
        int maxDistance = 0,
        int minSeparation = 0,
        int attempts = DefaultAttempts,
        bool allowOverlap = false)
    {
        if (string.IsNullOrWhiteSpace(relativeTo) ||
            relativeTo != relativeTo.Trim())
        {
            throw new ArgumentException(
                "StructureSet relativeTo must be a non-empty trimmed value.",
                nameof(relativeTo));
        }

        if (minDistance < 0 ||
            maxDistance < minDistance)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minDistance),
                "StructureSet placement distance must satisfy 0 <= min <= max.");
        }

        if (minSeparation < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minSeparation));
        }

        if (attempts <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(attempts),
                "StructureSet placement attempts must be positive.");
        }

        RelativeTo = relativeTo;
        MinDistance = minDistance;
        MaxDistance = maxDistance;
        MinSeparation = minSeparation;
        Attempts = attempts;
        AllowOverlap = allowOverlap;
    }

    public string RelativeTo { get; }

    public int MinDistance { get; }

    public int MaxDistance { get; }

    public int MinSeparation { get; }

    public int Attempts { get; }

    public bool AllowOverlap { get; }
}

public sealed class StructureSetElementDefinition
{
    public StructureSetElementDefinition(
        string id,
        string structure,
        StructureSetCount? count = null,
        float chance = 1f,
        bool required = false,
        StructureSetElementPlacementDefinition? placement = null)
    {
        ValidateLocalId(
            id,
            nameof(id));
        StructureDefinition.ValidateId(
            structure);

        if (!float.IsFinite(chance) ||
            chance is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chance),
                "StructureSet element chance must be within 0..1.");
        }

        Id = id;
        Structure = structure;
        Count =
            count ??
            StructureSetCount.One;
        Chance = chance;
        Required = required;
        Placement =
            placement ??
            new StructureSetElementPlacementDefinition();
    }

    public string Id { get; }

    public string Structure { get; }

    public StructureSetCount Count { get; }

    public float Chance { get; }

    public bool Required { get; }

    public StructureSetElementPlacementDefinition Placement { get; }

    internal static void ValidateLocalId(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value != value.Trim() ||
            value.Contains(
                ':',
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "StructureSet local ids must be non-empty, trimmed and must not contain ':'.",
                parameterName);
        }
    }
}

public sealed class StructureSetDefinition
{
    public StructureSetDefinition(
        string id,
        IEnumerable<StructureSetElementDefinition> elements,
        bool locatable = true,
        int priority = 0,
        IEnumerable<string>? conflictGroups = null,
        bool reserveSpace = false)
    {
        StructureDefinition.ValidateId(
            id);

        var authoredElements =
            elements?.ToArray() ??
            throw new ArgumentNullException(
                nameof(elements));
        if (authoredElements.Length == 0)
        {
            throw new ArgumentException(
                "StructureSet must contain at least one element.",
                nameof(elements));
        }

        var elementIds =
            new HashSet<string>(
                StringComparer.Ordinal);
        for (var index = 0;
             index < authoredElements.Length;
             index++)
        {
            var element =
                authoredElements[index] ??
                throw new ArgumentException(
                    "StructureSet elements cannot contain null.",
                    nameof(elements));
            if (!elementIds.Add(
                    element.Id))
            {
                throw new ArgumentException(
                    $"StructureSet {id} repeats element id {element.Id}.",
                    nameof(elements));
            }

            var relativeTo =
                element.Placement.RelativeTo;
            if (relativeTo is
                    "origin" or
                    "any")
            {
                continue;
            }

            if (!elementIds.Contains(
                    relativeTo) ||
                string.Equals(
                    relativeTo,
                    element.Id,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"StructureSet {id} element {element.Id} relativeTo must reference origin, any, or an earlier element.",
                    nameof(elements));
            }
        }

        var authoredConflictGroups =
            conflictGroups?.ToArray() ??
            Array.Empty<string>();
        var uniqueGroups =
            new HashSet<string>(
                StringComparer.Ordinal);
        foreach (var group in
                 authoredConflictGroups)
        {
            StructureSetElementDefinition.ValidateLocalId(
                group,
                nameof(conflictGroups));
            if (!uniqueGroups.Add(
                    group))
            {
                throw new ArgumentException(
                    $"StructureSet {id} repeats conflict group {group}.",
                    nameof(conflictGroups));
            }
        }

        Id = id;
        Locatable = locatable;
        Priority = priority;
        ConflictGroups =
            Array.AsReadOnly(
                authoredConflictGroups);
        ReserveSpace = reserveSpace;
        Elements =
            Array.AsReadOnly(
                authoredElements);
    }

    public string Id { get; }

    public bool Locatable { get; }

    public int Priority { get; }

    public IReadOnlyList<string> ConflictGroups { get; }

    public bool ReserveSpace { get; }

    public IReadOnlyList<StructureSetElementDefinition> Elements { get; }
}

public sealed class StructureSetRegistry
{
    public static StructureSetRegistry Empty { get; } =
        new(
            Array.Empty<StructureSetDefinition>());

    private readonly StructureSetDefinition[] _definitions;
    private readonly Dictionary<string, StructureSetDefinition> _byId;

    public StructureSetRegistry(
        IEnumerable<StructureSetDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(
            definitions);

        _definitions =
            definitions
                .OrderBy(
                    definition =>
                        definition.Id,
                    StringComparer.Ordinal)
                .ToArray();
        _byId =
            new Dictionary<string, StructureSetDefinition>(
                _definitions.Length,
                StringComparer.Ordinal);

        foreach (var definition in
                 _definitions)
        {
            if (!_byId.TryAdd(
                    definition.Id,
                    definition))
            {
                throw new ArgumentException(
                    $"Duplicate StructureSet id: {definition.Id}",
                    nameof(definitions));
            }
        }
    }

    public int Count =>
        _definitions.Length;

    public static StructureSetRegistry FromJson(
        IEnumerable<string> documents)
    {
        ArgumentNullException.ThrowIfNull(
            documents);

        return new StructureSetRegistry(
            documents.Select(
                StructureSetDefinitionJson.Parse));
    }

    public bool TryGet(
        string id,
        out StructureSetDefinition definition) =>
        _byId.TryGetValue(
            id,
            out definition!);

    public StructureSetDefinition Get(
        string id) =>
        _byId.TryGetValue(
            id,
            out var definition)
            ? definition
            : throw new KeyNotFoundException(
                $"Unknown StructureSet id: {id}");

    public bool ResolvesReference(
        string reference) =>
        _byId.ContainsKey(
            reference);

    public void ValidateStructures(
        StructureRegistry structures)
    {
        ArgumentNullException.ThrowIfNull(
            structures);

        foreach (var definition in
                 _definitions)
        {
            if (structures.ResolvesReference(
                    definition.Id))
            {
                throw new ArgumentException(
                    $"StructureSet id {definition.Id} conflicts with an existing Structure/group reference.");
            }

            foreach (var element in
                     definition.Elements)
            {
                if (!structures.ResolvesReference(
                        element.Structure))
                {
                    throw new ArgumentException(
                        $"StructureSet {definition.Id} element {element.Id} references missing Structure/group {element.Structure}.");
                }
            }
        }
    }

    public IEnumerable<StructureSetDefinition>
        Definitions() =>
        _definitions;
}

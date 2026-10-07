namespace Asteria.Core.World;

public sealed class StructureRegistry
{
    private readonly StructureDefinition[] _definitions;
    private readonly IReadOnlyDictionary<string, StructureDefinition> _byId;
    private readonly IReadOnlyDictionary<string, StructureDefinition[]> _byGroup;

    public StructureRegistry(
        IEnumerable<StructureDefinition> definitions)
    {
        _definitions =
            definitions?
                .OrderBy(
                    definition => definition.Id,
                    StringComparer.Ordinal)
                .ToArray() ??
            throw new ArgumentNullException(nameof(definitions));

        var byId =
            new Dictionary<string, StructureDefinition>(
                StringComparer.Ordinal);
        var groups =
            new Dictionary<string, List<StructureDefinition>>(
                StringComparer.Ordinal);

        foreach (var definition in _definitions)
        {
            if (!byId.TryAdd(definition.Id, definition))
            {
                throw new ArgumentException(
                    $"Duplicate structure id: {definition.Id}",
                    nameof(definitions));
            }

            if (definition.GroupId is not { } group)
            {
                continue;
            }

            var reference = GroupReference(
                definition.Id,
                group);

            if (!groups.TryGetValue(reference, out var members))
            {
                members = new List<StructureDefinition>();
                groups.Add(reference, members);
            }

            members.Add(definition);
        }

        _byId = byId;
        _byGroup = groups.ToDictionary(
            pair => pair.Key,
            pair => pair.Value
                .OrderBy(
                    definition => definition.Id,
                    StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);
    }

    public int Count => _definitions.Length;

    public IEnumerable<StructureDefinition> Definitions() =>
        _definitions;

    public StructureDefinition Get(string id) =>
        _byId.TryGetValue(id, out var definition)
            ? definition
            : throw new KeyNotFoundException(
                $"Unknown structure id: {id}");

    public IReadOnlyList<StructureDefinition> ReferenceMembers(
        string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        if (_byId.TryGetValue(reference, out var exact))
        {
            return new[] { exact };
        }

        return _byGroup.TryGetValue(reference, out var members)
            ? members
            : throw new KeyNotFoundException(
                $"Unknown structure or group reference: {reference}");
    }

    public void ValidateBlocks(BlockRegistry blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        foreach (var definition in _definitions)
        {
            foreach (var voxel in definition.Voxels)
            {
                _ = blocks.GetId(voxel.Block);
            }

            foreach (var block in
                     definition.Restrictions.GroundBlocks)
            {
                _ = blocks.GetId(block);
            }
        }
    }

    public static StructureRegistry FromJson(
        IEnumerable<string> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        return new StructureRegistry(
            documents.Select(StructureDefinitionJson.Parse));
    }

    private static string GroupReference(
        string structureId,
        string groupId)
    {
        if (groupId.Contains(':'))
        {
            BiomeDefinition.ValidateId(groupId);
            return groupId;
        }

        var separator = structureId.IndexOf(':');
        return $"{structureId[..separator]}:{groupId}";
    }
}

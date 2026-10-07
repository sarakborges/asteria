namespace Asteria.Core.World;

public sealed class StructureRegistry
{
    public static StructureRegistry Empty { get; } =
        new(
            Array.Empty<StructureDefinition>());

    private readonly StructureDefinition[] _definitions;
    private readonly Dictionary<
        string,
        StructureDefinition> _definitionsById;
    private readonly IReadOnlyDictionary<
        string,
        IReadOnlyList<StructureDefinition>> _groups;

    public StructureRegistry(
        IEnumerable<StructureDefinition> definitions)
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
        _definitionsById =
            new Dictionary<
                string,
                StructureDefinition>(
                _definitions.Length,
                StringComparer.Ordinal);
        var groups =
            new Dictionary<
                string,
                List<StructureDefinition>>(
                StringComparer.Ordinal);

        foreach (var definition in
                 _definitions)
        {
            if (!_definitionsById.TryAdd(
                    definition.Id,
                    definition))
            {
                throw new ArgumentException(
                    $"Duplicate structure id: {definition.Id}",
                    nameof(definitions));
            }

            if (definition.GroupReference is
                { } group)
            {
                if (!groups.TryGetValue(
                        group,
                        out var members))
                {
                    members = [];
                    groups.Add(
                        group,
                        members);
                }

                members.Add(
                    definition);
            }
        }

        _groups =
            groups.ToDictionary(
                pair =>
                    pair.Key,
                pair =>
                    (IReadOnlyList<StructureDefinition>)
                    Array.AsReadOnly(
                        pair.Value
                            .OrderBy(
                                definition =>
                                    definition.Id,
                                StringComparer.Ordinal)
                            .ToArray()),
                StringComparer.Ordinal);
    }

    public int Count =>
        _definitions.Length;

    public static StructureRegistry FromJson(
        IEnumerable<string> documents)
    {
        ArgumentNullException.ThrowIfNull(
            documents);

        return new StructureRegistry(
            documents.Select(
                StructureDefinitionJson.Parse));
    }

    public StructureDefinition Get(
        string id) =>
        _definitionsById.TryGetValue(
            id,
            out var definition)
            ? definition
            : throw new KeyNotFoundException(
                $"Unknown structure id: {id}");

    public IReadOnlyList<StructureDefinition>
        ResolveReference(
            string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            reference);

        if (_definitionsById.TryGetValue(
                reference,
                out var definition))
        {
            return [
                definition,
            ];
        }

        return _groups.TryGetValue(
                reference,
                out var members)
            ? members
            : throw new KeyNotFoundException(
                $"Unknown structure or structure group: {reference}");
    }

    public bool ResolvesReference(
        string reference) =>
        _definitionsById.ContainsKey(
            reference) ||
        _groups.ContainsKey(
            reference);

    public void ValidateBlocks(
        BlockRegistry blocks)
    {
        ArgumentNullException.ThrowIfNull(
            blocks);

        foreach (var definition in
                 _definitions)
        {
            if (definition.Generation.FluidPolicy ==
                StructureFluidPolicy.Preserve)
            {
                throw new ArgumentException(
                    $"Structure {definition.Id} uses fluidPolicy preserve, which is unsupported by the current block-only structure contract.");
            }

            foreach (var proximity in
                     definition.Restrictions.Proximity)
            {
                if (proximity.Target.Block is
                        { } targetBlock &&
                    !blocks.TryGetId(
                        targetBlock,
                        out _))
                {
                    throw new ArgumentException(
                        $"Structure {definition.Id} proximity references missing block {targetBlock}.");
                }
            }

            foreach (var groundBlock in
                     definition.Restrictions.GroundBlocks)
            {
                if (!blocks.TryGetId(
                        groundBlock,
                        out _))
                {
                    throw new ArgumentException(
                        $"Structure {definition.Id} restrictions.groundBlocks references missing block {groundBlock}.");
                }
            }

            foreach (var voxel in
                     definition.Voxels)
            {
                if (!blocks.TryGetId(
                        voxel.Block,
                        out var runtimeId))
                {
                    throw new ArgumentException(
                        $"Structure {definition.Id} references missing block {voxel.Block}.");
                }

                var block =
                    blocks.GetDefinition(
                        runtimeId);
                var rotations =
                    definition.Rotation
                        ? Enum.GetValues<StructureRotation>()
                        : [
                            StructureRotation.Degrees0,
                        ];

                foreach (var rotation in
                         rotations)
                {
                    var orientation =
                        StructureDefinition
                            .RotateOrientation(
                                rotation,
                                voxel.Orientation);
                    if (!block.Orientations.Contains(
                            orientation))
                    {
                        throw new ArgumentException(
                            $"Structure {definition.Id} rotation {rotation} produces unsupported orientation {orientation} for block {voxel.Block}.");
                    }
                }
            }
        }
    }

    public IEnumerable<StructureDefinition>
        Definitions() =>
        _definitions;

    public void ValidateFluids(
        FluidRegistry fluids)
    {
        ArgumentNullException.ThrowIfNull(
            fluids);

        foreach (var definition in
                 _definitions)
        {
            foreach (var proximity in
                     definition.Restrictions.Proximity)
            {
                if (proximity.Target.Fluid is
                        { } targetFluid &&
                    !fluids.TryGetId(
                        targetFluid,
                        out _))
                {
                    throw new ArgumentException(
                        $"Structure {definition.Id} proximity references missing fluid {targetFluid}.");
                }
            }
        }
    }
}

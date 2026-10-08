namespace Asteria.Core.World;

public sealed class BiomeRegistry
{
    private readonly BiomeDefinition[] _definitions;
    private readonly Dictionary<string, BiomeDefinition>
        _definitionsById;

    public BiomeRegistry(
        IEnumerable<BiomeDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        _definitions =
            definitions
                .OrderBy(
                    definition =>
                        definition.Id,
                    StringComparer.Ordinal)
                .ToArray();

        if (_definitions.Length == 0)
        {
            throw new ArgumentException(
                "Biome registry cannot be empty.",
                nameof(definitions));
        }

        _definitionsById =
            new Dictionary<string, BiomeDefinition>(
                _definitions.Length,
                StringComparer.Ordinal);

        foreach (var definition in
                 _definitions)
        {
            _ =
                DimensionId(
                    definition.Id);

            if (!_definitionsById.TryAdd(
                    definition.Id,
                    definition))
            {
                throw new ArgumentException(
                    $"Duplicate biome id: {definition.Id}",
                    nameof(definitions));
            }
        }

        ValidateReferences();
    }

    public int Count =>
        _definitions.Length;

    public static BiomeRegistry FromJson(
        IEnumerable<string> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        return new BiomeRegistry(
            documents.Select(
                BiomeDefinitionJson.Parse));
    }

    public bool Contains(string id) =>
        id is not null && _definitionsById.ContainsKey(id);

    public BiomeDefinition Get(
        string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _definitionsById.TryGetValue(
            id,
            out var definition)
            ? definition
            : throw new KeyNotFoundException(
                $"Unknown biome id: {id}");
    }

    public void ValidateBlocks(
        BlockRegistry blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        foreach (var definition in
                 _definitions)
        {
            foreach (var layer in
                     definition.SurfaceLayers)
            {
                RequireBlock(
                    blocks,
                    definition.Id,
                    layer.Block);

                if (layer.Patch is null)
                {
                    continue;
                }

                foreach (var block in
                         layer.Patch.Blocks)
                {
                    RequireBlock(
                        blocks,
                        definition.Id,
                        block);
                }
            }

            foreach (var material in definition.CaveMaterials)
            {
                RequireBlock(blocks, definition.Id, material.Block);
                if (blocks.GetDefinition(
                    blocks.GetId(material.Block)).Shape.Kind != BlockShapeKind.Cube)
                    throw new ArgumentException(
                        $"Biome {definition.Id} cave material {material.Block} must use cube geometry.");
                foreach (var source in material.ReplaceBlocks)
                {
                    RequireBlock(blocks, definition.Id, source);
                    if (blocks.GetDefinition(
                        blocks.GetId(source)).Shape.Kind != BlockShapeKind.Cube)
                        throw new ArgumentException(
                            $"Biome {definition.Id} cave source {source} must use cube geometry.");
                }
            }

            foreach (var spike in definition.CaveSpikes)
            {
                RequireBlock(blocks, definition.Id, spike.Block);
                if (blocks.GetDefinition(
                        blocks.GetId(spike.Block)).Shape.Kind != BlockShapeKind.Spike)
                {
                    throw new ArgumentException(
                        $"Biome {definition.Id} caveSpikes block {spike.Block} must use spike geometry.");
                }
            }

            foreach (var decoration in
                     definition.Decorations)
            {
                RequireBlock(
                    blocks,
                    definition.Id,
                    decoration.Block);

                foreach (var block in
                         decoration.SurfaceBlocks)
                {
                    RequireBlock(
                        blocks,
                        definition.Id,
                        block);
                }
            }
        }
    }

    public void ValidateFluids(
        FluidRegistry fluids)
    {
        ArgumentNullException.ThrowIfNull(fluids);

        foreach (var definition in
                 _definitions)
        {
            var fill = definition.SurfaceTerrain?.Crater?.FluidFill;
            if (fill is null)
            {
                continue;
            }

            if (!fluids.TryGetId(fill.Fluid, out _))
            {
                throw new ArgumentException(
                    $"Biome {definition.Id} references missing fluid {fill.Fluid}.");
            }
        }
    }

    private void ValidateReferences()
    {
        foreach (var definition in
                 _definitions)
        {
            ValidateLayoutReferences(
                definition,
                definition.SurfaceLayout,
                target =>
                    target.SurfaceLayout is not null,
                "surfaceLayout");
            ValidateLayoutReferences(
                definition,
                definition.VolumeLayout,
                target =>
                    target.VolumeLayout is not null,
                "volumeLayout");
            ValidateLayoutReferences(
                definition,
                definition.UndergroundLayout,
                target =>
                    target.UndergroundLayout is not null,
                "undergroundLayout");
            ValidateCaveSpikeSurfaceReferences(definition);
        }
    }

    private void ValidateCaveSpikeSurfaceReferences(
        BiomeDefinition definition)
    {
        foreach (var spike in definition.CaveSpikes)
        foreach (var biomeId in spike.SurfaceBiomes)
        {
            if (!_definitionsById.TryGetValue(biomeId, out var target) ||
                target.SurfaceLayout is null ||
                DimensionId(target.Id) != DimensionId(definition.Id))
            {
                throw new ArgumentException(
                    $"Biome {definition.Id} cave spike {spike.Block} references " +
                    $"an unknown or incompatible surface biome: {biomeId}.");
            }
        }
    }

    private void ValidateLayoutReferences(
        BiomeDefinition definition,
        BiomePlacementLayoutDefinition? layout,
        Func<BiomeDefinition, bool> participates,
        string domain)
    {
        if (layout is null)
        {
            return;
        }

        foreach (var forbidden in
                 layout.CannotBorder)
        {
            if (!_definitionsById.TryGetValue(
                    forbidden,
                    out var target))
            {
                throw new ArgumentException(
                    $"Biome {definition.Id} {domain}.cannotBorder references missing biome {forbidden}.");
            }

            var ownDimension =
                DimensionId(
                    definition.Id);
            var targetDimension =
                DimensionId(
                    target.Id);

            if (!string.Equals(
                    ownDimension,
                    targetDimension,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Biome {definition.Id} {domain}.cannotBorder target {forbidden} belongs to another dimension.");
            }

            if (string.Equals(
                    definition.Id,
                    forbidden,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Biome {definition.Id} cannot forbid bordering itself.");
            }

            if (!participates(
                    target))
            {
                throw new ArgumentException(
                    $"Biome {definition.Id} {domain}.cannotBorder target {forbidden} does not participate in the same placement domain.");
            }
        }
    }

    private static string DimensionId(
        string biomeId)
    {
        var slash =
            biomeId.IndexOf(
                '/',
                StringComparison.Ordinal);

        if (slash <= 0)
        {
            throw new ArgumentException(
                $"Biome id must include a dimension path: {biomeId}");
        }

        return biomeId[..slash];
    }

    private static void RequireBlock(
        BlockRegistry blocks,
        string biomeId,
        string blockId)
    {
        if (!blocks.TryGetId(
                blockId,
                out _))
        {
            throw new ArgumentException(
                $"Biome {biomeId} references missing block {blockId}.");
        }
    }
}

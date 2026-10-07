namespace Asteria.Core.World;

public sealed class DimensionRegistry
{
    private readonly DimensionDefinition[] _definitions;
    private readonly Dictionary<
        DimensionId,
        DimensionDefinition> _byId;

    public DimensionRegistry(
        IEnumerable<DimensionDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(
            definitions);

        _definitions =
            definitions
                .OrderBy(
                    definition =>
                        definition.Id.Value,
                    StringComparer.Ordinal)
                .ToArray();

        if (_definitions.Length == 0)
        {
            throw new ArgumentException(
                "Dimension registry cannot be empty.",
                nameof(definitions));
        }

        _byId =
            new Dictionary<
                DimensionId,
                DimensionDefinition>();

        foreach (var definition in
                 _definitions)
        {
            if (!_byId.TryAdd(
                    definition.Id,
                    definition))
            {
                throw new ArgumentException(
                    $"Duplicate dimension id: {definition.Id}",
                    nameof(definitions));
            }
        }
    }

    public int Count =>
        _definitions.Length;

    public static DimensionRegistry FromJson(
        IEnumerable<string> documents)
    {
        ArgumentNullException.ThrowIfNull(
            documents);

        return new DimensionRegistry(
            documents.Select(
                DimensionDefinitionJson.Parse));
    }

    public DimensionDefinition Get(
        DimensionId id) =>
        _byId.TryGetValue(
            id,
            out var definition)
            ? definition
            : throw new KeyNotFoundException(
                $"Unknown dimension id: {id}");

    public IEnumerable<DimensionDefinition>
        Definitions() =>
        _definitions;

    public void ValidateBlocks(
        BlockRegistry blocks)
    {
        ArgumentNullException.ThrowIfNull(
            blocks);

        foreach (var dimension in
                 _definitions)
        {
            if (dimension.Shell is not
                { } shell)
            {
                continue;
            }

            if (!blocks.TryGetId(
                    shell.Block,
                    out _))
            {
                throw new ArgumentException(
                    $"Dimension {dimension.Id} references missing sphere shell block {shell.Block}.");
            }
        }
    }

    public void ValidateFluids(
        FluidRegistry fluids)
    {
        ArgumentNullException.ThrowIfNull(fluids);

        foreach (var dimension in _definitions)
        {
            if (dimension.GeneratedOcean is not { } ocean)
            {
                continue;
            }

            if (!fluids.TryGetId(ocean.Fluid, out _))
            {
                throw new ArgumentException(
                    $"Dimension {dimension.Id} references missing generated ocean fluid {ocean.Fluid}.");
            }
        }
    }

    public void ValidateBiomes(
        BiomeRegistry biomes)
    {
        ArgumentNullException.ThrowIfNull(
            biomes);

        foreach (var dimension in
                 _definitions)
        {
            foreach (var biomeId in
                     dimension.Biomes)
            {
                var biome =
                    biomes.Get(
                        biomeId);

                if (!biome.BelongsToDimension(
                        dimension.Id.Value))
                {
                    throw new ArgumentException(
                        $"Dimension {dimension.Id} references biome {biomeId} owned by another dimension.");
                }
            }
        }
    }
}

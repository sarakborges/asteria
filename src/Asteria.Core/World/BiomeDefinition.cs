using System.Collections.ObjectModel;

namespace Asteria.Core.World;

public sealed class BiomeDefinition
{
    public BiomeDefinition(
        string id,
        BiomeSurfaceLayoutDefinition surfaceLayout,
        BiomeTerrainDefinition surfaceTerrain,
        IEnumerable<BiomeSurfaceLayerDefinition> surfaceLayers,
        IEnumerable<BiomeDecorationDefinition>? decorations = null)
    {
        ValidateId(id);
        Id = id;
        SurfaceLayout =
            surfaceLayout ??
            throw new ArgumentNullException(nameof(surfaceLayout));
        SurfaceTerrain =
            surfaceTerrain ??
            throw new ArgumentNullException(nameof(surfaceTerrain));

        var layers =
            surfaceLayers?.ToArray() ??
            throw new ArgumentNullException(nameof(surfaceLayers));
        ValidateSurfaceLayers(layers);
        SurfaceLayers =
            Array.AsReadOnly(layers);

        var authoredDecorations =
            decorations?.ToArray() ??
            Array.Empty<BiomeDecorationDefinition>();
        Decorations =
            Array.AsReadOnly(authoredDecorations);
    }

    public string Id { get; }

    public BiomeSurfaceLayoutDefinition SurfaceLayout { get; }

    public BiomeTerrainDefinition SurfaceTerrain { get; }

    public IReadOnlyList<BiomeSurfaceLayerDefinition> SurfaceLayers { get; }

    public IReadOnlyList<BiomeDecorationDefinition> Decorations { get; }

    public bool BelongsToDimension(string dimensionId)
    {
        ArgumentNullException.ThrowIfNull(dimensionId);
        return Id.StartsWith(
            dimensionId + "/",
            StringComparison.Ordinal);
    }

    internal static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            id != id.Trim() ||
            !id.Contains(':'))
        {
            throw new ArgumentException(
                "Biome id must be a trimmed namespaced id.",
                nameof(id));
        }

        var split = id.Split(':');
        if (split.Length != 2 ||
            split[0].Length == 0 ||
            split[1].Length == 0)
        {
            throw new ArgumentException(
                $"Invalid biome id: {id}",
                nameof(id));
        }

        foreach (var character in id)
        {
            var valid =
                character is >= 'a' and <= 'z' ||
                character is >= '0' and <= '9' ||
                character is ':' or '/' or '_' or '-' or '.';

            if (!valid)
            {
                throw new ArgumentException(
                    $"Invalid biome id character '{character}' in {id}.",
                    nameof(id));
            }
        }

        if (id.Contains(
                "..",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Biome id cannot contain '..': {id}",
                nameof(id));
        }
    }

    private static void ValidateSurfaceLayers(
        IReadOnlyList<BiomeSurfaceLayerDefinition> layers)
    {
        if (layers.Count == 0)
        {
            throw new ArgumentException(
                "Biome surface layers cannot be empty.",
                nameof(layers));
        }

        uint finiteDepth = 0;

        for (var index = 0;
             index < layers.Count;
             index++)
        {
            var final = index == layers.Count - 1;
            var layer = layers[index];

            if (final && layer.Depth is not null)
            {
                throw new ArgumentException(
                    "The final biome surface layer is the core layer and must omit depth.",
                    nameof(layers));
            }

            if (!final && layer.Depth is null)
            {
                throw new ArgumentException(
                    "Every biome surface layer before the final core layer requires depth.",
                    nameof(layers));
            }

            if (final && layer.Patch is not null)
            {
                throw new ArgumentException(
                    "The final biome core layer cannot author a surface patch.",
                    nameof(layers));
            }

            if (layer.Depth is { } depth)
            {
                finiteDepth =
                    checked(
                        finiteDepth +
                        depth);

                if (finiteDepth > 64)
                {
                    throw new ArgumentException(
                        "Biome finite surface-layer depth cannot exceed 64 blocks.",
                        nameof(layers));
                }
            }

            if (layer.Patch is
                    { } patch &&
                patch.Blocks.Contains(
                    layer.Block,
                    StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    $"Biome surface patch cannot repeat its base block {layer.Block}.",
                    nameof(layers));
            }
        }
    }
}

public sealed class BiomeSurfaceLayoutDefinition
{
    public BiomeSurfaceLayoutDefinition(
        float weight = 1f,
        uint regionMin = 192,
        uint regionMax = 384,
        IEnumerable<string>? cannotBorder = null)
    {
        if (!float.IsFinite(weight) ||
            weight <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(weight),
                "Biome weight must be finite and positive.");
        }

        if (regionMin == 0 ||
            regionMax < regionMin ||
            regionMax > 16_384)
        {
            throw new ArgumentOutOfRangeException(
                nameof(regionMax),
                "Biome region size must satisfy 0 < min <= max <= 16384.");
        }

        var forbidden =
            cannotBorder?.ToArray() ??
            Array.Empty<string>();
        var seen =
            new HashSet<string>(
                StringComparer.Ordinal);
        foreach (var id in forbidden)
        {
            BiomeDefinition.ValidateId(id);
            if (!seen.Add(id))
            {
                throw new ArgumentException(
                    $"Duplicate cannotBorder biome id: {id}",
                    nameof(cannotBorder));
            }
        }

        Weight = weight;
        RegionMin = regionMin;
        RegionMax = regionMax;
        CannotBorder =
            Array.AsReadOnly(forbidden);
    }

    public float Weight { get; }

    public uint RegionMin { get; }

    public uint RegionMax { get; }

    public IReadOnlyList<string> CannotBorder { get; }
}

public sealed class BiomeTerrainDefinition
{
    public BiomeTerrainDefinition(
        float baseHeightOffset,
        float macroAmplitude,
        uint macroScale,
        float detailAmplitude,
        uint detailScale)
    {
        if (!float.IsFinite(baseHeightOffset))
        {
            throw new ArgumentOutOfRangeException(nameof(baseHeightOffset));
        }

        ValidateNoise(
            macroAmplitude,
            macroScale,
            nameof(macroAmplitude),
            nameof(macroScale));
        ValidateNoise(
            detailAmplitude,
            detailScale,
            nameof(detailAmplitude),
            nameof(detailScale));

        if (detailScale > macroScale)
        {
            throw new ArgumentException(
                "Biome detail scale cannot exceed macro scale.",
                nameof(detailScale));
        }

        BaseHeightOffset = baseHeightOffset;
        MacroAmplitude = macroAmplitude;
        MacroScale = macroScale;
        DetailAmplitude = detailAmplitude;
        DetailScale = detailScale;
    }

    public float BaseHeightOffset { get; }

    public float MacroAmplitude { get; }

    public uint MacroScale { get; }

    public float DetailAmplitude { get; }

    public uint DetailScale { get; }

    private static void ValidateNoise(
        float amplitude,
        uint scale,
        string amplitudeName,
        string scaleName)
    {
        if (!float.IsFinite(amplitude) ||
            amplitude < 0f ||
            amplitude > 512f)
        {
            throw new ArgumentOutOfRangeException(
                amplitudeName,
                "Terrain amplitude must be finite and within 0..512.");
        }

        if (scale is < 2 or > 16_384)
        {
            throw new ArgumentOutOfRangeException(
                scaleName,
                "Terrain scale must be within 2..16384.");
        }
    }
}

public sealed class BiomeSurfaceLayerDefinition
{
    public BiomeSurfaceLayerDefinition(
        string block,
        uint? depth = null,
        BiomeSurfacePatchDefinition? patch = null)
    {
        ValidateBlockId(block);

        if (depth == 0 ||
            depth > 64)
        {
            throw new ArgumentOutOfRangeException(
                nameof(depth),
                "Finite biome surface depth must be within 1..64.");
        }

        Block = block;
        Depth = depth;
        Patch = patch;
    }

    public string Block { get; }

    public uint? Depth { get; }

    public BiomeSurfacePatchDefinition? Patch { get; }

    internal static void ValidateBlockId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            id != id.Trim())
        {
            throw new ArgumentException(
                $"Invalid namespaced block id: {id}",
                nameof(id));
        }

        var split =
            id.Split(':');
        if (split.Length != 2 ||
            split[0].Length == 0 ||
            split[1].Length == 0 ||
            id.Contains(
                "..",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Invalid namespaced block id: {id}",
                nameof(id));
        }

        foreach (var character in id)
        {
            var valid =
                character is >= 'a' and <= 'z' ||
                character is >= '0' and <= '9' ||
                character is ':' or '/' or '_' or '-' or '.';

            if (!valid)
            {
                throw new ArgumentException(
                    $"Invalid block id character '{character}' in {id}.",
                    nameof(id));
            }
        }
    }
}

public sealed class BiomeSurfacePatchDefinition
{
    public BiomeSurfacePatchDefinition(
        uint spacing,
        uint radius,
        uint jitter,
        float chance,
        IEnumerable<string> blocks)
    {
        if (spacing is < 2 or > 512)
        {
            throw new ArgumentOutOfRangeException(nameof(spacing));
        }

        if (radius == 0 ||
            radius > 256 ||
            radius + jitter > spacing)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius),
                "Patch radius + jitter must fit inside spacing.");
        }

        if (jitter > spacing / 2)
        {
            throw new ArgumentOutOfRangeException(nameof(jitter));
        }

        if (!float.IsFinite(chance) ||
            chance <= 0f ||
            chance > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(chance));
        }

        var alternatives =
            blocks?.ToArray() ??
            throw new ArgumentNullException(nameof(blocks));

        if (alternatives.Length is < 1 or > 8)
        {
            throw new ArgumentException(
                "Surface patch must contain 1..8 block alternatives.",
                nameof(blocks));
        }

        var seen =
            new HashSet<string>(
                StringComparer.Ordinal);
        foreach (var block in alternatives)
        {
            BiomeSurfaceLayerDefinition.ValidateBlockId(
                block);
            if (!seen.Add(block))
            {
                throw new ArgumentException(
                    $"Duplicate patch block: {block}",
                    nameof(blocks));
            }
        }

        Spacing = spacing;
        Radius = radius;
        Jitter = jitter;
        Chance = chance;
        Blocks =
            Array.AsReadOnly(alternatives);
    }

    public uint Spacing { get; }

    public uint Radius { get; }

    public uint Jitter { get; }

    public float Chance { get; }

    public IReadOnlyList<string> Blocks { get; }
}

public sealed class BiomeDecorationDefinition
{
    public BiomeDecorationDefinition(
        string block,
        float chance,
        IEnumerable<string> surfaceBlocks)
    {
        BiomeSurfaceLayerDefinition.ValidateBlockId(
            block);

        if (!float.IsFinite(chance) ||
            chance <= 0f ||
            chance > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chance),
                "Decoration chance must be within (0, 1].");
        }

        var supports =
            surfaceBlocks?.ToArray() ??
            throw new ArgumentNullException(nameof(surfaceBlocks));

        if (supports.Length == 0)
        {
            throw new ArgumentException(
                "Decoration must author at least one allowed surface block.",
                nameof(surfaceBlocks));
        }

        var seen =
            new HashSet<string>(
                StringComparer.Ordinal);
        foreach (var support in supports)
        {
            BiomeSurfaceLayerDefinition.ValidateBlockId(
                support);
            if (!seen.Add(support))
            {
                throw new ArgumentException(
                    $"Duplicate decoration surface block: {support}",
                    nameof(surfaceBlocks));
            }
        }

        Block = block;
        Chance = chance;
        SurfaceBlocks =
            Array.AsReadOnly(supports);
    }

    public string Block { get; }

    public float Chance { get; }

    public IReadOnlyList<string> SurfaceBlocks { get; }
}

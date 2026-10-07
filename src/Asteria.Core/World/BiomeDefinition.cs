using System.Collections.ObjectModel;

namespace Asteria.Core.World;

public sealed class BiomeDefinition
{
    public BiomeDefinition(
        string id,
        BiomeSurfaceLayoutDefinition? surfaceLayout,
        BiomeTerrainDefinition? surfaceTerrain,
        IEnumerable<BiomeSurfaceLayerDefinition>? surfaceLayers = null,
        IEnumerable<BiomeDecorationDefinition>? decorations = null,
        BiomeTintPaletteDefinition? tints = null,
        BiomeTerrain3dDefinition? terrain3d = null,
        BiomeVolumeLayoutDefinition? volumeLayout = null,
        BiomeUndergroundLayoutDefinition? undergroundLayout = null,
        BiomeSurfaceFluidDefinition? surfaceFluid = null)
    {
        ValidateId(id);
        Id = id;
        if ((surfaceLayout is null) !=
            (surfaceTerrain is null))
        {
            throw new ArgumentException(
                "Biome surfaceLayout and surfaceTerrain must either both be authored or both be absent.");
        }

        if (surfaceLayout is null &&
            volumeLayout is null &&
            undergroundLayout is null)
        {
            throw new ArgumentException(
                "Biome must participate in at least one placement domain.");
        }

        if (volumeLayout is not null &&
            terrain3d?.FloatingFormation is null)
        {
            throw new ArgumentException(
                "Volume biome currently requires terrain3d.floatingFormation.");
        }

        SurfaceLayout = surfaceLayout;
        SurfaceTerrain = surfaceTerrain;
        VolumeLayout = volumeLayout;
        UndergroundLayout = undergroundLayout;

        var layers =
            surfaceLayers?.ToArray() ??
            Array.Empty<BiomeSurfaceLayerDefinition>();

        if ((surfaceLayout is not null ||
             volumeLayout is not null) &&
            layers.Length == 0)
        {
            throw new ArgumentException(
                "Surface and volume biomes require an exposed solid material profile.",
                nameof(surfaceLayers));
        }

        if (layers.Length > 0)
        {
            ValidateSurfaceLayers(layers);
        }
        SurfaceLayers =
            Array.AsReadOnly(layers);

        var authoredDecorations =
            decorations?.ToArray() ??
            Array.Empty<BiomeDecorationDefinition>();
        Decorations =
            Array.AsReadOnly(authoredDecorations);
        Tints =
            tints ??
            BiomeTintPaletteDefinition.Empty;
        Terrain3d = terrain3d;

        if (surfaceFluid is BiomeVolcanoCraterFluidDefinition &&
            surfaceTerrain?.Shape is not BiomeVolcanoTerrainShapeDefinition)
        {
            throw new ArgumentException(
                "volcano_crater surfaceFluid requires volcano surfaceTerrain.",
                nameof(surfaceFluid));
        }

        SurfaceFluid = surfaceFluid;
    }

    public string Id { get; }

    public BiomeSurfaceLayoutDefinition? SurfaceLayout { get; }

    public BiomeTerrainDefinition? SurfaceTerrain { get; }

    public BiomeVolumeLayoutDefinition? VolumeLayout { get; }

    public BiomeUndergroundLayoutDefinition? UndergroundLayout { get; }

    public IReadOnlyList<BiomeSurfaceLayerDefinition> SurfaceLayers { get; }

    public IReadOnlyList<BiomeDecorationDefinition> Decorations { get; }

    public BiomeTintPaletteDefinition Tints { get; }

    public BiomeTerrain3dDefinition? Terrain3d { get; }

    public BiomeSurfaceFluidDefinition? SurfaceFluid { get; }

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

public abstract class BiomePlacementLayoutDefinition
{
    protected BiomePlacementLayoutDefinition(
        float weight,
        uint regionMin,
        uint regionMax,
        IEnumerable<string>? cannotBorder)
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

public sealed class BiomeSurfaceLayoutDefinition :
    BiomePlacementLayoutDefinition
{
    public BiomeSurfaceLayoutDefinition(
        float weight = 1f,
        uint regionMin = 192,
        uint regionMax = 384,
        IEnumerable<string>? cannotBorder = null)
        : base(
            weight,
            regionMin,
            regionMax,
            cannotBorder)
    {
    }
}

public sealed class BiomeVolumeLayoutDefinition :
    BiomePlacementLayoutDefinition
{
    public BiomeVolumeLayoutDefinition(
        float weight = 1f,
        uint regionMin = 192,
        uint regionMax = 384,
        IEnumerable<string>? cannotBorder = null)
        : base(
            weight,
            regionMin,
            regionMax,
            cannotBorder)
    {
    }
}

public sealed class BiomeUndergroundLayoutDefinition :
    BiomePlacementLayoutDefinition
{
    public BiomeUndergroundLayoutDefinition(
        float weight = 1f,
        uint regionMin = 192,
        uint regionMax = 384,
        IEnumerable<string>? cannotBorder = null)
        : base(
            weight,
            regionMin,
            regionMax,
            cannotBorder)
    {
    }
}

public sealed class BiomeTerrainDefinition
{
    public BiomeTerrainDefinition(
        float baseHeightOffset,
        float macroAmplitude,
        uint macroScale,
        float detailAmplitude,
        uint detailScale)
        : this(
            new BiomeNoiseTerrainShapeDefinition(
                baseHeightOffset,
                macroAmplitude,
                macroScale,
                detailAmplitude,
                detailScale))
    {
    }

    public BiomeTerrainDefinition(
        BiomeTerrainShapeDefinition shape,
        IEnumerable<BiomeTerrainModifierDefinition>? modifiers = null)
    {
        Shape =
            shape ??
            throw new ArgumentNullException(nameof(shape));
        Modifiers =
            Array.AsReadOnly(
                modifiers?.ToArray() ??
                Array.Empty<BiomeTerrainModifierDefinition>());
    }

    public BiomeTerrainShapeDefinition Shape { get; }

    public IReadOnlyList<BiomeTerrainModifierDefinition> Modifiers { get; }

    public float MaximumHeightOffset =>
        Shape.MaximumHeightOffset +
        Modifiers.Sum(modifier => modifier.MaximumHeightOffset);
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
        uint scale,
        float coverage,
        float roughness,
        IEnumerable<string> blocks)
    {
        if (scale is < 2 or > 512)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scale),
                "Surface patch scale must be within 2..512.");
        }

        if (!float.IsFinite(coverage) ||
            coverage <= 0f ||
            coverage > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coverage),
                "Surface patch coverage must be within (0, 1].");
        }

        if (!float.IsFinite(roughness) ||
            roughness < 0f ||
            roughness > 0.5f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(roughness),
                "Surface patch roughness must be within 0..0.5.");
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

        Scale = scale;
        Coverage = coverage;
        Roughness = roughness;
        Blocks =
            Array.AsReadOnly(alternatives);
    }

    public uint Scale { get; }

    public float Coverage { get; }

    public float Roughness { get; }

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


public readonly record struct BiomeTintColor(
    byte Red,
    byte Green,
    byte Blue)
{
    public static BiomeTintColor Parse(
        string value)
    {
        var parsed =
            BlockPreviewColor.Parse(
                value);
        return new BiomeTintColor(
            parsed.Red,
            parsed.Green,
            parsed.Blue);
    }
}

public sealed class BiomeTintPaletteDefinition
{
    public static BiomeTintPaletteDefinition Empty { get; } =
        new();

    public BiomeTintPaletteDefinition(
        BiomeTintColor? grass = null,
        BiomeTintColor? leaf = null,
        BiomeTintColor? foliage = null)
    {
        Grass = grass;
        Leaf = leaf;
        Foliage = foliage;
    }

    public BiomeTintColor? Grass { get; }

    public BiomeTintColor? Leaf { get; }

    public BiomeTintColor? Foliage { get; }

    public BiomeTintColor? For(
        BlockTint tint) =>
        tint switch
        {
            BlockTint.None => null,
            BlockTint.Grass => Grass,
            BlockTint.Leaf => Leaf,
            BlockTint.Foliage => Foliage,
            _ => throw new ArgumentOutOfRangeException(
                nameof(tint)),
        };
}


/// <summary>
/// Biome-owned optional additive contributions to the single terrain
/// density field. Absence leaves the base surface unchanged.
/// </summary>
public sealed record BiomeTerrain3dDefinition(
    BiomeFloatingFormationDefinition? FloatingFormation = null);

public sealed class BiomeFloatingFormationDefinition
{
    public BiomeFloatingFormationDefinition(
        int minY,
        int maxY,
        uint horizontalScale,
        uint detailScale,
        float coverage,
        float roughness,
        float densityScale)
    {
        if (minY < 0 || maxY <= minY ||
            (long)maxY - minY > 512)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxY),
                "Floating formation needs non-negative minY, maxY > minY, and span <= 512.");
        }

        if (horizontalScale is < 2 or > 16_384 ||
            detailScale is < 2 or > 16_384 ||
            detailScale > horizontalScale)
        {
            throw new ArgumentOutOfRangeException(
                nameof(detailScale),
                "Floating noise scales must satisfy 2 <= detail <= horizontal <= 16384.");
        }

        if (!float.IsFinite(coverage) ||
            coverage is <= 0f or > 1f ||
            !float.IsFinite(roughness) ||
            roughness is < 0f or > 0.5f ||
            !float.IsFinite(densityScale) ||
            densityScale is <= 0f or > 512f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coverage),
                "Floating coverage, roughness, and density scale must be finite and within their authored bounds.");
        }

        MinY = minY;
        MaxY = maxY;
        HorizontalScale = horizontalScale;
        DetailScale = detailScale;
        Coverage = coverage;
        Roughness = roughness;
        DensityScale = densityScale;
    }

    public int MinY { get; }
    public int MaxY { get; }
    public uint HorizontalScale { get; }
    public uint DetailScale { get; }
    public float Coverage { get; }
    public float Roughness { get; }
    public float DensityScale { get; }
}

using System.Numerics;

namespace Asteria.Core.World;

public sealed class BiomeTintField
{
    private readonly BiomeField _biomes;
    private readonly SurfaceTerrainColumnCache? _columns;
    private readonly IReadOnlyDictionary<string, BiomeTintPaletteDefinition>
        _palettes;

    public BiomeTintField(
        BiomeField biomes,
        IEnumerable<BiomeDefinition> definitions,
        SurfaceTerrainColumnCache? columns = null)
    {
        _biomes =
            biomes ??
            throw new ArgumentNullException(
                nameof(biomes));
        ArgumentNullException.ThrowIfNull(
            definitions);
        _columns = columns;

        _palettes =
            definitions
                .OrderBy(
                    definition =>
                        definition.Id,
                    StringComparer.Ordinal)
                .ToDictionary(
                    definition =>
                        definition.Id,
                    definition =>
                        definition.Tints,
                    StringComparer.Ordinal);

        if (_palettes.Count == 0)
        {
            throw new ArgumentException(
                "Biome tint field requires at least one active biome.",
                nameof(definitions));
        }
    }

    public BiomeTintSampleGrid SampleGrid(
        int originX,
        int originZ,
        int width,
        int depth) =>
        new(
            _biomes.SampleGrid(
                originX,
                originZ,
                width,
                depth),
            _palettes);

    public BiomeTintSampleGrid SampleChunkGrid(int chunkX, int chunkZ)
    {
        if (_columns is null)
        {
            var (originX, _, originZ) = VoxelCoordinates.ChunkOrigin(
                new ChunkCoord(chunkX, 0, chunkZ));
            return SampleGrid(
                originX,
                originZ,
                Chunk.Size + 1,
                Chunk.Size + 1);
        }

        return new BiomeTintSampleGrid(
            _columns.SampleChunkBiomes(chunkX, chunkZ),
            _palettes);
    }
}

public sealed class BiomeTintSampleGrid
{
    private readonly BiomeSampleGrid _samples;
    private readonly IReadOnlyDictionary<string, BiomeTintPaletteDefinition>
        _palettes;

    internal BiomeTintSampleGrid(
        BiomeSampleGrid samples,
        IReadOnlyDictionary<string, BiomeTintPaletteDefinition> palettes)
    {
        _samples =
            samples ??
            throw new ArgumentNullException(
                nameof(samples));
        _palettes =
            palettes ??
            throw new ArgumentNullException(
                nameof(palettes));
    }

    public Vector3 Resolve(
        BlockTint tint,
        BlockPreviewColor fallback,
        int worldX,
        int worldZ)
    {
        if (tint == BlockTint.None)
        {
            return Vector3.One;
        }

        var localX =
            checked(
                worldX -
                _samples.OriginX);
        var localZ =
            checked(
                worldZ -
                _samples.OriginZ);

        if ((uint)localX >=
                (uint)_samples.Width ||
            (uint)localZ >=
                (uint)_samples.Depth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(worldX),
                "Biome tint sample lies outside the captured grid.");
        }

        var sample =
            _samples[
                localX,
                localZ];
        var blended = Vector3.Zero;
        var authoredWeight = 0f;

        foreach (var influence in sample.Influences)
        {
            if (!_palettes.TryGetValue(influence.BiomeId, out var palette) ||
                palette.For(tint) is not { } authored)
            {
                // An unspecified tint channel is not a color contribution.
                // Blending the missing preview color desaturates foliage.
                continue;
            }

            blended += ToLinear(authored) * influence.Weight;
            authoredWeight += influence.Weight;
        }

        // Normalize the weights of defined palette colors only.
        // Preserve the original fallback if no influence defines the tint.
        return authoredWeight > 0f
            ? ToSrgb(blended / authoredWeight)
            : ToSrgb(ToLinear(fallback));
    }

    private static Vector3 ToLinear(
        BlockPreviewColor color) =>
        new(
            SrgbToLinear(
                color.Red /
                255f),
            SrgbToLinear(
                color.Green /
                255f),
            SrgbToLinear(
                color.Blue /
                255f));

    private static Vector3 ToLinear(
        BiomeTintColor color) =>
        new(
            SrgbToLinear(
                color.Red /
                255f),
            SrgbToLinear(
                color.Green /
                255f),
            SrgbToLinear(
                color.Blue /
                255f));

    private static Vector3 ToSrgb(
        Vector3 linear) =>
        new(
            LinearToSrgb(
                linear.X),
            LinearToSrgb(
                linear.Y),
            LinearToSrgb(
                linear.Z));

    private static float SrgbToLinear(
        float value) =>
        value <= 0.04045f
            ? value / 12.92f
            : MathF.Pow(
                (value + 0.055f) /
                1.055f,
                2.4f);

    private static float LinearToSrgb(
        float value)
    {
        value =
            Math.Clamp(
                value,
                0f,
                1f);

        return value <= 0.0031308f
            ? value * 12.92f
            : 1.055f *
              MathF.Pow(
                  value,
                  1f / 2.4f) -
              0.055f;
    }
}

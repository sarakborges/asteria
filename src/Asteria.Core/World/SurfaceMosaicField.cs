namespace Asteria.Core.World;

/// <summary>One deterministic block-or-fluid surface choice at X/Z.</summary>
public readonly record struct SurfaceMosaicSample(
    BlockRuntimeId Block,
    FluidRuntimeId Fluid)
{
    public bool HasChoice => !Block.IsAir || !Fluid.IsNone;
    public bool IsFluid => !Fluid.IsNone;
}

/// <summary>
/// Immutable selection owner shared by generated-fluid and material fields.
/// Every result depends only on the seed, biome identity and world X/Z.
/// No biome-specific algorithm or separate material/fluid noise is involved.
/// </summary>
public sealed class SurfaceMosaicField
{
    private readonly ulong _seed;
    private readonly IReadOnlyDictionary<string, Rule> _rules;

    public SurfaceMosaicField(
        ulong seed,
        IEnumerable<BiomeDefinition> biomes,
        BlockRegistry blocks,
        FluidRegistry fluids)
    {
        ArgumentNullException.ThrowIfNull(biomes);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(fluids);
        _seed = seed;
        _rules = biomes
            .Where(biome => biome.Palette.SurfaceMosaic is not null)
            .OrderBy(biome => biome.Id, StringComparer.Ordinal)
            .ToDictionary(
                biome => biome.Id,
                biome => new Rule(biome, blocks, fluids),
                StringComparer.Ordinal);
    }

    public bool HasRules => _rules.Count > 0;

    public SurfaceMosaicSample Sample(
        BiomeSample biome, int worldX, int worldZ)
    {
        ArgumentNullException.ThrowIfNull(biome);
        return _rules.TryGetValue(biome.Primary, out var rule)
            ? rule.Sample(_seed, worldX, worldZ)
            : default;
    }

    private sealed class Rule
    {
        private readonly GenerationDomain _main;
        private readonly GenerationDomain _detail;
        private readonly uint _scale;
        private readonly uint _detailScale;
        private readonly double _detailStrength;
        private readonly SurfaceMosaicSample[] _choices;
        private readonly double[] _thresholds;

        public Rule(BiomeDefinition biome, BlockRegistry blocks, FluidRegistry fluids)
        {
            var mosaic = biome.Palette.SurfaceMosaic!;
            _scale = mosaic.Scale;
            _detailScale = mosaic.DetailScale;
            _detailStrength = mosaic.DetailStrength;
            _main = GenerationDomain.Named(
                $"material/surface-mosaic/main/v1/{biome.Id}");
            _detail = GenerationDomain.Named(
                $"material/surface-mosaic/detail/v1/{biome.Id}");
            _choices = new SurfaceMosaicSample[mosaic.Entries.Count];
            _thresholds = new double[mosaic.Entries.Count];
            var weight = mosaic.Entries.Sum(entry => (double)entry.Weight);
            var cumulative = 0d;
            for (var i = 0; i < _choices.Length; i++)
            {
                var entry = mosaic.Entries[i];
                _choices[i] = entry.Block is { } block
                    ? new SurfaceMosaicSample(blocks.GetId(block), FluidRuntimeId.None)
                    : new SurfaceMosaicSample(BlockRuntimeId.Air, fluids.GetId(entry.Fluid!));
                cumulative += entry.Weight / weight;
                _thresholds[i] = cumulative;
            }
            _thresholds[^1] = 1d;
        }

        public SurfaceMosaicSample Sample(ulong seed, int x, int z)
        {
            var broad = WorldGenerationEntropy.SmoothNoise2D(
                seed, _main, x, z, _scale);
            var detail = _detailStrength == 0d ? 0d :
                WorldGenerationEntropy.SmoothNoise2D(
                    seed, _detail, x, z, _detailScale);
            var unit = Math.Clamp(
                (broad + detail * _detailStrength + 1d) * 0.5d,
                0d, 0.999999999999d);
            for (var i = 0; i < _choices.Length; i++)
                if (unit < _thresholds[i])
                    return _choices[i];
            return _choices[^1];
        }
    }
}

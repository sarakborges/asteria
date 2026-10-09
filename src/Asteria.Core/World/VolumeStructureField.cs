namespace Asteria.Core.World;

/// <summary>
/// Immutable, bounded candidate resolution for authored formations anchored
/// to carved cave faces or additive island surfaces. Placement never writes
/// chunks or infers ownership from the biome's name.
/// </summary>
public sealed class VolumeStructureField
{
    private readonly ulong _seed;
    private readonly SurfaceTerrainField _terrain;
    private readonly VolumeBiomeField _volumes;
    private readonly BiomeSurfaceMaterialField _materials;
    private readonly Rule[] _rules;

    public VolumeStructureField(
        ulong seed,
        IEnumerable<BiomeDefinition> volumeBiomes,
        StructureRegistry structures,
        BlockRegistry blocks,
        SurfaceTerrainField terrain,
        VolumeBiomeField volumes,
        BiomeSurfaceMaterialField materials,
        bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(volumeBiomes);
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(blocks);
        _seed = seed;
        _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
        _volumes = volumes ?? throw new ArgumentNullException(nameof(volumes));
        _materials = materials ?? throw new ArgumentNullException(nameof(materials));

        // Disabled generation must not resolve optional authored structure
        // families. Flat/Void and spawnStructures=false also work with
        // StructureRegistry.Empty, just like surface structure generation.
        if (!enabled)
        {
            _rules = [];
            return;
        }

        var rules = new List<Rule>();
        foreach (var biome in volumeBiomes.OrderBy(b => b.Id, StringComparer.Ordinal))
        foreach (var authored in biome.VolumeStructures
                     .OrderBy(rule => rule.Structure, StringComparer.Ordinal))
        {
            var variants = structures.ResolveReference(authored.Structure)
                .Select(definition => CreateVariant(definition, blocks))
                .ToArray();
            if (variants.Length == 0)
                throw new ArgumentException(
                    $"Volume structure group {authored.Structure} has no variants.");

            // An author may not attach a downward-facing blueprint to
            // an upward-facing terrain support (or vice versa).
            var direction = authored.SupportSurface ==
                DecorationSupportSurface.Floor ? 1 : -1;
            if (variants.Any(v => Math.Sign(v.MinY) != direction ||
                                  Math.Sign(v.MaxY) != direction))
                throw new ArgumentException(
                    $"Volume structure {authored.Structure} blueprint direction does not match its supportSurface.");

            var maximumRadius = variants.Max(variant => variant.Radius);
            if (maximumRadius > 6 || maximumRadius * 2 >= authored.Spacing)
                throw new ArgumentException(
                    $"Volume structure {authored.Structure} needs a compact (<7) horizontal footprint.");
            rules.Add(new Rule(
                biome.Id,
                biome.VolumeLayout!.Placement,
                authored,
                variants,
                maximumRadius,
                variants.Min(variant => variant.MinY),
                variants.Max(variant => variant.MaxY),
                GenerationDomain.Named(
                    $"worldgen/volume-structure/{biome.Id}/{authored.Structure}/v1")));
        }
        _rules = rules.ToArray();
    }

    public bool HasRules => _rules.Length > 0;

    /// <summary>
    /// Returns world-space voxels only for candidates whose immutable
    /// template fits real density voids. All candidate decisions are made in
    /// world coordinates, including anchors outside the requested chunk.
    /// </summary>
    public IReadOnlyList<VolumeStructureVoxel> VoxelsForChunk(ChunkCoord coord)
    {
        if (_rules.Length == 0)
            return Array.Empty<VolumeStructureVoxel>();

        var origin = VoxelCoordinates.ChunkOrigin(coord);
        var top = (long)origin.Y + Chunk.Size - 1;
        var right = (long)origin.X + Chunk.Size - 1;
        var back = (long)origin.Z + Chunk.Size - 1;
        var results = new List<VolumeStructureVoxel>();

        foreach (var rule in _rules)
        {
            // Template offsets are relative to the supporting solid at Y.
            if (top < (long)rule.Definition.MinY + rule.MinOffsetY ||
                origin.Y > (long)rule.Definition.MaxY + rule.MaxOffsetY)
                continue;

            var spacing = rule.Definition.Spacing;
            var jitter = spacing / 4;
            var radius = rule.Radius + jitter;
            var firstX = Cell((long)origin.X - radius, spacing);
            var lastX = Cell(right + radius, spacing);
            var firstZ = Cell((long)origin.Z - radius, spacing);
            var lastZ = Cell(back + radius, spacing);
            for (var z = firstZ; z <= lastZ; z++)
            for (var x = firstX; x <= lastX; x++)
            {
                var hash = WorldGenerationEntropy.Sample2D(
                    _seed, rule.Domain, x, z);
                if (WorldGenerationEntropy.Unit(hash) >= rule.Definition.Chance)
                    continue;

                // Separate deterministic hashes give independent axes and
                // family variations without global/random state.
                var sampleX = WorldGenerationEntropy.Sample2D(
                    _seed, rule.Domain, x + 113, z);
                var sampleZ = WorldGenerationEntropy.Sample2D(
                    _seed, rule.Domain, x, z + 317);
                var wx = (long)x * spacing + spacing / 2 +
                         (long)(sampleX % (uint)(2 * jitter + 1)) - jitter;
                var wz = (long)z * spacing + spacing / 2 +
                         (long)(sampleZ % (uint)(2 * jitter + 1)) - jitter;
                if (wx is < int.MinValue or > int.MaxValue ||
                    wz is < int.MinValue or > int.MaxValue)
                    continue;
                if (wx + rule.Radius < origin.X ||
                    wx - rule.Radius > right ||
                    wz + rule.Radius < origin.Z ||
                    wz - rule.Radius > back)
                    continue;

                var variant = rule.Variants[
                    (int)(WorldGenerationEntropy.Sample2D(
                        _seed, rule.Domain, x + 509, z + 1021) %
                        (uint)rule.Variants.Length)];
                if (!TryAnchor(rule, variant, (int)wx, (int)wz,
                              out var supportY))
                    continue;

                foreach (var voxel in variant.Voxels)
                {
                    var worldX = (long)wx + voxel.X;
                    var worldY = (long)supportY + voxel.Y;
                    var worldZ = (long)wz + voxel.Z;
                    if (worldX < origin.X || worldX > right ||
                        worldY < origin.Y || worldY > top ||
                        worldZ < origin.Z || worldZ > back)
                        continue;
                    results.Add(new VolumeStructureVoxel(
                        (int)worldX, (int)worldY, (int)worldZ, voxel.Block));
                }
            }
        }
        return results;
    }

    private bool TryAnchor(Rule rule, Variant variant, int x, int z,
                           out int supportY)
    {
        supportY = 0;
        var direction = rule.Definition.SupportSurface ==
                        DecorationSupportSurface.Floor ? 1 : -1;
        var cavity = rule.Placement == VolumeBiomePlacement.CarvedVoid
            ? _volumes.SampleCave(x, z)
            : null;

        if (rule.Placement == VolumeBiomePlacement.CarvedVoid &&
            cavity?.Primary != rule.Biome)
            return false;

        for (var y = rule.Definition.MaxY; y >= rule.Definition.MinY; y--)
        {
            if ((long)y + variant.MinY <= 0 ||
                (long)y + variant.MaxY > int.MaxValue - 1)
                continue;

            var volume = cavity ?? _volumes.Sample(x, y, z);
            if (volume?.Primary != rule.Biome ||
                _terrain.DensityAt(x, y, z) < 0d ||
                _terrain.DensityAt(x, y + direction, z) >= 0d)
                continue;

            if (rule.Placement == VolumeBiomePlacement.CarvedVoid &&
                !_terrain.IsCaveVoidAt(x, y + direction, z))
                continue;

            var face = direction > 0
                ? BiomePaletteFace.Floor : BiomePaletteFace.Ceiling;
            var support = rule.Placement == VolumeBiomePlacement.Additive
                ? _materials.SampleColumn(volume, x, z).BlockAt(0)
                : _materials.VolumeBlockAt(volume, x, y, z, face);
            if (!variant.GroundBlocks.Contains(support))
                continue;

            var valid = true;
            foreach (var voxel in variant.Voxels)
            {
                var px = (long)x + voxel.X;
                var py = (long)y + voxel.Y;
                var pz = (long)z + voxel.Z;
                // Every bottom-most stem or arch foot must have real
                // terrain support, not just the central anchor column.
                var touchesGround = voxel.Y == direction;
                if (px is < int.MinValue or > int.MaxValue ||
                    pz is < int.MinValue or > int.MaxValue ||
                    py <= 0 || py > int.MaxValue ||
                    _terrain.DensityAt((int)px, (int)py, (int)pz) >= 0d ||
                    (touchesGround &&
                     _terrain.DensityAt((int)px, (int)py - direction,
                         (int)pz) < 0d) ||
                    (rule.Placement == VolumeBiomePlacement.CarvedVoid &&
                     !_terrain.IsCaveVoidAt((int)px, (int)py, (int)pz)))
                {
                    valid = false;
                    break;
                }
            }
            if (!valid)
                continue;

            supportY = y;
            return true;
        }
        return false;
    }

    private static Variant CreateVariant(
        StructureDefinition definition, BlockRegistry blocks)
    {
        if (definition.Rotation || definition.GroundAnchorY != 0 ||
            definition.FluidVoxels.Count > 0 ||
            definition.ClearVoxels.Count > 0 ||
            definition.Connectors.Count > 0 ||
            definition.Voxels.Count == 0 ||
            definition.Voxels.Any(v => v.Detail is not null ||
                v.Orientation != BlockOrientation.Y))
            throw new ArgumentException(
                $"Volume template {definition.Id} needs unrotated block-only voxels, groundAnchorY=0, and no connectors/details.");

        var radius = definition.Voxels.Max(v =>
            Math.Max(Math.Abs(v.X), Math.Abs(v.Z)));
        var minimumY = definition.Voxels.Min(v => v.Y);
        var maximumY = definition.Voxels.Max(v => v.Y);
        if (minimumY < -16 || maximumY > 16 || minimumY == 0 ||
            maximumY == 0 || minimumY * maximumY < 0)
            throw new ArgumentException(
                $"Volume template {definition.Id} must fit entirely on one side of its support face within 16 blocks.");

        var support = definition.Restrictions.GroundBlocks
            .Select(blocks.GetId).ToHashSet();
        if (support.Count == 0)
            throw new ArgumentException(
                $"Volume template {definition.Id} requires support materials.");

        return new Variant(
            definition.Voxels.Select(v =>
                new RelativeVoxel(v.X, v.Y, v.Z, blocks.GetId(v.Block)))
                .ToArray(),
            support,
            radius,
            minimumY,
            maximumY);
    }

    private static int Cell(long coordinate, int spacing) =>
        checked((int)Math.Floor(coordinate / (double)spacing));

    private sealed record RelativeVoxel(int X, int Y, int Z,
                                        BlockRuntimeId Block);

    private sealed record Variant(
        IReadOnlyList<RelativeVoxel> Voxels,
        IReadOnlySet<BlockRuntimeId> GroundBlocks,
        int Radius, int MinY, int MaxY);

    private sealed record Rule(
        string Biome,
        VolumeBiomePlacement Placement,
        BiomeVolumeStructureDefinition Definition,
        IReadOnlyList<Variant> Variants,
        int Radius, int MinOffsetY, int MaxOffsetY,
        GenerationDomain Domain);
}

public readonly record struct VolumeStructureVoxel(
    int X, int Y, int Z, BlockRuntimeId Block);

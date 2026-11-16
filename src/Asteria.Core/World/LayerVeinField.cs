namespace Asteria.Core.World;

/// <summary>
/// Deterministic chunk-independent sparse vein anchors. Ore always remains a
/// face layer on a stone-family host voxel, never a replacement block.
/// Anchors are sampled in world space, so deposits cross chunk boundaries
/// without seams or neighbor materialization.
/// </summary>
public sealed class LayerVeinField
{
    private static readonly BlockFace[] Faces = Enum.GetValues<BlockFace>();
    private readonly ulong _seed;
    private readonly Vein[] _veins;

    private sealed record Vein(
        string LayerId, LayerVeinDefinition Rule,
        GenerationDomain Domain, IReadOnlySet<BlockRuntimeId> Hosts,
        BlockSurfaceState Surface);

    public LayerVeinField(
        ulong seed, string dimension, BlockRegistry blocks,
        AttachedLayerRegistry layers)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(layers);
        BlockDefinition.ValidateId(dimension);
        _seed = seed;
        _veins = layers.Definitions
            .Where(layer => layer.Generation is { } generation &&
                StringComparer.Ordinal.Equals(generation.Dimension, dimension))
            .Select(layer =>
            {
                var rule = layer.Generation!;
                var hosts = rule.HostBlocks.Select(blocks.GetId).ToHashSet();
                foreach (var host in hosts)
                {
                    var definition = blocks.GetDefinition(host);
                    if (definition.Mining.Unbreakable ||
                        definition.Visual.Kind != BlockVisualKind.Geometry)
                        throw new ArgumentException(
                            $"Vein {layer.Id} cannot use host {definition.Id}.");
                }
                var surface = new BlockSurfaceState(null,
                    Faces.Select(face => new AttachedBlockLayer(face, layer.Id)));
                return new Vein(layer.Id, rule,
                    GenerationDomain.Named($"layer-vein/{dimension}/{layer.Id}"),
                    hosts, surface);
            })
            .ToArray();
    }

    public bool HasVeins => _veins.Length > 0;

    public void Apply(Chunk chunk, ChunkCoord coord)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (_veins.Length == 0) return;
        var (originX, originY, originZ) = VoxelCoordinates.ChunkOrigin(coord);
        foreach (var vein in _veins)
        {
            var rule = vein.Rule;
            if (originY > rule.MaxY || originY + Chunk.Size - 1 < rule.MinY)
                continue;

            // Include anchors in adjacent cells, including negative world
            // coordinates. Each anchor's decision depends only on world seed.
            var spacing = rule.Spacing;
            var radius = rule.Radius;
            var firstX = FloorDiv(originX - radius - spacing + 1, spacing);
            var lastX = FloorDiv(originX + Chunk.Size - 1 + radius, spacing);
            var firstY = FloorDiv(originY - radius - spacing + 1, spacing);
            var lastY = FloorDiv(originY + Chunk.Size - 1 + radius, spacing);
            var firstZ = FloorDiv(originZ - radius - spacing + 1, spacing);
            var lastZ = FloorDiv(originZ + Chunk.Size - 1 + radius, spacing);

            for (var gz = firstZ; gz <= lastZ; gz++)
            for (var gy = firstY; gy <= lastY; gy++)
            for (var gx = firstX; gx <= lastX; gx++)
            {
                var random = WorldGenerationEntropy.Sample3D(
                    _seed, vein.Domain, gx, gy, gz);
                if ((random >> 48) / 65536d >= rule.Chance) continue;
                var cx = gx * spacing + (int)((random & 0xffffUL) % (uint)spacing);
                var cy = gy * spacing + (int)(((random >> 16) & 0xffffUL) % (uint)spacing);
                var cz = gz * spacing + (int)(((random >> 32) & 0xffffUL) % (uint)spacing);
                if (cy < rule.MinY || cy > rule.MaxY) continue;

                var lowX = Math.Max(0, cx - radius - originX);
                var highX = Math.Min(Chunk.Size - 1, cx + radius - originX);
                var lowY = Math.Max(0, cy - radius - originY);
                var highY = Math.Min(Chunk.Size - 1, cy + radius - originY);
                var lowZ = Math.Max(0, cz - radius - originZ);
                var highZ = Math.Min(Chunk.Size - 1, cz + radius - originZ);
                if (lowX > highX || lowY > highY || lowZ > highZ) continue;

                var square = radius * radius;
                for (var z = lowZ; z <= highZ; z++)
                for (var y = lowY; y <= highY; y++)
                for (var x = lowX; x <= highX; x++)
                {
                    var wy = originY + y;
                    if (wy < rule.MinY || wy > rule.MaxY) continue;
                    var dx = originX + x - cx;
                    var dy = wy - cy;
                    var dz = originZ + z - cz;
                    if (dx * dx + dy * dy + dz * dz > square) continue;
                    var cell = chunk.GetCell(x, y, z);
                    if (cell.IsEmpty || !vein.Hosts.Contains(cell.Block))
                        continue;
                    // Never overwrite an authored structure/another deposit.
                    var previous = chunk.GetSurfaceState(x, y, z);
                    if (previous.Layers.Count > 0) continue;
                    if (previous.IsEmpty)
                        chunk.SetSurfaceState(x, y, z, vein.Surface);
                    else
                        chunk.SetSurfaceState(x, y, z,
                            new BlockSurfaceState(previous.DyeId,
                                vein.Surface.Layers, previous.MetaTags));
                }
            }
        }
    }

    private static int FloorDiv(int value, int size) =>
        value >= 0 ? value / size : (value + 1) / size - 1;
}

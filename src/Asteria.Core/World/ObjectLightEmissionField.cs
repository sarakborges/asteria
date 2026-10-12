using Asteria.Core.Content;

namespace Asteria.Core.World;

/// <summary>
/// Immutable, sparse lighting view derived from authoritative attached Objects.
/// Indexed once per lighting pass; no duplicate mutable light-source owner.
/// </summary>
public sealed class ObjectLightEmissionField
{
    private readonly Dictionary<WorldVoxelCoord, BlockLightEmission> _sources = [];

    private ObjectLightEmissionField() { }

    public static ObjectLightEmissionField Capture(
        VoxelWorld world, PackContentRegistry<ObjectDefinition>? definitions)
    {
        ArgumentNullException.ThrowIfNull(world);
        var field = new ObjectLightEmissionField();
        if (definitions is null) return field;

        foreach (var coord in world.LoadedChunkCoords
                     .OrderBy(c => c.Y).ThenBy(c => c.Z).ThenBy(c => c.X))
        {
            var chunk = world.GetChunk(coord);
            if (chunk.ObjectCount == 0) continue;
            var origin = VoxelCoordinates.ChunkOrigin(coord);
            chunk.VisitObjectCells((x, y, z, obj) =>
            {
                if (!definitions.TryGet(obj.Id, out var definition) ||
                    definition is null || definition.LightEmission.IsDark)
                    return;

                var support = new WorldVoxelCoord(
                    origin.X + x, origin.Y + y, origin.Z + z);
                var target = Target(support, obj.Face);
                if (target.Y < 0 || !world.IsLoadedAt(target) ||
                    !world.GetCellOrEmpty(target).IsEmpty)
                    return;
                field.Add(target, definition.LightEmission);
            });
        }

        return field;
    }

    /// <summary>Initial lighting of one independent materialized chunk.
    /// Cross-seam emitters are reconciled by loaded-world lighting updates.</summary>
    public static ObjectLightEmissionField Capture(
        Chunk chunk, PackContentRegistry<ObjectDefinition>? definitions)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        var field = new ObjectLightEmissionField();
        if (definitions is null || chunk.ObjectCount == 0) return field;
        chunk.VisitObjectCells((x, y, z, obj) =>
        {
            if (!definitions.TryGet(obj.Id, out var definition) ||
                definition is null || definition.LightEmission.IsDark)
                return;
            var target = Target(new WorldVoxelCoord(x, y, z), obj.Face);
            if (!Chunk.Contains(target.X, target.Y, target.Z) ||
                !chunk.GetCell(target.X, target.Y, target.Z).IsEmpty)
                return;
            field.Add(target, definition.LightEmission);
        });
        return field;
    }

    private void Add(WorldVoxelCoord target, BlockLightEmission emission)
    {
        if (_sources.TryGetValue(target, out var previous))
            emission = Combine(previous, emission);
        _sources[target] = emission;
    }

    public BlockLightEmission At(WorldVoxelCoord position) =>
        _sources.TryGetValue(position, out var light) ? light : default;

    public static WorldVoxelCoord Target(WorldVoxelCoord support, BlockFace face) =>
        face switch
        {
            BlockFace.Top => support + (0, 1, 0),
            BlockFace.Bottom => support + (0, -1, 0),
            BlockFace.Right => support + (1, 0, 0),
            BlockFace.Left => support + (-1, 0, 0),
            BlockFace.Front => support + (0, 0, 1),
            BlockFace.Back => support + (0, 0, -1),
            _ => throw new ArgumentOutOfRangeException(nameof(face))
        };

    public static BlockLightEmission Combine(BlockLightEmission a, BlockLightEmission b) =>
        new(Math.Max(a.Red, b.Red), Math.Max(a.Green, b.Green),
            Math.Max(a.Blue, b.Blue));
}

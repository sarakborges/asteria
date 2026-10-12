using System.Numerics;
using Asteria.Core.Content;

namespace Asteria.Core.World;

/// <summary>
/// Deterministic, bounded resident-sapling growth. WorldTick is already
/// persistent per Sphere, and successful replacements are ordinary chunk
/// mutations. No separate save timer, mutable world shadow, or unbounded
/// sapling registry is maintained.
/// </summary>
public sealed class SaplingGrowthRuntime
{
    public const ulong CheckPeriodTicks = 200;
    private const int MaximumCandidatesPerCheck = 16;

    private readonly VoxelWorld _world;
    private readonly VoxelMutationRuntime _mutations;
    private readonly BlockRegistry _blocks;
    private readonly IReadOnlyDictionary<string, StructureDefinition[]> _trees;
    private readonly ulong _seed;
    private ulong _previousCheck;
    private int _nextChunk;

    public SaplingGrowthRuntime(
        VoxelWorld world,
        VoxelMutationRuntime mutations,
        BlockRegistry blocks,
        PackContentRegistry<ObjectDefinition> objects,
        StructureRegistry structures,
        ulong seed, ulong initialTick = 0)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _mutations = mutations ?? throw new ArgumentNullException(nameof(mutations));
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(structures);
        _seed = seed;
        _previousCheck = initialTick / CheckPeriodTicks;

        var trees = new Dictionary<string, StructureDefinition[]>(StringComparer.Ordinal);
        foreach (var obj in objects.Definitions)
        {
            if (obj.GrowthStructure is not { } reference)
                continue;
            if (!structures.ResolvesReference(reference))
                throw new ArgumentException(
                    $"Growable Object {obj.Id} references an unknown mature Structure {reference}.");
            var variants = structures.ResolveReference(reference).ToArray();
            foreach (var tree in variants)
            {
                if (tree.Voxels.Count == 0 ||
                    tree.FluidVoxels.Count != 0 || tree.ClearVoxels.Count != 0 ||
                    tree.Objects.Count != 0 || tree.Connectors.Count != 0 ||
                    !tree.Voxels.Any(v => v.X == 0 &&
                                          v.Y == 1 && v.Z == 0))
                    throw new ArgumentException(
                        $"Growable Object {obj.Id} requires a self-contained trunk-rooted Structure: {tree.Id}.");
            }
            trees.Add(obj.Id, variants);
        }
        _trees = trees;
    }

    /// <summary>At most one loaded Object chunk and one tree per logical
    /// check. No scan or physical edit occurs while the world is paused or
    /// has no active gameplay player.</summary>
    public bool Advance(ulong worldTick, WorldAabb? playerBounds = null,
        bool enabled = true)
    {
        var check = worldTick / CheckPeriodTicks;
        if (check <= _previousCheck || !enabled)
            return false;
        _previousCheck = check;
        if (_trees.Count == 0) return false;

        var chunks = _world.ResidentObjectChunkCoords
            .OrderBy(c => c.X).ThenBy(c => c.Y).ThenBy(c => c.Z)
            .ToArray();
        if (chunks.Length == 0) return false;
        if (_nextChunk >= chunks.Length) _nextChunk = 0;
        var coord = chunks[_nextChunk++];
        if (!_world.TryGetChunk(coord, out var chunk)) return false;

        var candidates = new List<(WorldVoxelCoord Support, ObjectCell Object)>();
        var (originX, originY, originZ) = VoxelCoordinates.ChunkOrigin(coord);
        chunk.VisitObjectCells((x, y, z, obj) =>
        {
            if (_trees.ContainsKey(obj.Id) && obj.Face == BlockFace.Top)
                candidates.Add((new WorldVoxelCoord(
                    originX + x, originY + y, originZ + z), obj));
        });
        if (candidates.Count == 0) return false;

        // Rotate the candidate window; dense nurseries cannot starve
        // saplings beyond the first 16 positions in their host chunk.
        var start = (int)(check % (ulong)candidates.Count);
        for (var i = 0; i < Math.Min(candidates.Count, MaximumCandidatesPerCheck); i++)
        {
            var candidate = candidates[(start + i) % candidates.Count];
            var hash = StableHash(_seed, candidate.Support,
                candidate.Object.Id, check);
            if ((hash & 7UL) != 0) continue;
            var variants = _trees[candidate.Object.Id];
            var variant = variants[(int)((hash >> 8) % (ulong)variants.Length)];
            if (TryGrow(candidate.Support, candidate.Object, variant, playerBounds))
                return true;
        }
        return false;
    }

    private bool TryGrow(WorldVoxelCoord support, ObjectCell sapling,
        StructureDefinition tree, WorldAabb? playerBounds)
    {
        if (!_world.TryGetObjects(support, out var current) ||
            !current.Contains(sapling) ||
            !_world.TryGetCell(support, out var supportCell) ||
            supportCell.IsEmpty ||
            !_blocks.GetDefinition(supportCell.Block).IsCollidable)
            return false;

        var edits = new List<VoxelStructureChange>(tree.Voxels.Count);
        foreach (var voxel in tree.Voxels)
        {
            // Existing authored y=0 terrain markers do not replace the
            // player's actual soil; the sapling already identifies the root.
            if (voxel.Y <= 0) continue;
            // StructureDefinitionJson already subtracts the authored anchor;
            // each voxel is a local offset relative to the root.
            var x = (long)support.X + voxel.X;
            var y = (long)support.Y + voxel.Y;
            var z = (long)support.Z + voxel.Z;
            if (x < int.MinValue || x > int.MaxValue ||
                y < 0 || y > int.MaxValue ||
                z < int.MinValue || z > int.MaxValue)
                return false;
            var position = new WorldVoxelCoord((int)x, (int)y, (int)z);
            if (!_world.TryGetCell(position, out var occupant) ||
                !occupant.IsEmpty ||
                !_world.GetFluidOrEmpty(position).IsEmpty)
                return false;
            if (playerBounds is { } player &&
                position.X < player.Maximum.X && position.X + 1 > player.Minimum.X &&
                position.Y < player.Maximum.Y && position.Y + 1 > player.Minimum.Y &&
                position.Z < player.Maximum.Z && position.Z + 1 > player.Minimum.Z)
                return false;

            // Never bury another attached Object at its support face.
            var beneath = position + (0, -1, 0);
            if (beneath != support &&
                _world.TryGetObjects(beneath, out var attachments) &&
                attachments.Any(obj => obj.Face == BlockFace.Top))
                return false;

            if (!_blocks.TryGetId(voxel.Block, out var block))
                throw new InvalidOperationException(
                    $"Mature Structure {tree.Id} references unknown block {voxel.Block}.");
            var detail = voxel.Detail;
            var cell = new VoxelCell(block,
                textureRotation: detail?.TextureRotation ?? TextureRotation.Degrees0,
                orientation: voxel.Orientation,
                facing: detail?.Facing ?? HorizontalFacing.South,
                state: detail?.State ?? (ushort)0);
            var state = detail is null
                ? BlockStateSnapshot.FromCell(cell)
                : new BlockStateSnapshot(cell, detail.Mask, detail.Surface);
            edits.Add(new VoxelStructureChange(position, state, FluidCell.Empty));
        }
        return edits.Count != 0 &&
            _mutations.ApplyStructureChanges(edits,
                removeObjects: [(support, sapling)]);
    }

    private static ulong StableHash(
        ulong seed, WorldVoxelCoord support, string id, ulong tick)
    {
        var hash = unchecked((seed ^ tick) * 1099511628211UL);
        foreach (var character in id)
            hash = unchecked((hash ^ character) * 1099511628211UL);
        hash = unchecked((hash ^ (uint)support.X) * 1099511628211UL);
        hash = unchecked((hash ^ (uint)support.Y) * 1099511628211UL);
        hash = unchecked((hash ^ (uint)support.Z) * 1099511628211UL);
        return hash ^ (hash >> 29);
    }
}

namespace Asteria.Core.World;

public enum ManualStructurePlacementResult : byte
{
    Placed,
    UnknownReference,
    InvalidPlacement,
    NonResident,
    ProtectedVoxel,
    PlayerCollision,
}

/// <summary>
/// Stages an entire authored structure or connected/set expansion before
/// asking the canonical voxel mutation owner to publish the resulting changes.
/// Rejection changes no world cells and enqueues no gameplay work.
/// </summary>
public sealed class ManualStructurePlacementRuntime
{
    private readonly BiomeWorldGenerator _generator;
    private readonly VoxelWorld _world;
    private readonly VoxelMutationRuntime _mutations;
    private readonly BlockRegistry _blocks;
    private readonly StructureRegistry _structures;
    private readonly StructureSetRegistry _sets;
    private readonly ManualStructurePlacementLedger _committed;

    public ManualStructurePlacementRuntime(
        BiomeWorldGenerator generator, VoxelWorld world,
        VoxelMutationRuntime mutations, BlockRegistry blocks,
        StructureRegistry structures, StructureSetRegistry sets,
        ManualStructurePlacementLedger committed)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _mutations = mutations ?? throw new ArgumentNullException(nameof(mutations));
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _structures = structures ?? throw new ArgumentNullException(nameof(structures));
        _sets = sets ?? throw new ArgumentNullException(nameof(sets));
        _committed = committed ??
            throw new ArgumentNullException(nameof(committed));
    }

    public ManualStructurePlacementResult TryPlace(
        string reference, int? variation, int anchorX, int anchorZ,
        WorldAabb playerBounds)
    {
        if (!_structures.ResolvesReference(reference) &&
            !_sets.ResolvesReference(reference))
            return ManualStructurePlacementResult.UnknownReference;

        if (!_generator.TryPrepareManualStructure(
                reference, variation, anchorX, anchorZ, _committed, out var plan) ||
            plan is null)
            return ManualStructurePlacementResult.InvalidPlacement;

        var placements = plan.Pieces;

        // Insertion order represents worldgen's authored piece precedence.
        // A later overlapping piece deterministically wins at that voxel.
        var staged = new Dictionary<WorldVoxelCoord, VoxelStructureChange>();
        var claimed = new HashSet<WorldVoxelCoord>();
        foreach (var placement in placements)
        {
            foreach (var tuple in placement.PayloadPositions())
            {
                if (tuple.Y < 0)
                    return ManualStructurePlacementResult.InvalidPlacement;

                var at = new WorldVoxelCoord(tuple.X, tuple.Y, tuple.Z);
                if (!_world.IsLoadedAt(at))
                    return ManualStructurePlacementResult.NonResident;

                var cell = _world.GetCellOrEmpty(at);
                var fluid = _world.GetFluidOrEmpty(at);
                if (!cell.IsEmpty &&
                    _blocks.GetDefinition(cell.Block).Mining.Unbreakable)
                    return ManualStructurePlacementResult.ProtectedVoxel;

                if (placement.Generation.FluidPolicy ==
                        StructureFluidPolicy.Forbid && !fluid.IsEmpty)
                    return ManualStructurePlacementResult.InvalidPlacement;
                if (placement.Generation.ReplacePolicy ==
                        StructureReplacePolicy.AirOnly &&
                    (!cell.IsEmpty || !fluid.IsEmpty))
                    return ManualStructurePlacementResult.InvalidPlacement;

                if (!staged.ContainsKey(at))
                    staged.Add(at, new VoxelStructureChange(
                        at, cell.IsEmpty ? null :
                            BlockStateSnapshot.Capture(_world, at, cell),
                        fluid));
            }

            foreach (var voxel in placement.ClearVoxels)
            {
                var at = new WorldVoxelCoord(voxel.X, voxel.Y, voxel.Z);
                if (placement.Generation.ReplacePolicy != StructureReplacePolicy.Any &&
                    !claimed.Add(at))
                    continue;
                claimed.Add(at);
                staged[at] = new VoxelStructureChange(at, null, FluidCell.Empty);
            }

            foreach (var voxel in placement.Voxels)
            {
                var at = new WorldVoxelCoord(voxel.X, voxel.Y, voxel.Z);
                if (placement.Generation.ReplacePolicy != StructureReplacePolicy.Any &&
                    !claimed.Add(at))
                    continue;
                if (Intersects(playerBounds, voxel.X, voxel.Y, voxel.Z))
                    return ManualStructurePlacementResult.PlayerCollision;
                claimed.Add(at);
                staged[at] = new VoxelStructureChange(
                    at, new BlockStateSnapshot(
                        voxel.Cell, voxel.Mask, voxel.Surface), FluidCell.Empty);
            }

            foreach (var voxel in placement.FluidVoxels)
            {
                var at = new WorldVoxelCoord(voxel.X, voxel.Y, voxel.Z);
                if (placement.Generation.ReplacePolicy != StructureReplacePolicy.Any &&
                    !claimed.Add(at))
                    continue;
                if (Intersects(playerBounds, voxel.X, voxel.Y, voxel.Z))
                    return ManualStructurePlacementResult.PlayerCollision;
                claimed.Add(at);
                staged[at] = new VoxelStructureChange(at, null, voxel.Fluid);
            }
        }

        // All checks are complete before publishing any mutation/side effect.
        // Canonical mutation owner does the final resident-state validation.
        if (!_mutations.ApplyStructureChanges(staged.Values
                .OrderBy(value => value.Position.X)
                .ThenBy(value => value.Position.Z)
                .ThenBy(value => value.Position.Y)
                .ToArray()))
            return ManualStructurePlacementResult.NonResident;

        _committed.RecordCommitted(plan.Footprint);
        return ManualStructurePlacementResult.Placed;
    }

    private static bool Intersects(
        WorldAabb bounds, int x, int y, int z) =>
        x < bounds.Maximum.X && x + 1f > bounds.Minimum.X &&
        y < bounds.Maximum.Y && y + 1f > bounds.Minimum.Y &&
        z < bounds.Maximum.Z && z + 1f > bounds.Minimum.Z;
}

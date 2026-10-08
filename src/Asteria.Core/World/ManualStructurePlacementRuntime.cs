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
/// Applies prevalidated authored structure operations to resident voxels
/// through the one mutation owner. Structure geometry/ground/biome/generated
/// conflicts are decided by SurfaceStructureField; this capability owns only
/// authoritative loaded-world mutation validation and publication.
/// </summary>
public sealed class ManualStructurePlacementRuntime
{
    public const int MaximumPayloadCount = 4096;
    public const int MaximumHorizontalSpan = 64;

    private readonly BiomeWorldGenerator _generator;
    private readonly VoxelWorld _world;
    private readonly VoxelMutationRuntime _mutations;
    private readonly BlockRegistry _blocks;
    private readonly StructureRegistry _structures;

    public ManualStructurePlacementRuntime(
        BiomeWorldGenerator generator, VoxelWorld world,
        VoxelMutationRuntime mutations, BlockRegistry blocks,
        StructureRegistry structures)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _mutations = mutations ?? throw new ArgumentNullException(nameof(mutations));
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _structures = structures ?? throw new ArgumentNullException(nameof(structures));
    }

    public ManualStructurePlacementResult TryPlace(
        string reference, int? variation, int anchorX, int anchorZ,
        WorldAabb playerBounds)
    {
        if (!_structures.ResolvesReference(reference))
            return ManualStructurePlacementResult.UnknownReference;

        if (!_generator.TryPrepareManualStructure(
                reference, variation, anchorX, anchorZ, out var placement))
            return ManualStructurePlacementResult.InvalidPlacement;

        foreach (var position in placement.PayloadPositions())
        {
            if (position.Y < 0 ||
                !_world.IsLoadedAt(new WorldVoxelCoord(
                    position.X, position.Y, position.Z)))
                return ManualStructurePlacementResult.NonResident;

            var at = new WorldVoxelCoord(position.X, position.Y, position.Z);
            var cell = _world.GetCellOrEmpty(at);
            var fluid = _world.GetFluidOrEmpty(at);

            if (!cell.IsEmpty && _blocks.GetDefinition(cell.Block).Mining.Unbreakable)
                return ManualStructurePlacementResult.ProtectedVoxel;

            if (placement.Generation.ReplacePolicy == StructureReplacePolicy.AirOnly &&
                (!cell.IsEmpty || !fluid.IsEmpty))
                return ManualStructurePlacementResult.InvalidPlacement;
        }

        // Reject rather than displace the player into unknown/unloaded space.
        foreach (var voxel in placement.Voxels)
        {
            if (Intersects(playerBounds, voxel.X, voxel.Y, voxel.Z))
                return ManualStructurePlacementResult.PlayerCollision;
        }
        foreach (var voxel in placement.FluidVoxels)
        {
            if (Intersects(playerBounds, voxel.X, voxel.Y, voxel.Z))
                return ManualStructurePlacementResult.PlayerCollision;
        }

        // Single-threaded authoritative application after complete validation.
        // No world work or async dispatch may interleave inside this method.
        foreach (var voxel in placement.ClearVoxels)
        {
            var at = new WorldVoxelCoord(voxel.X, voxel.Y, voxel.Z);
            Clear(at);
        }

        foreach (var voxel in placement.Voxels)
        {
            var at = new WorldVoxelCoord(voxel.X, voxel.Y, voxel.Z);
            var snapshot = new BlockStateSnapshot(
                voxel.Cell, voxel.Mask, voxel.Surface);
            if (!_mutations.SetBlockStateAt(at, snapshot, out _) &&
                !SameBlockState(at, snapshot))
                throw new InvalidOperationException(
                    $"Validated structure block mutation failed at {at}.");
        }

        foreach (var voxel in placement.FluidVoxels)
        {
            var at = new WorldVoxelCoord(voxel.X, voxel.Y, voxel.Z);
            if (!_world.GetCellOrEmpty(at).IsEmpty)
                Clear(at);
            if (_world.GetFluidOrEmpty(at) != voxel.Fluid &&
                !_mutations.SetFluidAt(at, voxel.Fluid, out _))
                throw new InvalidOperationException(
                    $"Validated structure fluid mutation failed at {at}.");
        }

        return ManualStructurePlacementResult.Placed;
    }

    private void Clear(WorldVoxelCoord position)
    {
        if (!_world.GetCellOrEmpty(position).IsEmpty &&
            !_mutations.SetCellAt(position, VoxelCell.Empty, out _))
            throw new InvalidOperationException(
                $"Validated structure clear failed at {position}.");

        if (!_world.GetFluidOrEmpty(position).IsEmpty &&
            !_mutations.SetFluidAt(position, FluidCell.Empty, out _))
            throw new InvalidOperationException(
                $"Validated structure fluid clear failed at {position}.");
    }

    private bool SameBlockState(
        WorldVoxelCoord position, BlockStateSnapshot wanted)
    {
        var cell = _world.GetCellOrEmpty(position);
        if (cell.IsEmpty) return false;
        var state = BlockStateSnapshot.Capture(_world, position, cell);
        return state.Cell == wanted.Cell &&
               state.MicroblockMask == wanted.MicroblockMask &&
               state.SurfaceState == wanted.SurfaceState;
    }

    private static bool Intersects(
        WorldAabb bounds, int x, int y, int z) =>
        x < bounds.Maximum.X && x + 1f > bounds.Minimum.X &&
        y < bounds.Maximum.Y && y + 1f > bounds.Minimum.Y &&
        z < bounds.Maximum.Z && z + 1f > bounds.Minimum.Z;
}

using Asteria.Core.Content;

namespace Asteria.Core.World;

/// <summary>The single non-mutating placement rule used by preview and gameplay.</summary>
public readonly record struct ObjectPlacementDecision(
    bool Accepted, WorldVoxelCoord Support, ObjectCell Object);

public static class ObjectPlacementResolver
{
    public static ObjectPlacementDecision Resolve(
        VoxelWorld world, BlockRegistry blocks,
        PackContentRegistry<ObjectDefinition> definitions,
        InventoryStack? held, VoxelWorldHit? hit,
        FluidRegistry? fluids = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(definitions);
        if (held is not { Kind: InventoryEntryKind.Object } ||
            hit is not { } supportHit ||
            !definitions.TryGet(held.Id, out var definition) ||
            definition is null ||
            !VoxelHitFace.TryResolve(supportHit, out var face) ||
            !definition.Supports(face) ||
            !world.TryGetCell(supportHit.Voxel, out var support))
            return default;

        if (definition.SupportFluid is { } fluidId)
        {
            if (face != BlockFace.Top || !support.IsEmpty ||
                fluids is null ||
                !fluids.TryGetId(fluidId, out var required) ||
                !world.TryGetFluid(supportHit.Voxel, out var fluid) ||
                fluid.Fluid != required || fluid.Level != FluidCell.MaxLevel)
                return default;
        }
        else if (support.IsEmpty || !blocks.GetDefinition(support.Block).IsCollidable)
            return default;

        var target = supportHit.Voxel +
            (supportHit.NormalX, supportHit.NormalY, supportHit.NormalZ);
        if (target.Y < 0 || !world.TryGetCell(target, out var occupying) ||
            !occupying.IsEmpty)
            return default;

        // Fluid-filled target space remains valid; attaching the Object does
        // not alter the source, only the occupied support's Object list.
        return new ObjectPlacementDecision(
            true, supportHit.Voxel, new ObjectCell(definition.Id, face));
    }
}

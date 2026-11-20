namespace Asteria.Core.World;

public static class VoxelLightingMedium
{
    public static byte Dampening(
        Chunk chunk,
        BlockRegistry blocks,
        FluidRegistry fluids,
        int x,
        int y,
        int z)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(fluids);

        var cell =
            chunk.GetCell(x, y, z);
        var fluid =
            chunk.GetFluid(x, y, z);

        return Dampening(
            chunk,
            blocks,
            fluids,
            x,
            y,
            z,
            cell,
            fluid);
    }

    public static byte Dampening(
        Chunk chunk,
        BlockRegistry blocks,
        FluidRegistry fluids,
        int x,
        int y,
        int z,
        VoxelCell cell,
        FluidCell fluid)
    {
        var block =
            BlockDampening(
                chunk,
                blocks,
                x,
                y,
                z,
                cell);
        var liquid =
            FluidDampening(
                fluids,
                fluid);

        // A block and a fluid represent two media occupying the same voxel
        // state. Match Mineclone's rule: the strongest medium wins instead of
        // double-charging attenuation if stale/transition state overlaps.
        return Math.Max(block, liquid);
    }

    public static BlockLightEmission Emission(
        BlockRegistry blocks,
        FluidRegistry fluids,
        VoxelCell cell,
        FluidCell fluid,
        BlockLightEmission objectEmission = default)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(fluids);

        var block =
            cell.IsEmpty
                ? default
                : blocks
                    .GetDefinition(cell.Block)
                    .LightEmission;
        var liquid =
            FluidEmission(
                fluids,
                fluid);

        return new BlockLightEmission(
            Math.Max(
                Math.Max(block.Red, liquid.Red),
                objectEmission.Red),
            Math.Max(
                Math.Max(block.Green, liquid.Green),
                objectEmission.Green),
            Math.Max(
                Math.Max(block.Blue, liquid.Blue),
                objectEmission.Blue));
    }

    public static BlockLightEmission FluidEmission(
        FluidRegistry fluids,
        FluidCell fluid)
    {
        ArgumentNullException.ThrowIfNull(fluids);

        if (fluid.IsEmpty)
        {
            return default;
        }

        return fluids.GetLightEmissionAtLevel(
            fluid.Fluid, fluid.Level);
    }

    public static BlockLightEmission ScaleFluidEmission(
        BlockLightEmission fullEmission,
        byte level) =>
        FluidDefinition.ScaleLightEmission(fullEmission, level);

    public static byte FluidDampening(
        FluidRegistry fluids,
        FluidCell fluid)
    {
        ArgumentNullException.ThrowIfNull(fluids);

        if (fluid.IsEmpty)
        {
            return 0;
        }

        return fluids.GetLightDampeningAtLevel(
            fluid.Fluid, fluid.Level);
    }

    public static byte ScaleFluidDampening(
        byte fullDampening,
        byte level) =>
        FluidDefinition.ScaleLightDampening(fullDampening, level);

    private static byte BlockDampening(
        Chunk chunk,
        BlockRegistry blocks,
        int x,
        int y,
        int z,
        VoxelCell cell)
    {
        if (cell.IsEmpty)
        {
            return 0;
        }

        var definition =
            blocks.GetDefinition(cell.Block);
        var fullDampening =
            definition.LightDampening;

        if (fullDampening == 0 ||
            definition.Visual.Kind == BlockVisualKind.CrossedSprite)
        {
            return 0;
        }

        // Standard cubes occupy the complete voxel. Skip shape/occupancy
        // calculation entirely in the dominant terrain-block case.
        if (!cell.HasMicroblockGeometry &&
            definition.Shape.Kind == BlockShapeKind.Cube)
        {
            return fullDampening;
        }

        var occupancy =
            VoxelMeshLighting.OccupancyFraction(
                chunk,
                blocks,
                x,
                y,
                z,
                cell,
                definition);

        return (byte)Math.Clamp(
            (int)MathF.Ceiling(
                fullDampening * occupancy),
            0,
            VoxelLight.MaxLevel);
    }
}

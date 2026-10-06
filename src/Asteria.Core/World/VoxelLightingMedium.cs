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

    public static byte FluidDampening(
        FluidRegistry fluids,
        FluidCell fluid)
    {
        ArgumentNullException.ThrowIfNull(fluids);

        if (fluid.IsEmpty)
        {
            return 0;
        }

        var full =
            fluids
                .GetDefinition(fluid.Fluid)
                .LightDampening;

        return ScaleFluidDampening(
            full,
            fluid.Level);
    }

    public static byte ScaleFluidDampening(
        byte fullDampening,
        byte level)
    {
        if (fullDampening == 0 ||
            level == 0)
        {
            return 0;
        }

        var clampedDampening =
            Math.Min(
                fullDampening,
                VoxelLight.MaxLevel);
        var clampedLevel =
            Math.Clamp(
                level,
                FluidCell.MinLevel,
                FluidCell.MaxLevel);

        var numerator =
            clampedDampening *
            clampedLevel;

        return checked(
            (byte)Math.Min(
                VoxelLight.MaxLevel,
                (numerator +
                 FluidCell.MaxLevel - 1) /
                FluidCell.MaxLevel));
    }

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

        if (fullDampening == 0)
        {
            return 0;
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

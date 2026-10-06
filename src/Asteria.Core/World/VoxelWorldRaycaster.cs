using System.Numerics;

namespace Asteria.Core.World;

public readonly record struct VoxelWorldHit(
    WorldVoxelCoord Voxel,
    int NormalX,
    int NormalY,
    int NormalZ)
{
    public bool HasSurfaceNormal =>
        NormalX != 0 || NormalY != 0 || NormalZ != 0;
}

public static class VoxelWorldRaycaster
{
    public static VoxelWorldHit? Raycast(
        VoxelWorld world,
        BlockRegistry blocks,
        Vector3 origin,
        Vector3 direction,
        float maxDistance)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);

        if (maxDistance < 0f ||
            direction.LengthSquared() <= float.Epsilon)
        {
            return null;
        }

        var resolution = BlockGeometry.Resolution;
        var scaledOrigin = origin * resolution;
        var scaledDirection =
            Vector3.Normalize(direction) * resolution;

        var fineX = (int)MathF.Floor(scaledOrigin.X);
        var fineY = (int)MathF.Floor(scaledOrigin.Y);
        var fineZ = (int)MathF.Floor(scaledOrigin.Z);

        var stepX = Math.Sign(scaledDirection.X);
        var stepY = Math.Sign(scaledDirection.Y);
        var stepZ = Math.Sign(scaledDirection.Z);

        var deltaX = ReciprocalAbs(scaledDirection.X);
        var deltaY = ReciprocalAbs(scaledDirection.Y);
        var deltaZ = ReciprocalAbs(scaledDirection.Z);

        var maxX = FirstBoundaryDistance(
            scaledOrigin.X,
            fineX,
            scaledDirection.X);
        var maxY = FirstBoundaryDistance(
            scaledOrigin.Y,
            fineY,
            scaledDirection.Y);
        var maxZ = FirstBoundaryDistance(
            scaledOrigin.Z,
            fineZ,
            scaledDirection.Z);

        var normalX = 0;
        var normalY = 0;
        var normalZ = 0;

        while (true)
        {
            var voxelX = FloorDiv(fineX, resolution);
            var voxelY = FloorDiv(fineY, resolution);
            var voxelZ = FloorDiv(fineZ, resolution);
            var position =
                new WorldVoxelCoord(voxelX, voxelY, voxelZ);

            if (world.TryGetCell(position, out var cell) &&
                !cell.IsEmpty)
            {
                var definition = blocks.GetDefinition(cell.Block);
                var mask =
                    world.GetMicroblockMaskOrEmpty(position);

                if (BlockGeometry.IsOccupied(
                        definition,
                        cell,
                        mask,
                        Mod(fineX, resolution),
                        Mod(fineY, resolution),
                        Mod(fineZ, resolution)))
                {
                    return new VoxelWorldHit(
                        position,
                        normalX,
                        normalY,
                        normalZ);
                }
            }

            float distance;
            if (maxX <= maxY && maxX <= maxZ)
            {
                distance = maxX;
                fineX += stepX;
                normalX = -stepX;
                normalY = 0;
                normalZ = 0;
                maxX += deltaX;
            }
            else if (maxY <= maxZ)
            {
                distance = maxY;
                fineY += stepY;
                normalX = 0;
                normalY = -stepY;
                normalZ = 0;
                maxY += deltaY;
            }
            else
            {
                distance = maxZ;
                fineZ += stepZ;
                normalX = 0;
                normalY = 0;
                normalZ = -stepZ;
                maxZ += deltaZ;
            }

            if (distance > maxDistance)
            {
                return null;
            }
        }
    }

    private static float ReciprocalAbs(float value) =>
        value == 0f
            ? float.PositiveInfinity
            : 1f / MathF.Abs(value);

    private static float FirstBoundaryDistance(
        float origin,
        int fine,
        float direction)
    {
        if (direction > 0f)
        {
            return (fine + 1f - origin) / direction;
        }

        if (direction < 0f)
        {
            return (origin - fine) / -direction;
        }

        return float.PositiveInfinity;
    }

    private static int FloorDiv(int value, int divisor)
    {
        var quotient = value / divisor;
        var remainder = value % divisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }

    private static int Mod(int value, int divisor)
    {
        var remainder = value % divisor;
        return remainder < 0 ? remainder + divisor : remainder;
    }
}

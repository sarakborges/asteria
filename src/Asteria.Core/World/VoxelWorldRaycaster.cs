using System.Numerics;

namespace Asteria.Core.World;

public readonly record struct VoxelWorldHit(
    WorldVoxelCoord Voxel,
    int NormalX,
    int NormalY,
    int NormalZ)
{
    public bool HasSurfaceNormal =>
        NormalX != 0 ||
        NormalY != 0 ||
        NormalZ != 0;
}

public static class VoxelWorldRaycaster
{
    private const float EntryEpsilon = 0.00001f;

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

        direction = Vector3.Normalize(direction);

        var voxelX =
            (int)MathF.Floor(origin.X);
        var voxelY =
            (int)MathF.Floor(origin.Y);
        var voxelZ =
            (int)MathF.Floor(origin.Z);

        var stepX = Math.Sign(direction.X);
        var stepY = Math.Sign(direction.Y);
        var stepZ = Math.Sign(direction.Z);

        var deltaX = ReciprocalAbs(direction.X);
        var deltaY = ReciprocalAbs(direction.Y);
        var deltaZ = ReciprocalAbs(direction.Z);

        var maxX =
            FirstBoundaryDistance(
                origin.X,
                voxelX,
                direction.X);
        var maxY =
            FirstBoundaryDistance(
                origin.Y,
                voxelY,
                direction.Y);
        var maxZ =
            FirstBoundaryDistance(
                origin.Z,
                voxelZ,
                direction.Z);

        var entryDistance = 0f;
        var normalX = 0;
        var normalY = 0;
        var normalZ = 0;

        while (entryDistance <= maxDistance)
        {
            var position =
                new WorldVoxelCoord(
                    voxelX,
                    voxelY,
                    voxelZ);

            if (world.TryGetCell(
                    position,
                    out var cell) &&
                !cell.IsEmpty)
            {
                var definition =
                    blocks.GetDefinition(
                        cell.Block);

                var exitDistance =
                    Math.Min(maxX, Math.Min(maxY, maxZ));

                if (definition.Visual.Kind == BlockVisualKind.GroundSprite)
                {
                    var groundHit = RaycastGroundSprite(
                        position, definition.Visual, origin, direction,
                        entryDistance, Math.Min(exitDistance, maxDistance));
                    if (groundHit is not null)
                        return groundHit;
                }
                else if (!BlockGeometry.RequiresFineMeshing(definition, cell))
                {
                    return new VoxelWorldHit(
                        position, normalX, normalY, normalZ);
                }
                else
                {
                    var partialHit =
                    RaycastPartialVoxel(
                        world,
                        definition,
                        cell,
                        position,
                        origin,
                        direction,
                        entryDistance,
                        Math.Min(
                            exitDistance,
                            maxDistance),
                        normalX,
                        normalY,
                        normalZ);

                    if (partialHit is not null)
                        return partialHit;
                }
            }

            float distance;

            if (maxX <= maxY &&
                maxX <= maxZ)
            {
                distance = maxX;
                voxelX += stepX;
                normalX = -stepX;
                normalY = 0;
                normalZ = 0;
                maxX += deltaX;
            }
            else if (maxY <= maxZ)
            {
                distance = maxY;
                voxelY += stepY;
                normalX = 0;
                normalY = -stepY;
                normalZ = 0;
                maxY += deltaY;
            }
            else
            {
                distance = maxZ;
                voxelZ += stepZ;
                normalX = 0;
                normalY = 0;
                normalZ = -stepZ;
                maxZ += deltaZ;
            }

            if (distance > maxDistance)
            {
                return null;
            }

            entryDistance = distance;
        }

        return null;
    }

    private static VoxelWorldHit? RaycastGroundSprite(
        WorldVoxelCoord position,
        BlockVisualDefinition visual,
        Vector3 origin,
        Vector3 direction,
        float entryDistance,
        float exitDistance)
    {
        var halfWidth = visual.Width * 0.5f;
        var minimumX = position.X + 0.5f - halfWidth;
        var maximumX = position.X + 0.5f + halfWidth;
        var minimumZ = position.Z + 0.5f - halfWidth;
        var maximumZ = position.Z + 0.5f + halfWidth;
        var minimumY = position.Y + visual.BaseOffset;
        var maximumY = minimumY + visual.TargetHeight;
        var near = entryDistance;
        var far = exitDistance;
        if (!ClipRayAxis(origin.X, direction.X, minimumX, maximumX, ref near, ref far) ||
            !ClipRayAxis(origin.Y, direction.Y, minimumY, maximumY, ref near, ref far) ||
            !ClipRayAxis(origin.Z, direction.Z, minimumZ, maximumZ, ref near, ref far))
        {
            return null;
        }

        // Ground objects are pickup-only; no placement normal is consumed.
        return new VoxelWorldHit(position, 0, 0, 0);
    }

    private static bool ClipRayAxis(
        float origin,
        float direction,
        float minimum,
        float maximum,
        ref float near,
        ref float far)
    {
        if (direction == 0f)
            return origin >= minimum && origin <= maximum;

        var first = (minimum - origin) / direction;
        var second = (maximum - origin) / direction;
        near = Math.Max(near, Math.Min(first, second));
        far = Math.Min(far, Math.Max(first, second));
        return near <= far;
    }

    private static VoxelWorldHit? RaycastPartialVoxel(
        VoxelWorld world,
        BlockDefinition definition,
        VoxelCell cell,
        WorldVoxelCoord position,
        Vector3 origin,
        Vector3 direction,
        float entryDistance,
        float exitDistance,
        int entryNormalX,
        int entryNormalY,
        int entryNormalZ)
    {
        if (exitDistance < entryDistance)
        {
            return null;
        }

        var resolution =
            BlockGeometry.Resolution;
        var sampleDistance =
            entryDistance +
            EntryEpsilon;
        var sample =
            origin +
            direction * sampleDistance;

        var fineOriginX =
            checked(position.X * resolution);
        var fineOriginY =
            checked(position.Y * resolution);
        var fineOriginZ =
            checked(position.Z * resolution);

        var fineX =
            Math.Clamp(
                (int)MathF.Floor(
                    sample.X * resolution),
                fineOriginX,
                fineOriginX + resolution - 1);
        var fineY =
            Math.Clamp(
                (int)MathF.Floor(
                    sample.Y * resolution),
                fineOriginY,
                fineOriginY + resolution - 1);
        var fineZ =
            Math.Clamp(
                (int)MathF.Floor(
                    sample.Z * resolution),
                fineOriginZ,
                fineOriginZ + resolution - 1);

        var scaledOrigin =
            origin * resolution;
        var scaledDirection =
            direction * resolution;

        var stepX =
            Math.Sign(scaledDirection.X);
        var stepY =
            Math.Sign(scaledDirection.Y);
        var stepZ =
            Math.Sign(scaledDirection.Z);

        var deltaX =
            ReciprocalAbs(
                scaledDirection.X);
        var deltaY =
            ReciprocalAbs(
                scaledDirection.Y);
        var deltaZ =
            ReciprocalAbs(
                scaledDirection.Z);

        var maxX =
            FirstBoundaryDistance(
                scaledOrigin.X,
                fineX,
                scaledDirection.X);
        var maxY =
            FirstBoundaryDistance(
                scaledOrigin.Y,
                fineY,
                scaledDirection.Y);
        var maxZ =
            FirstBoundaryDistance(
                scaledOrigin.Z,
                fineZ,
                scaledDirection.Z);

        var normalX = entryNormalX;
        var normalY = entryNormalY;
        var normalZ = entryNormalZ;
        var mask =
            world.GetMicroblockMaskOrEmpty(
                position);

        while (true)
        {
            var localX =
                fineX - fineOriginX;
            var localY =
                fineY - fineOriginY;
            var localZ =
                fineZ - fineOriginZ;

            if ((uint)localX >= resolution ||
                (uint)localY >= resolution ||
                (uint)localZ >= resolution)
            {
                return null;
            }

            if (BlockGeometry.IsOccupied(
                    definition,
                    cell,
                    mask,
                    localX,
                    localY,
                    localZ))
            {
                return new VoxelWorldHit(
                    position,
                    normalX,
                    normalY,
                    normalZ);
            }

            float distance;

            if (maxX <= maxY &&
                maxX <= maxZ)
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

            if (distance >
                exitDistance + EntryEpsilon)
            {
                return null;
            }
        }
    }

    private static float ReciprocalAbs(
        float value) =>
        value == 0f
            ? float.PositiveInfinity
            : 1f / MathF.Abs(value);

    private static float FirstBoundaryDistance(
        float origin,
        int cell,
        float direction)
    {
        if (direction > 0f)
        {
            return (
                cell + 1f - origin) /
                direction;
        }

        if (direction < 0f)
        {
            return (
                origin - cell) /
                -direction;
        }

        return float.PositiveInfinity;
    }
}

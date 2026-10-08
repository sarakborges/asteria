namespace Asteria.Core.World;

public readonly record struct ChunkNeighborMeshInvalidation(
    ChunkCoord Neighbor,
    ChunkMeshletMask Terrain,
    ChunkMeshletMask Fluid);

public static class ChunkTopologyFrontier
{
    public static IEnumerable<WorldVoxelCoord> LightingSeeds(
        ChunkCoord coord) =>
        LightingSeeds(coord, static _ => true);

    // Only a resident neighbor can exchange light with this chunk.
    // The two opposing faces are seeded together, including when the
    // source chunk was just retired and only the neighbor remains.
    public static IEnumerable<WorldVoxelCoord> LightingSeeds(
        ChunkCoord coord,
        Func<ChunkCoord, bool> neighborIsResident)
    {
        ArgumentNullException.ThrowIfNull(neighborIsResident);

        var above =
            neighborIsResident(new ChunkCoord(coord.X, coord.Y + 1, coord.Z));
        var below =
            coord.Y > 0 &&
            neighborIsResident(new ChunkCoord(coord.X, coord.Y - 1, coord.Z));
        var negativeX =
            neighborIsResident(new ChunkCoord(coord.X - 1, coord.Y, coord.Z));
        var positiveX =
            neighborIsResident(new ChunkCoord(coord.X + 1, coord.Y, coord.Z));
        var negativeZ =
            neighborIsResident(new ChunkCoord(coord.X, coord.Y, coord.Z - 1));
        var positiveZ =
            neighborIsResident(new ChunkCoord(coord.X, coord.Y, coord.Z + 1));

        var (originX, originY, originZ) =
            VoxelCoordinates.ChunkOrigin(coord);

        if (above || below)
        {
            for (var z = 0; z < Chunk.Size; z++)
            {
                for (var x = 0; x < Chunk.Size; x++)
                {
                    if (above)
                    {
                        yield return new WorldVoxelCoord(
                            originX + x, originY + Chunk.Size - 1, originZ + z);
                        yield return new WorldVoxelCoord(
                            originX + x, originY + Chunk.Size, originZ + z);
                    }

                    if (below)
                    {
                        yield return new WorldVoxelCoord(
                            originX + x, originY, originZ + z);
                        yield return new WorldVoxelCoord(
                            originX + x, originY - 1, originZ + z);
                    }
                }
            }
        }

        if (negativeX || positiveX)
        {
            for (var y = 0; y < Chunk.Size; y++)
            {
                for (var z = 0; z < Chunk.Size; z++)
                {
                    if (negativeX)
                    {
                        yield return new WorldVoxelCoord(
                            originX, originY + y, originZ + z);
                        yield return new WorldVoxelCoord(
                            originX - 1, originY + y, originZ + z);
                    }

                    if (positiveX)
                    {
                        yield return new WorldVoxelCoord(
                            originX + Chunk.Size - 1, originY + y, originZ + z);
                        yield return new WorldVoxelCoord(
                            originX + Chunk.Size, originY + y, originZ + z);
                    }
                }
            }
        }

        if (negativeZ || positiveZ)
        {
            for (var y = 0; y < Chunk.Size; y++)
            {
                for (var x = 0; x < Chunk.Size; x++)
                {
                    if (negativeZ)
                    {
                        yield return new WorldVoxelCoord(
                            originX + x, originY + y, originZ);
                        yield return new WorldVoxelCoord(
                            originX + x, originY + y, originZ - 1);
                    }

                    if (positiveZ)
                    {
                        yield return new WorldVoxelCoord(
                            originX + x, originY + y, originZ + Chunk.Size - 1);
                        yield return new WorldVoxelCoord(
                            originX + x, originY + y, originZ + Chunk.Size);
                    }
                }
            }
        }
    }

    public static IEnumerable<ChunkNeighborMeshInvalidation>
        PresentedNeighborMeshInvalidationsForAddition(
            ChunkCoord source,
            VoxelWorld world,
            Func<ChunkCoord, bool> isPresented)
    {
        ArgumentNullException.ThrowIfNull(
            world);
        ArgumentNullException.ThrowIfNull(
            isPresented);

        if (!world.TryGetChunk(
                source,
                out var sourceChunk))
        {
            yield break;
        }

        for (var y = -1;
             y <= 1;
             y++)
        {
            for (var z = -1;
                 z <= 1;
                 z++)
            {
                for (var x = -1;
                     x <= 1;
                     x++)
                {
                    if (x == 0 &&
                        y == 0 &&
                        z == 0)
                    {
                        continue;
                    }

                    var neighbor =
                        new ChunkCoord(
                            source.X + x,
                            source.Y + y,
                            source.Z + z);

                    if (!isPresented(
                            neighbor) ||
                        !world.TryGetChunk(
                            neighbor,
                            out var neighborChunk))
                    {
                        continue;
                    }

                    var sourceHasContent =
                        sourceChunk
                            .DependencyBoundaryHasContent(
                                x,
                                y,
                                z);
                    var neighborHasContent =
                        neighborChunk
                            .DependencyBoundaryHasContent(
                                -x,
                                -y,
                                -z);
                    var neighborHasFluid =
                        neighborChunk
                            .DependencyBoundaryHasFluid(
                                -x,
                                -y,
                                -z);
                    var cardinal =
                        Math.Abs(x) +
                        Math.Abs(y) +
                        Math.Abs(z) ==
                        1;
                    var sourceHasCardinalFluid =
                        cardinal &&
                        sourceChunk
                            .DependencyBoundaryHasFluid(
                                x,
                                y,
                                z);
                    var meshlets =
                        ChunkMeshletMask
                            .ForDependencyOffset(
                                -x,
                                -y,
                                -z);
                    var terrain =
                        sourceHasContent &&
                        neighborHasContent
                            ? meshlets
                            : ChunkMeshletMask.None;
                    var fluid =
                        neighborHasFluid &&
                        sourceHasContent ||
                        sourceHasCardinalFluid
                            ? meshlets
                            : ChunkMeshletMask.None;

                    if (terrain.IsEmpty &&
                        fluid.IsEmpty)
                    {
                        continue;
                    }

                    yield return
                        new ChunkNeighborMeshInvalidation(
                            neighbor,
                            terrain,
                            fluid);
                }
            }
        }
    }

    public static IEnumerable<(
        ChunkCoord Neighbor,
        ChunkMeshletMask Meshlets)> PresentedNeighborMeshlets(
        ChunkCoord source,
        Func<ChunkCoord, bool> isPresented)
    {
        ArgumentNullException.ThrowIfNull(isPresented);

        for (var y = -1; y <= 1; y++)
        {
            for (var z = -1; z <= 1; z++)
            {
                for (var x = -1; x <= 1; x++)
                {
                    if (x == 0 && y == 0 && z == 0)
                    {
                        continue;
                    }

                    var neighbor = new ChunkCoord(
                        source.X + x,
                        source.Y + y,
                        source.Z + z);

                    if (!isPresented(neighbor))
                    {
                        continue;
                    }

                    yield return (
                        neighbor,
                        ChunkMeshletMask.ForDependencyOffset(
                            -x,
                            -y,
                            -z));
                }
            }
        }
    }
}

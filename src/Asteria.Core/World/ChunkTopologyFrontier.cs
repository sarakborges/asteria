namespace Asteria.Core.World;

public static class ChunkTopologyFrontier
{
    public static IEnumerable<WorldVoxelCoord> LightingSeeds(
        ChunkCoord coord)
    {
        var (originX, originY, originZ) =
            VoxelCoordinates.ChunkOrigin(coord);

        for (var z = 0; z < Chunk.Size; z++)
        {
            for (var x = 0; x < Chunk.Size; x++)
            {
                yield return new WorldVoxelCoord(
                    originX + x,
                    originY + Chunk.Size - 1,
                    originZ + z);
                yield return new WorldVoxelCoord(
                    originX + x,
                    originY - 1,
                    originZ + z);
                yield return new WorldVoxelCoord(
                    originX + x,
                    originY,
                    originZ + z);
                yield return new WorldVoxelCoord(
                    originX + x,
                    originY + Chunk.Size,
                    originZ + z);
            }
        }

        for (var y = 0; y < Chunk.Size; y++)
        {
            for (var z = 0; z < Chunk.Size; z++)
            {
                yield return new WorldVoxelCoord(
                    originX,
                    originY + y,
                    originZ + z);
                yield return new WorldVoxelCoord(
                    originX - 1,
                    originY + y,
                    originZ + z);
                yield return new WorldVoxelCoord(
                    originX + Chunk.Size - 1,
                    originY + y,
                    originZ + z);
                yield return new WorldVoxelCoord(
                    originX + Chunk.Size,
                    originY + y,
                    originZ + z);
            }
        }

        for (var y = 0; y < Chunk.Size; y++)
        {
            for (var x = 0; x < Chunk.Size; x++)
            {
                yield return new WorldVoxelCoord(
                    originX + x,
                    originY + y,
                    originZ);
                yield return new WorldVoxelCoord(
                    originX + x,
                    originY + y,
                    originZ - 1);
                yield return new WorldVoxelCoord(
                    originX + x,
                    originY + y,
                    originZ + Chunk.Size - 1);
                yield return new WorldVoxelCoord(
                    originX + x,
                    originY + y,
                    originZ + Chunk.Size);
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

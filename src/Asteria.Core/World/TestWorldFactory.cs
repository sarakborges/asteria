namespace Asteria.Core.World;

public sealed record TestWorldFixture(
    BlockRegistry Blocks,
    VoxelWorld World);

public static class TestWorldFactory
{
    public static TestWorldFixture Create(BlockRegistry blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var world = new VoxelWorld();

        for (var chunkY = 0; chunkY <= 1; chunkY++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
            {
                for (var chunkX = -1; chunkX <= 1; chunkX++)
                {
                    var coord =
                        new ChunkCoord(chunkX, chunkY, chunkZ);
                    world.InsertChunk(
                        coord,
                        CreateChunk(blocks, coord));
                }
            }
        }

        PlaceCrossChunkQa(world, blocks);
        return new TestWorldFixture(blocks, world);
    }

    private static Chunk CreateChunk(
        BlockRegistry blocks,
        ChunkCoord coord)
    {
        var grass = blocks.GetId(TestChunkFactory.GrassId);
        var dirt = blocks.GetId(TestChunkFactory.DirtId);
        var stone = blocks.GetId(TestChunkFactory.StoneId);
        var sand = blocks.GetId(TestChunkFactory.SandId);
        var gravel = blocks.GetId(TestChunkFactory.GravelId);
        var clay = blocks.GetId(TestChunkFactory.ClayId);
        var mud = blocks.GetId(TestChunkFactory.MudId);
        var chunk = new Chunk();
        var (originX, originY, originZ) =
            VoxelCoordinates.ChunkOrigin(coord);

        for (var x = 0; x < Chunk.Size; x++)
        {
            for (var z = 0; z < Chunk.Size; z++)
            {
                var worldX = originX + x;
                var worldZ = originZ + z;
                var wave =
                    MathF.Sin(worldX * 0.21f) * 1.7f +
                    MathF.Cos(worldZ * 0.17f) * 1.5f;
                var surfaceY = Math.Clamp(
                    7 + (int)MathF.Round(wave),
                    4,
                    12);

                var band = Mod(worldX, 20);
                var surfaceBlock = band switch
                {
                    < 4 => grass,
                    < 7 => sand,
                    < 10 => gravel,
                    < 13 => clay,
                    _ => mud,
                };

                for (var y = 0; y < Chunk.Size; y++)
                {
                    var worldY = originY + y;
                    if (worldY > surfaceY)
                    {
                        continue;
                    }

                    var block = worldY switch
                    {
                        _ when worldY == surfaceY =>
                            surfaceBlock,
                        _ when worldY >= surfaceY - 2 =>
                            dirt,
                        _ => stone,
                    };

                    chunk.SetBlock(x, y, z, block);
                }
            }
        }

        return chunk;
    }

    private static void PlaceCrossChunkQa(
        VoxelWorld world,
        BlockRegistry blocks)
    {
        var stone = blocks.GetId(TestChunkFactory.StoneId);

        // Vertical seam probe through the Y=15/16 chunk boundary.
        for (var y = 14; y <= 18; y++)
        {
            world.SetBlockAt(
                new WorldVoxelCoord(5, y, 5),
                stone,
                out _);
        }

        if (blocks.TryGetId(
                "asteria:log_oak_hollow",
                out var hollowLog))
        {
            world.SetCellAt(
                new WorldVoxelCoord(7, 12, 2),
                new VoxelCell(
                    hollowLog,
                    orientation: BlockOrientation.X),
                out _);
        }

        if (blocks.TryGetId(
                "asteria:log_oak_stripped",
                out var strippedLog))
        {
            world.SetCellAt(
                new WorldVoxelCoord(9, 12, 2),
                new VoxelCell(
                    strippedLog,
                    orientation: BlockOrientation.Z),
                out _);
        }

        if (blocks.TryGetId(
                "asteria:sand_layer",
                out var sandLayer))
        {
            world.SetBlockAt(
                new WorldVoxelCoord(1, 12, 2),
                sandLayer,
                out _);
        }

        if (blocks.TryGetId(
                "asteria:snow_layer",
                out var snowLayer))
        {
            world.SetBlockAt(
                new WorldVoxelCoord(3, 12, 2),
                snowLayer,
                out _);
        }

        world.SetBlockAt(
            new WorldVoxelCoord(11, 12, 2),
            stone,
            out _);

        var address = VoxelCoordinates.FromWorld(11, 12, 2);
        var sculpted = MicroblockMask.Full
            .Edit(
                0,
                0,
                0,
                MicroblockResolution.Thick,
                occupied: false)
            .Edit(
                7,
                7,
                7,
                MicroblockResolution.Thin,
                occupied: false);

        world
            .GetChunk(address.Chunk)
            .SetMicroblockMask(
                address.Local.X,
                address.Local.Y,
                address.Local.Z,
                sculpted);
    }

    private static int Mod(int value, int divisor)
    {
        var remainder = value % divisor;
        return remainder < 0
            ? remainder + divisor
            : remainder;
    }
}

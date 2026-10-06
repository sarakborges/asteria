namespace Asteria.Core.World;

public static class DeterministicChunkProvider
{
    public static Chunk Materialize(
        BlockRegistry blocks,
        ChunkCoord coord)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var grass =
            blocks.GetId(TestChunkFactory.GrassId);
        var dirt =
            blocks.GetId(TestChunkFactory.DirtId);
        var stone =
            blocks.GetId(TestChunkFactory.StoneId);
        var sand =
            blocks.GetId(TestChunkFactory.SandId);
        var gravel =
            blocks.GetId(TestChunkFactory.GravelId);
        var clay =
            blocks.GetId(TestChunkFactory.ClayId);
        var mud =
            blocks.GetId(TestChunkFactory.MudId);
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

        PlaceQaContent(
            chunk,
            blocks,
            coord,
            stone);

        return chunk;
    }

    private static void PlaceQaContent(
        Chunk chunk,
        BlockRegistry blocks,
        ChunkCoord coord,
        BlockRuntimeId stone)
    {
        for (var y = 14; y <= 18; y++)
        {
            SetBlockIfOwned(
                chunk,
                coord,
                new WorldVoxelCoord(5, y, 5),
                stone);
        }

        if (blocks.TryGetId(
                "asteria:log_oak_hollow",
                out var hollowLog))
        {
            SetCellIfOwned(
                chunk,
                coord,
                new WorldVoxelCoord(7, 12, 2),
                new VoxelCell(
                    hollowLog,
                    orientation: BlockOrientation.X));
        }

        if (blocks.TryGetId(
                "asteria:log_oak_stripped",
                out var strippedLog))
        {
            SetCellIfOwned(
                chunk,
                coord,
                new WorldVoxelCoord(9, 12, 2),
                new VoxelCell(
                    strippedLog,
                    orientation: BlockOrientation.Z));
        }

        if (blocks.TryGetId(
                "asteria:sand_layer",
                out var sandLayer))
        {
            SetBlockIfOwned(
                chunk,
                coord,
                new WorldVoxelCoord(1, 12, 2),
                sandLayer);
        }

        if (blocks.TryGetId(
                "asteria:snow_layer",
                out var snowLayer))
        {
            SetBlockIfOwned(
                chunk,
                coord,
                new WorldVoxelCoord(3, 12, 2),
                snowLayer);
        }

        var sculptedPosition =
            new WorldVoxelCoord(11, 12, 2);
        if (SetBlockIfOwned(
                chunk,
                coord,
                sculptedPosition,
                stone))
        {
            var address = VoxelCoordinates.FromWorld(
                sculptedPosition.X,
                sculptedPosition.Y,
                sculptedPosition.Z);
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

            chunk.SetMicroblockMask(
                address.Local.X,
                address.Local.Y,
                address.Local.Z,
                sculpted);
        }
    }

    private static bool SetBlockIfOwned(
        Chunk chunk,
        ChunkCoord owner,
        WorldVoxelCoord position,
        BlockRuntimeId block) =>
        SetCellIfOwned(
            chunk,
            owner,
            position,
            new VoxelCell(block));

    private static bool SetCellIfOwned(
        Chunk chunk,
        ChunkCoord owner,
        WorldVoxelCoord position,
        VoxelCell cell)
    {
        var address = VoxelCoordinates.FromWorld(
            position.X,
            position.Y,
            position.Z);

        if (address.Chunk != owner)
        {
            return false;
        }

        chunk.SetCell(
            address.Local.X,
            address.Local.Y,
            address.Local.Z,
            cell);
        return true;
    }

    private static int Mod(
        int value,
        int divisor)
    {
        var remainder = value % divisor;
        return remainder < 0
            ? remainder + divisor
            : remainder;
    }
}

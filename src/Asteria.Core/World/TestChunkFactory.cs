namespace Asteria.Core.World;

public sealed record TestChunkFixture(BlockRegistry Blocks, Chunk Chunk);

public static class TestChunkFactory
{
    public const string GrassId = "asteria:grass_block";
    public const string DirtId = "asteria:dirt";
    public const string StoneId = "asteria:stone";
    public const string SandId = "asteria:sand";
    public const string GravelId = "asteria:gravel";
    public const string ClayId = "asteria:clay";
    public const string MudId = "asteria:mud";
    public const string GrassPlantId = "asteria:grass";
    public const string BrownMushroomId = "asteria:mushroom_brown";

    public static TestChunkFixture Create(BlockRegistry blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var grass = blocks.GetId(GrassId);
        var dirt = blocks.GetId(DirtId);
        var stone = blocks.GetId(StoneId);
        var sand = blocks.GetId(SandId);
        var gravel = blocks.GetId(GravelId);
        var clay = blocks.GetId(ClayId);
        var mud = blocks.GetId(MudId);
        blocks.TryGetId(
            GrassPlantId,
            out var grassPlant);
        blocks.TryGetId(
            BrownMushroomId,
            out var brownMushroom);
        var chunk = new Chunk();

        for (var x = 0; x < Chunk.Size; x++)
        {
            for (var z = 0; z < Chunk.Size; z++)
            {
                var wave = MathF.Sin(x * 0.31f) * 1.7f + MathF.Cos(z * 0.27f) * 1.5f;
                var surfaceY = Math.Clamp(6 + (int)MathF.Round(wave), 3, Chunk.Size - 2);
                var surfaceBlock = x switch
                {
                    < 4 => grass,
                    < 7 => sand,
                    < 10 => gravel,
                    < 13 => clay,
                    _ => mud,
                };

                for (var y = 0; y <= surfaceY; y++)
                {
                    var block = y switch
                    {
                        _ when y == surfaceY => surfaceBlock,
                        _ when y >= surfaceY - 2 => dirt,
                        _ => stone,
                    };

                    chunk.SetBlock(x, y, z, block);
                }

                var decorationY =
                    surfaceY + 1;

                if (decorationY < Chunk.Size &&
                    surfaceBlock == grass &&
                    !grassPlant.IsAir &&
                    GroundDecorationHash(
                        x,
                        z,
                        salt: 11) %
                        3 == 0)
                {
                    chunk.SetBlock(
                        x,
                        decorationY,
                        z,
                        grassPlant);
                }
                else if (
                    decorationY < Chunk.Size &&
                    surfaceBlock == mud &&
                    !brownMushroom.IsAir &&
                    GroundDecorationHash(
                        x,
                        z,
                        salt: 29) %
                        5 == 0)
                {
                    chunk.SetBlock(
                        x,
                        decorationY,
                        z,
                        brownMushroom);
                }
            }
        }

        PlaceShapeShowcase(chunk, blocks, stone);

        return new TestChunkFixture(blocks, chunk);
    }

    private static uint GroundDecorationHash(
        int x,
        int z,
        uint salt)
    {
        unchecked
        {
            var hash =
                (uint)x * 0x9E3779B9u ^
                (uint)z * 0x85EBCA6Bu ^
                salt * 0xC2B2AE35u;
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            return hash;
        }
    }

    private static void PlaceShapeShowcase(
        Chunk chunk,
        BlockRegistry blocks,
        BlockRuntimeId stone)
    {
        const int y = 12;
        const int z = 2;

        if (blocks.TryGetId("asteria:sand_layer", out var sandLayer))
        {
            chunk.SetBlock(1, y, z, sandLayer);
        }

        if (blocks.TryGetId("asteria:snow_layer", out var snowLayer))
        {
            chunk.SetBlock(3, y, z, snowLayer);
        }

        if (blocks.TryGetId("asteria:log_oak_hollow", out var hollowLog))
        {
            chunk.SetCell(
                5,
                y,
                z,
                new VoxelCell(hollowLog, orientation: BlockOrientation.Y));

            chunk.SetCell(
                7,
                y,
                z,
                new VoxelCell(hollowLog, orientation: BlockOrientation.X));
        }

        if (blocks.TryGetId("asteria:log_oak_stripped", out var strippedLog))
        {
            chunk.SetCell(
                9,
                y,
                z,
                new VoxelCell(strippedLog, orientation: BlockOrientation.Z));
        }

        chunk.SetBlock(11, y, z, stone);
        var sculpted = MicroblockMask.Full
            .Edit(0, 0, 0, MicroblockResolution.Thick, occupied: false)
            .Edit(7, 7, 7, MicroblockResolution.Thin, occupied: false);
        chunk.SetMicroblockMask(11, y, z, sculpted);
    }
}

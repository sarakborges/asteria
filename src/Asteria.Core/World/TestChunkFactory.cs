namespace Asteria.Core.World;

public sealed record TestChunkFixture(BlockRegistry Blocks, Chunk Chunk);

public static class TestChunkFactory
{
    public const string GrassId = "asteria:grass_block";
    public const string DirtId = "asteria:dirt";
    public const string StoneId = "asteria:stone";

    public static TestChunkFixture Create()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition(GrassId),
            new BlockDefinition(DirtId),
            new BlockDefinition(StoneId),
        ]);

        var grass = blocks.GetId(GrassId);
        var dirt = blocks.GetId(DirtId);
        var stone = blocks.GetId(StoneId);
        var chunk = new Chunk();

        for (var x = 0; x < Chunk.Size; x++)
        {
            for (var z = 0; z < Chunk.Size; z++)
            {
                var wave = MathF.Sin(x * 0.31f) * 1.7f + MathF.Cos(z * 0.27f) * 1.5f;
                var surfaceY = Math.Clamp(6 + (int)MathF.Round(wave), 3, Chunk.Size - 2);

                for (var y = 0; y <= surfaceY; y++)
                {
                    var block = y switch
                    {
                        _ when y == surfaceY => grass,
                        _ when y >= surfaceY - 2 => dirt,
                        _ => stone,
                    };

                    chunk.SetBlock(x, y, z, block);
                }
            }
        }

        return new TestChunkFixture(blocks, chunk);
    }
}

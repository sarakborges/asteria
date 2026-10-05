namespace Asteria.Core.World;

public static class TestWorldGenerator
{
    public static Chunk GenerateChunk()
    {
        var chunk = new Chunk();

        for (var x = 0; x < Chunk.SizeX; x++)
        {
            for (var z = 0; z < Chunk.SizeZ; z++)
            {
                var wave = MathF.Sin(x * 0.31f) * 2.2f + MathF.Cos(z * 0.27f) * 2.0f;
                var surfaceY = Math.Clamp(11 + (int)MathF.Round(wave), 5, Chunk.SizeY - 2);

                for (var y = 0; y <= surfaceY; y++)
                {
                    var block = y switch
                    {
                        _ when y == surfaceY => BlockId.Grass,
                        _ when y >= surfaceY - 2 => BlockId.Dirt,
                        _ => BlockId.Stone,
                    };

                    chunk.SetBlock(x, y, z, block);
                }
            }
        }

        return chunk;
    }
}

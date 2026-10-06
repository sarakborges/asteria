namespace Asteria.Core.World;

public interface IChunkProvider
{
    Chunk Materialize(ChunkCoord coord);
}

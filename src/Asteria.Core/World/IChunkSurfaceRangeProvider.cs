namespace Asteria.Core.World;

public readonly record struct ChunkSurfaceRange(
    int MinimumWorldY,
    int MaximumWorldY);

public interface IChunkSurfaceRangeProvider
{
    ChunkSurfaceRange GetSurfaceRange(
        int chunkX,
        int chunkZ);
}

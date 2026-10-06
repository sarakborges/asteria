namespace Asteria.Core.World;

/// <summary>
/// Pure chunk materialization boundary consumed by residency workers.
/// Implementations may be invoked concurrently and therefore must be
/// deterministic for the same immutable inputs and safe for concurrent reads.
/// </summary>
public interface IChunkProvider
{
    Chunk Materialize(ChunkCoord coord);
}

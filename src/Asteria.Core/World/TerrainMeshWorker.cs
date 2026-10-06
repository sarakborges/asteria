using System.Diagnostics;

namespace Asteria.Core.World;

public sealed record TerrainMeshletBuild(
    ChunkCoord Coord,
    int MeshletIndex,
    ChunkMeshData Data);

public sealed record TerrainMeshWorkerResult(
    WorldMeshBatch SourceBatch,
    IReadOnlyDictionary<ChunkMeshletKey, ulong>
        ContentRevisions,
    MeshDependencyStamp Dependencies,
    IReadOnlyList<TerrainMeshletBuild> Meshlets,
    double WorkerMilliseconds);

public sealed class TerrainMeshWorker
{
    private readonly SingleFlightWorker<TerrainMeshWorkerResult>
        _worker = new();

    public bool IsRunning => _worker.IsRunning;

    public bool TryStart(
        VoxelWorld world,
        BlockRegistry blocks,
        TerrainTextureLookup textures,
        WorldMeshBatch batch,
        MeshletContentRevisions revisions)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(revisions);

        if (batch.IsEmpty ||
            IsRunning)
        {
            return false;
        }

        var snapshot =
            world.CaptureMeshWorkerSnapshot(
                batch.DirtyMeshlets);
        var capturedRevisions =
            revisions.Capture(
                batch.DirtyMeshlets);

        return _worker.TryStart(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            var meshlets =
                BuildMeshlets(
                    snapshot.World,
                    blocks,
                    textures,
                    batch.DirtyMeshlets);
            stopwatch.Stop();

            return new TerrainMeshWorkerResult(
                batch,
                capturedRevisions,
                snapshot.Dependencies,
                meshlets,
                stopwatch.Elapsed.TotalMilliseconds);
        });
    }

    public bool TryTakeCompleted(
        out TerrainMeshWorkerResult? result,
        out Exception? error) =>
        _worker.TryTakeCompleted(
            out result,
            out error);

    private static IReadOnlyList<TerrainMeshletBuild>
        BuildMeshlets(
            VoxelWorld world,
            BlockRegistry blocks,
            TerrainTextureLookup textures,
            IReadOnlyDictionary<ChunkCoord, ChunkMeshletMask> dirty)
    {
        var result =
            new List<TerrainMeshletBuild>();

        foreach (var (coord, mask) in dirty
                     .OrderBy(entry => entry.Key.Y)
                     .ThenBy(entry => entry.Key.Z)
                     .ThenBy(entry => entry.Key.X))
        {
            if (!world.ContainsChunk(coord))
            {
                continue;
            }

            foreach (var meshletIndex in mask.Indices())
            {
                result.Add(
                    new TerrainMeshletBuild(
                        coord,
                        meshletIndex,
                        ChunkMeshDataBuilder.BuildMeshlet(
                            world,
                            coord,
                            blocks,
                            textures,
                            meshletIndex)));
            }
        }

        return result;
    }
}

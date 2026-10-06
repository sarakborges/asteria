using System.Diagnostics;

namespace Asteria.Core.World;

public sealed record FluidMeshletBuild(
    ChunkCoord Coord,
    int MeshletIndex,
    ChunkFluidMeshData Data);

public sealed record FluidMeshWorkerResult(
    WorldMeshBatch SourceBatch,
    IReadOnlyDictionary<ChunkMeshletKey, ulong>
        ContentRevisions,
    MeshDependencyStamp Dependencies,
    IReadOnlyList<FluidMeshletBuild> Meshlets,
    double WorkerMilliseconds);

public sealed class FluidMeshWorker
{
    private readonly SingleFlightWorker<FluidMeshWorkerResult>
        _worker = new();

    public bool IsRunning => _worker.IsRunning;

    public bool TryStart(
        VoxelWorld world,
        BlockRegistry blocks,
        FluidRegistry fluids,
        WorldMeshBatch batch,
        MeshletContentRevisions revisions)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(fluids);
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
                    fluids,
                    batch.DirtyMeshlets);
            stopwatch.Stop();

            return new FluidMeshWorkerResult(
                batch,
                capturedRevisions,
                snapshot.Dependencies,
                meshlets,
                stopwatch.Elapsed.TotalMilliseconds);
        });
    }

    public bool TryTakeCompleted(
        out FluidMeshWorkerResult? result,
        out Exception? error) =>
        _worker.TryTakeCompleted(
            out result,
            out error);

    private static IReadOnlyList<FluidMeshletBuild>
        BuildMeshlets(
            VoxelWorld world,
            BlockRegistry blocks,
            FluidRegistry fluids,
            IReadOnlyDictionary<ChunkCoord, ChunkMeshletMask> dirty)
    {
        var result =
            new List<FluidMeshletBuild>();

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
                    new FluidMeshletBuild(
                        coord,
                        meshletIndex,
                        FluidMeshDataBuilder.BuildMeshlet(
                            world,
                            coord,
                            blocks,
                            fluids,
                            meshletIndex)));
            }
        }

        return result;
    }
}

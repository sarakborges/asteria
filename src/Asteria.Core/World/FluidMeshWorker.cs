using System.Diagnostics;

namespace Asteria.Core.World;

public sealed record FluidMeshletBuild(
    ChunkCoord Coord,
    int MeshletIndex,
    ChunkFluidMeshData Data,
    bool LightOnly = false);

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
        MeshletContentRevisions revisions,
        IReadOnlyDictionary<ChunkMeshletKey, ChunkFluidMeshData>? relightable = null)
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
                    batch.DirtyMeshlets,
                    relightable);
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
            IReadOnlyDictionary<ChunkCoord, ChunkMeshletMask> dirty,
            IReadOnlyDictionary<ChunkMeshletKey, ChunkFluidMeshData>? relightable)
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
                var key = new ChunkMeshletKey(coord, meshletIndex);
                if (relightable is not null &&
                    relightable.TryGetValue(key, out var previous) &&
                    FluidMeshLightingUpdater.TryRelight(
                        world, coord, blocks, previous, out var relit))
                {
                    result.Add(new FluidMeshletBuild(
                        coord, meshletIndex, relit, LightOnly: true));
                    continue;
                }
                result.Add(new FluidMeshletBuild(
                    coord, meshletIndex,
                    FluidMeshDataBuilder.BuildMeshlet(
                        world, coord, blocks, fluids, meshletIndex)));
            }
        }

        return result;
    }
}

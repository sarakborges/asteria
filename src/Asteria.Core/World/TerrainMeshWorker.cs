using System.Diagnostics;

namespace Asteria.Core.World;

public sealed record TerrainMeshletBuild(
    ChunkCoord Coord,
    int MeshletIndex,
    ChunkMeshData Data,
    bool LightOnly = false);

public sealed record TerrainMeshWorkerResult(
    WorldMeshBatch SourceBatch,
    IReadOnlyDictionary<ChunkMeshletKey, ulong>
        ContentRevisions,
    MeshDependencyStamp Dependencies,
    IReadOnlyList<TerrainMeshletBuild> Meshlets,
    double WorkerMilliseconds,
    double CaptureMilliseconds = 0d);

public sealed class TerrainMeshWorker
{
    private readonly SingleFlightWorker<TerrainMeshWorkerResult>
        _worker = new();

    public bool IsRunning => _worker.IsRunning;

    public bool TryStart(
        VoxelWorld world,
        BlockRegistry blocks,
        TerrainTextureLookup textures,
        BiomeTintField biomeTints,
        AttachedLayerRegistry layers,
        DyeRegistry dyes,
        WorldMeshBatch batch,
        MeshletContentRevisions revisions,
        IReadOnlyDictionary<ChunkMeshletKey, ChunkMeshData>? relightable = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentNullException.ThrowIfNull(biomeTints);
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(dyes);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(revisions);

        if (batch.IsEmpty ||
            IsRunning)
        {
            return false;
        }

        var captureStart = Stopwatch.GetTimestamp();
        var snapshot =
            world.CaptureMeshWorkerSnapshot(
                batch.DirtyMeshlets);
        var capturedRevisions =
            revisions.Capture(
                batch.DirtyMeshlets);

        var captureMilliseconds = Stopwatch.GetElapsedTime(
            captureStart).TotalMilliseconds;

        return _worker.TryStart(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            var meshlets =
                BuildMeshlets(
                    snapshot.World,
                    blocks,
                    textures,
                    biomeTints,
                    layers,
                    dyes,
                    batch.DirtyMeshlets,
                    relightable);
            stopwatch.Stop();

            return new TerrainMeshWorkerResult(
                batch,
                capturedRevisions,
                snapshot.Dependencies,
                meshlets,
                stopwatch.Elapsed.TotalMilliseconds,
                captureMilliseconds);
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
            BiomeTintField biomeTints,
            AttachedLayerRegistry layers,
            DyeRegistry dyes,
            IReadOnlyDictionary<ChunkCoord, ChunkMeshletMask> dirty,
            IReadOnlyDictionary<ChunkMeshletKey, ChunkMeshData>? relightable)
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

            BiomeTintSampleGrid? tintSamples = null;
            foreach (var meshletIndex in mask.Indices())
            {
                var key = new ChunkMeshletKey(coord, meshletIndex);
                if (relightable is not null &&
                    relightable.TryGetValue(key, out var geometry) &&
                    ChunkMeshLightingUpdater.TryRelight(
                        world, coord, blocks, geometry, out var relit))
                {
                    result.Add(new TerrainMeshletBuild(
                        coord, meshletIndex, relit, LightOnly: true));
                    continue;
                }

                // Immutable biome samples are only needed by a new mesh.
                tintSamples ??= biomeTints.SampleChunkGrid(coord.X, coord.Z);
                result.Add(new TerrainMeshletBuild(
                    coord, meshletIndex,
                    ChunkMeshDataBuilder.BuildMeshlet(
                        world, coord, blocks, textures, meshletIndex,
                        tintSamples, layers, dyes)));
            }
        }

        return result;
    }
}

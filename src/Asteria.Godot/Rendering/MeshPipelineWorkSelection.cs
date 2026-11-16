using Asteria.Core.World;

namespace Asteria.Client.Rendering;

public enum MeshPipelineCompletionKind : byte
{
    Applied = 0,
    RequeuedStale = 1,
}

public readonly record struct MeshPipelineWorkerReport(
    MeshPipelineCompletionKind Kind,
    double WorkerMilliseconds,
    int Accepted,
    int Stale,
    double CaptureMilliseconds = 0d,
    int RelitWithoutMeshing = 0,
    int SalvagedFromStaleBatch = 0);

internal static class MeshPipelineWorkSelection
{
    public static WorldMeshBatch FilterPresented(
        VoxelWorld world,
        ChunkPresentationController presentations,
        WorldMeshBatch source)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(presentations);
        ArgumentNullException.ThrowIfNull(source);

        var filtered =
            source.DirtyMeshlets
                .Where(entry =>
                    presentations.Contains(entry.Key) &&
                    world.ContainsChunk(entry.Key))
                .ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value);

        return new WorldMeshBatch(filtered);
    }

    public static int CountMeshlets(
        WorldMeshBatch batch) =>
        batch.DirtyMeshlets
            .Values
            .Sum(mask => mask.SelectedCount);
}

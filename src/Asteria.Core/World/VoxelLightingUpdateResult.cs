namespace Asteria.Core.World;

public sealed record VoxelLightingUpdateResult(
    IReadOnlyList<WorldVoxelCoord> ChangedPositions,
    int ProcessedVoxelCount,
    int MediumSampleRequests = 0,
    int MediumSampleCacheMisses = 0);

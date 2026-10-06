namespace Asteria.Core.World;

public sealed record VoxelLightingUpdateResult(
    IReadOnlyList<WorldVoxelCoord> ChangedPositions,
    int ProcessedVoxelCount);

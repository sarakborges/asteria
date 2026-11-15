namespace Asteria.Core.World;

/// <summary>
/// Enforces the spatial access contract declared by one generation-plan step.
/// Generator-owned data reads and chunk writes have separate envelopes. This
/// region does not grant holder-status access: those prerequisites are fulfilled
/// through section generation futures and dependency claims.
/// </summary>
public sealed class ChunkGenerationAccessRegion
{
    private readonly ChunkCoord _targetSection;
    private readonly ChunkGenerationStepDefinition _step;

    public ChunkGenerationAccessRegion(
        ChunkGenerationPlan plan,
        ChunkCoord targetSection,
        ChunkGenerationStepId stepId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (targetSection.Y < 0)
            throw new ArgumentOutOfRangeException(
                nameof(targetSection), "Sphere chunk Y cannot be negative.");

        _targetSection = targetSection;
        _step = plan.GetStep(stepId);
    }

    public ChunkCoord TargetSection => _targetSection;

    public ChunkGenerationStepId StepId => _step.Id;

    public ChunkGenerationDependencyEnvelope ReadEnvelope => _step.ReadEnvelope;

    public ChunkGenerationDependencyEnvelope WriteEnvelope => _step.WriteEnvelope;

    /// <summary>
    /// Whether immutable generator-owned data for this section may be sampled
    /// by the active step. This does not authorize reading a resident chunk.
    /// </summary>
    public bool CanReadGeneratorData(ChunkCoord sourceSection) =>
        IsWithinEnvelope(sourceSection, _step.ReadEnvelope);

    /// <summary>Whether this step may write into the specified section.</summary>
    public bool CanWrite(ChunkCoord destinationSection) =>
        IsWithinEnvelope(destinationSection, _step.WriteEnvelope);

    public bool CanReadWorldPosition(int worldX, int worldY, int worldZ) =>
        worldY >= 0 &&
        CanReadGeneratorData(
            VoxelCoordinates.FromWorld(worldX, worldY, worldZ).Chunk);

    public bool CanWriteWorldPosition(int worldX, int worldY, int worldZ) =>
        worldY >= 0 &&
        CanWrite(
            VoxelCoordinates.FromWorld(worldX, worldY, worldZ).Chunk);

    public void EnsureCanReadGeneratorData(ChunkCoord sourceSection)
    {
        if (CanReadGeneratorData(sourceSection))
            return;

        throw CreateAccessException(
            "read immutable generator data from",
            sourceSection,
            _step.ReadEnvelope);
    }

    public void EnsureCanWrite(ChunkCoord destinationSection)
    {
        if (CanWrite(destinationSection))
            return;

        throw CreateAccessException(
            "write into",
            destinationSection,
            _step.WriteEnvelope);
    }

    public void EnsureCanReadWorldPosition(int worldX, int worldY, int worldZ)
    {
        var sourceSection =
            VoxelCoordinates.FromWorld(worldX, worldY, worldZ).Chunk;
        if (worldY >= 0 && CanReadGeneratorData(sourceSection))
            return;

        throw new InvalidOperationException(
            $"Generation step {_step.Id} for target section {_targetSection} " +
            $"cannot read immutable generator data at world position " +
            $"({worldX}, {worldY}, {worldZ}) in section {sourceSection}; " +
            $"declared read envelope is {Format(_step.ReadEnvelope)}.");
    }

    public void EnsureCanWriteWorldPosition(int worldX, int worldY, int worldZ)
    {
        var destinationSection =
            VoxelCoordinates.FromWorld(worldX, worldY, worldZ).Chunk;
        if (worldY >= 0 && CanWrite(destinationSection))
            return;

        throw new InvalidOperationException(
            $"Generation step {_step.Id} for target section {_targetSection} " +
            $"cannot write at world position ({worldX}, {worldY}, {worldZ}) " +
            $"in section {destinationSection}; declared write envelope is " +
            $"{Format(_step.WriteEnvelope)}.");
    }

    private bool IsWithinEnvelope(
        ChunkCoord accessedSection,
        ChunkGenerationDependencyEnvelope envelope)
    {
        if (accessedSection.Y < 0)
            return false;

        var deltaX = (long)accessedSection.X - _targetSection.X;
        var deltaY = (long)accessedSection.Y - _targetSection.Y;
        var deltaZ = (long)accessedSection.Z - _targetSection.Z;

        return Math.Max(Math.Abs(deltaX), Math.Abs(deltaZ)) <=
                envelope.HorizontalRadius &&
            deltaY >= envelope.MinimumSectionOffset &&
            deltaY <= envelope.MaximumSectionOffset;
    }

    private InvalidOperationException CreateAccessException(
        string operation,
        ChunkCoord accessedSection,
        ChunkGenerationDependencyEnvelope envelope) =>
        new(
            $"Generation step {_step.Id} for target section {_targetSection} " +
            $"cannot {operation} section {accessedSection}; " +
            $"declared envelope is {Format(envelope)}.");

    private static string Format(ChunkGenerationDependencyEnvelope envelope) =>
        $"horizontal radius {envelope.HorizontalRadius}, " +
        $"section offsets [{envelope.MinimumSectionOffset}, " +
        $"{envelope.MaximumSectionOffset}]";
}

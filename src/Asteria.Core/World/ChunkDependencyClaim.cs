namespace Asteria.Core.World;

/// <summary>
/// Reference-counted lease retaining the holder set required by one generation
/// task. Disposing the lease releases those references exactly once.
/// </summary>
public sealed class ChunkDependencyClaim : IDisposable
{
    private ChunkHolderMap? _owner;
    private readonly ChunkColumnCoord[] _columns;
    private readonly ChunkCoord[] _sectionTargets;

    internal ChunkDependencyClaim(
        ChunkHolderMap owner,
        ChunkColumnCoord[] columns,
        ChunkCoord[] sectionTargets)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _columns = columns ?? throw new ArgumentNullException(nameof(columns));
        _sectionTargets = sectionTargets ?? throw new ArgumentNullException(nameof(sectionTargets));
    }

    public IReadOnlyList<ChunkColumnCoord> Columns => _columns;

    /// <summary>Exact 3D section coordinates covered by the generation dependency envelope.</summary>
    public IReadOnlyList<ChunkCoord> SectionTargets => _sectionTargets;

    /// <summary>
    /// Requests the holder-owned future for an exact section/status retained by
    /// this lease. The horizontal holder is shared across its vertical sections.
    /// </summary>
    public Task<ChunkGenerationFutureResult> GetSectionGenerationFuture(
        ChunkCoord section,
        ChunkGenerationStatus status)
    {
        if (!_sectionTargets.Contains(section))
            throw new ArgumentException(
                $"Section {section} is outside this dependency claim.",
                nameof(section));
        var owner = Volatile.Read(ref _owner) ??
            throw new ObjectDisposedException(nameof(ChunkDependencyClaim));
        return owner.GetSectionGenerationFuture(section, status);
    }

    public Task<ChunkGenerationFutureResult> GetSectionGenerationFuture(
        ChunkGenerationSectionRequirement requirement)
    {
        var owner = Volatile.Read(ref _owner) ??
            throw new ObjectDisposedException(nameof(ChunkDependencyClaim));
        if (!_sectionTargets.Contains(requirement.Coord))
            throw new ArgumentException(
                $"Section {requirement.Coord} is outside this dependency claim.",
                nameof(requirement));
        return owner.GetSectionGenerationFuture(
            requirement.Coord, requirement.Status);
    }

    public void Dispose()
    {
        var owner = Interlocked.Exchange(ref _owner, null);
        owner?.ReleaseDependencyClaims(_columns);
    }
}

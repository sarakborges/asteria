#if DEBUG
using System.Diagnostics;
#endif

namespace Asteria.Core.World;

public sealed partial class SurfaceChunkMaterializer
{
    /// <summary>
    /// Resumable construction of directional-face occupancy masks. Each Advance
    /// performs one bounded preparation phase, allowing terrain setup to yield
    /// between independent horizontal boundary reads.
    /// </summary>
    private sealed class DirectionalFaceOccupancyBuildJob
    {
        private readonly SurfaceChunkMaterializer _owner;
        private readonly Func<int, int, int, bool> _isSolid;
        private readonly SurfaceTerrainColumn _column;
        private readonly int _originX;
        private readonly int _originY;
        private readonly int _originZ;
        private readonly TerrainDensityVolume? _densityVolume;
        private readonly int _maximumDepth;
        private readonly int _regionSize;
        private readonly int _wordCount;
        private readonly ulong[] _xRows;
        private readonly ulong[] _zRows;
        private readonly ulong[] _yRows;
        private readonly ushort[] _xBoundaryMasks;
        private readonly ushort[] _zBoundaryMasks;

        private (int OriginY, DensityOccupancySlab Volume)[] _bottomSlabs = [];
        private (int OriginY, DensityOccupancySlab Volume)[] _topSlabs = [];
        private int _phase;
        private int _densityProbeCount;
        private int _boundaryDensityProbeCount;
        private int _horizontalBoundaryBatchCount;
        private int _horizontalBoundaryColumnCount;
        private double _densitySlabReadMilliseconds;
        private double _horizontalBoundaryReadMilliseconds;
        private double _occupancyRowAssemblyMilliseconds;
        private double _candidateMaskBuildMilliseconds;
        private double _boundaryColumnBoundsMilliseconds;
        private double _caveSamplerBuildMilliseconds;
        private double _boundaryColumnEvaluationMilliseconds;
        private double _densityTerrainColumnMilliseconds;
        private double _occupancyCacheAccessMilliseconds;
        private double _surfaceBiomeSamplingMilliseconds;
        private double _baseHeightSamplingMilliseconds;
        private double _volumeBiomeSamplingMilliseconds;

        public DirectionalFaceOccupancyBuildJob(
            SurfaceChunkMaterializer owner,
            Func<int, int, int, bool> isSolid,
            SurfaceTerrainColumn column,
            int originX,
            int originY,
            int originZ,
            TerrainDensityVolume? densityVolume)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _isSolid = isSolid ?? throw new ArgumentNullException(nameof(isSolid));
            _column = column ?? throw new ArgumentNullException(nameof(column));
            _originX = originX;
            _originY = originY;
            _originZ = originZ;
            _densityVolume = densityVolume;

            _maximumDepth = checked((int)_owner._materials.MaximumDirectionalPaintDepth);
            if (_maximumDepth is < 0 or > 64)
                throw new InvalidOperationException(
                    "Directional face palette depth must be between 0 and 64 voxels.");

            if (_maximumDepth == 0)
            {
                _regionSize = Chunk.Size;
                _wordCount = 1;
                _xRows = Array.Empty<ulong>();
                _zRows = Array.Empty<ulong>();
                _yRows = Array.Empty<ulong>();
                _xBoundaryMasks = Array.Empty<ushort>();
                _zBoundaryMasks = Array.Empty<ushort>();
                Result = new DirectionalFaceOccupancyMasks(
                    0, 1,
                    Array.Empty<ulong>(),
                    Array.Empty<ulong>(),
                    Array.Empty<ulong>(),
                    new ushort[Chunk.Area]);
                IsComplete = true;
                return;
            }

            _regionSize = checked(Chunk.Size + _maximumDepth * 2);
            _wordCount = (_regionSize + 63) >> 6;
            _xRows = new ulong[Chunk.Area * _wordCount];
            _zRows = new ulong[Chunk.Area * _wordCount];
            _yRows = new ulong[Chunk.Area * _wordCount];
            _xBoundaryMasks = new ushort[_regionSize * Chunk.Size];
            _zBoundaryMasks = new ushort[Chunk.Size * _regionSize];
        }

        public bool IsComplete { get; private set; }

        public DirectionalFaceOccupancyMasks Result { get; private set; } = null!;

        public int DensityProbes => _densityProbeCount;

        public int BoundaryDensityProbes => _boundaryDensityProbeCount;

        public DirectionalFaceOccupancyPreparationProfile PreparationProfile { get; private set; }

        /// <summary>
        /// Advances one phase: vertical halo slabs, one cardinal boundary, row
        /// assembly, or final candidate-mask construction.
        /// </summary>
        public bool Advance()
        {
            if (IsComplete)
                return true;

            switch (_phase)
            {
                case 0:
                    SampleVerticalSlabs();
                    break;
                case 1:
                    ReadLeftXBoundary();
                    break;
                case 2:
                    ReadRightXBoundary();
                    break;
                case 3:
                    ReadNegativeZBoundary();
                    break;
                case 4:
                    ReadPositiveZBoundary();
                    break;
                case 5:
                    AssembleOccupancyRows();
                    break;
                case 6:
                    BuildCandidateMask();
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Directional occupancy builder entered invalid phase {_phase}.");
            }

            _phase++;
            return IsComplete;
        }

        private void SampleVerticalSlabs()
        {
#if DEBUG
            var started = Stopwatch.GetTimestamp();
#endif
            _bottomSlabs = _owner.SampleVerticalDensitySlabs(
                _column,
                _originX,
                _originZ,
                Math.Max(0, _originY - _maximumDepth),
                _originY,
                out var bottomDensityProbes);
            var topStartY = checked(_originY + Chunk.Size);
            _topSlabs = _owner.SampleVerticalDensitySlabs(
                _column,
                _originX,
                _originZ,
                topStartY,
                checked(topStartY + _maximumDepth),
                out var topDensityProbes);
            _densityProbeCount = checked(bottomDensityProbes + topDensityProbes);
            _boundaryDensityProbeCount = _densityProbeCount;
#if DEBUG
            _densitySlabReadMilliseconds =
                Stopwatch.GetElapsedTime(started).TotalMilliseconds;
#endif
        }

        private void ReadLeftXBoundary()
        {
            Span<int> columnIndices = stackalloc int[Chunk.Area];
            Span<ushort> requestedYMasks = stackalloc ushort[Chunk.Area];
            Span<ushort> solidMasks = stackalloc ushort[Chunk.Area];
#if DEBUG
            var started = Stopwatch.GetTimestamp();
#endif
            var metrics = _owner.ReadDirectionalFaceXBoundaryMasks(
                -_maximumDepth,
                0,
                _maximumDepth,
                _originX,
                _originY,
                _originZ,
                _densityVolume,
                _xBoundaryMasks,
                columnIndices,
                requestedYMasks,
                solidMasks);
            RecordBoundaryMetrics(metrics);
#if DEBUG
            _horizontalBoundaryReadMilliseconds +=
                Stopwatch.GetElapsedTime(started).TotalMilliseconds;
#endif
        }

        private void ReadRightXBoundary()
        {
            Span<int> columnIndices = stackalloc int[Chunk.Area];
            Span<ushort> requestedYMasks = stackalloc ushort[Chunk.Area];
            Span<ushort> solidMasks = stackalloc ushort[Chunk.Area];
#if DEBUG
            var started = Stopwatch.GetTimestamp();
#endif
            var metrics = _owner.ReadDirectionalFaceXBoundaryMasks(
                Chunk.Size,
                Chunk.Size + _maximumDepth,
                _maximumDepth,
                _originX,
                _originY,
                _originZ,
                _densityVolume,
                _xBoundaryMasks,
                columnIndices,
                requestedYMasks,
                solidMasks);
            RecordBoundaryMetrics(metrics);
#if DEBUG
            _horizontalBoundaryReadMilliseconds +=
                Stopwatch.GetElapsedTime(started).TotalMilliseconds;
#endif
        }

        private void ReadNegativeZBoundary()
        {
            Span<int> columnIndices = stackalloc int[Chunk.Area];
            Span<ushort> requestedYMasks = stackalloc ushort[Chunk.Area];
            Span<ushort> solidMasks = stackalloc ushort[Chunk.Area];
#if DEBUG
            var started = Stopwatch.GetTimestamp();
#endif
            var metrics = _owner.ReadDirectionalFaceZBoundaryMasks(
                -_maximumDepth,
                0,
                _maximumDepth,
                _originX,
                _originY,
                _originZ,
                _regionSize,
                _densityVolume,
                _zBoundaryMasks,
                columnIndices,
                requestedYMasks,
                solidMasks);
            RecordBoundaryMetrics(metrics);
#if DEBUG
            _horizontalBoundaryReadMilliseconds +=
                Stopwatch.GetElapsedTime(started).TotalMilliseconds;
#endif
        }

        private void ReadPositiveZBoundary()
        {
            Span<int> columnIndices = stackalloc int[Chunk.Area];
            Span<ushort> requestedYMasks = stackalloc ushort[Chunk.Area];
            Span<ushort> solidMasks = stackalloc ushort[Chunk.Area];
#if DEBUG
            var started = Stopwatch.GetTimestamp();
#endif
            var metrics = _owner.ReadDirectionalFaceZBoundaryMasks(
                Chunk.Size,
                Chunk.Size + _maximumDepth,
                _maximumDepth,
                _originX,
                _originY,
                _originZ,
                _regionSize,
                _densityVolume,
                _zBoundaryMasks,
                columnIndices,
                requestedYMasks,
                solidMasks);
            RecordBoundaryMetrics(metrics);
#if DEBUG
            _horizontalBoundaryReadMilliseconds +=
                Stopwatch.GetElapsedTime(started).TotalMilliseconds;
#endif
        }

        private void RecordBoundaryMetrics(BoundaryMaskReadMetrics metrics)
        {
            _densityProbeCount = checked(_densityProbeCount + metrics.DensityProbes);
            _boundaryDensityProbeCount = checked(
                _boundaryDensityProbeCount + metrics.DensityProbes);
            _horizontalBoundaryBatchCount = checked(
                _horizontalBoundaryBatchCount + metrics.BatchCount);
            _horizontalBoundaryColumnCount = checked(
                _horizontalBoundaryColumnCount + metrics.ColumnCount);
            _boundaryColumnBoundsMilliseconds += metrics.ColumnBoundsMilliseconds;
            _caveSamplerBuildMilliseconds += metrics.CaveSamplerBuildMilliseconds;
            _boundaryColumnEvaluationMilliseconds += metrics.ColumnEvaluationMilliseconds;
            _densityTerrainColumnMilliseconds += metrics.DensityTerrainColumnMilliseconds;
            _occupancyCacheAccessMilliseconds += metrics.OccupancyCacheAccessMilliseconds;
            _surfaceBiomeSamplingMilliseconds += metrics.SurfaceBiomeSamplingMilliseconds;
            _baseHeightSamplingMilliseconds += metrics.BaseHeightSamplingMilliseconds;
            _volumeBiomeSamplingMilliseconds += metrics.VolumeBiomeSamplingMilliseconds;
        }

        private bool VerticalHaloIsSolid(int localX, int localY, int localZ)
        {
            var worldY = checked(_originY + localY);
            var slabs = localY < 0 ? _bottomSlabs : _topSlabs;
            foreach (var slab in slabs)
            {
                var slabLocalY = worldY - slab.OriginY;
                if ((uint)slabLocalY < (uint)slab.Volume.Height)
                    return slab.Volume.IsSolidAtUnchecked(
                        localX, slabLocalY, localZ);
            }

            throw new InvalidOperationException(
                $"No sampled vertical halo slab covers world Y={worldY}.");
        }

        private static void MarkSolid(ulong[] rows, int rowOffset, int bitIndex) =>
            rows[rowOffset + (bitIndex >> 6)] |= 1UL << (bitIndex & 63);

        private static void MarkMask(
            ulong[] rows,
            int rowOffset,
            int bitIndex,
            ushort mask)
        {
            if (mask == 0)
                return;

            var wordOffset = bitIndex >> 6;
            var bitOffset = bitIndex & 63;
            rows[rowOffset + wordOffset] |= (ulong)mask << bitOffset;
            if (bitOffset == 0)
                return;

            var overflow = (ulong)mask >> (64 - bitOffset);
            if (overflow != 0)
                rows[rowOffset + wordOffset + 1] |= overflow;
        }

        private static bool IsSolid(ulong[] rows, int rowOffset, int bitIndex) =>
            (rows[rowOffset + (bitIndex >> 6)] &
             (1UL << (bitIndex & 63))) != 0;

        private void AssembleOccupancyRows()
        {
#if DEBUG
            var started = Stopwatch.GetTimestamp();
#endif
            var hasDensityColumnMasks =
                _densityVolume is not null &&
                _densityVolume.Width == Chunk.Size &&
                _densityVolume.Height == Chunk.Size &&
                _densityVolume.Depth == Chunk.Size;
            if (hasDensityColumnMasks)
            {
                // The main terrain volume already stores one 16-bit occupancy mask
                // per X/Z column. Copy those masks directly instead of probing
                // every interior voxel through a delegate.
                var belowWorldMinimumCount = Math.Clamp(
                    _maximumDepth - _originY,
                    0,
                    _maximumDepth);
                var belowWorldMinimumMask = belowWorldMinimumCount switch
                {
                    0 => 0UL,
                    64 => ulong.MaxValue,
                    _ => (1UL << belowWorldMinimumCount) - 1UL,
                };

                for (var z = 0; z < Chunk.Size; z++)
                for (var x = 0; x < Chunk.Size; x++)
                {
                    var rowOffset = (z * Chunk.Size + x) * _wordCount;
                    _yRows[rowOffset] |= belowWorldMinimumMask;
                    var solidMask =
                        _densityVolume!.SolidColumnMaskAtUnchecked(x, z);
                    MarkMask(
                        _yRows,
                        rowOffset,
                        _maximumDepth,
                        solidMask);

                    // Reuse the same source mask to populate the interior X/Z rows.
                    var remainingSolidMask = (uint)solidMask;
                    while (remainingSolidMask != 0)
                    {
                        var localY = System.Numerics.BitOperations
                            .TrailingZeroCount(remainingSolidMask);
                        MarkSolid(
                            _xRows,
                            (localY * Chunk.Size + z) * _wordCount,
                            _maximumDepth + x);
                        MarkSolid(
                            _zRows,
                            (localY * Chunk.Size + x) * _wordCount,
                            _maximumDepth + z);
                        remainingSolidMask &= remainingSolidMask - 1;
                    }
                }

                foreach (var slab in _bottomSlabs)
                {
                    var bitStart = checked(
                        _maximumDepth + slab.OriginY - _originY);
                    var masks = slab.Volume.ColumnSolidMasks;
                    for (var z = 0; z < Chunk.Size; z++)
                    for (var x = 0; x < Chunk.Size; x++)
                    {
                        var rowOffset = (z * Chunk.Size + x) * _wordCount;
                        MarkMask(
                            _yRows,
                            rowOffset,
                            bitStart,
                            masks[z * Chunk.Size + x]);
                    }
                }

                foreach (var slab in _topSlabs)
                {
                    var bitStart = checked(
                        _maximumDepth + slab.OriginY - _originY);
                    var masks = slab.Volume.ColumnSolidMasks;
                    for (var z = 0; z < Chunk.Size; z++)
                    for (var x = 0; x < Chunk.Size; x++)
                    {
                        var rowOffset = (z * Chunk.Size + x) * _wordCount;
                        MarkMask(
                            _yRows,
                            rowOffset,
                            bitStart,
                            masks[z * Chunk.Size + x]);
                    }
                }
            }
            else
            {
                for (var z = 0; z < Chunk.Size; z++)
                for (var x = 0; x < Chunk.Size; x++)
                {
                    var rowOffset = (z * Chunk.Size + x) * _wordCount;
                    for (var regionY = 0; regionY < _regionSize; regionY++)
                    {
                        var localY = regionY - _maximumDepth;
                        bool solid;
                        if ((uint)localY < Chunk.Size)
                            solid = _isSolid(x, localY, z);
                        else
                        {
                            var worldY = checked(_originY + localY);
                            solid = worldY < 0 || VerticalHaloIsSolid(
                                x, localY, z);
                        }
                        if (solid)
                            MarkSolid(_yRows, rowOffset, regionY);
                    }
                }
            }

            if (!hasDensityColumnMasks)
            {
                // Fallback providers expose occupancy only through an indexer.
                for (var y = 0; y < Chunk.Size; y++)
                for (var z = 0; z < Chunk.Size; z++)
                for (var x = 0; x < Chunk.Size; x++)
                {
                    var yRowOffset = (z * Chunk.Size + x) * _wordCount;
                    if (!IsSolid(_yRows, yRowOffset, _maximumDepth + y))
                        continue;

                    var xRowOffset = (y * Chunk.Size + z) * _wordCount;
                    MarkSolid(_xRows, xRowOffset, _maximumDepth + x);

                    var zRowOffset = (y * Chunk.Size + x) * _wordCount;
                    MarkSolid(_zRows, zRowOffset, _maximumDepth + z);
                }
            }

            // Boundary masks already carry all Y occupancy for one horizontal
            // column. Iterate set bits instead of scanning every Y.
            for (var z = 0; z < Chunk.Size; z++)
            {
                for (var regionX = 0; regionX < _maximumDepth; regionX++)
                {
                    var remainingMask =
                        (uint)_xBoundaryMasks[regionX * Chunk.Size + z];
                    while (remainingMask != 0)
                    {
                        var y = System.Numerics.BitOperations
                            .TrailingZeroCount(remainingMask);
                        var rowOffset =
                            (y * Chunk.Size + z) * _wordCount;
                        MarkSolid(_xRows, rowOffset, regionX);
                        remainingMask &= remainingMask - 1;
                    }
                }

                for (var regionX = _maximumDepth + Chunk.Size;
                     regionX < _regionSize;
                     regionX++)
                {
                    var remainingMask =
                        (uint)_xBoundaryMasks[regionX * Chunk.Size + z];
                    while (remainingMask != 0)
                    {
                        var y = System.Numerics.BitOperations
                            .TrailingZeroCount(remainingMask);
                        var rowOffset =
                            (y * Chunk.Size + z) * _wordCount;
                        MarkSolid(_xRows, rowOffset, regionX);
                        remainingMask &= remainingMask - 1;
                    }
                }
            }

            for (var x = 0; x < Chunk.Size; x++)
            {
                for (var regionZ = 0; regionZ < _maximumDepth; regionZ++)
                {
                    var remainingMask =
                        (uint)_zBoundaryMasks[x * _regionSize + regionZ];
                    while (remainingMask != 0)
                    {
                        var y = System.Numerics.BitOperations
                            .TrailingZeroCount(remainingMask);
                        var rowOffset =
                            (y * Chunk.Size + x) * _wordCount;
                        MarkSolid(_zRows, rowOffset, regionZ);
                        remainingMask &= remainingMask - 1;
                    }
                }

                for (var regionZ = _maximumDepth + Chunk.Size;
                     regionZ < _regionSize;
                     regionZ++)
                {
                    var remainingMask =
                        (uint)_zBoundaryMasks[x * _regionSize + regionZ];
                    while (remainingMask != 0)
                    {
                        var y = System.Numerics.BitOperations
                            .TrailingZeroCount(remainingMask);
                        var rowOffset =
                            (y * Chunk.Size + x) * _wordCount;
                        MarkSolid(_zRows, rowOffset, regionZ);
                        remainingMask &= remainingMask - 1;
                    }
                }
            }
#if DEBUG
            _occupancyRowAssemblyMilliseconds =
                Stopwatch.GetElapsedTime(started).TotalMilliseconds;
#endif
        }

        private void BuildCandidateMask()
        {
#if DEBUG
            var started = Stopwatch.GetTimestamp();
#endif
            var candidateColumns = DirectionalFaceCandidateMask.Build(
                _regionSize,
                _wordCount,
                _maximumDepth,
                _xRows,
                _zRows,
                _yRows);
#if DEBUG
            _candidateMaskBuildMilliseconds =
                Stopwatch.GetElapsedTime(started).TotalMilliseconds;
#endif
            PreparationProfile = new DirectionalFaceOccupancyPreparationProfile(
                _densitySlabReadMilliseconds,
                _horizontalBoundaryReadMilliseconds,
                _occupancyRowAssemblyMilliseconds,
                _candidateMaskBuildMilliseconds,
                _boundaryColumnBoundsMilliseconds,
                _caveSamplerBuildMilliseconds,
                _boundaryColumnEvaluationMilliseconds,
                _densityTerrainColumnMilliseconds,
                _occupancyCacheAccessMilliseconds,
                _surfaceBiomeSamplingMilliseconds,
                _baseHeightSamplingMilliseconds,
                _volumeBiomeSamplingMilliseconds,
                _horizontalBoundaryBatchCount,
                _horizontalBoundaryColumnCount);
            Result = new DirectionalFaceOccupancyMasks(
                _maximumDepth,
                _wordCount,
                _xRows,
                _zRows,
                _yRows,
                candidateColumns);
            IsComplete = true;
        }
    }
}

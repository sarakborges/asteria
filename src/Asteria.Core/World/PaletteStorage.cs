using System.Runtime.CompilerServices;

namespace Asteria.Core.World;

internal readonly record struct PaletteStorageCapacityMetrics(
    int OccupiedVoxelCount,
    int PaletteEntryCount,
    int PaletteCapacity,
    int UsageEntryCount,
    int UsageCapacity,
    int LookupEntryCount,
    int LookupCapacity,
    int FreeSlotCount,
    int FreeSlotCapacity,
    int IndexCount,
    int OccupancyWordCount,
    int MeshletOccupancyCount,
    bool GrowthDiagnosticsAvailable,
    long PaletteGrowthArrayBytes,
    int PaletteGrowthEvents,
    long UsageGrowthArrayBytes,
    int UsageGrowthEvents,
    long LookupBucketGrowthArrayBytes,
    long LookupEntryGrowthArrayBytes,
    int LookupGrowthEvents,
    long FreeSlotGrowthArrayBytes,
    int FreeSlotGrowthEvents);

internal sealed class PaletteStorage<T>
    where T : struct, IEquatable<T>
{
    // Worker snapshots share storage until one side mutates. The shared state
    // is immutable once IsShared becomes true; Set detaches before writing.
    // This preserves independent chunk semantics without copying a full 4096
    // voxel index array for every mesh-worker dependency chunk.
    private sealed class StorageState
    {
        public StorageState()
        {
            Palette = [];
            Usage = [];
            Lookup = [];
            FreeSlots = new Stack<ushort>();
            Indices = new ushort[Chunk.Volume];
            Occupied = new ulong[(Chunk.Volume + 63) / 64];
            MeshletOccupiedCounts = new int[ChunkMeshletMask.Count];
        }

        private StorageState(StorageState source)
        {
            Palette = new List<T>(source.Palette);
            Usage = new List<ushort>(source.Usage);
            Lookup = new Dictionary<T, ushort>(source.Lookup);
            FreeSlots = new Stack<ushort>(source.FreeSlots.Reverse());
            Indices = (ushort[])source.Indices.Clone();
            Occupied = (ulong[])source.Occupied.Clone();
            OccupiedCount = source.OccupiedCount;
            MeshletOccupiedCounts =
                (int[])source.MeshletOccupiedCounts.Clone();
        }

        public List<T> Palette { get; }

        public List<ushort> Usage { get; }

        public Dictionary<T, ushort> Lookup { get; }

        public Stack<ushort> FreeSlots { get; }

        public ushort[] Indices { get; }

        public ulong[] Occupied { get; }

        public int[] MeshletOccupiedCounts { get; }

        public int OccupiedCount { get; set; }

#if DEBUG
        // Cumulative payload bytes of arrays allocated when these collections
        // increased capacity. Superseded arrays are included; object/array
        // headers and unrelated allocations are intentionally excluded.
        public long PaletteGrowthArrayBytes { get; set; }
        public int PaletteGrowthEvents { get; set; }
        public long UsageGrowthArrayBytes { get; set; }
        public int UsageGrowthEvents { get; set; }
        public long LookupBucketGrowthArrayBytes { get; set; }
        public long LookupEntryGrowthArrayBytes { get; set; }
        public int LookupGrowthEvents { get; set; }
        public long FreeSlotGrowthArrayBytes { get; set; }
        public int FreeSlotGrowthEvents { get; set; }
#endif

        public bool IsShared { get; set; }

        public StorageState Copy() => new(this);
    }

    private StorageState _state = new();

    public PaletteStorage()
    {
    }

    public int OccupiedCount => _state.OccupiedCount;

    public int ActivePaletteEntryCount => _state.Lookup.Count;

    internal PaletteStorageCapacityMetrics CapacityMetrics
    {
        get
        {
            var state = _state;
            return new PaletteStorageCapacityMetrics(
                state.OccupiedCount,
                state.Palette.Count,
                state.Palette.Capacity,
                state.Usage.Count,
                state.Usage.Capacity,
                state.Lookup.Count,
                state.Lookup.EnsureCapacity(0),
                state.FreeSlots.Count,
                state.FreeSlots.EnsureCapacity(0),
                state.Indices.Length,
                state.Occupied.Length,
                state.MeshletOccupiedCounts.Length,
#if DEBUG
                true,
                state.PaletteGrowthArrayBytes,
                state.PaletteGrowthEvents,
                state.UsageGrowthArrayBytes,
                state.UsageGrowthEvents,
                state.LookupBucketGrowthArrayBytes,
                state.LookupEntryGrowthArrayBytes,
                state.LookupGrowthEvents,
                state.FreeSlotGrowthArrayBytes,
                state.FreeSlotGrowthEvents);
#else
                false,
                0L, 0, 0L, 0, 0L, 0L, 0, 0L, 0);
#endif
        }
    }

    public int OccupiedCountInMeshlet(int meshletIndex)
    {
        ChunkMeshletMask.Bounds(meshletIndex);
        return _state.MeshletOccupiedCounts[meshletIndex];
    }

    public T Get(int voxelIndex)
    {
        var state = _state;
        var paletteIndex = state.Indices[voxelIndex];

        return paletteIndex == 0
            ? default
            : state.Palette[paletteIndex - 1];
    }

    public bool Set(int voxelIndex, T value) =>
        Set(voxelIndex, value, out _);

    public bool Set(int voxelIndex, T value, out T previous)
    {
        var state = _state;
        var previousIndex = state.Indices[voxelIndex];
        previous = ValueAt(state, previousIndex);

        if (previous.Equals(value))
            return false;

        // A no-op write above does not detach a shared snapshot. Any real
        // write first obtains a private state, including palette bookkeeping.
        if (state.IsShared)
        {
            state = state.Copy();
            _state = state;
        }

        var nextIndex =
            value.Equals(default)
                ? (ushort)0
                : FindOrInsert(value);

        Release(previousIndex);
        Retain(nextIndex);

        state.Indices[voxelIndex] = nextIndex;
        UpdateOccupancy(
            voxelIndex,
            previousIndex != 0,
            nextIndex != 0);

        return true;
    }

    /// <summary>
    /// Initializes one voxel in a fresh palette without running the normal
    /// replacement path. The caller owns a batch that writes each occupied
    /// index only once and commits the chunk revision after the batch.
    /// </summary>
    public bool SetInitial(int voxelIndex, T value) =>
        SetInitialCore(voxelIndex, value, skipOccupied: false);
    
    /// <summary>
    /// Writes an initial palette value using a slot cached by the batch writer.
    /// On a cache miss, the value is interned once and the resolved slot is
    /// returned for subsequent writes of the same runtime block id.
    /// </summary>
    /// <summary>
    /// Verifies once that a batch writer may use the private-storage fast path.
    /// The owning chunk must remain unpublished until that writer completes.
    /// </summary>
    internal void EnsurePrivateForInitialWrites()
    {
        if (_state.IsShared)
            throw new InvalidOperationException(
                "Initial palette writes require private chunk storage.");
    }

    internal bool SetInitialWithPaletteIndex(
        int voxelIndex,
        T value,
        ushort cachedPaletteIndex,
        out ushort resolvedPaletteIndex)
    {
        if (_state.IsShared)
            throw new InvalidOperationException(
                "Initial palette writes require private chunk storage.");
        if ((uint)voxelIndex >= Chunk.Volume)
            throw new ArgumentOutOfRangeException(nameof(voxelIndex));

        return SetInitialWithPaletteIndexUnchecked(
            voxelIndex, value, cachedPaletteIndex, out resolvedPaletteIndex);
    }

    /// <summary>
    /// Initial write for a caller that has already proven the linear index.
    /// </summary>
    internal bool SetInitialWithPaletteIndexUnchecked(
        int voxelIndex,
        T value,
        ushort cachedPaletteIndex,
        out ushort resolvedPaletteIndex)
    {
        var state = _state;
        var previousIndex = state.Indices[voxelIndex];
        if (previousIndex != 0)
        {
            if (ValueAt(state, previousIndex).Equals(value))
            {
                resolvedPaletteIndex =
                    cachedPaletteIndex != 0 ? cachedPaletteIndex : previousIndex;
                return false;
            }

            throw new InvalidOperationException(
                "An initial palette write cannot replace an occupied voxel.");
        }

        if (value.Equals(default))
        {
            resolvedPaletteIndex = 0;
            return false;
        }

        var nextIndex = cachedPaletteIndex;
        if (nextIndex == 0)
        {
            if (!state.Lookup.TryGetValue(value, out nextIndex))
            {
                if (state.Palette.Count >= ushort.MaxValue)
                    throw new InvalidOperationException(
                        "Chunk palette capacity was exhausted.");

                AddPaletteEntry(state, value);
                AddUsageEntry(state);
                nextIndex = checked((ushort)state.Palette.Count);
                AddLookupEntry(state, value, nextIndex);
            }
        }
        else if (nextIndex > state.Palette.Count)
        {
            throw new InvalidOperationException(
                "Cached initial palette index is outside the active palette.");
        }

        Retain(nextIndex);
        state.Indices[voxelIndex] = nextIndex;
        UpdateOccupancy(
            voxelIndex,
            wasOccupied: false,
            isOccupied: true);
        resolvedPaletteIndex = nextIndex;
        return true;
    }

    /// <summary>
    /// Writes into a slot that the caller guarantees has never been written.
    /// The owning chunk must be fresh and private, and each linear index must
    /// be supplied at most once. Used only by the bounded TerrainLayer pass.
    /// </summary>
    internal bool SetInitialFreshSlotWithPaletteIndexUnchecked(
        int voxelIndex,
        T value,
        ushort cachedPaletteIndex,
        out ushort resolvedPaletteIndex)
    {
        var state = _state;
        if (value.Equals(default))
        {
            resolvedPaletteIndex = 0;
            return false;
        }

        var nextIndex = cachedPaletteIndex;
        if (nextIndex == 0 &&
            !state.Lookup.TryGetValue(value, out nextIndex))
        {
            if (state.Palette.Count >= ushort.MaxValue)
                throw new InvalidOperationException(
                    "Chunk palette capacity was exhausted.");

            AddPaletteEntry(state, value);
            AddUsageEntry(state);
            nextIndex = checked((ushort)state.Palette.Count);
            AddLookupEntry(state, value, nextIndex);
        }

        Retain(nextIndex);
        state.Indices[voxelIndex] = nextIndex;
        UpdateOccupancy(
            voxelIndex,
            wasOccupied: false,
            isOccupied: true);
        resolvedPaletteIndex = nextIndex;
        return true;
    }

    /// <summary>
    /// Adds an initial value only when the slot is empty. Used by generation
    /// passes that batch additions while preserving pre-existing structure data.
    /// </summary>
    public bool SetInitialIfEmpty(int voxelIndex, T value) =>
        SetInitialCore(voxelIndex, value, skipOccupied: true);

    private bool SetInitialCore(
        int voxelIndex,
        T value,
        bool skipOccupied)
    {
        var state = _state;
        if (state.IsShared)
            throw new InvalidOperationException(
                "Initial palette writes require private chunk storage.");

        var previousIndex = state.Indices[voxelIndex];
        if (previousIndex != 0)
        {
            if (skipOccupied || ValueAt(state, previousIndex).Equals(value))
                return false;

            throw new InvalidOperationException(
                "An initial palette write cannot replace an occupied voxel.");
        }

        if (value.Equals(default))
            return false;

        ushort nextIndex;
        if (!state.Lookup.TryGetValue(value, out nextIndex))
        {
            if (state.Palette.Count >= ushort.MaxValue)
                throw new InvalidOperationException(
                    "Chunk palette capacity was exhausted.");

            AddPaletteEntry(state, value);
            AddUsageEntry(state);
            nextIndex = checked((ushort)state.Palette.Count);
            AddLookupEntry(state, value, nextIndex);
        }

        Retain(nextIndex);
        state.Indices[voxelIndex] = nextIndex;
        UpdateOccupancy(voxelIndex, wasOccupied: false, isOccupied: true);
        return true;
    }

    public void VisitOccupied(
        Action<int, T> visit)
    {
        ArgumentNullException.ThrowIfNull(visit);

        VisitOccupied(
            0,
            int.MaxValue,
            visit,
            out _);
    }

    public int VisitOccupied(
        int startVoxelIndex,
        int maximumItems,
        Action<int, T> visit,
        out int visitedCount)
    {
        if (startVoxelIndex < 0 ||
            startVoxelIndex > Chunk.Volume)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startVoxelIndex));
        }

        if (maximumItems <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumItems));
        }

        ArgumentNullException.ThrowIfNull(visit);
        visitedCount = 0;

        var state = _state;
        if (startVoxelIndex == Chunk.Volume ||
            state.OccupiedCount == 0)
        {
            return Chunk.Volume;
        }

        var firstWordIndex = startVoxelIndex >> 6;
        var firstBitOffset = startVoxelIndex & 63;
        for (var wordIndex = firstWordIndex;
             wordIndex < state.Occupied.Length;
             wordIndex++)
        {
            var remaining = state.Occupied[wordIndex];
            if (wordIndex == firstWordIndex &&
                firstBitOffset != 0)
            {
                remaining &= ulong.MaxValue << firstBitOffset;
            }

            while (remaining != 0)
            {
                var bit =
                    System.Numerics.BitOperations
                        .TrailingZeroCount(remaining);
                var voxelIndex = wordIndex * 64 + bit;
                var paletteIndex = state.Indices[voxelIndex];

                if (paletteIndex != 0)
                {
                    visit(
                        voxelIndex,
                        state.Palette[paletteIndex - 1]);
                    visitedCount++;

                    if (visitedCount >= maximumItems)
                        return voxelIndex + 1;
                }

                remaining &= remaining - 1;
            }
        }

        return Chunk.Volume;
    }

    // A chunk row is 16 voxels wide and never crosses a 64-bit
    // occupancy word. This tests a box using only its packed bitset,
    // without visiting or unpacking voxel palette entries.
    public int VisitOccupiedInBox(
        int minX,
        int minY,
        int minZ,
        int width,
        int height,
        int depth,
        Action<int, T> visit)
    {
        if (width <= 0 || height <= 0 || depth <= 0 ||
            !Chunk.Contains(minX, minY, minZ) ||
            !Chunk.Contains(
                minX + width - 1,
                minY + height - 1,
                minZ + depth - 1))
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        ArgumentNullException.ThrowIfNull(visit);
        var state = _state;
        if (state.OccupiedCount == 0)
            return 0;

        var visitedCount = 0;
        var xEnd = minX + width;
        var yEnd = minY + height;
        var zEnd = minZ + depth;
        for (var y = minY; y < yEnd; y++)
        {
            for (var z = minZ; z < zEnd; z++)
            {
                var x = minX;
                while (x < xEnd)
                {
                    var first = x + Chunk.Size * (z + Chunk.Size * y);
                    var bitOffset = first & 63;
                    var segmentLength = Math.Min(xEnd - x, 64 - bitOffset);
                    var rowMask =
                        ((1UL << segmentLength) - 1UL) << bitOffset;
                    var remaining = state.Occupied[first >> 6] & rowMask;
                    var wordStart = (first >> 6) * 64;

                    while (remaining != 0)
                    {
                        var bit = System.Numerics.BitOperations
                            .TrailingZeroCount(remaining);
                        var voxelIndex = wordStart + bit;
                        var paletteIndex = state.Indices[voxelIndex];
                        if (paletteIndex != 0)
                        {
                            visit(voxelIndex, state.Palette[paletteIndex - 1]);
                            visitedCount++;
                        }

                        remaining &= remaining - 1;
                    }

                    x += segmentLength;
                }
            }
        }

        return visitedCount;
    }

    public bool AnyOccupiedInBox(
        int minX,
        int minY,
        int minZ,
        int width,
        int height,
        int depth)
    {
        if (width <= 0 || height <= 0 || depth <= 0 ||
            !Chunk.Contains(minX, minY, minZ) ||
            !Chunk.Contains(
                minX + width - 1,
                minY + height - 1,
                minZ + depth - 1))
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        var state = _state;
        var rowMask = (1UL << width) - 1UL;
        for (var y = minY; y < minY + height; y++)
        {
            for (var z = minZ; z < minZ + depth; z++)
            {
                var first = minX + Chunk.Size * (z + Chunk.Size * y);
                if ((state.Occupied[first >> 6] &
                     (rowMask << (first & 63))) != 0UL)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public PaletteStorage<T> Clone()
    {
        var state = _state;
        state.IsShared = true;
        return new PaletteStorage<T>(state);
    }

    private PaletteStorage(StorageState sharedState)
    {
        _state = sharedState;
        sharedState.IsShared = true;
    }

    private static T ValueAt(StorageState state, ushort paletteIndex) =>
        paletteIndex == 0
            ? default
            : state.Palette[paletteIndex - 1];

    private ushort FindOrInsert(T value)
    {
        var state = _state;
        if (state.Lookup.TryGetValue(value, out var existing))
            return existing;

        ushort index;
        if (state.FreeSlots.TryPop(out var reusable))
        {
            index = reusable;
            state.Palette[index - 1] = value;
        }
        else
        {
            if (state.Palette.Count >= ushort.MaxValue)
                throw new InvalidOperationException(
                    "Chunk palette capacity was exhausted.");

            AddPaletteEntry(state, value);
            AddUsageEntry(state);
            index = checked((ushort)state.Palette.Count);
        }

        AddLookupEntry(state, value, index);
        return index;
    }

    private static void AddPaletteEntry(StorageState state, T value)
    {
#if DEBUG
        var previousCapacity = state.Palette.Capacity;
#endif
        state.Palette.Add(value);
#if DEBUG
        if (state.Palette.Capacity != previousCapacity)
        {
            state.PaletteGrowthArrayBytes +=
                (long)state.Palette.Capacity * Unsafe.SizeOf<T>();
            state.PaletteGrowthEvents++;
        }
#endif
    }

    private static void AddUsageEntry(StorageState state)
    {
#if DEBUG
        var previousCapacity = state.Usage.Capacity;
#endif
        state.Usage.Add(0);
#if DEBUG
        if (state.Usage.Capacity != previousCapacity)
        {
            state.UsageGrowthArrayBytes +=
                (long)state.Usage.Capacity * sizeof(ushort);
            state.UsageGrowthEvents++;
        }
#endif
    }

    private static void AddLookupEntry(
        StorageState state,
        T value,
        ushort paletteIndex)
    {
#if DEBUG
        var previousCapacity = state.Lookup.EnsureCapacity(0);
#endif
        state.Lookup.Add(value, paletteIndex);
#if DEBUG
        var nextCapacity = state.Lookup.EnsureCapacity(0);
        if (nextCapacity != previousCapacity)
        {
            state.LookupBucketGrowthArrayBytes +=
                (long)nextCapacity * sizeof(int);
            state.LookupEntryGrowthArrayBytes +=
                (long)nextCapacity * Unsafe.SizeOf<LookupEntryLayout>();
            state.LookupGrowthEvents++;
        }
#endif
    }

    private static void PushFreeSlot(
        StorageState state,
        ushort paletteIndex)
    {
#if DEBUG
        var previousCapacity = state.FreeSlots.EnsureCapacity(0);
#endif
        state.FreeSlots.Push(paletteIndex);
#if DEBUG
        var nextCapacity = state.FreeSlots.EnsureCapacity(0);
        if (nextCapacity != previousCapacity)
        {
            state.FreeSlotGrowthArrayBytes +=
                (long)nextCapacity * sizeof(ushort);
            state.FreeSlotGrowthEvents++;
        }
#endif
    }

    // Mirrors the field order of Dictionary<TKey, TValue>.Entry on the
    // supported .NET runtime so the benchmark can estimate entry-array payload.
    private readonly struct LookupEntryLayout
    {
        public readonly int HashCode;
        public readonly int Next;
        public readonly T Key;
        public readonly ushort Value;
    }

    private void Retain(ushort paletteIndex)
    {
        if (paletteIndex == 0)
            return;

        var state = _state;
        var index = paletteIndex - 1;
        state.Usage[index] =
            checked((ushort)(state.Usage[index] + 1));
    }

    private void Release(ushort paletteIndex)
    {
        if (paletteIndex == 0)
            return;

        var state = _state;
        var index = paletteIndex - 1;
        var usage = state.Usage[index];
        if (usage == 0)
            throw new InvalidOperationException(
                "Chunk palette usage cannot underflow.");

        usage--;
        state.Usage[index] = usage;
        if (usage != 0)
            return;

        var value = state.Palette[index];
        state.Lookup.Remove(value);
        PushFreeSlot(state, paletteIndex);
    }

    private void UpdateOccupancy(
        int voxelIndex,
        bool wasOccupied,
        bool isOccupied)
    {
        if (wasOccupied == isOccupied)
            return;

        var state = _state;
        var word = voxelIndex >> 6;
        var mask = 1UL << (voxelIndex & 63);

        if (isOccupied)
        {
            state.Occupied[word] |= mask;
            state.OccupiedCount++;
        }
        else
        {
            state.Occupied[word] &= ~mask;
            state.OccupiedCount--;
        }

        var localY = voxelIndex / Chunk.Area;
        var localZ = voxelIndex / Chunk.Size % Chunk.Size;
        var localX = voxelIndex % Chunk.Size;
        var meshletIndex =
            localX / ChunkMeshletMask.Edge +
            localZ / ChunkMeshletMask.Edge * ChunkMeshletMask.PerAxis +
            localY / ChunkMeshletMask.Edge *
            ChunkMeshletMask.PerAxis * ChunkMeshletMask.PerAxis;
        state.MeshletOccupiedCounts[meshletIndex] += isOccupied ? 1 : -1;
    }
}

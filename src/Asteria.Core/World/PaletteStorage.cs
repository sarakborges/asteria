namespace Asteria.Core.World;

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
        }

        public List<T> Palette { get; }

        public List<ushort> Usage { get; }

        public Dictionary<T, ushort> Lookup { get; }

        public Stack<ushort> FreeSlots { get; }

        public ushort[] Indices { get; }

        public ulong[] Occupied { get; }

        public int OccupiedCount { get; set; }

        public bool IsShared { get; set; }

        public StorageState Copy() => new(this);
    }

    private StorageState _state = new();

    public int OccupiedCount => _state.OccupiedCount;

    public int ActivePaletteEntryCount => _state.Lookup.Count;

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

            state.Palette.Add(value);
            state.Usage.Add(0);
            index = checked((ushort)state.Palette.Count);
        }

        state.Lookup.Add(value, index);
        return index;
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
        state.FreeSlots.Push(paletteIndex);
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
    }
}

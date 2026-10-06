namespace Asteria.Core.World;

internal sealed class PaletteStorage<T>
    where T : struct, IEquatable<T>
{
    private readonly List<T> _palette;
    private readonly List<ushort> _usage;
    private readonly Dictionary<T, ushort> _lookup;
    private readonly Stack<ushort> _freeSlots;
    private readonly ushort[] _indices;
    private readonly ulong[] _occupied;

    public PaletteStorage()
    {
        _palette = [];
        _usage = [];
        _lookup = [];
        _freeSlots = new Stack<ushort>();
        _indices = new ushort[Chunk.Volume];
        _occupied = new ulong[(Chunk.Volume + 63) / 64];
    }

    private PaletteStorage(PaletteStorage<T> source)
    {
        _palette = new List<T>(source._palette);
        _usage = new List<ushort>(source._usage);
        _lookup = new Dictionary<T, ushort>(source._lookup);
        _freeSlots = new Stack<ushort>(
            source._freeSlots.Reverse());
        _indices = (ushort[])source._indices.Clone();
        _occupied = (ulong[])source._occupied.Clone();
        OccupiedCount = source.OccupiedCount;
    }

    public int OccupiedCount { get; private set; }

    public int ActivePaletteEntryCount => _lookup.Count;

    public T Get(int voxelIndex)
    {
        var paletteIndex = _indices[voxelIndex];

        return paletteIndex == 0
            ? default
            : _palette[paletteIndex - 1];
    }

    public bool Set(int voxelIndex, T value)
    {
        var previousIndex = _indices[voxelIndex];
        var previous =
            previousIndex == 0
                ? default
                : _palette[previousIndex - 1];

        if (previous.Equals(value))
        {
            return false;
        }

        var nextIndex =
            value.Equals(default)
                ? (ushort)0
                : FindOrInsert(value);

        Release(previousIndex);
        Retain(nextIndex);

        _indices[voxelIndex] = nextIndex;
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

        for (var wordIndex = 0;
             wordIndex < _occupied.Length;
             wordIndex++)
        {
            var remaining = _occupied[wordIndex];

            while (remaining != 0)
            {
                var bit =
                    System.Numerics.BitOperations
                        .TrailingZeroCount(remaining);
                var voxelIndex =
                    wordIndex * 64 + bit;

                if (voxelIndex >= Chunk.Volume)
                {
                    return;
                }

                var paletteIndex =
                    _indices[voxelIndex];

                if (paletteIndex != 0)
                {
                    visit(
                        voxelIndex,
                        _palette[paletteIndex - 1]);
                }

                remaining &= remaining - 1;
            }
        }
    }

    public PaletteStorage<T> Clone() =>
        new(this);

    private ushort FindOrInsert(T value)
    {
        if (_lookup.TryGetValue(
                value,
                out var existing))
        {
            return existing;
        }

        ushort index;

        if (_freeSlots.TryPop(
                out var reusable))
        {
            index = reusable;
            _palette[index - 1] = value;
        }
        else
        {
            if (_palette.Count >= ushort.MaxValue)
            {
                throw new InvalidOperationException(
                    "Chunk palette capacity was exhausted.");
            }

            _palette.Add(value);
            _usage.Add(0);
            index =
                checked((ushort)_palette.Count);
        }

        _lookup.Add(value, index);
        return index;
    }

    private void Retain(ushort paletteIndex)
    {
        if (paletteIndex == 0)
        {
            return;
        }

        var index = paletteIndex - 1;
        _usage[index] =
            checked((ushort)(_usage[index] + 1));
    }

    private void Release(ushort paletteIndex)
    {
        if (paletteIndex == 0)
        {
            return;
        }

        var index = paletteIndex - 1;
        var usage = _usage[index];

        if (usage == 0)
        {
            throw new InvalidOperationException(
                "Chunk palette usage cannot underflow.");
        }

        usage--;
        _usage[index] = usage;

        if (usage != 0)
        {
            return;
        }

        var value = _palette[index];
        _lookup.Remove(value);
        _freeSlots.Push(paletteIndex);
    }

    private void UpdateOccupancy(
        int voxelIndex,
        bool wasOccupied,
        bool isOccupied)
    {
        if (wasOccupied == isOccupied)
        {
            return;
        }

        var word = voxelIndex >> 6;
        var mask =
            1UL << (voxelIndex & 63);

        if (isOccupied)
        {
            _occupied[word] |= mask;
            OccupiedCount++;
        }
        else
        {
            _occupied[word] &= ~mask;
            OccupiedCount--;
        }
    }
}

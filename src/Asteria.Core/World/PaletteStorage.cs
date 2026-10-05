namespace Asteria.Core.World;

internal sealed class PaletteStorage<T>
    where T : struct, IEquatable<T>
{
    private readonly T[] _palette = new T[Chunk.Volume];
    private readonly ushort[] _usage = new ushort[Chunk.Volume];
    private readonly ushort[] _indices = new ushort[Chunk.Volume];
    private readonly ulong[] _occupied = new ulong[Chunk.Volume / 64];
    private int _paletteLength;

    public int OccupiedCount { get; private set; }

    public int ActivePaletteEntryCount
    {
        get
        {
            var count = 0;
            for (var index = 0; index < _paletteLength; index++)
            {
                if (_usage[index] > 0)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public T Get(int voxelIndex)
    {
        var paletteIndex = _indices[voxelIndex];
        return paletteIndex == 0 ? default : _palette[paletteIndex - 1];
    }

    public bool Set(int voxelIndex, T value)
    {
        var previousIndex = _indices[voxelIndex];
        var previous = previousIndex == 0 ? default : _palette[previousIndex - 1];
        if (previous.Equals(value))
        {
            return false;
        }

        var nextIndex = value.Equals(default)
            ? (ushort)0
            : FindOrInsert(value);

        if (previousIndex != 0)
        {
            _usage[previousIndex - 1]--;
        }

        if (nextIndex != 0)
        {
            _usage[nextIndex - 1]++;
        }

        _indices[voxelIndex] = nextIndex;
        UpdateOccupancy(voxelIndex, previousIndex != 0, nextIndex != 0);
        return true;
    }

    private ushort FindOrInsert(T value)
    {
        var firstFree = -1;

        for (var index = 0; index < _paletteLength; index++)
        {
            if (_usage[index] == 0)
            {
                if (firstFree < 0)
                {
                    firstFree = index;
                }

                continue;
            }

            if (_palette[index].Equals(value))
            {
                return checked((ushort)(index + 1));
            }
        }

        if (firstFree >= 0)
        {
            _palette[firstFree] = value;
            return checked((ushort)(firstFree + 1));
        }

        if (_paletteLength >= ushort.MaxValue || _paletteLength >= _palette.Length)
        {
            throw new InvalidOperationException("Chunk palette capacity was exhausted.");
        }

        _palette[_paletteLength] = value;
        _paletteLength++;
        return checked((ushort)_paletteLength);
    }

    private void UpdateOccupancy(int voxelIndex, bool wasOccupied, bool isOccupied)
    {
        if (wasOccupied == isOccupied)
        {
            return;
        }

        var word = voxelIndex >> 6;
        var mask = 1UL << (voxelIndex & 63);

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

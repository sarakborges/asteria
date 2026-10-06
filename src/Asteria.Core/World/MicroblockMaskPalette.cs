namespace Asteria.Core.World;

internal sealed class MicroblockMaskPalette
{
    private readonly List<MicroblockMask> _masks;
    private readonly Dictionary<MicroblockMask, ushort> _ids;

    public MicroblockMaskPalette()
    {
        _masks = [];
        _ids = [];
    }

    private MicroblockMaskPalette(
        MicroblockMaskPalette source)
    {
        _masks =
            new List<MicroblockMask>(
                source._masks);
        _ids =
            new Dictionary<MicroblockMask, ushort>(
                source._ids);
    }

    public int Count => _masks.Count;

    public ushort Intern(MicroblockMask mask)
    {
        if (mask.IsFull)
        {
            return 0;
        }

        if (_ids.TryGetValue(mask, out var existing))
        {
            return existing;
        }

        if (_masks.Count >= ushort.MaxValue)
        {
            throw new InvalidOperationException("Microblock mask palette capacity was exhausted.");
        }

        _masks.Add(mask);
        var id = checked((ushort)_masks.Count);
        _ids.Add(mask, id);
        return id;
    }

    public MicroblockMaskPalette Clone() =>
        new(this);

    public MicroblockMask Get(ushort id)
    {
        if (id == 0)
        {
            return MicroblockMask.Full;
        }

        var index = id - 1;
        if (index >= _masks.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(id), $"Unknown microblock mask id: {id}");
        }

        return _masks[index];
    }
}

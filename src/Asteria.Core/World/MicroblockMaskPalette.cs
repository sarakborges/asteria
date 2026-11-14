namespace Asteria.Core.World;

internal sealed class MicroblockMaskPalette
{
    private sealed class PaletteState
    {
        public PaletteState()
        {
            Masks = [];
            Ids = [];
        }

        private PaletteState(PaletteState source)
        {
            Masks = new List<MicroblockMask>(source.Masks);
            Ids = new Dictionary<MicroblockMask, ushort>(source.Ids);
        }

        public List<MicroblockMask> Masks { get; }

        public Dictionary<MicroblockMask, ushort> Ids { get; }

        public bool IsShared { get; set; }

        public PaletteState Copy() => new(this);
    }

    private PaletteState _state = new();

    public MicroblockMaskPalette()
    {
    }

    public int Count => _state.Masks.Count;

    public ushort Intern(MicroblockMask mask)
    {
        if (mask.IsFull)
            return 0;

        var state = _state;
        if (state.Ids.TryGetValue(mask, out var existing))
            return existing;

        if (state.Masks.Count >= ushort.MaxValue)
            throw new InvalidOperationException(
                "Microblock mask palette capacity was exhausted.");

        // The common worker path is read-only. Detach the palette only when
        // a genuinely new mask is authored, not when an existing mask is used.
        if (state.IsShared)
        {
            state = state.Copy();
            _state = state;
        }

        state.Masks.Add(mask);
        var id = checked((ushort)state.Masks.Count);
        state.Ids.Add(mask, id);
        return id;
    }

    public MicroblockMaskPalette Clone()
    {
        var state = _state;
        state.IsShared = true;
        return new MicroblockMaskPalette(state);
    }

    private MicroblockMaskPalette(PaletteState sharedState)
    {
        _state = sharedState;
        sharedState.IsShared = true;
    }

    public MicroblockMask Get(ushort id)
    {
        if (id == 0)
            return MicroblockMask.Full;

        var state = _state;
        var index = id - 1;
        if (index >= state.Masks.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(id), $"Unknown microblock mask id: {id}");
        }

        return state.Masks[index];
    }
}

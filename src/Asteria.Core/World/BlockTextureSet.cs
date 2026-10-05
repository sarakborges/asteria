namespace Asteria.Core.World;

public sealed class BlockTextureSet
{
    public BlockTextureSet(
        IEnumerable<BlockTextureLayer>? top = null,
        IEnumerable<BlockTextureLayer>? bottom = null,
        IEnumerable<BlockTextureLayer>? left = null,
        IEnumerable<BlockTextureLayer>? right = null,
        IEnumerable<BlockTextureLayer>? front = null,
        IEnumerable<BlockTextureLayer>? back = null)
    {
        Top = Freeze(top);
        Bottom = Freeze(bottom);
        Left = Freeze(left);
        Right = Freeze(right);
        Front = Freeze(front);
        Back = Freeze(back);
    }

    public IReadOnlyList<BlockTextureLayer> Top { get; }
    public IReadOnlyList<BlockTextureLayer> Bottom { get; }
    public IReadOnlyList<BlockTextureLayer> Left { get; }
    public IReadOnlyList<BlockTextureLayer> Right { get; }
    public IReadOnlyList<BlockTextureLayer> Front { get; }
    public IReadOnlyList<BlockTextureLayer> Back { get; }

    public bool IsEmpty =>
        Top.Count == 0 && Bottom.Count == 0 &&
        Left.Count == 0 && Right.Count == 0 &&
        Front.Count == 0 && Back.Count == 0;

    public IReadOnlyList<BlockTextureLayer> ForFace(BlockFace face) => face switch
    {
        BlockFace.Top => Top,
        BlockFace.Bottom => Bottom,
        BlockFace.Left => Left,
        BlockFace.Right => Right,
        BlockFace.Front => Front,
        BlockFace.Back => Back,
        _ => throw new ArgumentOutOfRangeException(nameof(face)),
    };

    private static IReadOnlyList<BlockTextureLayer> Freeze(IEnumerable<BlockTextureLayer>? values) =>
        values?.ToArray() ?? Array.Empty<BlockTextureLayer>();
}

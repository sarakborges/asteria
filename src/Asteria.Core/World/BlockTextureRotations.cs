namespace Asteria.Core.World;

public readonly record struct BlockTextureRotations(
    bool Top = false,
    bool Bottom = false,
    bool Left = false,
    bool Right = false,
    bool Front = false,
    bool Back = false)
{
    public bool Any => Top || Bottom || Left || Right || Front || Back;

    public bool Rotates(BlockFace face) => face switch
    {
        BlockFace.Top => Top,
        BlockFace.Bottom => Bottom,
        BlockFace.Left => Left,
        BlockFace.Right => Right,
        BlockFace.Front => Front,
        BlockFace.Back => Back,
        _ => throw new ArgumentOutOfRangeException(nameof(face)),
    };
}

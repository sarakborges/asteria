namespace Asteria.Core.World;

public static class BlockUvRotation
{
    public static TextureRotation ForWorldFace(
        BlockFace worldFace,
        BlockOrientation orientation,
        TextureRotation authoredRotation,
        bool usesAuthoredRotation)
    {
        var orientationTurn = orientation switch
        {
            BlockOrientation.Y => 0,
            BlockOrientation.Z => worldFace switch
            {
                BlockFace.Right => 1,
                BlockFace.Left => 3,
                BlockFace.Top or BlockFace.Back => 2,
                BlockFace.Bottom or BlockFace.Front => 0,
                _ => throw new ArgumentOutOfRangeException(nameof(worldFace)),
            },
            BlockOrientation.X => worldFace switch
            {
                BlockFace.Back => 1,
                BlockFace.Right or
                BlockFace.Left or
                BlockFace.Top or
                BlockFace.Bottom or
                BlockFace.Front => 3,
                _ => throw new ArgumentOutOfRangeException(nameof(worldFace)),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(orientation)),
        };

        var authoredTurn = usesAuthoredRotation ? (int)authoredRotation : 0;
        return TextureRotationExtensions.FromQuarterTurn(
            checked((byte)(orientationTurn + authoredTurn)));
    }
}

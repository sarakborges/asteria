namespace Asteria.Core.World;

public enum TextureRotation : byte
{
    Degrees0 = 0,
    Degrees90 = 1,
    Degrees180 = 2,
    Degrees270 = 3,
}

public static class TextureRotationExtensions
{
    public static TextureRotation FromQuarterTurn(byte quarterTurn) =>
        (TextureRotation)(quarterTurn % 4);
}

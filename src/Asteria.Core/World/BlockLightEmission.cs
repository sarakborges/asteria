namespace Asteria.Core.World;

public readonly record struct BlockLightEmission
{
    public BlockLightEmission(byte red, byte green, byte blue)
    {
        if (red > 15 || green > 15 || blue > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(red), "Block light channels must be within 0..15.");
        }

        Red = red;
        Green = green;
        Blue = blue;
    }

    public byte Red { get; }

    public byte Green { get; }

    public byte Blue { get; }

    public bool IsDark => Red == 0 && Green == 0 && Blue == 0;
}

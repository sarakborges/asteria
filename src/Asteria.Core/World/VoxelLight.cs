namespace Asteria.Core.World;

public readonly record struct VoxelLight
{
    public const byte MaxLevel = 15;

    private readonly ushort _packed;

    public VoxelLight(byte sky, byte red, byte green, byte blue)
    {
        Validate(sky, nameof(sky));
        Validate(red, nameof(red));
        Validate(green, nameof(green));
        Validate(blue, nameof(blue));

        _packed = (ushort)(
            sky |
            (red << 4) |
            (green << 8) |
            (blue << 12));
    }

    private VoxelLight(ushort packed)
    {
        _packed = packed;
    }

    public byte Sky => (byte)(_packed & 0x0f);

    public byte Red => (byte)((_packed >> 4) & 0x0f);

    public byte Green => (byte)((_packed >> 8) & 0x0f);

    public byte Blue => (byte)((_packed >> 12) & 0x0f);

    public byte BlockPeak => Math.Max(Red, Math.Max(Green, Blue));

    public bool IsDark => _packed == 0;

    public VoxelLight WithSky(byte sky) =>
        new(sky, Red, Green, Blue);

    public VoxelLight WithBlock(byte red, byte green, byte blue) =>
        new(Sky, red, green, blue);

    public static VoxelLight Max(VoxelLight left, VoxelLight right) =>
        new(
            Math.Max(left.Sky, right.Sky),
            Math.Max(left.Red, right.Red),
            Math.Max(left.Green, right.Green),
            Math.Max(left.Blue, right.Blue));

    public static VoxelLight Attenuate(VoxelLight value, byte attenuation) =>
        new(
            SaturatingSubtract(value.Sky, attenuation),
            SaturatingSubtract(value.Red, attenuation),
            SaturatingSubtract(value.Green, attenuation),
            SaturatingSubtract(value.Blue, attenuation));

    public static VoxelLight FromEmission(BlockLightEmission emission) =>
        new(0, emission.Red, emission.Green, emission.Blue);

    private static byte SaturatingSubtract(byte value, byte amount) =>
        value > amount ? (byte)(value - amount) : (byte)0;

    private static void Validate(byte value, string parameterName)
    {
        if (value > MaxLevel)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"Voxel light must be within 0..{MaxLevel}.");
        }
    }
}

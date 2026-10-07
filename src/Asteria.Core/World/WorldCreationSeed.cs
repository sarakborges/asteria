using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace Asteria.Core.World;

/// <summary>
/// A world's seed is a full-width unsigned 64-bit integer.
/// Browser input remains a decimal string until validated by Core.
/// </summary>
public static class WorldCreationSeed
{
    public static ulong GenerateRandom()
    {
        Span<byte> entropy =
            stackalloc byte[sizeof(ulong)];
        RandomNumberGenerator.Fill(
            entropy);
        return BinaryPrimitives
            .ReadUInt64LittleEndian(
                entropy);
    }

    public static bool TryParse(
        string? decimalSeed,
        out ulong seed)
    {
        seed = default;

        if (string.IsNullOrWhiteSpace(
                decimalSeed))
        {
            return false;
        }

        return ulong.TryParse(
            decimalSeed.Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out seed);
    }

    public static string Format(
        ulong seed) =>
        seed.ToString(
            CultureInfo.InvariantCulture);
}

using System.Numerics;

namespace Asteria.Core.World;

public enum MicroblockResolution : byte
{
    Thick = 4,
    Thin = 2,
    ExtraThin = 1,
}

public readonly struct MicroblockMask : IEquatable<MicroblockMask>
{
    public const int Edge = 8;
    public const int CellCount = Edge * Edge * Edge;

    private readonly ulong _layer0;
    private readonly ulong _layer1;
    private readonly ulong _layer2;
    private readonly ulong _layer3;
    private readonly ulong _layer4;
    private readonly ulong _layer5;
    private readonly ulong _layer6;
    private readonly ulong _layer7;

    private MicroblockMask(
        ulong layer0,
        ulong layer1,
        ulong layer2,
        ulong layer3,
        ulong layer4,
        ulong layer5,
        ulong layer6,
        ulong layer7)
    {
        _layer0 = layer0;
        _layer1 = layer1;
        _layer2 = layer2;
        _layer3 = layer3;
        _layer4 = layer4;
        _layer5 = layer5;
        _layer6 = layer6;
        _layer7 = layer7;
    }

    public static MicroblockMask Empty => default;

    public static MicroblockMask Full => new(
        ulong.MaxValue,
        ulong.MaxValue,
        ulong.MaxValue,
        ulong.MaxValue,
        ulong.MaxValue,
        ulong.MaxValue,
        ulong.MaxValue,
        ulong.MaxValue);

    public bool IsEmpty => Equals(Empty);

    public bool IsFull => Equals(Full);

    public int OccupiedCount =>
        BitOperations.PopCount(_layer0) +
        BitOperations.PopCount(_layer1) +
        BitOperations.PopCount(_layer2) +
        BitOperations.PopCount(_layer3) +
        BitOperations.PopCount(_layer4) +
        BitOperations.PopCount(_layer5) +
        BitOperations.PopCount(_layer6) +
        BitOperations.PopCount(_layer7);

    public bool Contains(int x, int y, int z)
    {
        if ((uint)x >= Edge || (uint)y >= Edge || (uint)z >= Edge)
        {
            return false;
        }

        return (GetLayer(z) & (1UL << (x + y * Edge))) != 0;
    }

    public byte LightDampening(byte fullDampening)
    {
        if (fullDampening > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(fullDampening));
        }

        return (byte)((fullDampening * OccupiedCount + CellCount - 1) / CellCount);
    }

    public MicroblockMask Edit(
        int x,
        int y,
        int z,
        MicroblockResolution resolution,
        bool occupied)
    {
        if ((uint)x >= Edge || (uint)y >= Edge || (uint)z >= Edge)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Microblock coordinates must be within 0..7.");
        }

        var width = (int)resolution;
        if (width is not (1 or 2 or 4))
        {
            throw new ArgumentOutOfRangeException(nameof(resolution));
        }

        Span<ulong> layers = stackalloc ulong[Edge];
        CopyLayersTo(layers);

        var originX = x / width * width;
        var originY = y / width * width;
        var originZ = z / width * width;

        for (var editZ = originZ; editZ < originZ + width; editZ++)
        {
            for (var editY = originY; editY < originY + width; editY++)
            {
                for (var editX = originX; editX < originX + width; editX++)
                {
                    var bit = 1UL << (editX + editY * Edge);
                    if (occupied)
                    {
                        layers[editZ] |= bit;
                    }
                    else
                    {
                        layers[editZ] &= ~bit;
                    }
                }
            }
        }

        return FromLayers(layers);
    }

    public bool Equals(MicroblockMask other) =>
        _layer0 == other._layer0 &&
        _layer1 == other._layer1 &&
        _layer2 == other._layer2 &&
        _layer3 == other._layer3 &&
        _layer4 == other._layer4 &&
        _layer5 == other._layer5 &&
        _layer6 == other._layer6 &&
        _layer7 == other._layer7;

    public override bool Equals(object? obj) => obj is MicroblockMask other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(_layer0, _layer1, _layer2, _layer3, _layer4, _layer5, _layer6, _layer7);

    public static bool operator ==(MicroblockMask left, MicroblockMask right) => left.Equals(right);

    public static bool operator !=(MicroblockMask left, MicroblockMask right) => !left.Equals(right);

    private ulong GetLayer(int z) => z switch
    {
        0 => _layer0,
        1 => _layer1,
        2 => _layer2,
        3 => _layer3,
        4 => _layer4,
        5 => _layer5,
        6 => _layer6,
        7 => _layer7,
        _ => 0,
    };

    private void CopyLayersTo(Span<ulong> layers)
    {
        layers[0] = _layer0;
        layers[1] = _layer1;
        layers[2] = _layer2;
        layers[3] = _layer3;
        layers[4] = _layer4;
        layers[5] = _layer5;
        layers[6] = _layer6;
        layers[7] = _layer7;
    }

    private static MicroblockMask FromLayers(ReadOnlySpan<ulong> layers) => new(
        layers[0],
        layers[1],
        layers[2],
        layers[3],
        layers[4],
        layers[5],
        layers[6],
        layers[7]);
}

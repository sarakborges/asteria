using System.Numerics;

namespace Asteria.Core.World;

/// <summary>Horizontal wind shared by foliage shaders and ambient particle motion.</summary>
public readonly record struct DimensionWindDefinition
{
    public static DimensionWindDefinition Default { get; } = new(0.93f, 0.37f, 0.45f);

    public DimensionWindDefinition(float directionX, float directionZ, float strength)
    {
        if (!float.IsFinite(directionX) || !float.IsFinite(directionZ) ||
            !float.IsFinite(strength) || strength < 0f || strength > 16f ||
            directionX * directionX + directionZ * directionZ <= 0.000001f)
            throw new ArgumentOutOfRangeException(nameof(directionX),
                "Wind direction must be nonzero and finite; strength must be within 0..16.");
        DirectionX = directionX;
        DirectionZ = directionZ;
        Strength = strength;
    }

    public float DirectionX { get; }
    public float DirectionZ { get; }
    public float Strength { get; }

    public Vector2 Velocity => Vector2.Normalize(new Vector2(DirectionX, DirectionZ)) * Strength;
}

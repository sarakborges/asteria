using System.Numerics;

namespace Asteria.Core.World;

public readonly record struct WorldAabb
{
    public WorldAabb(
        Vector3 minimum,
        Vector3 maximum)
    {
        if (!IsFinite(minimum) ||
            !IsFinite(maximum))
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimum),
                "AABB bounds must be finite.");
        }

        if (maximum.X < minimum.X ||
            maximum.Y < minimum.Y ||
            maximum.Z < minimum.Z)
        {
            throw new ArgumentException(
                "AABB maximum must not be lower than its minimum.",
                nameof(maximum));
        }

        Minimum = minimum;
        Maximum = maximum;
    }

    public Vector3 Minimum { get; }

    public Vector3 Maximum { get; }

    public bool Intersects(WorldAabb other) =>
        Minimum.X < other.Maximum.X &&
        Maximum.X > other.Minimum.X &&
        Minimum.Y < other.Maximum.Y &&
        Maximum.Y > other.Minimum.Y &&
        Minimum.Z < other.Maximum.Z &&
        Maximum.Z > other.Minimum.Z;

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}

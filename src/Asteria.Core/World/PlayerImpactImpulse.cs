using System.Numerics;

namespace Asteria.Core.World;

/// <summary>
/// Portable attack-impact direction policy. Core decides the impulse;
/// Godot's character motion and terrain collision decide the displacement.
/// Rejected/invulnerable attacks never reach this policy's consumer.
/// </summary>
public static class PlayerImpactImpulse
{
    public const float MeleeHorizontalSpeed = 6f;
    public const float MeleeVerticalSpeed = 2.4f;
    public const float ProjectileHorizontalSpeed = 4.5f;
    public const float ProjectileVerticalSpeed = 1.8f;

    public static Vector3 FromMelee(Vector3 attacker, Vector3 player) =>
        FromContact(attacker, player,
            MeleeHorizontalSpeed, MeleeVerticalSpeed);

    public static Vector3 FromProjectile(Vector3 impact, Vector3 player) =>
        FromContact(impact, player,
            ProjectileHorizontalSpeed, ProjectileVerticalSpeed);

    private static Vector3 FromContact(
        Vector3 source, Vector3 player, float horizontal, float vertical)
    {
        if (!Finite(source) || !Finite(player))
            throw new ArgumentOutOfRangeException(nameof(source),
                "Attack and player coordinates must be finite.");

        var direction = new Vector2(player.X - source.X, player.Z - source.Z);
        // A direct overlap must still displace the player; use a stable
        // world-space axis rather than unstable NaN-normalization.
        direction = direction.LengthSquared() < 0.000001f
            ? Vector2.UnitX : Vector2.Normalize(direction);
        return new Vector3(
            direction.X * horizontal, vertical,
            direction.Y * horizontal);
    }

    private static bool Finite(Vector3 point) =>
        float.IsFinite(point.X) &&
        float.IsFinite(point.Y) &&
        float.IsFinite(point.Z);
}

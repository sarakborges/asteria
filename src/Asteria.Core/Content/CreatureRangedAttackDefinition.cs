using System.Text.Json;

namespace Asteria.Core.Content;

/// <summary>
/// Optional authored ranged attack capability. AI chooses when to fire; the
/// existing projectile runtime resolves the projectile's flight and impacts.
/// </summary>
public sealed record CreatureRangedAttackDefinition(
    string Projectile,
    float MinimumDistance,
    float Speed,
    float Damage,
    float Radius,
    float GravityScale,
    float LifetimeSeconds,
    float CooldownSeconds)
{
    public static CreatureRangedAttackDefinition? Parse(JsonElement creature)
    {
        if (!creature.TryGetProperty("rangedAttack", out var source))
            return null;
        if (source.ValueKind != JsonValueKind.Object)
            throw new FormatException("rangedAttack must be an object.");

        var projectile = PackContentFields.RequiredString(source, "projectile");
        var separator = projectile.IndexOf(':');
        if (separator <= 0 || separator == projectile.Length - 1 ||
            projectile.Length > 128)
            throw new FormatException("Ranged attack projectile must be a namespaced ID.");

        var minimum = PackContentFields.PositiveFloat(source, "minDistance");
        var speed = PackContentFields.PositiveFloat(source, "speed");
        var damage = PackContentFields.NonNegativeFloat(source, "damage");
        var radius = PackContentFields.PositiveFloat(source, "radius");
        var gravity = source.TryGetProperty("gravityScale", out _)
            ? PackContentFields.NonNegativeFloat(source, "gravityScale") : 0f;
        var lifetime = PackContentFields.PositiveFloat(source, "lifetimeSeconds");
        var cooldown = PackContentFields.PositiveFloat(source, "cooldownSeconds");
        // These limits agree with ProjectileRuntime's validated launch envelope.
        if (minimum > 128f || speed > 256f || damage > 1000f ||
            radius < 1f / 64f || radius > 0.75f || gravity > 4f ||
            lifetime > 30f || cooldown > 60f)
            throw new FormatException("Ranged attack parameter is outside safe bounds.");

        return new CreatureRangedAttackDefinition(
            projectile, minimum, speed, damage, radius,
            gravity, lifetime, cooldown);
    }
}

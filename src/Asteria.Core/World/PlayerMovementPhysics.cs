using System.Numerics;

namespace Asteria.Core.World;

/// <summary>
/// Engine-independent MineClone movement policy. The Godot adapter retains
/// authoritative physics collision and movement/input ownership.
/// </summary>
public static class PlayerMovementPhysics
{
    public static Vector3 FlightVelocity(
        Vector3 current, Vector3 target, float deltaSeconds)
    {
        if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0f)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));

        var rate = target == Vector3.Zero
            ? PlayerMovementSpeeds.FlightDeceleration
            : PlayerMovementSpeeds.FlightAcceleration;
        var maximumChange = rate * deltaSeconds;
        var difference = target - current;
        var distance = difference.Length();
        if (distance <= maximumChange || distance <= 1.1920929e-7f)
            return target;
        return current + difference * (maximumChange / distance);
    }

    public static float ApplyGravity(
        float verticalSpeed, float gravityStrength, float deltaSeconds)
    {
        if (!float.IsFinite(verticalSpeed) ||
            !float.IsFinite(gravityStrength) || gravityStrength < 0f ||
            !float.IsFinite(deltaSeconds) || deltaSeconds < 0f)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));

        return MathF.Max(-PlayerMovementSpeeds.Flight,
            verticalSpeed - gravityStrength * deltaSeconds);
    }
}

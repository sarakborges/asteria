using System.Numerics;
using Asteria.Core.Content;

namespace Asteria.Core.World;

public enum CreatureHopPhase : byte
{
    Idle,
    Anticipate,
    Airborne,
    Land,
}

/// <summary>Value state persisted with its authoritative creature instance.</summary>
public readonly record struct CreatureHopMotion(
    CreatureHopPhase Phase,
    float SecondsRemaining,
    float VerticalSpeed,
    Vector2 Direction,
    float FacingRadians,
    uint HopCount)
{
    public Vector2 KnockbackVelocity { get; init; }

    public float KnockbackSeconds { get; init; }

    public static CreatureHopMotion Initial =>
        new(CreatureHopPhase.Idle, 1f, 0f, Vector2.Zero, 0f, 0);
}

/// <summary>
/// Pure bounded movement policy adapted from MineClone's slime hop phases.
/// Queries existing loaded voxel collision; it does not generate terrain.
/// </summary>
public static class CreatureHopSolver
{
    private const float GroundProbe = 0.06f;
    private const float MaximumStep = 0.08f;
    private const float MaximumDelta = 0.05f;

    private static readonly Vector2[] Directions =
    [
        new(0, -1), new(1, -1), new(1, 0), new(1, 1),
        new(0, 1), new(-1, 1), new(-1, 0), new(-1, -1),
    ];

    public static CreatureInstanceState Step(
        CreatureInstanceState instance,
        CreatureDefinition definition,
        VoxelWorld world,
        BlockRegistry blocks,
        float gravityStrength,
        float deltaSeconds,
        Vector3? targetPosition = null,
        Func<WorldAabb, bool>? intersectsEntity = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        if (!float.IsFinite(gravityStrength) || gravityStrength < 0f ||
            !float.IsFinite(deltaSeconds) || deltaSeconds < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        }

        if (targetPosition is { } target &&
            (!float.IsFinite(target.X) || !float.IsFinite(target.Y) ||
             !float.IsFinite(target.Z)))
            throw new ArgumentOutOfRangeException(nameof(targetPosition));

        var dt = Math.Min(deltaSeconds, MaximumDelta);
        if (dt == 0f) return instance;

        var feet = instance.Position;
        if (feet.Y < 0f ||
            !world.IsLoadedAt(new WorldVoxelCoord(
                (int)MathF.Floor(feet.X),
                (int)MathF.Floor(feet.Y),
                (int)MathF.Floor(feet.Z))))
        {
            return instance;
        }

        var motion = instance.Motion;
        if (motion.KnockbackSeconds > 0f)
        {
            var impulse = new Vector3(
                motion.KnockbackVelocity.X * dt,
                0f,
                motion.KnockbackVelocity.Y * dt);
            var collided = Move(world, blocks, definition.Collider, ref feet, impulse, intersectsEntity);
            var seconds = MathF.Max(0f, motion.KnockbackSeconds - dt);
            var velocity = collided || seconds == 0f
                ? Vector2.Zero
                : motion.KnockbackVelocity * MathF.Max(0f, 1f - 10f * dt);
            motion = motion with
            {
                KnockbackVelocity = velocity,
                KnockbackSeconds = collided ? 0f : seconds,
            };
        }

        if (motion.Phase != CreatureHopPhase.Airborne &&
            IsGrounded(world, blocks, definition.Collider, feet) ==
                VoxelWorldCollisionState.Clear)
        {
            motion = motion with
            {
                Phase = CreatureHopPhase.Airborne,
                VerticalSpeed = 0,
            };
        }

        switch (motion.Phase)
        {
            case CreatureHopPhase.Idle:
                motion = motion with { SecondsRemaining = motion.SecondsRemaining - dt };
                if (motion.SecondsRemaining <= 0f)
                {
                    var heading = ChooseHeading(instance, definition, targetPosition,
                        motion.HopCount);
                    motion = motion with
                    {
                        Phase = CreatureHopPhase.Anticipate,
                        SecondsRemaining = definition.AnticipationSeconds,
                        Direction = heading,
                        FacingRadians = MathF.Atan2(-heading.X, -heading.Y),
                    };
                }
                break;

            case CreatureHopPhase.Anticipate:
                motion = motion with { SecondsRemaining = motion.SecondsRemaining - dt };
                if (motion.SecondsRemaining <= 0f)
                {
                    var heading = ChooseHeading(instance, definition, targetPosition,
                        motion.HopCount);
                    motion = motion with
                    {
                        Phase = CreatureHopPhase.Airborne,
                        VerticalSpeed = definition.JumpSpeed,
                        Direction = heading,
                        FacingRadians = MathF.Atan2(-heading.X, -heading.Y),
                        HopCount = motion.HopCount + 1,
                    };
                }
                break;

            case CreatureHopPhase.Airborne:
                var displacement = motion.Direction * (definition.MoveSpeed * dt);
                if (displacement.LengthSquared() > 0f)
                {
                    var horizontal = new Vector3(displacement.X, 0f, displacement.Y);
                    if (Move(world, blocks, definition.Collider, ref feet, horizontal, intersectsEntity))
                        motion = motion with { Direction = Vector2.Zero };
                }

                var fallScale = motion.VerticalSpeed <= 0f
                    ? definition.FallGravityScale : 1f;
                var verticalSpeed = motion.VerticalSpeed - gravityStrength * fallScale * dt;
                var vertical = new Vector3(0f, verticalSpeed * dt, 0f);
                if (Move(world, blocks, definition.Collider, ref feet, vertical, intersectsEntity))
                {
                    motion = verticalSpeed <= 0f
                        ? motion with
                        {
                            Phase = CreatureHopPhase.Land,
                            SecondsRemaining = definition.LandingSeconds,
                            VerticalSpeed = 0f,
                            Direction = Vector2.Zero,
                        }
                        : motion with { VerticalSpeed = 0f };
                }
                else
                {
                    motion = motion with { VerticalSpeed = verticalSpeed };
                }
                break;

            case CreatureHopPhase.Land:
                motion = motion with { SecondsRemaining = motion.SecondsRemaining - dt };
                if (motion.SecondsRemaining <= 0f)
                {
                    motion = motion with
                    {
                        Phase = CreatureHopPhase.Idle,
                        SecondsRemaining = definition.JumpInterval,
                    };
                }
                break;
        }

        return instance with { Position = feet, Motion = motion };
    }

    private static Vector2 ChooseHeading(
        CreatureInstanceState creature,
        CreatureDefinition definition,
        Vector3? targetPosition,
        uint hopCount)
    {
        if (targetPosition is { } target && definition.AggroRange > 0f &&
            MathF.Abs(target.Y - creature.Position.Y) <= 3f)
        {
            var toward = new Vector2(
                target.X - creature.Position.X, target.Z - creature.Position.Z);
            var distanceSquared = toward.LengthSquared();
            if (distanceSquared > 0.04f &&
                distanceSquared <= definition.AggroRange * definition.AggroRange)
                return Vector2.Normalize(toward);
        }

        return Directions[
            (int)(Mix(creature.Id.Value, hopCount) % (uint)Directions.Length)]
            .Normalized();
    }

    private static VoxelWorldCollisionState IsGrounded(
        VoxelWorld world,
        BlockRegistry blocks,
        CreatureCollider collider,
        Vector3 feet) =>
        VoxelWorldCollision.Query(
            world, blocks, Bounds(collider, feet - Vector3.UnitY * GroundProbe));

    private static bool Move(
        VoxelWorld world,
        BlockRegistry blocks,
        CreatureCollider collider,
        ref Vector3 feet,
        Vector3 displacement,
        Func<WorldAabb, bool>? intersectsEntity)
    {
        if (displacement.LengthSquared() == 0f) return false;
        var steps = Math.Max(1, (int)MathF.Ceiling(displacement.Length() / MaximumStep));
        var step = displacement / steps;
        for (var i = 0; i < steps; i++)
        {
            var candidate = feet + step;
            var bounds = Bounds(collider, candidate);
            if (!VoxelWorldCollision.IsClear(world, blocks, bounds) ||
                (intersectsEntity?.Invoke(bounds) ?? false))
                return true;
            feet = candidate;
        }

        return false;
    }

    public static WorldAabb Bounds(CreatureCollider collider, Vector3 feet)
    {
        var size = collider.Size;
        var offset = collider.CenterOffset;
        var center = feet + new Vector3(offset.X, offset.Y, offset.Z);
        var half = new Vector3(size.X, size.Y, size.Z) * 0.5f;
        return new WorldAabb(center - half, center + half);
    }

    private static uint Mix(ulong id, uint hop)
    {
        var value = (uint)id ^ (uint)(id >> 32) ^ (hop * 0x9E3779B9u);
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        return value;
    }

    private static Vector2 Normalized(this Vector2 value) =>
        Vector2.Normalize(value);
}

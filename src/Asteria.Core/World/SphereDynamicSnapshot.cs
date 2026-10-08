using System.Numerics;

namespace Asteria.Core.World;

/// <summary>
/// Bounded, immutable-copy boundary for Sphere-owned detached runtime
/// snapshots. Their original owners still restore and validate content-specific
/// rules when runtime sessions are constructed.
/// </summary>
internal static class SphereDynamicSnapshot
{
    private const int MaximumFallingBlocks = 2048;
    private const int MaximumDroppedBlocks = 2048;

    public static BlockPhysicsRuntimeSnapshot? ValidateAndCopy(
        BlockPhysicsRuntimeSnapshot? snapshot)
    {
        if (snapshot is null) return null;
        ArgumentNullException.ThrowIfNull(snapshot.ActiveBlocks);
        if (snapshot.ActiveBlocks.Count > MaximumFallingBlocks)
            throw new InvalidDataException("Too many saved falling blocks.");

        var blocks = snapshot.ActiveBlocks.ToArray();
        ValidateIds(blocks.Select(block => block.Id.Value), snapshot.NextId);
        foreach (var state in blocks)
            if (state.Block is null ||
                !double.IsFinite(state.CenterY) || state.CenterY < 0 ||
                !double.IsFinite(state.VelocityY))
                throw new InvalidDataException("Invalid saved falling-block motion.");

        return new BlockPhysicsRuntimeSnapshot(
            snapshot.NextId, Array.AsReadOnly(blocks));
    }

    public static DroppedBlockRuntimeSnapshot? ValidateAndCopy(
        DroppedBlockRuntimeSnapshot? snapshot)
    {
        if (snapshot is null) return null;
        ArgumentNullException.ThrowIfNull(snapshot.ActiveBlocks);
        if (snapshot.ActiveBlocks.Count > MaximumDroppedBlocks)
            throw new InvalidDataException("Too many saved dropped items.");

        var entries = snapshot.ActiveBlocks.ToArray();
        ValidateIds(entries.Select(entry => entry.State.Id.Value), snapshot.NextId);
        foreach (var entry in entries)
        {
            var drop = entry.State;
            if (drop.Stack is null || drop.Stack.Quantity != 1 ||
                !IsFinite(drop.Position) || drop.Position.Y < 0 ||
                !IsFinite(drop.Velocity) ||
                !double.IsFinite(drop.AgeSeconds) || drop.AgeSeconds < 0 ||
                entry.SettledSupport is { Y: < 0 })
                throw new InvalidDataException("Invalid saved dropped-item state.");
        }

        return new DroppedBlockRuntimeSnapshot(
            snapshot.NextId, Array.AsReadOnly(entries));
    }

    public static CreatureRuntimeSnapshot? ValidateAndCopy(
        CreatureRuntimeSnapshot? snapshot)
    {
        if (snapshot is null) return null;
        ArgumentNullException.ThrowIfNull(snapshot.Creatures);
        if (snapshot.Creatures.Count > CreatureRuntime.MaximumActive)
            throw new InvalidDataException("Too many saved creatures.");

        var entries = snapshot.Creatures.ToArray();
        ValidateIds(entries.Select(creature => creature.Id.Value), snapshot.NextId);
        foreach (var creature in entries)
            if (string.IsNullOrWhiteSpace(creature.DefinitionId) ||
                !IsFinite(creature.Position) || creature.Position.Y < 0 ||
                !float.IsFinite(creature.Health) || creature.Health < 0 ||
                !double.IsFinite(creature.AgeSeconds) || creature.AgeSeconds < 0 ||
                !creature.MetaTags.IsValid ||
                !Enum.IsDefined(creature.Motion.Phase) ||
                !float.IsFinite(creature.Motion.SecondsRemaining) ||
                !float.IsFinite(creature.Motion.VerticalSpeed) ||
                !float.IsFinite(creature.Motion.Direction.X) ||
                !float.IsFinite(creature.Motion.Direction.Y) ||
                !float.IsFinite(creature.Motion.FacingRadians) ||
                !float.IsFinite(creature.Motion.KnockbackVelocity.X) ||
                !float.IsFinite(creature.Motion.KnockbackVelocity.Y) ||
                !float.IsFinite(creature.Motion.KnockbackSeconds) ||
                !float.IsFinite(creature.HurtSecondsRemaining) ||
                !float.IsFinite(creature.DeathSecondsRemaining))
                throw new InvalidDataException("Invalid saved creature state.");

        return new CreatureRuntimeSnapshot(
            snapshot.NextId, Array.AsReadOnly(entries));
    }

    public static BlockPhysicsRuntimeSnapshot? Copy(
        BlockPhysicsRuntimeSnapshot? snapshot) =>
        snapshot is null ? null : new BlockPhysicsRuntimeSnapshot(
            snapshot.NextId, Array.AsReadOnly(snapshot.ActiveBlocks.ToArray()));

    public static DroppedBlockRuntimeSnapshot? Copy(
        DroppedBlockRuntimeSnapshot? snapshot) =>
        snapshot is null ? null : new DroppedBlockRuntimeSnapshot(
            snapshot.NextId, Array.AsReadOnly(snapshot.ActiveBlocks.ToArray()));

    public static CreatureRuntimeSnapshot? Copy(
        CreatureRuntimeSnapshot? snapshot) =>
        snapshot is null ? null : new CreatureRuntimeSnapshot(
            snapshot.NextId, Array.AsReadOnly(snapshot.Creatures.ToArray()));

    private static void ValidateIds(IEnumerable<ulong> ids, ulong nextId)
    {
        var seen = new HashSet<ulong>();
        foreach (var id in ids)
            if (id == 0 || id > nextId || !seen.Add(id))
                throw new InvalidDataException("Saved entity IDs are duplicated or invalid.");
    }

    private static bool IsFinite(Vector3 position) =>
        float.IsFinite(position.X) &&
        float.IsFinite(position.Y) &&
        float.IsFinite(position.Z);
}

using System.Numerics;
using Asteria.Core.Content;

namespace Asteria.Core.World;

/// <summary>
/// Bounded, tick-driven natural spawns. Uses authored biome pools and the
/// existing generator destination query; never scans/materializes chunks.
/// One attempt per interval, even after a paused/slow frame.
/// </summary>
public sealed class CreatureNaturalSpawnRuntime
{
    public const int AttemptIntervalSeconds = 10;
    private const int MinimumRadius = 18;
    private const int RadiusRange = 15;

    private static readonly (int X, int Z)[] Directions =
    [
        (1, 0), (1, 1), (0, 1), (-1, 1),
        (-1, 0), (-1, -1), (0, -1), (1, -1),
    ];

    private readonly CreatureDefinition[] _natural;

    public CreatureNaturalSpawnRuntime(
        PackContentRegistry<CreatureDefinition> definitions,
        BiomeRegistry biomes,
        ulong nextAttemptTick = 0)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(biomes);

        _natural = definitions.Definitions
            .Where(definition => definition.NaturalSpawn is not null)
            .ToArray();

        foreach (var definition in _natural)
        foreach (var id in definition.NaturalSpawn!.SurfaceBiomes)
        {
            if (biomes.Get(id).SurfaceLayout is null)
                throw new InvalidOperationException(
                    $"Natural spawn biome {id} is not a surface biome.");
        }

        NextAttemptTick = nextAttemptTick;
    }

    public ulong NextAttemptTick { get; private set; }

    public bool TryAdvance(
        ulong currentTick,
        uint ticksPerSecond,
        ulong dimensionSeed,
        Vector3 observer,
        bool enabled,
        BiomeWorldGenerator generator,
        VoxelWorld world,
        BlockRegistry blocks,
        CreatureRuntime creatures)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(creatures);
        if (ticksPerSecond == 0)
            throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));
        if (!float.IsFinite(observer.X) ||
            !float.IsFinite(observer.Y) ||
            !float.IsFinite(observer.Z))
            throw new ArgumentOutOfRangeException(nameof(observer));

        var interval = (ulong)ticksPerSecond * AttemptIntervalSeconds;
        if (NextAttemptTick == 0)
        {
            NextAttemptTick = SaturatingAdd(currentTick, interval);
            return false;
        }

        if (currentTick < NextAttemptTick)
            return false;

        NextAttemptTick = SaturatingAdd(currentTick, interval);
        if (!enabled || _natural.Length == 0 ||
            creatures.Count >= CreatureRuntime.MaximumActive)
            return false;

        var column = SampleColumn(dimensionSeed, currentTick, observer);
        if (column is not { } candidate) return false;

        // BiomeField is the sole owner of surface-biome identity.
        var biomeId = generator.Biomes.Sample(candidate.X, candidate.Z).Primary;
        var available = _natural.Where(definition =>
            definition.NaturalSpawn!.SurfaceBiomes.Contains(
                biomeId, StringComparer.Ordinal)).ToArray();

        if (available.Length == 0)
            return false;

        var totalWeight = available.Sum(def => def.NaturalSpawn!.Weight);
        var pick = (int)(Mix(dimensionSeed ^ currentTick ^ 0x9e3779b97f4a7c15ul) %
            (ulong)totalWeight);
        CreatureDefinition? selected = null;
        foreach (var definition in available)
        {
            pick -= definition.NaturalSpawn!.Weight;
            if (pick < 0)
            {
                selected = definition;
                break;
            }
        }

        if (selected is null) throw new InvalidOperationException(
            "Natural spawn weights did not select a definition.");

        // A radius of zero restricts each attempt to the one sampled column:
        // no unbounded search or materialization on the frame thread.
        var safe = generator.FindGeneratedSurfaceDestination(
            candidate.X, candidate.Z, maxRadius: 0);
        if (safe is not { } feet)
            return false;

        var position = new Vector3(
            feet.X + 0.5f, feet.Y, feet.Z + 0.5f);
        if (Vector3.DistanceSquared(position, observer) < 12f * 12f)
            return false;

        var support = new WorldVoxelCoord(feet.X, feet.Y - 1, feet.Z);
        var body = new WorldVoxelCoord(feet.X, feet.Y, feet.Z);
        var head = new WorldVoxelCoord(feet.X, feet.Y + 1, feet.Z);
        if (support.Y < 0 ||
            !world.IsLoadedAt(support) ||
            !world.IsLoadedAt(body) ||
            !world.IsLoadedAt(head) ||
            world.GetCellOrEmpty(support).IsEmpty ||
            !world.GetFluidOrEmpty(body).IsEmpty ||
            !world.GetFluidOrEmpty(head).IsEmpty)
            return false;

        var collider = selected.Collider;
        var center = position + new Vector3(
            collider.CenterOffset.X,
            collider.CenterOffset.Y,
            collider.CenterOffset.Z);
        var half = new Vector3(
            collider.Size.X, collider.Size.Y, collider.Size.Z) * 0.5f;
        if (!VoxelWorldCollision.IsClear(
                world, blocks, new WorldAabb(center - half, center + half)))
            return false;

        foreach (var existing in creatures.ActiveCreatures)
        {
            if (Vector3.DistanceSquared(existing.Position, position) < 6.25f)
                return false;
        }

        return creatures.TrySpawn(selected.Id, position, out _);
    }

    public static (int X, int Z)? SampleColumn(
        ulong seed,
        ulong tick,
        Vector3 observer)
    {
        if (!float.IsFinite(observer.X) ||
            !float.IsFinite(observer.Z) ||
            observer.X <= int.MinValue + 64 ||
            observer.X >= int.MaxValue - 64 ||
            observer.Z <= int.MinValue + 64 ||
            observer.Z >= int.MaxValue - 64)
            return null;

        var hash = Mix(seed ^ tick);
        var direction = Directions[(int)(hash % (ulong)Directions.Length)];
        var radius = MinimumRadius +
            (int)((hash >> 8) % RadiusRange);
        return (
            (int)MathF.Floor(observer.X) + direction.X * radius,
            (int)MathF.Floor(observer.Z) + direction.Z * radius);
    }

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;

    private static ulong Mix(ulong input)
    {
        var value = input + 0x9e3779b97f4a7c15ul;
        value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9ul;
        value = (value ^ (value >> 27)) * 0x94d049bb133111ebul;
        return value ^ (value >> 31);
    }
}

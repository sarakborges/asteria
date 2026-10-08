using System.Numerics;

namespace Asteria.Core.World;

/// <summary>
/// A detached snapshot of shared player state. Inventory arrays are copied
/// at both capture and read so no caller can mutate an in-flight save.
/// </summary>
public sealed class PlayerSessionSnapshot
{
    private readonly InventoryStack?[] _backpack;
    private readonly InventoryStack?[] _hotbar;
    private readonly InventoryStack? _cursor;
    private readonly int _selectedSlot;

    public PlayerSessionSnapshot(
        PlayerGameMode gameMode, bool flying, PlayerInventorySnapshot inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (!Enum.IsDefined(gameMode) ||
            flying && !gameMode.AllowsFlight() ||
            gameMode.IsSpectator() && !flying ||
            inventory.SelectedSlot is < 0 or >= PlayerInventory.HotbarSlots ||
            inventory.Backpack is null ||
            inventory.Backpack.Length != PlayerInventory.BackpackSlots ||
            inventory.Hotbar is null ||
            inventory.Hotbar.Length != PlayerInventory.HotbarSlots)
            throw new InvalidDataException("Invalid player session snapshot.");

        GameMode = gameMode;
        Flying = flying;
        _selectedSlot = inventory.SelectedSlot;
        _backpack = inventory.Backpack.ToArray();
        _hotbar = inventory.Hotbar.ToArray();
        _cursor = inventory.Cursor;
    }

    public PlayerGameMode GameMode { get; }
    public bool Flying { get; }
    public PlayerInventorySnapshot Inventory =>
        new(_selectedSlot, _backpack.ToArray(), _hotbar.ToArray(), _cursor);
}

/// <summary>Saved scalar state for one initialized Sphere.</summary>
public sealed record SphereClockSnapshot
{
    public SphereClockSnapshot(
        DimensionId dimension, ulong worldTick, DayNightClockState? dayNight,
        Vector3? position, ulong naturalSpawnNextAttemptTick)
    {
        if (string.IsNullOrWhiteSpace(dimension.Value) ||
            dayNight is { Day: 0 } ||
            position is { } pos &&
            (!float.IsFinite(pos.X) || !float.IsFinite(pos.Y) ||
             !float.IsFinite(pos.Z) || pos.Y < 0))
            throw new InvalidDataException("Invalid saved Sphere clock or position.");

        Dimension = dimension;
        WorldTick = worldTick;
        DayNight = dayNight;
        Position = position;
        NaturalSpawnNextAttemptTick = naturalSpawnNextAttemptTick;
    }

    public DimensionId Dimension { get; }
    public ulong WorldTick { get; }
    public DayNightClockState? DayNight { get; }
    public Vector3? Position { get; }
    public ulong NaturalSpawnNextAttemptTick { get; }
}

/// <summary>
/// Core-only session state paired with spatial snapshots. This deliberately
/// excludes unsaved entity/container systems: it is not a playable save.
/// </summary>
public sealed class GameplaySessionSnapshot
{
    private readonly IReadOnlyList<SphereClockSnapshot> _spheres;

    public GameplaySessionSnapshot(
        DimensionChunkSaveSnapshot spatial,
        string name, uint ticksPerSecond, bool spawnCreatures,
        PlayerSessionSnapshot player, IEnumerable<SphereClockSnapshot> spheres)
    {
        Spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
        Player = player ?? throw new ArgumentNullException(nameof(player));
        ArgumentNullException.ThrowIfNull(spheres);
        if (string.IsNullOrWhiteSpace(name) || ticksPerSecond == 0)
            throw new InvalidDataException("Invalid saved world metadata.");

        var entries = spheres.Take(257).ToArray();
        if (entries.Length != spatial.Spheres.Count ||
            entries.Length > 256 || entries.Any(entry => entry is null))
            throw new InvalidDataException("Sphere clock count does not match spatial state.");

        Array.Sort(entries, static (a, b) =>
            StringComparer.Ordinal.Compare(a.Dimension.Value, b.Dimension.Value));
        for (var index = 0; index < entries.Length; index++)
            if (entries[index].Dimension != spatial.Spheres[index].Dimension)
                throw new InvalidDataException("Sphere clock identity mismatch.");

        Name = name;
        TicksPerSecond = ticksPerSecond;
        SpawnCreatures = spawnCreatures;
        _spheres = Array.AsReadOnly(entries);
    }

    public DimensionChunkSaveSnapshot Spatial { get; }
    public string Name { get; }
    public uint TicksPerSecond { get; }
    public bool SpawnCreatures { get; }
    public PlayerSessionSnapshot Player { get; }
    public IReadOnlyList<SphereClockSnapshot> Spheres => _spheres;
}

/// <summary>
/// Captures shared player/rules and each Sphere's clock and position without
/// duplicating mutable authority. Full load publication remains a higher-level
/// transaction, after storage boxes and detached entities are saved too.
/// </summary>
public static class GameplaySessionSaveCodec
{
    public static GameplaySessionSnapshot Capture(
        DimensionSessionStateStore source,
        BlockRegistry blocks, FluidRegistry fluids,
        DyeRegistry dyes, AttachedLayerRegistry layers)
    {
        ArgumentNullException.ThrowIfNull(source);
        var spatial = DimensionChunkSaveCodec.Capture(
            source, blocks, fluids, dyes, layers);
        var clocks = source.CreatedDimensions().Select(state =>
            new SphereClockSnapshot(
                state.Dimension.Id, state.WorldTick, state.DayNight,
                state.PlayerPosition, state.NaturalSpawnNextAttemptTick));
        return new GameplaySessionSnapshot(
            spatial, source.Name, source.GameRules.TicksPerSecond,
            source.GameRules.SpawnCreatures,
            source.Player.CaptureState(), clocks);
    }

    public static DimensionSessionStateStore Restore(
        WorldCreationOptions creation, DimensionRegistry dimensions,
        GameplaySessionSnapshot snapshot,
        BlockRegistry blocks, FluidRegistry fluids,
        DyeRegistry dyes, AttachedLayerRegistry layers)
    {
        ArgumentNullException.ThrowIfNull(creation);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!string.Equals(creation.Name, snapshot.Name, StringComparison.Ordinal))
            throw new InvalidDataException("Saved world name differs from creation identity.");

        // New, independent world only. Any invalid spatial record aborts
        // before an active or loaded state is published.
        var restored = DimensionChunkSaveCodec.Restore(
            creation, dimensions, snapshot.Spatial,
            blocks, fluids, dyes, layers);
        restored.GameRules.SetTicksPerSecond(snapshot.TicksPerSecond);
        restored.GameRules.SetSpawnCreatures(snapshot.SpawnCreatures);
        restored.Player.RestoreState(snapshot.Player);
        foreach (var clock in snapshot.Spheres)
        {
            var state = restored.GetOrCreate(clock.Dimension);
            state.WorldTick = clock.WorldTick;
            state.DayNight = clock.DayNight;
            state.PlayerPosition = clock.Position;
            state.NaturalSpawnNextAttemptTick = clock.NaturalSpawnNextAttemptTick;
        }

        return restored;
    }
}

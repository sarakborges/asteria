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
    private readonly InventoryStack?[] _equipment;
    private readonly InventoryStack? _cursor;
    private readonly int _selectedSlot;

    public PlayerSessionSnapshot(
        PlayerGameMode gameMode, bool flying, PlayerInventorySnapshot inventory,
        float health = PlayerHealth.DefaultMaximum)
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

        if (!float.IsFinite(health) ||
            health < 0f || health > PlayerHealth.DefaultMaximum)
            throw new InvalidDataException("Invalid player health snapshot.");

        GameMode = gameMode;
        Flying = flying;
        Health = health;
        _selectedSlot = inventory.SelectedSlot;
        _backpack = inventory.Backpack.ToArray();
        _hotbar = inventory.Hotbar.ToArray();
        _equipment = (inventory.Equipment ??
            new InventoryStack?[PlayerInventory.EquipmentSlots]).ToArray();
        if (_equipment.Length != PlayerInventory.EquipmentSlots ||
            _equipment.Any(stack => stack is not null &&
                (stack.Kind != InventoryEntryKind.Item || stack.Quantity != 1)))
            throw new InvalidDataException("Invalid player equipment snapshot.");
        _cursor = inventory.Cursor;
    }

    public PlayerGameMode GameMode { get; }
    public bool Flying { get; }
    public float Health { get; }
    public PlayerInventorySnapshot Inventory =>
        new(_selectedSlot, _backpack.ToArray(), _hotbar.ToArray(),
            _cursor, _equipment.ToArray());
}

/// <summary>
/// Detached contents of one placed Storage Box; no browser or IO adapter may
/// modify the authoritative saved slots through a returned array.
/// </summary>
public sealed class SavedStorageBoxContents
{
    private readonly InventoryStack?[] _slots;

    public SavedStorageBoxContents(
        WorldVoxelCoord position, IReadOnlyList<InventoryStack?> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (position.Y < 0 || slots.Count != StorageBoxRuntime.SlotCount ||
            !slots.Any(stack => stack is not null))
            throw new InvalidDataException("Invalid saved storage box contents.");
        Position = position;
        _slots = slots.ToArray();
    }

    public WorldVoxelCoord Position { get; }
    public InventoryStack?[] Slots => (InventoryStack?[])_slots.Clone();
}

/// <summary>Saved scalar state for one initialized Sphere.</summary>
public sealed record SphereClockSnapshot
{
    private readonly IReadOnlyList<SavedStorageBoxContents> _storageBoxes;
    private readonly IReadOnlyList<ManualStructurePlacementFootprint> _manualStructures;
    private readonly BlockPhysicsRuntimeSnapshot? _blockPhysics;
    private readonly DroppedBlockRuntimeSnapshot? _droppedBlocks;
    private readonly CreatureRuntimeSnapshot? _creatures;
    private readonly FluidUpdateQueueSnapshot? _fluidWork;
    private readonly BlockPhysicsUpdateQueueSnapshot? _physicsWork;

    public SphereClockSnapshot(
        DimensionId dimension, ulong worldTick, DayNightClockState? dayNight,
        Vector3? position, ulong naturalSpawnNextAttemptTick,
        IEnumerable<SavedStorageBoxContents>? storageBoxes = null,
        IEnumerable<ManualStructurePlacementFootprint>? manualStructures = null,
        BlockPhysicsRuntimeSnapshot? blockPhysics = null,
        DroppedBlockRuntimeSnapshot? droppedBlocks = null,
        CreatureRuntimeSnapshot? creatures = null,
        FluidUpdateQueueSnapshot? fluidWork = null,
        BlockPhysicsUpdateQueueSnapshot? physicsWork = null)
    {
        if (string.IsNullOrWhiteSpace(dimension.Value) ||
            dayNight is { Day: 0 } ||
            position is { } pos &&
            (!float.IsFinite(pos.X) || !float.IsFinite(pos.Y) ||
             !float.IsFinite(pos.Z) || pos.Y < 0))
            throw new InvalidDataException("Invalid saved Sphere clock or position.");

        var boxes = (storageBoxes ?? []).Take(65537).ToArray();
        if (boxes.Length > 65536 || boxes.Any(box => box is null) ||
            boxes.Select(box => box.Position).Distinct().Count() != boxes.Length)
            throw new InvalidDataException("Invalid saved storage box identities.");

        var footprints = (manualStructures ?? []).Take(65537).ToArray();
        if (footprints.Length > 65536 || footprints.Any(value => value is null))
            throw new InvalidDataException("Invalid saved manual structure count.");

        Dimension = dimension;
        WorldTick = worldTick;
        DayNight = dayNight;
        Position = position;
        NaturalSpawnNextAttemptTick = naturalSpawnNextAttemptTick;
        _storageBoxes = Array.AsReadOnly(boxes);
        _manualStructures = Array.AsReadOnly(footprints);
        _blockPhysics = SphereDynamicSnapshot.ValidateAndCopy(blockPhysics);
        _droppedBlocks = SphereDynamicSnapshot.ValidateAndCopy(droppedBlocks);
        _creatures = SphereDynamicSnapshot.ValidateAndCopy(creatures);
        _fluidWork = fluidWork;
        _physicsWork = physicsWork;
    }

    public IReadOnlyList<SavedStorageBoxContents> StorageBoxes => _storageBoxes;
    public IReadOnlyList<ManualStructurePlacementFootprint> ManualStructures =>
        _manualStructures;
    public BlockPhysicsRuntimeSnapshot? BlockPhysics =>
        SphereDynamicSnapshot.Copy(_blockPhysics);
    public DroppedBlockRuntimeSnapshot? DroppedBlocks =>
        SphereDynamicSnapshot.Copy(_droppedBlocks);
    public CreatureRuntimeSnapshot? Creatures =>
        SphereDynamicSnapshot.Copy(_creatures);
    public FluidUpdateQueueSnapshot? FluidWork => _fluidWork;
    public BlockPhysicsUpdateQueueSnapshot? PhysicsWork => _physicsWork;

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
        PlayerSessionSnapshot player, IEnumerable<SphereClockSnapshot> spheres,
        DimensionId? activeSphere = null)
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

        if (activeSphere is { } active &&
            !spatial.Spheres.Any(sphere => sphere.Dimension == active))
            throw new InvalidDataException("Active Sphere is missing from the snapshot.");

        ActiveSphere = activeSphere;
        Name = name;
        TicksPerSecond = ticksPerSecond;
        SpawnCreatures = spawnCreatures;
        _spheres = Array.AsReadOnly(entries);
    }

    public DimensionChunkSaveSnapshot Spatial { get; }
    public DimensionId? ActiveSphere { get; }
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
        DyeRegistry dyes, AttachedLayerRegistry layers,
        DimensionId? activeSphere = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var spatial = DimensionChunkSaveCodec.Capture(
            source, blocks, fluids, dyes, layers);
        var clocks = source.CreatedDimensions().Select(state =>
            new SphereClockSnapshot(
                state.Dimension.Id, state.WorldTick, state.DayNight,
                state.PlayerPosition, state.NaturalSpawnNextAttemptTick,
                state.StorageBoxes.CaptureOccupied().Select(box =>
                    new SavedStorageBoxContents(box.Position, box.Slots)),
                state.ManualStructures.Snapshot(),
                state.BlockPhysics, state.DroppedBlocks, state.Creatures,
                state.PendingFluidWork, state.PendingPhysicsWork));
        return new GameplaySessionSnapshot(
            spatial, source.Name, source.GameRules.TicksPerSecond,
            source.GameRules.SpawnCreatures,
            source.Player.CaptureState(), clocks, activeSphere);
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
            state.StorageBoxes.RestoreOccupied(
                clock.StorageBoxes, state.World, blocks);
            state.ManualStructures.RestoreCommitted(clock.ManualStructures);
            state.BlockPhysics = clock.BlockPhysics;
            state.DroppedBlocks = clock.DroppedBlocks;
            state.Creatures = clock.Creatures;
            state.PendingFluidWork = clock.FluidWork;
            state.PendingPhysicsWork = clock.PhysicsWork;
        }

        return restored;
    }
}

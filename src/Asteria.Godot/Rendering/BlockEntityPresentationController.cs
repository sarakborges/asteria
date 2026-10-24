using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

public sealed class BlockEntityPresentationController
{
    private static readonly IComparer<FallingBlockId>
        FallingBlockIdComparer =
            Comparer<FallingBlockId>.Create(
                static (left, right) =>
                    left.Value.CompareTo(
                        right.Value));
    private static readonly IComparer<DroppedBlockId>
        DroppedBlockIdComparer =
            Comparer<DroppedBlockId>.Create(
                static (left, right) =>
                    left.Value.CompareTo(
                        right.Value));

    private const int MaximumSharedMeshCount = 256;

    // Restored falling-block snapshots can contain hundreds of entities. Limit
    // node and per-instance mesh creation so world entry does not pay that cost
    // in one gameplay frame; existing presentations still sync every frame.
    private const int MaximumNewPresentationsPerSync = 64;

    private readonly Node3D _parent;
    private readonly BlockRegistry _blocks;
    private readonly FluidRegistry _fluids;
    private readonly TerrainTextureLookup _textures;
    private readonly VoxelTerrainMaterialSet _materials;
    private readonly Func<InventoryEntry, Texture2D?> _itemIcon;
    private readonly SortedDictionary<FallingBlockId, FallingBlockPresentation>
        _falling =
            new(FallingBlockIdComparer);
    private readonly SortedDictionary<DroppedBlockId, DroppedBlockPresentation>
        _dropped =
            new(DroppedBlockIdComparer);
    private readonly Dictionary<BlockStateSnapshot, ArrayMesh>
        _sharedMeshes = [];
    private readonly Dictionary<BlockStateSnapshot, ChunkMeshData>
        _sharedFallingGeometry = [];
    private readonly HashSet<FallingBlockId> _activeFalling = [];
    private readonly HashSet<DroppedBlockId> _activeDropped = [];
    private readonly List<FallingBlockId> _retiredFalling = [];
    private readonly List<DroppedBlockId> _retiredDropped = [];

    public BlockEntityPresentationController(
        Node3D parent,
        VoxelWorld world,
        BlockRegistry blocks,
        FluidRegistry fluids,
        TerrainTextureLookup textures,
        VoxelTerrainMaterialSet materials,
        Func<InventoryEntry, Texture2D?> itemIcon)
    {
        _parent =
            parent ??
            throw new ArgumentNullException(nameof(parent));
        ArgumentNullException.ThrowIfNull(world);
        _blocks =
            blocks ??
            throw new ArgumentNullException(nameof(blocks));
        _fluids =
            fluids ??
            throw new ArgumentNullException(nameof(fluids));
        _textures =
            textures ??
            throw new ArgumentNullException(nameof(textures));
        _materials =
            materials ??
            throw new ArgumentNullException(nameof(materials));
        _itemIcon = itemIcon ?? throw new ArgumentNullException(nameof(itemIcon));
    }

    public void SyncByIdOrder(
        IEnumerable<FallingBlockState> falling,
        IEnumerable<DroppedBlockState> dropped)
    {
        ArgumentNullException.ThrowIfNull(falling);
        ArgumentNullException.ThrowIfNull(dropped);

        var remainingNewPresentations =
            MaximumNewPresentationsPerSync;
        SyncFalling(falling, ref remainingNewPresentations);
        SyncDropped(dropped, ref remainingNewPresentations);
    }

    private void SyncFalling(
        IEnumerable<FallingBlockState> states,
        ref int remainingNewPresentations)
    {
        _activeFalling.Clear();

        foreach (var state in states)
        {
            _activeFalling.Add(state.Id);

            if (!_falling.TryGetValue(
                    state.Id,
                    out var presentation))
            {
                if (remainingNewPresentations <= 0)
                    continue;

                var geometry = GetFallingGeometry(state.Block);
                presentation = new FallingBlockPresentation(
                    state, ChunkMeshBuilder.CreateMesh(geometry, _materials));
                _falling.Add(
                    state.Id,
                    presentation);
                _parent.AddChild(
                    presentation.Root);
                remainingNewPresentations--;
            }

            presentation.Apply(state);
        }

        _retiredFalling.Clear();

        foreach (var id in _falling.Keys)
        {
            if (!_activeFalling.Contains(id))
            {
                _retiredFalling.Add(id);
            }
        }

        foreach (var id in _retiredFalling)
        {
            _falling[id].Retire();
            _falling.Remove(id);
        }
    }

    private void SyncDropped(
        IEnumerable<DroppedBlockState> states,
        ref int remainingNewPresentations)
    {
        _activeDropped.Clear();

        foreach (var state in states)
        {
            _activeDropped.Add(state.Id);

            if (!_dropped.TryGetValue(
                    state.Id,
                    out var presentation))
            {
                if (remainingNewPresentations <= 0)
                    continue;

                presentation = state.Block is { } block
                    ? new DroppedBlockPresentation(state, GetMesh(block))
                    : new DroppedBlockPresentation(
                        state,
                        _itemIcon(state.Stack.Entry) ??
                        throw new InvalidOperationException(
                            $"Missing icon for dropped item {state.Stack.Id}"));
                _dropped.Add(
                    state.Id,
                    presentation);
                _parent.AddChild(
                    presentation.Root);
                remainingNewPresentations--;
            }

            presentation.Apply(state);
        }

        _retiredDropped.Clear();

        foreach (var id in _dropped.Keys)
        {
            if (!_activeDropped.Contains(id))
            {
                _retiredDropped.Add(id);
            }
        }

        foreach (var id in _retiredDropped)
        {
            _dropped[id].Retire();
            _dropped.Remove(id);
        }
    }

    private ChunkMeshData GetFallingGeometry(
        BlockStateSnapshot block)
    {
        if (_sharedFallingGeometry.TryGetValue(block, out var geometry))
            return geometry;

        geometry = BlockStateMeshBuilder.BuildData(
            block, _blocks, _fluids, _textures);
        if (_sharedFallingGeometry.Count < MaximumSharedMeshCount)
            _sharedFallingGeometry.Add(block, geometry);

        return geometry;
    }

    private ArrayMesh GetMesh(
        BlockStateSnapshot block)
    {
        if (_sharedMeshes.TryGetValue(
                block,
                out var mesh))
        {
            return mesh;
        }

        mesh =
            BlockStateMeshBuilder.Build(
                block,
                _blocks,
                _fluids,
                _textures,
                _materials);

        if (_sharedMeshes.Count <
            MaximumSharedMeshCount)
        {
            _sharedMeshes.Add(
                block,
                mesh);
        }

        return mesh;
    }
}

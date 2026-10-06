using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

public sealed class BlockEntityPresentationController
{
    private const int MaximumSharedMeshCount = 256;

    private readonly Node3D _parent;
    private readonly BlockRegistry _blocks;
    private readonly FluidRegistry _fluids;
    private readonly TerrainTextureLookup _textures;
    private readonly VoxelTerrainMaterialSet _materials;
    private readonly SortedDictionary<FallingBlockId, FallingBlockPresentation>
        _falling = [];
    private readonly SortedDictionary<DroppedBlockId, DroppedBlockPresentation>
        _dropped = [];
    private readonly Dictionary<BlockStateSnapshot, ArrayMesh>
        _sharedMeshes = [];
    private readonly List<FallingBlockState> _fallingStates = [];
    private readonly List<DroppedBlockState> _droppedStates = [];
    private readonly HashSet<FallingBlockId> _activeFalling = [];
    private readonly HashSet<DroppedBlockId> _activeDropped = [];
    private readonly List<FallingBlockId> _retiredFalling = [];
    private readonly List<DroppedBlockId> _retiredDropped = [];

    public BlockEntityPresentationController(
        Node3D parent,
        BlockRegistry blocks,
        FluidRegistry fluids,
        TerrainTextureLookup textures,
        VoxelTerrainMaterialSet materials)
    {
        _parent =
            parent ??
            throw new ArgumentNullException(nameof(parent));
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
    }

    public void Sync(
        IEnumerable<FallingBlockState> falling,
        IEnumerable<DroppedBlockState> dropped)
    {
        ArgumentNullException.ThrowIfNull(falling);
        ArgumentNullException.ThrowIfNull(dropped);

        _fallingStates.Clear();
        _fallingStates.AddRange(falling);
        _fallingStates.Sort(
            static (left, right) =>
                left.Id.Value.CompareTo(
                    right.Id.Value));

        _droppedStates.Clear();
        _droppedStates.AddRange(dropped);
        _droppedStates.Sort(
            static (left, right) =>
                left.Id.Value.CompareTo(
                    right.Id.Value));

        SyncFalling(_fallingStates);
        SyncDropped(_droppedStates);
    }

    private void SyncFalling(
        IEnumerable<FallingBlockState> states)
    {
        _activeFalling.Clear();

        foreach (var state in states)
        {
            _activeFalling.Add(state.Id);

            if (!_falling.TryGetValue(
                    state.Id,
                    out var presentation))
            {
                presentation =
                    new FallingBlockPresentation(
                        state,
                        GetMesh(state.Block));
                _falling.Add(
                    state.Id,
                    presentation);
                _parent.AddChild(
                    presentation.Root);
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
        IEnumerable<DroppedBlockState> states)
    {
        _activeDropped.Clear();

        foreach (var state in states)
        {
            _activeDropped.Add(state.Id);

            if (!_dropped.TryGetValue(
                    state.Id,
                    out var presentation))
            {
                presentation =
                    new DroppedBlockPresentation(
                        state,
                        GetMesh(state.Block));
                _dropped.Add(
                    state.Id,
                    presentation);
                _parent.AddChild(
                    presentation.Root);
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

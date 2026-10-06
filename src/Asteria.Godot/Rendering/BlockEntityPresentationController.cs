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
    private readonly Dictionary<FallingBlockId, FallingBlockPresentation>
        _falling = [];
    private readonly Dictionary<DroppedBlockId, DroppedBlockPresentation>
        _dropped = [];
    private readonly Dictionary<BlockStateSnapshot, ArrayMesh>
        _sharedMeshes = [];

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

        SyncFalling(
            falling.OrderBy(
                state => state.Id.Value));
        SyncDropped(
            dropped.OrderBy(
                state => state.Id.Value));
    }

    private void SyncFalling(
        IEnumerable<FallingBlockState> states)
    {
        var active =
            new HashSet<FallingBlockId>();

        foreach (var state in states)
        {
            active.Add(state.Id);

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

        foreach (var id in
                 _falling.Keys
                     .Where(id =>
                         !active.Contains(id))
                     .OrderBy(id => id.Value)
                     .ToArray())
        {
            _falling[id].Retire();
            _falling.Remove(id);
        }
    }

    private void SyncDropped(
        IEnumerable<DroppedBlockState> states)
    {
        var active =
            new HashSet<DroppedBlockId>();

        foreach (var state in states)
        {
            active.Add(state.Id);

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

        foreach (var id in
                 _dropped.Keys
                     .Where(id =>
                         !active.Contains(id))
                     .OrderBy(id => id.Value)
                     .ToArray())
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

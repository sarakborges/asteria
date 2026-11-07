using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

public sealed class FallingBlockPresentation
{
    private readonly MeshInstance3D _meshInstance;
    private readonly VoxelTerrainMaterialSet _materials;
    private ChunkMeshData _geometry;
    private ArrayMesh _mesh;
    private Vector3 _lastPosition;
    private WorldVoxelCoord _lastLightingPosition;
    private readonly VoxelLightChunkStamp[] _lastLightingNeighborhood = new VoxelLightChunkStamp[8];
    private int _lastLightingNeighborhoodCount;
    private bool _hasPosition;
    private bool _hasLightingSample;

    public FallingBlockPresentation(
        FallingBlockState state,
        ArrayMesh mesh,
        ChunkMeshData geometry,
        VoxelTerrainMaterialSet materials)
    {
        Id = state.Id;
        _mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
        _geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        _materials = materials ?? throw new ArgumentNullException(nameof(materials));

        Root = new Node3D
        {
            Name = $"FallingBlock_{state.Id.Value}",
        };

        _meshInstance = new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = mesh,
        };
        Root.AddChild(_meshInstance);

        Apply(state);
    }

    public FallingBlockId Id { get; }

    public Node3D Root { get; }

    public void Apply(FallingBlockState state)
    {
        var position = new Vector3(
            state.ColumnX,
            (float)(state.CenterY - 0.5),
            state.ColumnZ);
        // Sleeping blocks can remain unchanged for thousands of frames.
        // A Node3D position setter crosses into the engine and invalidates
        // transforms even when the coordinates have not moved.
        if (_hasPosition && _lastPosition == position)
            return;

        Root.Position = position;
        _lastPosition = position;
        _hasPosition = true;
    }

    public void UpdateLighting(
        VoxelWorld world,
        BlockRegistry blocks,
        FallingBlockState state)
    {
        var voxelY = Math.Max(0, (int)Math.Floor(state.CenterY - 0.5));
        var position = new WorldVoxelCoord(
            state.ColumnX,
            voxelY,
            state.ColumnZ);
        Span<VoxelLightChunkStamp> neighborhood = stackalloc VoxelLightChunkStamp[8];
        var neighborhoodCount =
            world.CaptureLightingNeighborhood(position, neighborhood);

        if (_hasLightingSample &&
            _lastLightingPosition == position &&
            _lastLightingNeighborhoodCount == neighborhoodCount &&
            neighborhood[..neighborhoodCount].SequenceEqual(
                _lastLightingNeighborhood.AsSpan(0, neighborhoodCount)))
            return;

        if (!ChunkMeshLightingUpdater.TryRelightPortableBlock(
                world, position, blocks, _geometry, out var relitGeometry))
            return;

        if (!ChunkMeshBuilder.TryUpdateLighting(_mesh, relitGeometry))
        {
            // Each falling entity owns its ArrayMesh; rebuilding this fallback
            // cannot overwrite the lighting of another block instance.
            _mesh = ChunkMeshBuilder.CreateMesh(relitGeometry, _materials);
            _meshInstance.Mesh = _mesh;
        }

        _geometry = relitGeometry;
        neighborhood[..neighborhoodCount].CopyTo(_lastLightingNeighborhood);
        _lastLightingNeighborhoodCount = neighborhoodCount;
        _lastLightingPosition = position;
        _hasLightingSample = true;
    }

    public void Retire()
    {
        Root.QueueFree();
    }
}

using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

/// <summary>
/// One reusable, non-colliding ghost mesh for an accepted Core placement.
/// Uses the same voxel mesh/texture layers as the real terrain. Only the
/// selected cell's authored appearance rebuilds; moving the aim changes
/// the node transform, not the mesh or authoritative world state.
/// </summary>
public sealed class BlockPlacementPreviewPresentation
{
    private const float Opacity = 0.82f;
    private readonly BlockRegistry _blocks;
    private readonly FluidRegistry _fluids;
    private readonly TerrainTextureLookup _textures;
    private readonly VoxelTerrainMaterialSet _terrainMaterials;
    private readonly MeshInstance3D _visual;
    private VoxelCell? _shownCell;

    public BlockPlacementPreviewPresentation(
        Node3D parent, BlockRegistry blocks, FluidRegistry fluids,
        TerrainTextureLookup textures, VoxelTerrainMaterialSet materials)
    {
        ArgumentNullException.ThrowIfNull(parent);
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _fluids = fluids ?? throw new ArgumentNullException(nameof(fluids));
        _textures = textures ?? throw new ArgumentNullException(nameof(textures));
        _terrainMaterials = materials ?? throw new ArgumentNullException(nameof(materials));

        // A separate instance of the existing translucent terrain shader
        // preserves all authored base/overlay indices, cutouts, and vertex
        // tints without mutating any world material or duplicating shaders.
        var terrainShader = (ShaderMaterial)materials.Get(
            new TerrainRenderBatch(BlockRenderMode.Translucent, false));
        var previewMaterial = new ShaderMaterial { Shader = terrainShader.Shader };
        previewMaterial.SetShaderParameter("terrain_textures", materials.TextureArray);
        previewMaterial.SetShaderParameter("preview_opacity", Opacity);

        _visual = new MeshInstance3D
        {
            Name = "BlockPlacementPreview",
            Visible = false,
            MaterialOverride = previewMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(_visual);
    }

    public void Sync(BlockPlacementDecision placement)
    {
        if (!placement.Accepted || placement.Cell.IsEmpty)
        {
            Hide();
            return;
        }

        if (_shownCell != placement.Cell)
        {
            var candidate = BlockStateSnapshot.FromCell(placement.Cell);
            _visual.Mesh = BlockStateMeshBuilder.Build(
                candidate, _blocks, _fluids, _textures, _terrainMaterials);
            _shownCell = placement.Cell;
        }
        var position = placement.Position;
        var translation = new Vector3(position.X, position.Y, position.Z);
        if (_visual.Position != translation)
            _visual.Position = translation;
        _visual.Visible = true;
    }

    public void Hide() => _visual.Visible = false;
}

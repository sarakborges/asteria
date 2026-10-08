using Asteria.Client.Content;
using Asteria.Core.Content;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

/// <summary>
/// One world-space visual for the block currently being mined. Reuses the
/// actual portable block mesh, including microblock occupancy and authored
/// shape, with MineClone's pack-owned crack stages as material overrides.
/// </summary>
public sealed class MiningCrackPresentation
{
    private const int Stages = 10;
    private const string CrackShader = """
        shader_type spatial;
        render_mode unshaded, cull_disabled, depth_draw_never, blend_mix;
        uniform sampler2D crack_texture : source_color, filter_nearest, repeat_enable;
        varying vec3 local_position;
        varying vec3 local_normal;
        void vertex() {
            local_position = VERTEX;
            local_normal = NORMAL;
            VERTEX += NORMAL * 0.002;
        }
        void fragment() {
            vec3 axis = abs(local_normal);
            vec2 uv;
            if (axis.x > axis.y && axis.x > axis.z) {
                uv = local_position.zy;
            } else if (axis.y > axis.z) {
                uv = local_position.xz;
            } else {
                uv = local_position.xy;
            }
            vec4 crack = texture(crack_texture, uv);
            ALBEDO = crack.rgb;
            ALPHA = crack.a;
            ALPHA_SCISSOR_THRESHOLD = 0.1;
        }
        """;

    private readonly BlockRegistry _blocks;
    private readonly FluidRegistry _fluids;
    private readonly TerrainTextureLookup _textures;
    private readonly VoxelTerrainMaterialSet _materials;
    private readonly PackSelection _pack;
    private readonly MeshInstance3D _visual;
    private ShaderMaterial[]? _stages;
    private WorldVoxelCoord? _target;
    private BlockStateSnapshot? _block;
    private int _stage = -1;

    public MiningCrackPresentation(
        Node3D parent,
        PackSelection pack,
        BlockRegistry blocks,
        FluidRegistry fluids,
        TerrainTextureLookup textures,
        VoxelTerrainMaterialSet materials)
    {
        ArgumentNullException.ThrowIfNull(parent);
        _pack = pack;
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _fluids = fluids ?? throw new ArgumentNullException(nameof(fluids));
        _textures = textures ?? throw new ArgumentNullException(nameof(textures));
        _materials = materials ?? throw new ArgumentNullException(nameof(materials));
        _visual = new MeshInstance3D
        {
            Name = "ActiveMiningCracks",
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(_visual);
    }

    public void Sync(VoxelWorld world, BlockMiningRuntime mining)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(mining);
        if (mining.Target is not { } target ||
            mining.Progress is not { } progress || progress <= 0f ||
            !world.TryGetCell(target, out var cell) || cell.IsEmpty)
        {
            Hide();
            return;
        }

        var block = BlockStateSnapshot.Capture(world, target, cell);
        if (_target != target || _block != block)
        {
            _visual.Mesh = BlockStateMeshBuilder.Build(
                block, _blocks, _fluids, _textures, _materials);
            _visual.Position = new Vector3(target.X, target.Y, target.Z);
            _target = target;
            _block = block;
            _stage = -1;
        }

        var stage = Math.Clamp((int)MathF.Floor(progress * Stages), 0, Stages - 1);
        if (_stage != stage)
        {
            _stages ??= LoadStages();
            _visual.MaterialOverride = _stages[stage];
            _stage = stage;
        }
        _visual.Visible = true;
    }

    public void Hide()
    {
        if (!_visual.Visible && _target is null) return;
        _visual.Visible = false;
        _visual.Mesh = null;
        _target = null;
        _block = null;
        _stage = -1;
    }

    private ShaderMaterial[] LoadStages()
    {
        var shader = new Shader { Code = CrackShader };
        return Enumerable.Range(0, Stages).Select(i =>
        {
            var texture = ProjectPackFiles.LoadTexture(
                _pack, $"textures/destroy_stage/destroy_stage_{i}.png");
            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("crack_texture", texture);
            return material;
        }).ToArray();
    }
}

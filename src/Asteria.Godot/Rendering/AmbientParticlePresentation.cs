using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

/// <summary>
/// One MultiMesh per authored rule. Upload transforms in one buffer operation
/// rather than crossing the Godot managed/native boundary per particle.
/// </summary>
public sealed class AmbientParticlePresentation
{
    private const int TransformFloats = 12;

    private sealed class Batch
    {
        public Batch(MultiMesh multimesh, MultiMeshInstance3D node)
        {
            Mesh = multimesh;
            Node = node;
            Buffer = new float[AmbientParticleRuntime.MaximumActive * TransformFloats];
        }

        public MultiMesh Mesh { get; }
        public MultiMeshInstance3D Node { get; }
        public float[] Buffer { get; }
        public int Visible { get; set; }
        public Vector3 Minimum { get; set; }
        public Vector3 Maximum { get; set; }
    }

    private readonly Batch[] _batches;
    private readonly Dictionary<string, int> _indices;

    public AmbientParticlePresentation(Node3D parent, AmbientParticleRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(registry);
        _batches = new Batch[registry.Definitions.Count];
        _indices = new Dictionary<string, int>(
            registry.Definitions.Count, StringComparer.Ordinal);

        for (var i = 0; i < registry.Definitions.Count; i++)
        {
            var rule = registry.Definitions[i];
            var material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(
                    rule.Color.X, rule.Color.Y, rule.Color.Z, rule.Opacity),
            };
            var cube = new BoxMesh
            {
                Size = Vector3.One,
                Material = material,
            };
            var multimesh = new MultiMesh
            {
                Mesh = cube,
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                InstanceCount = AmbientParticleRuntime.MaximumActive,
                VisibleInstanceCount = 0,
            };
            var node = new MultiMeshInstance3D
            {
                Name = "AmbientParticles_" + i,
                Multimesh = multimesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            parent.AddChild(node);
            _batches[i] = new Batch(multimesh, node);
            _indices.Add(rule.Id, i);
        }
    }

    public void Sync(AmbientParticleRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        foreach (var batch in _batches)
        {
            batch.Visible = 0;
            batch.Minimum = new Vector3(float.PositiveInfinity,
                float.PositiveInfinity, float.PositiveInfinity);
            batch.Maximum = new Vector3(float.NegativeInfinity,
                float.NegativeInfinity, float.NegativeInfinity);
        }

        foreach (var particle in runtime.Particles)
        {
            var batch = _batches[_indices[particle.RuleId]];
            var offset = batch.Visible++ * TransformFloats;
            var position = particle.Position;
            var scale = particle.Scale;
            var data = batch.Buffer;
            // RenderingServer's 3D transform buffer is row-major:
            // (xx, yx, zx, px, xy, yy, zy, py, xz, yz, zz, pz).
            data[offset] = scale;
            data[offset + 1] = 0f;
            data[offset + 2] = 0f;
            data[offset + 3] = position.X;
            data[offset + 4] = 0f;
            data[offset + 5] = scale;
            data[offset + 6] = 0f;
            data[offset + 7] = position.Y;
            data[offset + 8] = 0f;
            data[offset + 9] = 0f;
            data[offset + 10] = scale;
            data[offset + 11] = position.Z;

            var center = new Vector3(position.X, position.Y, position.Z);
            var halfSize = Vector3.One * (scale * 0.5f);
            batch.Minimum = batch.Minimum.Min(center - halfSize);
            batch.Maximum = batch.Maximum.Max(center + halfSize);
        }

        foreach (var batch in _batches)
        {
            if (batch.Visible > 0)
            {
                // One upload per non-empty rule, with a reused managed buffer.
                RenderingServer.MultimeshSetBuffer(batch.Mesh.GetRid(), batch.Buffer);
                batch.Node.CustomAabb =
                    new Aabb(batch.Minimum, batch.Maximum - batch.Minimum);
            }

            if (batch.Mesh.VisibleInstanceCount != batch.Visible)
                batch.Mesh.VisibleInstanceCount = batch.Visible;
        }
    }

    public void Clear()
    {
        foreach (var batch in _batches)
        {
            batch.Visible = 0;
            batch.Mesh.VisibleInstanceCount = 0;
        }
    }
}

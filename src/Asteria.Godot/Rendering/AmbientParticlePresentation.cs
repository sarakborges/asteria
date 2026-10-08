using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

/// <summary>
/// Single batched visual mesh per authored rule. Particles remain plain Core data;
/// no gameplay entity or per-particle Godot node is ever created.
/// </summary>
public sealed class AmbientParticlePresentation
{
    private sealed class Batch
    {
        public Batch(MultiMesh multimesh)
        {
            Mesh = multimesh;
        }

        public MultiMesh Mesh { get; }
        public int Visible { get; set; }
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
            _batches[i] = new Batch(multimesh);
            _indices.Add(rule.Id, i);
        }
    }

    public void Sync(AmbientParticleRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        foreach (var batch in _batches)
            batch.Visible = 0;

        foreach (var particle in runtime.Particles)
        {
            var batch = _batches[_indices[particle.RuleId]];
            var position = particle.Position;
            var scale = particle.Scale;
            batch.Mesh.SetInstanceTransform(
                batch.Visible++,
                new Transform3D(
                    Basis.Identity.Scaled(new Vector3(scale, scale, scale)),
                    new Vector3(position.X, position.Y, position.Z)));
        }

        foreach (var batch in _batches)
            batch.Mesh.VisibleInstanceCount = batch.Visible;
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

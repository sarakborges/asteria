using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

/// <summary>
/// One batched unlit particle mesh per Sphere. All motion and lifetime state
/// comes from Core; there are no per-particle scene nodes or gameplay effects.
/// </summary>
internal sealed class CreatureParticlePresentation
{
    private readonly MultiMesh _batch;
    private readonly MultiMeshInstance3D _node;

    public CreatureParticlePresentation(Node3D parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _batch = new MultiMesh
        {
            UseColors = true,
            Mesh = new BoxMesh { Size = Vector3.One, Material = material },
            InstanceCount = CreatureParticleRuntime.MaximumActive,
            VisibleInstanceCount = 0,
        };
        _node = new MultiMeshInstance3D
        {
            Name = "CreatureParticles",
            Multimesh = _batch,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(_node);
    }

    public void Sync(CreatureParticleRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var particles = runtime.Particles;
        var count = Math.Min(particles.Count, CreatureParticleRuntime.MaximumActive);
        if (count == 0)
        {
            _batch.VisibleInstanceCount = 0;
            return;
        }

        var minimum = new Vector3(float.PositiveInfinity,
            float.PositiveInfinity, float.PositiveInfinity);
        var maximum = new Vector3(float.NegativeInfinity,
            float.NegativeInfinity, float.NegativeInfinity);
        for (var i = 0; i < count; i++)
        {
            var particle = particles[i];
            var scale = particle.Scale;
            var position = new Vector3(
                particle.Position.X, particle.Position.Y, particle.Position.Z);
            _batch.SetInstanceTransform(i,
                new Transform3D(Basis.Identity.Scaled(
                    new Vector3(scale, scale, scale)), position));
            _batch.SetInstanceColor(i, new Color(
                particle.Color.X, particle.Color.Y,
                particle.Color.Z, particle.Color.W));
            var extent = Vector3.One * scale;
            minimum = minimum.Min(position - extent);
            maximum = maximum.Max(position + extent);
        }
        _node.CustomAabb = new Aabb(minimum, maximum - minimum);
        _batch.VisibleInstanceCount = count;
    }

    public void Clear() => _batch.VisibleInstanceCount = 0;
}

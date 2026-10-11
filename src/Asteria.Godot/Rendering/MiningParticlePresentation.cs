using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

/// <summary>One pooled, batched voxel-fragment visual per Sphere. Core owns
/// lifetime and motion; this adapter only publishes GPU instances.</summary>
internal sealed class MiningParticlePresentation
{
    private readonly MultiMesh _batch;
    private readonly MultiMeshInstance3D _visual;

    public MiningParticlePresentation(Node3D parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.73f, 0.70f, 0.64f),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _batch = new MultiMesh
        {
            Mesh = new BoxMesh { Size = Vector3.One, Material = material },
            InstanceCount = MiningParticleRuntime.MaximumActive,
            VisibleInstanceCount = 0,
        };
        _visual = new MultiMeshInstance3D
        {
            Name = "MiningChips",
            Multimesh = _batch,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(_visual);
    }

    public void Sync(MiningParticleRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var count = Math.Min(runtime.Particles.Count, MiningParticleRuntime.MaximumActive);
        if (count == 0)
        {
            _batch.VisibleInstanceCount = 0;
            return;
        }

        var min = new Vector3(float.PositiveInfinity,
            float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity,
            float.NegativeInfinity, float.NegativeInfinity);
        for (var i = 0; i < count; ++i)
        {
            var particle = runtime.Particles[i];
            var size = particle.Scale;
            var position = new Vector3(
                particle.Position.X, particle.Position.Y, particle.Position.Z);
            _batch.SetInstanceTransform(i, new Transform3D(
                Basis.Identity.Scaled(new Vector3(size, size, size)), position));
            var extent = Vector3.One * size;
            min = min.Min(position - extent);
            max = max.Max(position + extent);
        }
        _visual.CustomAabb = new Aabb(min, max - min);
        _batch.VisibleInstanceCount = count;
    }

    public void Clear() => _batch.VisibleInstanceCount = 0;
}

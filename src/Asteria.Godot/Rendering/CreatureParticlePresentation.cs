using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

/// <summary>
/// One batched unlit particle mesh per Sphere. All motion and lifetime state
/// comes from Core. Both the transforms and their authored RGBA colors are
/// uploaded in one operation per frame, not per-particle Godot calls.
/// </summary>
internal sealed class CreatureParticlePresentation
{
    // Godot's 3D MultiMesh instance format: 12 transform floats + 4 RGBA floats.
    private const int FloatsPerInstance = 16;
    private readonly MultiMesh _batch;
    private readonly MultiMeshInstance3D _node;
    private readonly float[] _buffer =
        new float[CreatureParticleRuntime.MaximumActive * FloatsPerInstance];

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
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
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
            if (_batch.VisibleInstanceCount != 0)
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
            var position = particle.Position;
            var color = particle.Color;
            var offset = i * FloatsPerInstance;

            // Row-major Transform3D, matching RenderingServer.MultimeshSetBuffer.
            _buffer[offset] = scale;
            _buffer[offset + 1] = 0f;
            _buffer[offset + 2] = 0f;
            _buffer[offset + 3] = position.X;
            _buffer[offset + 4] = 0f;
            _buffer[offset + 5] = scale;
            _buffer[offset + 6] = 0f;
            _buffer[offset + 7] = position.Y;
            _buffer[offset + 8] = 0f;
            _buffer[offset + 9] = 0f;
            _buffer[offset + 10] = scale;
            _buffer[offset + 11] = position.Z;
            _buffer[offset + 12] = color.X;
            _buffer[offset + 13] = color.Y;
            _buffer[offset + 14] = color.Z;
            _buffer[offset + 15] = color.W;

            var center = new Vector3(position.X, position.Y, position.Z);
            var halfExtent = Vector3.One * (scale * 0.5f);
            minimum = minimum.Min(center - halfExtent);
            maximum = maximum.Max(center + halfExtent);
        }

        // The full preallocated buffer is required by the fixed InstanceCount.
        // VisibleInstanceCount limits drawing to the active prefix.
        RenderingServer.MultimeshSetBuffer(_batch.GetRid(), _buffer);
        _node.CustomAabb = new Aabb(minimum, maximum - minimum);
        if (_batch.VisibleInstanceCount != count)
            _batch.VisibleInstanceCount = count;
    }

    public void Clear() => _batch.VisibleInstanceCount = 0;
}

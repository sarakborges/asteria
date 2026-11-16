using Godot;

namespace Asteria.Client.Rendering;

/// <summary>Converts Bevy's explicit XYZ local Euler rotations to Godot;
/// Quaternion.FromEuler uses Godot's YXZ order instead.</summary>
internal static class PlayerVisualRotation
{
    public static Quaternion EulerXyz(Vector3 angles) =>
        new Quaternion(Vector3.Right, angles.X) *
        new Quaternion(Vector3.Up, angles.Y) *
        new Quaternion(Vector3.Back, angles.Z);
}

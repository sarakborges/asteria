namespace Asteria.Core.Content;

/// <summary>
/// Frame a creature portrait from its authored collider, using MineClone's
/// three-quarter camera offsets. This is visual data only; it does not change
/// the creature's physics collider or its gameplay camera.
/// </summary>
public static class CreaturePortraitFraming
{
    public readonly record struct Pose(
        ContentVector3 CameraPosition, ContentVector3 LookAt);

    public static Pose For(CreatureCollider collider)
    {
        ArgumentNullException.ThrowIfNull(collider);

        var size = collider.Size;
        var radius = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
        var distance = radius * 2.4f + 0.45f;
        var center = collider.CenterOffset;
        return new Pose(
            new ContentVector3(
                center.X + distance * 0.4f,
                center.Y + distance * 0.25f,
                center.Z + distance),
            center);
    }
}

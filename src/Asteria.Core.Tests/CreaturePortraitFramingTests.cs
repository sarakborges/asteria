using Asteria.Core.Content;

namespace Asteria.Core.Tests;

public sealed class CreaturePortraitFramingTests
{
    [Fact]
    public void UsesMineCloneThreeQuarterCameraForNormalCreature()
    {
        var collider = new CreatureCollider(
            new ContentVector3(0.78f, 0.84f, 0.78f),
            new ContentVector3(0f, 0.42f, 0f));

        var pose = CreaturePortraitFraming.For(collider);
        var distance = 0.84f * 2.4f + 0.45f;

        Assert.Equal(0f, pose.LookAt.X);
        Assert.Equal(0.42f, pose.LookAt.Y);
        Assert.Equal(0f, pose.LookAt.Z);
        Assert.Equal(distance * 0.4f, pose.CameraPosition.X, 5);
        Assert.Equal(0.42f + distance * 0.25f, pose.CameraPosition.Y, 5);
        Assert.Equal(distance, pose.CameraPosition.Z, 5);
    }

    [Fact]
    public void UsesLargestColliderAxisAndAllAuthoredCenterOffsets()
    {
        var collider = new CreatureCollider(
            new ContentVector3(1.92f, 1.5f, 1.82f),
            new ContentVector3(-0.1f, 0.75f, 0.2f));
        var pose = CreaturePortraitFraming.For(collider);
        var distance = 1.92f * 2.4f + 0.45f;

        Assert.Equal(collider.CenterOffset, pose.LookAt);
        Assert.Equal(-0.1f + distance * 0.4f, pose.CameraPosition.X, 5);
        Assert.Equal(0.75f + distance * 0.25f, pose.CameraPosition.Y, 5);
        Assert.Equal(0.2f + distance, pose.CameraPosition.Z, 5);
    }

    [Fact]
    public void FramingDoesNotModifyAuthoredCollider()
    {
        var collider = new CreatureCollider(
            new ContentVector3(1f, 2f, 3f),
            new ContentVector3(0f, 1f, 0f));

        _ = CreaturePortraitFraming.For(collider);

        Assert.Equal(new ContentVector3(1f, 2f, 3f), collider.Size);
        Assert.Equal(new ContentVector3(0f, 1f, 0f), collider.CenterOffset);
        Assert.Throws<ArgumentNullException>(() => CreaturePortraitFraming.For(null!));
    }
}

using System.Numerics;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class FluidBodyQueryTests
{
    [Fact]
    public void FullFluidCellImmersesPlayerBodyMidpoint()
    {
        var world =
            LoadedWorld();
        var water =
            new FluidRuntimeId(1);

        Assert.True(
            world.SetFluidAt(
                new WorldVoxelCoord(
                    2,
                    1,
                    2),
                FluidCell.Source(water),
                out _));

        var contact =
            FluidBodyQuery.Sample(
                world,
                BodyAt(
                    new Vector3(
                        2.5f,
                        0.2f,
                        2.5f)),
                eyeY: 1.8f);

        Assert.True(contact.IsImmersed);
        Assert.True(contact.EyeSubmerged);
        Assert.Equal(water, contact.Fluid);
        Assert.Equal(2f, contact.SurfaceY, 5);
    }

    [Fact]
    public void PartialFluidBelowBodyMidpointDoesNotTriggerSwimming()
    {
        var world =
            LoadedWorld();
        var water =
            new FluidRuntimeId(1);

        Assert.True(
            world.SetFluidAt(
                new WorldVoxelCoord(
                    2,
                    1,
                    2),
                FluidCell.Spreading(
                    water,
                    level: 1,
                    spreadDistance: 1),
                out _));

        var contact =
            FluidBodyQuery.Sample(
                world,
                BodyAt(
                    new Vector3(
                        2.5f,
                        0.3f,
                        2.5f)),
                eyeY: 1.9f);

        Assert.False(contact.IsImmersed);
        Assert.False(contact.EyeSubmerged);
    }

    [Fact]
    public void NearSurfaceUsesActualFluidHeight()
    {
        var world =
            LoadedWorld();
        var water =
            new FluidRuntimeId(1);

        Assert.True(
            world.SetFluidAt(
                new WorldVoxelCoord(
                    2,
                    1,
                    2),
                FluidCell.Source(water),
                out _));

        var contact =
            FluidBodyQuery.Sample(
                world,
                BodyAt(
                    new Vector3(
                        2.5f,
                        0.76f,
                        2.5f)),
                eyeY: 2.35f);

        Assert.True(contact.IsImmersed);
        Assert.True(
            contact.IsNearSurface(
                0.35f));
        Assert.False(contact.EyeSubmerged);
    }

    private static WorldAabb BodyAt(
        Vector3 feet) =>
        new(
            feet +
            new Vector3(
                -0.35f,
                0f,
                -0.35f),
            feet +
            new Vector3(
                0.35f,
                1.8f,
                0.35f));

    private static VoxelWorld LoadedWorld()
    {
        var world =
            new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());
        return world;
    }
}

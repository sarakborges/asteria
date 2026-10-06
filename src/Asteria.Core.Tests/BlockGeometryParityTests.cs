using System.Numerics;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BlockGeometryParityTests
{
    [Fact]
    public void SurfaceLayerCollisionRaycastAndMeshShareAuthoredThickness()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:layer",
                    shape:
                        BlockShapeDefinition.SurfaceLayer(
                            0.125f,
                            "asteria:stone")),
            ]);
        var world =
            LoadedWorld();
        var position =
            new WorldVoxelCoord(
                2,
                2,
                2);

        Assert.True(
            world.SetBlockAt(
                position,
                blocks.GetId(
                    "asteria:layer"),
                out _));

        var blocked =
            VoxelWorldCollision.QueryDetailed(
                world,
                blocks,
                new WorldAabb(
                    new Vector3(
                        2.2f,
                        2.02f,
                        2.2f),
                    new Vector3(
                        2.8f,
                        2.08f,
                        2.8f)));
        var clear =
            VoxelWorldCollision.QueryDetailed(
                world,
                blocks,
                new WorldAabb(
                    new Vector3(
                        2.2f,
                        2.5f,
                        2.2f),
                    new Vector3(
                        2.8f,
                        2.9f,
                        2.8f)));

        Assert.Equal(
            VoxelWorldCollisionState.Blocked,
            blocked.State);
        Assert.True(clear.IsClear);

        var hit =
            VoxelWorldRaycaster.Raycast(
                world,
                blocks,
                new Vector3(
                    2.5f,
                    3.5f,
                    2.5f),
                -Vector3.UnitY,
                3f);

        Assert.NotNull(hit);
        Assert.Equal(
            position,
            hit.Value.Voxel);

        var mesh =
            ChunkMeshDataBuilder.BuildMeshlet(
                world,
                ChunkCoord.Zero,
                blocks,
                new TerrainTextureLookup(
                    new Dictionary<string, int>()),
                meshletIndex: 0);

        Assert.True(mesh.HasCollision);
        Assert.InRange(
            mesh.CollisionFaces.Max(
                point => point.Y),
            2.1249f,
            2.1251f);
        Assert.InRange(
            mesh.CollisionFaces.Min(
                point => point.Y),
            1.9999f,
            2.0001f);
    }

    [Fact]
    public void HollowCenterAndWallAgreeBetweenCollisionAndRaycast()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:hollow",
                    shape:
                        BlockShapeDefinition.Hollow(
                            0.125f)),
            ]);
        var world =
            LoadedWorld();
        var position =
            new WorldVoxelCoord(
                2,
                2,
                2);

        Assert.True(
            world.SetBlockAt(
                position,
                blocks.GetId(
                    "asteria:hollow"),
                out _));

        var center =
            VoxelWorldCollision.QueryDetailed(
                world,
                blocks,
                new WorldAabb(
                    new Vector3(
                        2.45f,
                        2.1f,
                        2.45f),
                    new Vector3(
                        2.55f,
                        2.9f,
                        2.55f)));
        var wall =
            VoxelWorldCollision.QueryDetailed(
                world,
                blocks,
                new WorldAabb(
                    new Vector3(
                        2.01f,
                        2.1f,
                        2.45f),
                    new Vector3(
                        2.04f,
                        2.9f,
                        2.55f)));

        Assert.True(center.IsClear);
        Assert.Equal(
            VoxelWorldCollisionState.Blocked,
            wall.State);

        var centerRay =
            VoxelWorldRaycaster.Raycast(
                world,
                blocks,
                new Vector3(
                    2.5f,
                    1.5f,
                    2.5f),
                Vector3.UnitY,
                2f);
        var wallRay =
            VoxelWorldRaycaster.Raycast(
                world,
                blocks,
                new Vector3(
                    2.02f,
                    1.5f,
                    2.5f),
                Vector3.UnitY,
                2f);

        Assert.Null(centerRay);
        Assert.NotNull(wallRay);
        Assert.Equal(
            position,
            wallRay.Value.Voxel);
    }

    [Fact]
    public void MicroblockMaskDrivesCollisionAndRaycastFromSameOccupancy()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone",
                    tags: ["fragmentable"]),
            ]);
        var world =
            LoadedWorld();
        var position =
            new WorldVoxelCoord(
                2,
                2,
                2);

        Assert.True(
            world.SetBlockAt(
                position,
                blocks.GetId(
                    "asteria:stone"),
                out _));

        var address =
            VoxelCoordinates.FromWorld(
                position.X,
                position.Y,
                position.Z);
        Assert.True(
            world.GetChunk(address.Chunk)
                .SetMicroblockMask(
                    address.Local.X,
                    address.Local.Y,
                    address.Local.Z,
                    MicroblockMask.Empty.Edit(
                        0,
                        0,
                        0,
                        MicroblockResolution.Thick,
                        occupied: true)));

        var occupied =
            VoxelWorldCollision.QueryDetailed(
                world,
                blocks,
                new WorldAabb(
                    new Vector3(
                        2.05f,
                        2.05f,
                        2.05f),
                    new Vector3(
                        2.2f,
                        2.2f,
                        2.2f)));
        var gap =
            VoxelWorldCollision.QueryDetailed(
                world,
                blocks,
                new WorldAabb(
                    new Vector3(
                        2.7f,
                        2.7f,
                        2.7f),
                    new Vector3(
                        2.9f,
                        2.9f,
                        2.9f)));

        Assert.Equal(
            VoxelWorldCollisionState.Blocked,
            occupied.State);
        Assert.True(gap.IsClear);

        var hit =
            VoxelWorldRaycaster.Raycast(
                world,
                blocks,
                new Vector3(
                    1.5f,
                    2.1f,
                    2.1f),
                Vector3.UnitX,
                2f);
        var miss =
            VoxelWorldRaycaster.Raycast(
                world,
                blocks,
                new Vector3(
                    1.5f,
                    2.8f,
                    2.8f),
                Vector3.UnitX,
                2f);

        Assert.NotNull(hit);
        Assert.Equal(
            position,
            hit.Value.Voxel);
        Assert.Null(miss);
    }

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

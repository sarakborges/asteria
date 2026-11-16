using System.Numerics;
using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ProjectileRuntimeTests
{
    private static PackContentRegistry<CreatureDefinition> Definitions() =>
        PackContentRegistry<CreatureDefinition>.FromJson(
            ["""
            {
              "id":"asteria:slime",
              "model":"models/slime.glb",
              "health":10,
              "maxPerType":4,
              "collider":{"size":[1,1,1],"centerOffset":[0,0.5,0]},
              "jumpSpeed":5,
              "moveSpeed":1,
              "jumpInterval":2,
              "anticipationSeconds":0.2,
              "landingSeconds":0.3,
              "animations":{},
              "textures":{}
            }
            """],
            CreatureDefinition.Parse,
            definition => definition.Id);

    private static (VoxelWorld World, BlockRegistry Blocks) World()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        for (var x = 0; x < Chunk.Size; x++)
        for (var z = 0; z < Chunk.Size; z++)
            Assert.True(world.SetBlockAt(
                new WorldVoxelCoord(x, 0, z),
                blocks.GetId("asteria:stone"), out _));
        return (world, blocks);
    }

    private static ProjectileLaunch Launch(
        Vector3 position, Vector3 velocity,
        ProjectileOwner owner = ProjectileOwner.Player,
        CreatureInstanceId source = default,
        float lifetime = 2f) =>
        new("asteria:test", position, velocity, 0.15f, 3f, 0f,
            lifetime, owner, source);

    [Fact]
    public void SweptShotHitsVoxelInsteadOfTunnelingThroughIt()
    {
        var (world, blocks) = World();
        Assert.True(world.SetBlockAt(new WorldVoxelCoord(5, 1, 4),
            blocks.GetId("asteria:stone"), out _));
        var runtime = new ProjectileRuntime(Definitions());
        Assert.True(runtime.TryLaunch(
            Launch(new Vector3(1.5f, 1.5f, 4.5f),
                new Vector3(100, 0, 0)), out var shot));

        var hit = Assert.Single(runtime.Advance(
            0.1, world, blocks, [], null, false, 18f));
        Assert.Equal(shot.Id, hit.Id);
        Assert.Equal(ProjectileHitTarget.Block, hit.Target);
        Assert.Equal(new WorldVoxelCoord(5, 1, 4), hit.Voxel);
        Assert.InRange(hit.Position.X, 4.7f, 4.9f);
        Assert.Equal(0, runtime.Count);
        Assert.Empty(runtime.Advance(0.1, world, blocks, [], null, false, 18f));
    }

    [Fact]
    public void NearestCreatureHitUsesStableIdAndWallOccludesIt()
    {
        var (world, blocks) = World();
        var definitions = Definitions();
        var creatures = new CreatureRuntime(definitions);
        Assert.True(creatures.TrySpawn("asteria:slime",
            new Vector3(6, 1, 4.5f), out var first));
        Assert.True(creatures.TrySpawn("asteria:slime",
            new Vector3(6, 1, 4.5f), out _));

        var runtime = new ProjectileRuntime(definitions);
        var launch = Launch(new Vector3(1.5f, 1.5f, 4.5f),
            new Vector3(100, 0, 0));
        Assert.True(runtime.TryLaunch(launch, out _));
        var hit = Assert.Single(runtime.Advance(
            .1, world, blocks, creatures.ActiveCreatures, null, false, 18f));
        Assert.Equal(ProjectileHitTarget.Creature, hit.Target);
        Assert.Equal(first.Id, hit.Creature);
        Assert.Equal(3f, hit.Damage);

        Assert.True(world.SetBlockAt(new WorldVoxelCoord(4, 1, 4),
            blocks.GetId("asteria:stone"), out _));
        Assert.True(runtime.TryLaunch(launch, out _));
        hit = Assert.Single(runtime.Advance(
            .1, world, blocks, creatures.ActiveCreatures, null, false, 18f));
        Assert.Equal(ProjectileHitTarget.Block, hit.Target);
    }

    [Fact]
    public void ShooterIsIgnoredButOtherCreaturesAndPlayerCanBeHit()
    {
        var (world, blocks) = World();
        var definitions = Definitions();
        var creatures = new CreatureRuntime(definitions);
        Assert.True(creatures.TrySpawn("asteria:slime",
            new Vector3(2f, 1f, 4.5f), out var source));
        var runtime = new ProjectileRuntime(definitions);
        var player = new WorldAabb(
            new Vector3(5, 1, 4), new Vector3(6, 2, 5));
        Assert.True(runtime.TryLaunch(
            Launch(new Vector3(2f, 1.5f, 4.5f),
                new Vector3(60, 0, 0),
                ProjectileOwner.Creature, source.Id), out _));
        var hit = Assert.Single(runtime.Advance(
            .1, world, blocks, creatures.ActiveCreatures, player, true, 18f));
        Assert.Equal(ProjectileHitTarget.Player, hit.Target);

        Assert.True(runtime.TryLaunch(
            Launch(new Vector3(2f, 1.5f, 4.5f),
                new Vector3(60, 0, 0)), out _));
        // Player-owned shots never collide with their own player's hitbox.
        Assert.Empty(runtime.Advance(
            .1, world, blocks, [], player, true, 18f));
        Assert.Equal(1, runtime.Count);
    }

    [Fact]
    public void UnloadedChunksTerminateFlightBeforeCrossingBoundary()
    {
        var (world, blocks) = World();
        var runtime = new ProjectileRuntime(Definitions());
        Assert.True(runtime.TryLaunch(
            Launch(new Vector3(14.5f, 1.5f, 4.5f),
                new Vector3(100, 0, 0)), out _));
        var hit = Assert.Single(runtime.Advance(
            0.1, world, blocks, [], null, false, 18f));
        Assert.Equal(ProjectileHitTarget.Unloaded, hit.Target);
        Assert.Equal(16, hit.Voxel!.Value.X);
    }

    [Fact]
    public void GravityLifetimeAndInvalidSpawnsAreBounded()
    {
        var (world, blocks) = World();
        var runtime = new ProjectileRuntime(Definitions());
        var baseLaunch = Launch(new Vector3(2, 5, 4), Vector3.UnitX,
            lifetime: 0.3f);
        Assert.False(runtime.TryLaunch(
            baseLaunch with { Radius = float.NaN }, out _));
        Assert.False(runtime.TryLaunch(
            baseLaunch with { Velocity = new Vector3(float.PositiveInfinity) },
            out _));
        Assert.False(runtime.TryLaunch(
            baseLaunch with { Owner = ProjectileOwner.Creature }, out _));
        Assert.True(runtime.TryLaunch(
            baseLaunch with { GravityScale = 1f }, out var created));
        Assert.Empty(runtime.Advance(
            .1, world, blocks, [], null, false, 10f));
        var moved = Assert.Single(runtime.ActiveProjectiles);
        Assert.Equal(created.Id, moved.Id);
        Assert.InRange(moved.Position.Y, 4.89f, 4.91f);
        Assert.Empty(runtime.Advance(
            .1, world, blocks, [], null, false, 10f));
        Assert.Empty(runtime.Advance(
            .1, world, blocks, [], null, false, 10f));
        Assert.Equal(0, runtime.Count);
    }
}

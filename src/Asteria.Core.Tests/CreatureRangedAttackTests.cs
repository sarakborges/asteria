using System.Numerics;
using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class CreatureRangedAttackTests
{
    private static PackContentRegistry<CreatureDefinition> Definitions() =>
        PackContentRegistry<CreatureDefinition>.FromJson(
            ["""
            {
              "id": "asteria:slime",
              "model": "models/slime.glb",
              "health": 10,
              "maxPerType": 4,
              "contactDamage": 2,
              "collider": {"size": [0.78, 0.84, 0.78], "centerOffset": [0, 0.42, 0]},
              "jumpSpeed": 5,
              "moveSpeed": 1.4,
              "jumpInterval": 2,
              "anticipationSeconds": 0.24,
              "landingSeconds": 0.34,
              "animations": {"idle": "Idle"},
              "textures": {},
              "rangedAttack": {
                "projectile": "asteria:slime_orb",
                "minDistance": 3,
                "speed": 12,
                "damage": 2,
                "radius": 0.18,
                "gravityScale": 0,
                "lifetimeSeconds": 2.5,
                "cooldownSeconds": 2.6
              }
            }
            """],
            CreatureDefinition.Parse,
            definition => definition.Id);

    private static (VoxelWorld World, BlockRegistry Blocks) Ground()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        for (var x = 0; x < Chunk.Size; x++)
        for (var z = 0; z < Chunk.Size; z++)
            Assert.True(world.SetBlockAt(new WorldVoxelCoord(x, 0, z),
                blocks.GetId("asteria:stone"), out _));
        return (world, blocks);
    }

    private static WorldAabb PlayerAt(float x, float z = 4f) =>
        new(new Vector3(x - 0.3f, 1f, z - 0.3f),
            new Vector3(x + 0.3f, 2.8f, z + 0.3f));

    [Fact]
    public void FiresOnlyBeyondThreeBlocksAndWithinAggroRange()
    {
        var definitions = Definitions();
        var (world, blocks) = Ground();
        var creatures = new CreatureRuntime(definitions);
        var shots = new ProjectileRuntime(definitions);
        var feet = new Vector3(4, 1, 4);
        Assert.True(creatures.TrySpawn("asteria:slime", feet, out _));
        creatures.Advance(2.6, feet);

        Assert.Equal(0, creatures.FireRangedAttacks(
            shots, world, blocks, PlayerAt(7), true));
        Assert.Equal(0, creatures.FireRangedAttacks(
            shots, world, blocks, PlayerAt(4), true));
        Assert.Equal(0, creatures.FireRangedAttacks(
            shots, world, blocks, PlayerAt(16.01f), true));
        Assert.Equal(1, creatures.FireRangedAttacks(
            shots, world, blocks, PlayerAt(7.01f), true));

        var shot = Assert.Single(shots.ActiveProjectiles);
        Assert.Equal(ProjectileOwner.Creature, shot.Launch.Owner);
        Assert.Equal("asteria:slime_orb", shot.Launch.Kind);
        Assert.True(shot.Launch.Velocity.X > 0f);
        Assert.Equal(2f, shot.Launch.Damage);
        Assert.Equal(1u, Assert.Single(creatures.ActiveCreatures).AttacksDealt);
        Assert.True(Assert.Single(creatures.ActiveCreatures).AttackSecondsRemaining > 0f);
    }

    [Fact]
    public void ProjectileCollidesWithPlayerAndCooldownPreventsSpam()
    {
        var definitions = Definitions();
        var (world, blocks) = Ground();
        var creatures = new CreatureRuntime(definitions);
        var shots = new ProjectileRuntime(definitions);
        var feet = new Vector3(4, 1, 4);
        var player = PlayerAt(9);
        Assert.True(creatures.TrySpawn("asteria:slime", feet, out _));
        creatures.Advance(2.6, feet);
        Assert.Equal(1, creatures.FireRangedAttacks(
            shots, world, blocks, player, true));
        Assert.Equal(0, creatures.FireRangedAttacks(
            shots, world, blocks, player, true));

        ProjectileImpact? impact = null;
        for (var i = 0; i < 8 && impact is null; i++)
        {
            var hits = shots.Advance(0.1, world, blocks,
                creatures.ActiveCreatures, player, true, 18f);
            if (hits.Count > 0) impact = Assert.Single(hits);
        }
        Assert.NotNull(impact);
        Assert.Equal(ProjectileHitTarget.Player, impact.Value.Target);
        Assert.Equal(0, shots.Count);

        var state = Assert.Single(creatures.ActiveCreatures);
        var restored = new CreatureRuntime(definitions, creatures.CaptureState());
        Assert.Equal(state.NextRangedAttackAtSeconds,
            Assert.Single(restored.ActiveCreatures).NextRangedAttackAtSeconds);
        Assert.Equal(0, restored.FireRangedAttacks(
            shots, world, blocks, player, true));
        restored.Advance(2.61, feet);
        Assert.Equal(1, restored.FireRangedAttacks(
            shots, world, blocks, player, true));
    }

    [Fact]
    public void NoAiPeacefulDeathAndWallsBlockShots()
    {
        var definitions = Definitions();
        var (world, blocks) = Ground();
        var creatures = new CreatureRuntime(definitions);
        var shots = new ProjectileRuntime(definitions);
        var feet = new Vector3(4, 1, 4);
        var player = PlayerAt(10);
        Assert.True(creatures.TrySpawn("asteria:slime", feet, out var creature));
        creatures.Advance(3, feet);

        Assert.Equal(0, creatures.FireRangedAttacks(
            shots, world, blocks, player, false));
        Assert.True(creatures.TrySetNoAi(creature.Id, true));
        Assert.Equal(0, creatures.FireRangedAttacks(
            shots, world, blocks, player, true));
        Assert.True(creatures.TrySetNoAi(creature.Id, false));

        Assert.True(world.SetBlockAt(new WorldVoxelCoord(7, 1, 4),
            blocks.GetId("asteria:stone"), out _));
        Assert.Equal(0, creatures.FireRangedAttacks(
            shots, world, blocks, player, true));
        Assert.True(world.SetBlockAt(new WorldVoxelCoord(7, 1, 4),
            BlockRuntimeId.Air, out _));
        Assert.Equal(1, creatures.FireRangedAttacks(
            shots, world, blocks, player, true));
        Assert.True(creatures.TryKill(creature.Id, out _));
        creatures.Advance(10, feet);
        Assert.Equal(0, creatures.FireRangedAttacks(
            shots, world, blocks, player, true));
    }

    [Fact]
    public void AllAuthoredSlimesDefineValidatedRangedAttacks()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "packs", "default", "data", "creatures");
        var filenames = Directory.GetFiles(path, "slime*.json");
        Assert.Equal(20, filenames.Length);
        foreach (var file in filenames)
        {
            var definition = CreatureDefinition.Parse(File.ReadAllText(file));
            var ranged = Assert.IsType<CreatureRangedAttackDefinition>(definition.RangedAttack);
            Assert.Equal(3f, ranged.MinimumDistance);
            Assert.Equal(definition.ContactDamage, ranged.Damage);
            Assert.True(definition.AggroRange > ranged.MinimumDistance);
        }
    }

    [Fact]
    public void InvalidRangedDefinitionsAreRejected()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "packs", "default", "data",
            "creatures", "slime.json"));
        Assert.Throws<FormatException>(() =>
            CreatureDefinition.Parse(source.Replace(
                "\"minDistance\": 3", "\"minDistance\": -1")));
        Assert.Throws<FormatException>(() =>
            CreatureDefinition.Parse(source.Replace(
                "\"radius\": 0.18", "\"radius\": 2")));
        Assert.Throws<FormatException>(() =>
            CreatureDefinition.Parse(source.Replace(
                "\"cooldownSeconds\": 2.6", "\"cooldownSeconds\": 0")));
    }
}

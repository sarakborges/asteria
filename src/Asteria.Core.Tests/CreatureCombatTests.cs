using System.Numerics;
using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class CreatureCombatTests
{
    private static PackContentRegistry<CreatureDefinition> Definitions() =>
        PackContentRegistry<CreatureDefinition>.FromJson(
            ["""
            {
              "id":"asteria:slime_aqua",
              "model":"models/creatures/slime_hydro/slime_hydro.glb",
              "health":11,
              "maxPerType":4,
              "collider":{"size":[0.78,0.84,0.78],"centerOffset":[0,0.42,0]},
              "targetCollider":{"size":[0.9,0.9,0.9],"centerOffset":[0,0.45,0]},
              "jumpSpeed":5,
              "moveSpeed":1.4,
              "jumpInterval":2,
              "anticipationSeconds":0.24,
              "landingSeconds":0.34,
              "animations":{"idle":"Idle"},
              "textures":{"SlimeFace":"textures/creatures/slime_hydro/face.png"}
            }
            """],
            CreatureDefinition.Parse,
            definition => definition.Id);


    private static PackContentRegistry<CreatureDefinition> HostileDefinitions() =>
        PackContentRegistry<CreatureDefinition>.FromJson(
            ["""
            {
              "id":"asteria:slime_aqua",
              "model":"models/slime.glb",
              "health":11,
              "maxPerType":4,
              "collider":{"size":[0.78,0.84,0.78],"centerOffset":[0,0.42,0]},
              "jumpSpeed":5,
              "moveSpeed":1.4,
              "jumpInterval":2,
              "anticipationSeconds":0.24,
              "landingSeconds":0.34,
              "contactDamage":2,
              "animations":{"idle":"Idle","land":"Land"},
              "textures":{}
            }
            """], CreatureDefinition.Parse, definition => definition.Id);

    [Fact]
    public void HostileSlimeChoosesPlayerDirectionRatherThanWandering()
    {
        var (world, blocks) = World();
        var definitions = HostileDefinitions();
        Assert.Equal(12f, definitions.Get("asteria:slime_aqua").AggroRange);
        var runtime = new CreatureRuntime(definitions);
        var start = new Vector3(3.5f, 1f, 7.5f);
        var player = new Vector3(10.5f, 1f, 7.5f);
        Assert.True(runtime.TrySpawn("asteria:slime_aqua", start, out _));

        for (var i = 0; i < 60 &&
            Assert.Single(runtime.ActiveCreatures).Motion.HopCount == 0; i++)
            runtime.AdvanceWorld(0.025, player, world, blocks, 18f);

        var hopping = Assert.Single(runtime.ActiveCreatures);
        Assert.Equal(1u, hopping.Motion.HopCount);
        Assert.True(hopping.Motion.Direction.X > 0.99f);
        Assert.True(MathF.Abs(hopping.Motion.Direction.Y) < 0.01f);
        for (var i = 0; i < 12; i++)
            runtime.AdvanceWorld(0.025, player, world, blocks, 18f);
        Assert.True(Assert.Single(runtime.ActiveCreatures).Position.X > start.X);

        // Targeting can be disabled by the session (Creative/Spectator).
        var disabled = new CreatureRuntime(definitions);
        var wandering = new CreatureRuntime(definitions);
        Assert.True(disabled.TrySpawn("asteria:slime_aqua", start, out _));
        Assert.True(wandering.TrySpawn("asteria:slime_aqua", start, out _));
        for (var i = 0; i < 60; i++)
        {
            disabled.AdvanceWorld(0.025, player, world, blocks, 18f, false);
            wandering.AdvanceWorld(0.025, new Vector3(100, 1, 100), world, blocks, 18f);
        }
        Assert.Equal(Assert.Single(wandering.ActiveCreatures).Motion.Direction,
            Assert.Single(disabled.ActiveCreatures).Motion.Direction);
    }

    [Fact]
    public void ContactAttackReplaysAndPersistsUntilItsPresentationExpires()
    {
        var definitions = HostileDefinitions();
        var runtime = new CreatureRuntime(definitions);
        Assert.True(runtime.TrySpawn("asteria:slime_aqua",
            new Vector3(6.5f, 1, 7.5f), out var creature));
        var playerBounds = new WorldAabb(
            new Vector3(6.2f, 1, 7.2f), new Vector3(6.8f, 2, 7.8f));
        Assert.Null(runtime.FindContactHit(new WorldAabb(
            new Vector3(1, 1, 1), new Vector3(2, 2, 2))));
        var hit = runtime.FindContactHit(playerBounds);
        Assert.NotNull(hit);
        Assert.Equal(creature.Id, hit.Value.Id);
        Assert.Equal(2f, hit.Value.Damage);
        Assert.True(runtime.TryRegisterContactAttack(hit.Value.Id));
        var attacking = Assert.Single(runtime.ActiveCreatures);
        Assert.Equal(1u, attacking.AttacksDealt);
        Assert.Equal(CreatureRuntime.AttackAnimationSeconds,
            attacking.AttackSecondsRemaining);

        runtime = new CreatureRuntime(definitions, runtime.CaptureState());
        runtime.Advance(0.18, attacking.Position);
        Assert.True(Assert.Single(runtime.ActiveCreatures).AttackSecondsRemaining > 0f);
        Assert.True(runtime.TryRegisterContactAttack(creature.Id));
        Assert.Equal(2u, Assert.Single(runtime.ActiveCreatures).AttacksDealt);
        runtime.Advance(0.35, attacking.Position);
        Assert.Equal(0f, Assert.Single(runtime.ActiveCreatures).AttackSecondsRemaining);

        Assert.True(runtime.TrySetNoAi(creature.Id, true));
        Assert.False(runtime.TryRegisterContactAttack(creature.Id));
        Assert.True(runtime.TrySetNoAi(creature.Id, false));
        Assert.True(runtime.TryKill(creature.Id, out _));
        Assert.False(runtime.TryRegisterContactAttack(creature.Id));
    }

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

    private static AttackDefinition Punch() =>
        AttackDefinition.Parse("""
            {"id":"asteria:punch","damage":1,"effects":[
              {"effect":"knockback","chance":1,"strength":0.75}
            ]}
            """);

    [Fact]
    public void RaySelectionUsesNearestColliderAndStableIdOnTies()
    {
        var (world, blocks) = World();
        var runtime = new CreatureRuntime(Definitions());
        Assert.True(runtime.TrySpawn("asteria:slime_aqua",
            new Vector3(6.5f, 1, 7.5f), out var first));
        Assert.True(runtime.TrySpawn("asteria:slime_aqua",
            new Vector3(6.5f, 1, 7.5f), out _));

        var target = runtime.FindTarget(world, blocks,
            new Vector3(2, 1.4f, 7.5f), Vector3.UnitX, 6f);
        Assert.Equal(first.Id, target!.Value.Creature.Id);
        Assert.Null(runtime.FindTarget(world, blocks,
            new Vector3(2, 1.4f, 7.5f), Vector3.UnitX, 2f));
    }

    [Fact]
    public void BlocksOccludeAttacksAndEmptyTargetIsHarmless()
    {
        var (world, blocks) = World();
        var runtime = new CreatureRuntime(Definitions());
        Assert.True(runtime.TrySpawn("asteria:slime_aqua",
            new Vector3(6.5f, 1, 7.5f), out _));
        var origin = new Vector3(2, 1.4f, 7.5f);
        Assert.NotNull(runtime.FindTarget(
            world, blocks, origin, Vector3.UnitX, 6f));
        Assert.True(world.SetBlockAt(
            new WorldVoxelCoord(4, 1, 7),
            blocks.GetId("asteria:stone"), out _));
        Assert.Null(runtime.FindTarget(
            world, blocks, origin, Vector3.UnitX, 6f));
        Assert.Null(runtime.FindTarget(
            world, blocks, origin, -Vector3.UnitX, 6f));
    }

    [Fact]
    public void PunchDamageKnockbackAndDeathRemainInCore()
    {
        var (world, blocks) = World();
        var runtime = new CreatureRuntime(Definitions());
        Assert.True(runtime.TrySpawn("asteria:slime_aqua",
            new Vector3(6.5f, 1, 7.5f), out var creature));
        var attacker = new Vector3(2, 1, 7.5f);
        Assert.True(runtime.TryAttack(
            creature.Id, Punch(), attacker, out var first));
        Assert.False(first.Killed);
        Assert.Equal(10f, first.Health);
        var state = Assert.Single(runtime.ActiveCreatures);
        Assert.Equal(1u, state.AttacksReceived);
        Assert.True(state.Motion.KnockbackSeconds > 0);
        Assert.True(state.Motion.KnockbackVelocity.X > 0);

        runtime.AdvanceWorld(0.05, attacker, world, blocks, 18f);
        var moved = Assert.Single(runtime.ActiveCreatures);
        Assert.True(moved.Position.X > creature.Position.X);

        var saved = runtime.CaptureState();
        runtime = new CreatureRuntime(Definitions(), saved);
        Assert.Equal(1u, Assert.Single(runtime.ActiveCreatures).AttacksReceived);

        for (var hit = 0; hit < 10; hit++)
            Assert.True(runtime.TryAttack(
                creature.Id, Punch(), attacker, out var result));

        var dying = Assert.Single(runtime.ActiveCreatures);
        Assert.True(dying.IsDying);
        Assert.Equal(0f, dying.Health);
        Assert.False(runtime.TryAttack(
            creature.Id, Punch(), attacker, out _));

        // Death animation is observable for 0.75 seconds and survives a
        // dimension snapshot without a duplicate death.
        runtime = new CreatureRuntime(Definitions(), runtime.CaptureState());
        Assert.True(Assert.Single(runtime.ActiveCreatures).IsDying);
        runtime.AdvanceWorld(0.5, attacker, world, blocks, 18f);
        Assert.True(Assert.Single(runtime.ActiveCreatures).IsDying);
        runtime.AdvanceWorld(0.25, attacker, world, blocks, 18f);
        Assert.Equal(0, runtime.Count);
    }

    [Fact]
    public void RepeatedHurtAnimationRetriggersAndExpires()
    {
        var runtime = new CreatureRuntime(Definitions());
        Assert.True(runtime.TrySpawn("asteria:slime_aqua",
            new Vector3(4, 3, 4), out var creature));
        Assert.True(runtime.TryAttack(creature.Id, Punch(),
            new Vector3(1, 3, 4), out _));
        Assert.Equal(1u, Assert.Single(runtime.ActiveCreatures).AttacksReceived);
        Assert.True(Assert.Single(runtime.ActiveCreatures).HurtSecondsRemaining > 0);
        runtime.Advance(0.2, new Vector3(4, 3, 4));
        Assert.True(runtime.TryAttack(creature.Id, Punch(),
            new Vector3(1, 3, 4), out _));
        Assert.Equal(2u, Assert.Single(runtime.ActiveCreatures).AttacksReceived);
        Assert.Equal(CreatureRuntime.HurtAnimationSeconds,
            Assert.Single(runtime.ActiveCreatures).HurtSecondsRemaining);
        runtime.Advance(0.4, new Vector3(4, 3, 4));
        Assert.Equal(0f, Assert.Single(runtime.ActiveCreatures).HurtSecondsRemaining);
    }

    [Fact]
    public void DeadCreatureDoesNotInterceptFurtherRayAttacks()
    {
        var (world, blocks) = World();
        var runtime = new CreatureRuntime(Definitions());
        Assert.True(runtime.TrySpawn("asteria:slime_aqua",
            new Vector3(6.5f, 1, 7.5f), out var creature));
        for (var hit = 0; hit < 11; hit++)
            Assert.True(runtime.TryAttack(creature.Id, Punch(),
                new Vector3(2, 1, 7.5f), out _));
        Assert.Null(runtime.FindTarget(world, blocks,
            new Vector3(2, 1.4f, 7.5f), Vector3.UnitX, 6));
    }

    [Fact]
    public void InvalidAttacksRejectUnsafeValues()
    {
        Assert.Throws<FormatException>(() => AttackDefinition.Parse(
            """{"id":"asteria:bad","damage":-1}"""));
        Assert.Throws<FormatException>(() => AttackDefinition.Parse(
            """{"id":"asteria:bad","damage":1,"effects":[{"effect":"knockback","chance":1.3}]}"""));
        Assert.Throws<FormatException>(() => AttackDefinition.Parse(
            """{"id":"asteria:bad","damage":1,"effects":[{"effect":"unknown"}]}"""));
    }

    [Fact]
    public void AuthoredPunchLoadsFromSelectedPackData()
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory, "packs", "default", "data", "attacks");
        var attack = AttackDefinition.Parse(
            File.ReadAllText(Path.Combine(directory, "punch.json")));
        Assert.Equal("asteria:punch", attack.Id);
        Assert.Equal(1f, attack.Damage);
        var effect = Assert.Single(attack.Effects);
        Assert.Equal("knockback", effect.Effect);
        Assert.Equal(0.75f, effect.Strength);
    }
}

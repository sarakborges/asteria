using System.Numerics;
using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class MiningFragmentAppearanceTests
{
    private static readonly WorldVoxelCoord Position = new(2, 3, 4);
    private static readonly VoxelWorldHit Top = new(Position, 0, 1, 0);
    private static readonly VoxelWorldHit Front = new(Position, 0, 0, 1);

    private sealed record Fixture(
        VoxelWorld World, BlockRegistry Blocks,
        AttachedLayerRegistry Layers, VoxelMutationRuntime Mutations,
        BlockMiningRuntime Mining);

    private static Fixture Setup()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:block_grass",
                tint: BlockTint.Grass, secondaryProperties: ["dyed"],
                textures: new BlockTextureSet(
                    top: [new BlockTextureLayer("textures/grass_top.png", true)],
                    front: [
                        new BlockTextureLayer("textures/dirt.png"),
                        new BlockTextureLayer("textures/grass_overlay.png", true)
                    ]),
                mining: new BlockMiningDefinition(hardness: 0f)),
            new BlockDefinition("asteria:leaves",
                tint: BlockTint.Leaf,
                textures: new BlockTextureSet(
                    top: [new BlockTextureLayer("textures/leaves.png", true)]),
                mining: new BlockMiningDefinition(hardness: 0f))
        ]);
        var layers = AttachedLayerRegistry.FromJson([
            """
            {"id":"asteria:moss","texture":"textures/layers/moss.png",
             "scope":"face","tint":"foliage","mining":{"hardness":0}}
            """,
            """
            {"id":"asteria:ore_iron","texture":"textures/layers/iron.png",
             "scope":"voxel","tint":"none","mining":{"hardness":0}}
            """
        ]);
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var mutations = new VoxelMutationRuntime(
            world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var drops = new DroppedBlockRuntime(world, blocks);
        var interactions = new BlockInteractionRuntime(
            world, blocks, mutations, drops, layers);
        var toolDefinitions = PackContentRegistry<ToolDefinition>.FromJson(
            [], ToolDefinition.Parse, item => item.Id);
        var tools = new ToolGameplayRuntime(
            world, blocks, mutations, toolDefinitions);
        return new Fixture(world, blocks, layers, mutations,
            new BlockMiningRuntime(world, blocks, tools, interactions, layers));
    }

    [Fact]
    public void UsesActualHitFaceAndPreservesBaseOverlayTintFlags()
    {
        var f = Setup();
        Assert.True(f.Mutations.SetBlockAt(
            Position, f.Blocks.GetId("asteria:block_grass"), out _));
        Assert.True(f.Mutations.SetBlockSurfaceStateAt(Position,
            new BlockSurfaceState("asteria:dye_red"), out _));

        var front = MiningFragmentAppearance.Resolve(
            f.World, f.Blocks, f.Layers, Front)!;
        Assert.Equal(["textures/dirt.png", "textures/grass_overlay.png"],
            front.Textures.Select(x => x.Texture));
        Assert.False(front.Textures[0].Dyable);
        Assert.True(front.Textures[1].Dyable);
        Assert.Equal(BlockTint.Grass, front.Tint);
        Assert.Equal("asteria:dye_red", front.DyeId);

        var top = MiningFragmentAppearance.Resolve(
            f.World, f.Blocks, f.Layers, Top)!;
        Assert.Equal("textures/grass_top.png",
            Assert.Single(top.Textures).Texture);
    }

    [Fact]
    public void TopInteractiveLayerOverridesHostFaceAndHasItsOwnTint()
    {
        var f = Setup();
        Assert.True(f.Mutations.SetBlockAt(Position,
            f.Blocks.GetId("asteria:block_grass"), out _));
        Assert.True(f.Mutations.SetBlockSurfaceStateAt(Position,
            new BlockSurfaceState("asteria:dye_red", [
                new AttachedBlockLayer(BlockFace.Top, "asteria:ore_iron"),
                new AttachedBlockLayer(BlockFace.Top, "asteria:moss")
            ]), out _));
        var moss = MiningFragmentAppearance.Resolve(
            f.World, f.Blocks, f.Layers, Top)!;
        Assert.Equal("textures/layers/moss.png",
            Assert.Single(moss.Textures).Texture);
        Assert.Equal(BlockTint.Foliage, moss.Tint);
        Assert.Null(moss.DyeId);

        Assert.True(f.Mutations.SetBlockSurfaceStateAt(Position,
            new BlockSurfaceState(null, [
                new AttachedBlockLayer(BlockFace.Top, "asteria:ore_iron")
            ]), out _));
        var ore = MiningFragmentAppearance.Resolve(
            f.World, f.Blocks, f.Layers, Top)!;
        Assert.Equal("textures/layers/iron.png",
            Assert.Single(ore.Textures).Texture);
        Assert.Equal(BlockTint.None, ore.Tint);
        Assert.False(ore.Textures[0].Dyable);
        var otherFace = MiningFragmentAppearance.Resolve(
            f.World, f.Blocks, f.Layers, Front)!;
        Assert.Equal("textures/dirt.png", otherFace.Textures[0].Texture);
    }

    [Fact]
    public void FinishedMiningRetainsOriginalLayerMaterialAfterMutation()
    {
        var f = Setup();
        Assert.True(f.Mutations.SetBlockAt(Position,
            f.Blocks.GetId("asteria:block_grass"), out _));
        Assert.True(f.Mutations.SetBlockSurfaceStateAt(Position,
            new BlockSurfaceState(null, [
                new AttachedBlockLayer(BlockFace.Top, "asteria:moss")
            ]), out _));

        Assert.True(f.Mining.Advance(
            Top, null, 0, 0, PlayerGameMode.Survival));
        Assert.Equal(0, f.World.GetBlockSurfaceStateOrEmpty(Position).Layers.Count);
        Assert.Equal(f.Blocks.GetId("asteria:block_grass"),
            f.World.GetCellOrEmpty(Position).Block);
        var completed = f.Mining.LastFeedback!.Value;
        Assert.True(completed.Completed);
        Assert.Equal("textures/layers/moss.png",
            Assert.Single(completed.Appearance!.Textures).Texture);
        var particles = new MiningParticleRuntime();
        var tint = new Vector3(0.12f, 0.75f, 0.21f);
        particles.Emit(completed, tint);
        Assert.Equal(11, particles.Particles.Count);
        Assert.All(particles.Particles, p => {
            Assert.Equal("textures/layers/moss.png",
                Assert.Single(p.Appearance!.Textures).Texture);
            Assert.Equal(tint, p.Tint);
        });
    }

    [Fact]
    public void FinishedBlockMiningPreservesMaterialEvenAfterVoxelBecomesAir()
    {
        var f = Setup();
        Assert.True(f.Mutations.SetBlockAt(Position,
            f.Blocks.GetId("asteria:leaves"), out _));
        Assert.True(f.Mining.Advance(Top, null, 0, 0,
            PlayerGameMode.Survival));
        Assert.True(f.World.GetCellOrEmpty(Position).IsEmpty);
        Assert.Equal("textures/leaves.png",
            Assert.Single(f.Mining.LastFeedback!.Value.Appearance!.Textures).Texture);
        Assert.Equal(BlockTint.Leaf,
            f.Mining.LastFeedback.Value.Appearance!.Tint);
        Assert.Null(MiningFragmentAppearance.Resolve(
            f.World, f.Blocks, f.Layers, Top));
    }
}

using System.Numerics;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkMeshDataBuilderTests
{
    [Fact]
    public void SpikeClosesItsEndsAndProvidesMatchingCollisionTriangles()
    {
        var texture = new BlockTextureLayer("textures/test/spike.png");
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone_spike",
                textures: new BlockTextureSet(top: [texture]),
                shape: BlockShapeDefinition.Spike(
                    baseRadius: 0.42f, sides: 7,
                    irregularity: 0.12f, taperPower: 1.3f)),
        ]);
        var chunk = new Chunk();
        var spike = blocks.GetId("asteria:stone_spike");
        chunk.SetCell(2, 2, 2, new VoxelCell(spike));
        ChunkLightingSolver.Initialize(chunk, blocks, new FluidRegistry([]));
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, chunk);
        var mesh = ChunkMeshDataBuilder.BuildMeshlet(
            world, ChunkCoord.Zero, blocks,
            new TerrainTextureLookup(new Dictionary<string, int>
            {
                ["textures/test/spike.png"] = 0,
            }), 0);

        // Each of seven sides contributes 2 side triangles, one top
        // triangle, and one bottom triangle. The ends have no gaps.
        Assert.Equal(28, mesh.TriangleCount);
        Assert.Equal(28, mesh.CollisionTriangleCount);
        Assert.Contains(mesh.RenderBatches.SelectMany(batch => batch.Vertices),
            vertex => vertex.Normal == Vector3.UnitY);
        Assert.Contains(mesh.RenderBatches.SelectMany(batch => batch.Vertices),
            vertex => vertex.Normal == -Vector3.UnitY);

        var vertices = mesh.RenderBatches.SelectMany(batch => batch.Vertices).ToArray();
        for (var index = 0; index < vertices.Length; index += 3)
        {
            var normal = vertices[index].Normal;
            if (normal != Vector3.UnitY && normal != -Vector3.UnitY)
                continue;
            var a = vertices[index].Position;
            var b = vertices[index + 1].Position;
            var c = vertices[index + 2].Position;
            Assert.True(Vector3.Dot(Vector3.Cross(b - a, c - a), normal) > 0f,
                $"Spike end cap winding is reversed at triangle {index / 3}.");
        }
    }

    [Fact]
    public void CubeBesidePartialNeighborPreservesFineFaceAndCollision()
    {
        const string texture = "textures/test/stone.png";
        var layerTexture = new BlockTextureLayer(texture);
        var blocks = new BlockRegistry(
        [
            new BlockDefinition(
                "asteria:stone",
                textures: new BlockTextureSet(top: [layerTexture])),
            new BlockDefinition(
                "asteria:layer",
                textures: new BlockTextureSet(top: [layerTexture]),
                shape: BlockShapeDefinition.SurfaceLayer(
                    0.5f, "asteria:stone")),
        ]);
        var chunk = new Chunk();
        chunk.SetBlock(2, 2, 2, blocks.GetId("asteria:stone"));
        chunk.SetBlock(3, 2, 2, blocks.GetId("asteria:layer"));
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, chunk);
        var mesh = ChunkMeshDataBuilder.BuildMeshlet(
            world, ChunkCoord.Zero, blocks,
            new TerrainTextureLookup(new Dictionary<string, int>
            {
                [texture] = 0,
            }), meshletIndex: 0);

        Assert.True(mesh.HasRenderGeometry);
        Assert.True(mesh.HasCollision);
        var vertices = mesh.RenderBatches.SelectMany(batch => batch.Vertices).ToArray();
        Assert.Contains(vertices,
            vertex =>
                vertex.Normal == Vector3.UnitX &&
                Math.Abs(vertex.Position.X - 3f) < 0.0001f &&
                vertex.Position.Y > 2.5f);
        // The neighbor requires a fine shared face, but it must not duplicate
        // the full cube's ordinary outer faces or its collision geometry.
        Assert.Equal(6, vertices.Count(vertex =>
            vertex.Normal == -Vector3.UnitX &&
            Math.Abs(vertex.Position.X - 2f) < 0.0001f));
        Assert.Contains(vertices, vertex =>
            vertex.Normal == Vector3.UnitY &&
            Math.Abs(vertex.Position.Y - 3f) < 0.0001f);
    }

    [Fact]
    public void SingleOpaqueCubeBuildsTerrainAndCollisionInCore()
    {
        var texture =
            new BlockTextureLayer("res://textures/test.png");
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone",
                    textures:
                        new BlockTextureSet(
                            top: [texture])),
            ]);
        var stone =
            blocks.GetId("asteria:stone");
        var chunk = new Chunk();
        chunk.SetBlock(1, 1, 1, stone);

        var fluids = new FluidRegistry([]);
        ChunkLightingSolver.Initialize(
            chunk,
            blocks,
            fluids);

        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            chunk);

        var data =
            ChunkMeshDataBuilder.BuildMeshlet(
                world,
                ChunkCoord.Zero,
                blocks,
                new TerrainTextureLookup(
                    new Dictionary<string, int>
                    {
                        ["res://textures/test.png"] = 0,
                    }),
                meshletIndex: 0);

        Assert.True(data.TriangleCount > 0);
        Assert.Equal(12, data.TriangleCount);
        Assert.Equal(12, data.CollisionTriangleCount);
    }

    [Fact]
    public void TintableTerrainUsesBiomeTintAtMeshVertices()
    {
        var texture =
            new BlockTextureLayer(
                "textures/test/grass.png",
                dyable: true);
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:grass_block",
                    tint: BlockTint.Grass,
                    textures:
                        new BlockTextureSet(
                            top:
                            [
                                texture,
                            ]),
                    previewColor:
                        new BlockPreviewColor(
                            255,
                            0,
                            0)),
            ]);
        var biome =
            new BiomeDefinition(
                "asteria:test/green",
                new BiomeSurfaceLayoutDefinition(),
                new BiomeTerrainDefinition(
                    0,
                    0,
                    64,
                    0,
                    32),
                [
                    new BiomeSurfaceLayerDefinition(
                        "asteria:grass_block"),
                ],
                tints:
                    new BiomeTintPaletteDefinition(
                        grass:
                            new BiomeTintColor(
                                0,
                                255,
                                0)));
        var biomes =
            new BiomeRegistry(
            [
                biome,
            ]);
        var dimension =
            new DimensionDefinition(
                new DimensionId(
                    "asteria:test"),
                [
                    biome.Id,
                ],
                0,
                18f,
                new DimensionSpawnDefinition(
                    0,
                    0),
                new DimensionEnvironmentDefinition(
                    new DimensionColor(
                        0,
                        0,
                        0),
                    new DimensionColor(
                        255,
                        255,
                        255),
                    1f,
                    new DimensionColor(
                        0,
                        0,
                        0),
                    0f));
        var biomeField =
            new BiomeField(
                7,
                dimension,
                biomes);
        var tintSamples =
            new BiomeTintField(
                    biomeField,
                    [
                        biome,
                    ])
                .SampleGrid(
                    0,
                    0,
                    Chunk.Size + 1,
                    Chunk.Size + 1);
        var chunk =
            new Chunk();

        chunk.SetBlock(
            1,
            1,
            1,
            blocks.GetId(
                "asteria:grass_block"));
        ChunkLightingSolver.Initialize(
            chunk,
            blocks,
            new FluidRegistry([]));

        var world =
            new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            chunk);

        var data =
            ChunkMeshDataBuilder.BuildMeshlet(
                world,
                ChunkCoord.Zero,
                blocks,
                new TerrainTextureLookup(
                    new Dictionary<string, int>
                    {
                        ["textures/test/grass.png"] = 0,
                    }),
                meshletIndex: 0,
                tintSamples);

        Assert.Contains(
            data.RenderBatches
                .SelectMany(batch =>
                    batch.Vertices),
            vertex =>
                vertex.TintAndAo.Y >
                    0.99f &&
                vertex.TintAndAo.X <
                    0.01f &&
                vertex.TintAndAo.Z <
                    0.01f);
    }

    [Fact]
    public void CrossedSpriteBuildsDoubleSidedCutoutWithoutCollision()
    {
        var texture =
            new BlockTextureLayer(
                "textures/objects/grass.png",
                dyable: true);
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:grass",
                    tint: BlockTint.Foliage,
                    visual:
                        BlockVisualDefinition.CrossedSprite(
                            texture,
                            width: 0.72f,
                            height: 0.62f,
                            planes: 2),
                    isCollidable: false,
                    renderMode: BlockRenderMode.Cutout,
                    castsShadow: false,
                    lightDampening: 0,
                    previewColor:
                        new BlockPreviewColor(
                            111,
                            159,
                            80)),
            ]);
        var grass =
            blocks.GetId(
                "asteria:grass");
        var chunk =
            new Chunk();
        chunk.SetBlock(
            1,
            1,
            1,
            grass);

        ChunkLightingSolver.Initialize(
            chunk,
            blocks,
            new FluidRegistry([]));

        var world =
            new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            chunk);

        var data =
            ChunkMeshDataBuilder.BuildMeshlet(
                world,
                ChunkCoord.Zero,
                blocks,
                new TerrainTextureLookup(
                    new Dictionary<string, int>
                    {
                        ["textures/objects/grass.png"] = 3,
                    }),
                meshletIndex: 0);

        var batch =
            Assert.Single(
                data.RenderBatches);

        Assert.Equal(
            BlockRenderMode.Cutout,
            batch.Batch.RenderMode);
        Assert.Equal(
            8,
            batch.TriangleCount);
        Assert.Equal(
            0,
            data.CollisionTriangleCount);
        Assert.All(
            batch.Vertices,
            vertex =>
            {
                Assert.InRange(
                    vertex.Position.X,
                    1f,
                    2f);
                Assert.InRange(
                    vertex.Position.Y,
                    1f,
                    1.62f);
                Assert.InRange(
                    vertex.Position.Z,
                    1f,
                    2f);
                Assert.Equal(
                    3.25f,
                    vertex.EncodedTextureLayers.X);
            });
    }

    [Fact]
    public void GroundSpriteBuildsCompactCutoutWithoutCollision()
    {
        const string path = "textures/items/pebble.png";
        var blocks = new BlockRegistry(
        [
            new BlockDefinition(
                "asteria:pebble",
                visual: BlockVisualDefinition.GroundSprite(
                    new BlockTextureLayer(path)),
                isCollidable: false,
                renderMode: BlockRenderMode.Cutout,
                castsShadow: false,
                lightDampening: 0),
        ]);
        var chunk = new Chunk();
        chunk.SetBlock(1, 1, 1, blocks.GetId("asteria:pebble"));
        ChunkLightingSolver.Initialize(
            chunk, blocks, new FluidRegistry([]));
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, chunk);
        var mesh = ChunkMeshDataBuilder.BuildMeshlet(
            world, ChunkCoord.Zero, blocks,
            new TerrainTextureLookup(new Dictionary<string, int>
            {
                [path] = 2,
            }),
            meshletIndex: 0);

        var batch = Assert.Single(mesh.RenderBatches);
        Assert.Equal(BlockRenderMode.Cutout, batch.Batch.RenderMode);
        Assert.Equal(4, batch.TriangleCount);
        Assert.Equal(0, mesh.CollisionTriangleCount);
        Assert.All(batch.Vertices, vertex =>
        {
            Assert.InRange(vertex.Position.X, 1.289f, 1.711f);
            Assert.InRange(vertex.Position.Y, 1.0244f, 1.0246f);
            Assert.InRange(vertex.Position.Z, 1.289f, 1.711f);
        });
    }

    [Fact]
    public void FluidGeometryBuildsWithoutGodotTypes()
    {
        var blocks =
            new BlockRegistry([]);
        var fluids =
            new FluidRegistry(
            [
                new FluidDefinition(
                    "asteria:water",
                    new FluidColor(79, 159, 214),
                    opacity: 0.72f),
            ]);
        var water =
            fluids.GetId("asteria:water");
        var chunk = new Chunk();

        chunk.SetFluid(
            1,
            1,
            1,
            FluidCell.Source(water));
        ChunkLightingSolver.Initialize(
            chunk,
            blocks,
            fluids);

        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            chunk);

        var data =
            FluidMeshDataBuilder.BuildMeshlet(
                world,
                ChunkCoord.Zero,
                blocks,
                fluids,
                meshletIndex: 0);

        Assert.True(data.HasGeometry);
        Assert.True(data.TriangleCount > 0);
    }

    [Fact]
    public void IsolatedFluidTopUsesAuthoredCellHeight()
    {
        var blocks =
            new BlockRegistry([]);
        var fluids =
            new FluidRegistry(
            [
                new FluidDefinition(
                    "asteria:water",
                    new FluidColor(79, 159, 214),
                    opacity: 0.72f),
            ]);
        var water =
            fluids.GetId("asteria:water");
        var chunk = new Chunk();

        chunk.SetFluid(
            1,
            1,
            1,
            FluidCell.Spreading(
                water,
                FluidCell.MinLevel,
                spreadDistance: 7));

        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            chunk);

        var data =
            FluidMeshDataBuilder.BuildMeshlet(
                world,
                ChunkCoord.Zero,
                blocks,
                fluids,
                meshletIndex: 0);
        var topVertices =
            Assert.Single(data.Batches)
                .Vertices
                .Where(vertex =>
                    vertex.Normal ==
                    System.Numerics.Vector3.UnitY)
                .ToArray();

        Assert.Equal(6, topVertices.Length);

        foreach (var vertex in topVertices)
        {
            Assert.Equal(
                1f + FluidCell.MinLevel / 8f,
                vertex.Position.Y,
                precision: 5);
        }
    }

    [Fact]
    public void FluidTopSurfaceHasDedicatedInteriorFacingTriangles()
    {
        var blocks =
            new BlockRegistry([]);
        var fluids =
            new FluidRegistry(
            [
                new FluidDefinition(
                    "asteria:water",
                    new FluidColor(
                        79,
                        159,
                        214),
                    opacity: 0.72f),
            ]);
        var water =
            fluids.GetId(
                "asteria:water");
        var chunk =
            new Chunk();

        chunk.SetFluid(
            1,
            1,
            1,
            FluidCell.Source(water));

        var world =
            new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            chunk);

        var data =
            FluidMeshDataBuilder.BuildMeshlet(
                world,
                ChunkCoord.Zero,
                blocks,
                fluids,
                meshletIndex: 0);
        var vertices =
            Assert.Single(
                data.Batches)
                .Vertices;
        var topHeight =
            2f;

        var exterior =
            vertices.Count(vertex =>
                vertex.Normal ==
                    System.Numerics.Vector3.UnitY &&
                MathF.Abs(
                    vertex.Position.Y -
                    topHeight) <
                0.0001f);
        var interior =
            vertices.Count(vertex =>
                vertex.Normal ==
                    -System.Numerics.Vector3.UnitY &&
                MathF.Abs(
                    vertex.Position.Y -
                    topHeight) <
                0.0001f);

        Assert.Equal(6, exterior);
        Assert.Equal(6, interior);
    }

    [Fact]
    public void FluidSurfaceHeightMatchesAcrossChunkSeam()
    {
        var blocks =
            new BlockRegistry([]);
        var fluids =
            new FluidRegistry(
            [
                new FluidDefinition(
                    "asteria:water",
                    new FluidColor(79, 159, 214),
                    opacity: 0.72f),
            ]);
        var water =
            fluids.GetId("asteria:water");
        var left = new Chunk();
        var right = new Chunk();

        left.SetFluid(
            Chunk.Size - 1,
            1,
            8,
            FluidCell.Spreading(
                water,
                FluidCell.MaxLevel,
                spreadDistance: 0));
        right.SetFluid(
            0,
            1,
            8,
            FluidCell.Spreading(
                water,
                FluidCell.MinLevel,
                spreadDistance: 7));

        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            left);
        world.InsertChunk(
            new ChunkCoord(1, 0, 0),
            right);

        var leftData =
            FluidMeshDataBuilder.BuildMeshlet(
                world,
                ChunkCoord.Zero,
                blocks,
                fluids,
                meshletIndex: 3);
        var rightData =
            FluidMeshDataBuilder.BuildMeshlet(
                world,
                new ChunkCoord(1, 0, 0),
                blocks,
                fluids,
                meshletIndex: 2);

        var leftEdgeHeights =
            Assert.Single(leftData.Batches)
                .Vertices
                .Where(vertex =>
                    vertex.Normal ==
                        System.Numerics.Vector3.UnitY &&
                    MathF.Abs(
                        vertex.Position.X -
                        Chunk.Size) <
                    0.0001f)
                .Select(vertex => vertex.Position.Y)
                .Distinct()
                .Order()
                .ToArray();
        var rightEdgeHeights =
            Assert.Single(rightData.Batches)
                .Vertices
                .Where(vertex =>
                    vertex.Normal ==
                        System.Numerics.Vector3.UnitY &&
                    MathF.Abs(
                        vertex.Position.X) <
                    0.0001f)
                .Select(vertex => vertex.Position.Y)
                .Distinct()
                .Order()
                .ToArray();

        Assert.NotEmpty(leftEdgeHeights);
        Assert.Equal(
            leftEdgeHeights,
            rightEdgeHeights);
    }
    [Fact]
    public void DyedGrassUsesAuthoredTintWhileAdjacentUndyedGrassUsesBiomeTint()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:grass_block", tint: BlockTint.Grass,
                secondaryProperties: ["dyed"],
                textures: new BlockTextureSet(top: [
                    new BlockTextureLayer("textures/grass.png", dyable: true)
                ]),
                previewColor: new BlockPreviewColor(20, 200, 20))
        ]);
        var chunk = new Chunk();
        var id = blocks.GetId("asteria:grass_block");
        chunk.SetBlock(1, 1, 1, id);
        chunk.SetBlock(2, 1, 1, id);
        chunk.SetSurfaceState(1, 1, 1, new BlockSurfaceState("asteria:red"));
        ChunkLightingSolver.Initialize(chunk, blocks, new FluidRegistry([]));
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, chunk);
        var dyes = new DyeRegistry([
            new DyeDefinition("asteria:red", 0f, 1f, 1f / 3f)
        ]);
        var data = ChunkMeshDataBuilder.BuildMeshlet(
            world, ChunkCoord.Zero, blocks,
            new TerrainTextureLookup(new Dictionary<string, int> {
                ["textures/grass.png"] = 1
            }), 0, dyes: dyes);
        var vertices = data.RenderBatches.SelectMany(x => x.Vertices).ToArray();
        Assert.Contains(vertices, vertex =>
            vertex.TintAndAo.X > .99f && vertex.TintAndAo.Y < .02f &&
            vertex.TintAndAo.Z < .02f);
        Assert.Contains(vertices, vertex =>
            vertex.TintAndAo.Y > .7f && vertex.TintAndAo.X < .2f);
    }

    [Fact]
    public void DyedGeometryRejectsUnknownColorIds()
    {
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:grass_block", secondaryProperties: ["dyed"])
        ]);
        var chunk = new Chunk();
        chunk.SetBlock(1, 1, 1, blocks.GetId("asteria:grass_block"));
        chunk.SetSurfaceState(1, 1, 1, new BlockSurfaceState("asteria:missing"));
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, chunk);
        Assert.Throws<KeyNotFoundException>(() => ChunkMeshDataBuilder.BuildMeshlet(
            world, ChunkCoord.Zero, blocks,
            new TerrainTextureLookup(new Dictionary<string, int>()),
            0, dyes: new DyeRegistry([])));
    }

}

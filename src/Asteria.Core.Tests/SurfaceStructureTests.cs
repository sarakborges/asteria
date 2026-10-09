using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class SurfaceStructureTests
{
    [Fact]
    public void CoralReefVariantsUseOnlyNewCoralsAndRequireOceanWater()
    {
        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var structures = StructureRegistry.FromJson(ReadJsonDirectory("structures"));
        structures.ValidateBlocks(blocks);

        var coralIds = new[]
        {
            "asteria:coral_blue",
            "asteria:coral_pink",
            "asteria:coral_yellow"
        };
        foreach (var id in coralIds)
        {
            var block = blocks.GetDefinition(blocks.GetId(id));
            Assert.Equal("foliage", block.Category);
            Assert.True(block.IsCollidable);
            Assert.Equal(15, block.LightDampening);
            Assert.All(block.Textures.AllLayers(), layer =>
                Assert.Equal($"textures/blocks/{id["asteria:".Length..]}.png",
                    layer.Texture));
        }

        var variants = structures.ResolveReference("asteria:coral_reef");
        Assert.Equal(2, variants.Count);
        Assert.Equal(new[] { "asteria:coral_reef_01", "asteria:coral_reef_02" },
            variants.Select(variant => variant.Id));
        Assert.Equal(coralIds,
            variants.SelectMany(variant => variant.Voxels)
                .Select(voxel => voxel.Block)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal));

        foreach (var reef in variants)
        {
            Assert.False(reef.Restrictions.RequiresDryGround);
            Assert.Equal(1f, reef.Restrictions.RequiredBiomeCoverage);
            Assert.Equal(1, reef.Restrictions.MaxSlope);
            Assert.Equal(StructureFluidPolicy.Displace,
                reef.Generation.FluidPolicy);
            Assert.Equal(StructureReplacePolicy.Terrain,
                reef.Generation.ReplacePolicy);
            Assert.Contains(reef.Restrictions.Proximity, rule =>
                rule.Mode == StructureProximityMode.Required &&
                rule.Target.Fluid == "asteria:water" &&
                rule.MinDistance == 0 && rule.MaxDistance == 0);
            Assert.All(reef.Voxels, voxel =>
                Assert.Contains(voxel.Block, coralIds));
        }

        var overworld = DimensionRegistry.FromJson(
            ReadJsonDirectory("dimensions")).Get(DimensionId.Overworld);
        var root = Assert.Single(overworld.GeneratedSurfaceStructures,
            rule => rule.Biome == "asteria:overworld/ocean" &&
                    rule.Structure == "asteria:coral_reef");
        var weights = Assert.IsType<SurfaceHabitatWeights>(root.HabitatWeights);
        Assert.True(weights.For("rocky_reefs") >
                    weights.For("gravel_banks"));
        Assert.True(weights.For("gravel_banks") >
                    weights.For("sand_flats"));
    }

    [Fact]
    public void OceanReefVariantsRemainSubmergedGroundBoundAndHabitatWeighted()
    {
        var structures = StructureRegistry.FromJson(ReadJsonDirectory("structures"));
        var members = structures.ResolveReference("asteria:ocean_rock");
        Assert.Equal(4, members.Count);
        Assert.Equal(
            new[] {
                "asteria:ocean_rock_01", "asteria:ocean_rock_02",
                "asteria:ocean_rock_03", "asteria:ocean_rock_04",
            }, members.Select(member => member.Id));
        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        structures.ValidateBlocks(blocks);

        foreach (var reef in members)
        {
            Assert.False(reef.Restrictions.RequiresDryGround);
            Assert.Equal(1f, reef.Restrictions.RequiredBiomeCoverage);
            Assert.Equal(1, reef.Restrictions.MaxSlope);
            Assert.Equal(StructureFluidPolicy.Displace,
                reef.Generation.FluidPolicy);
            Assert.Equal(StructureReplacePolicy.Terrain,
                reef.Generation.ReplacePolicy);
            Assert.InRange(reef.MaximumY - reef.MinimumY, 1, 2);
            Assert.Contains(reef.Voxels, voxel =>
                voxel.Block == "asteria:stone_cobble");
            Assert.All(reef.Voxels, voxel =>
                Assert.Contains(voxel.Block,
                    new[] {
                        "asteria:stone", "asteria:stone_cobble",
                        "asteria:gravel", "asteria:clay",
                    }));
        }

        var dimensions = DimensionRegistry.FromJson(
            ReadJsonDirectory("dimensions"));
        var overworld = dimensions.Get(DimensionId.Overworld);
        var rocks = Assert.Single(overworld.GeneratedSurfaceStructures,
            rule => rule.Biome == "asteria:overworld/ocean" &&
                    rule.Structure == "asteria:ocean_rock");
        var weight = Assert.IsType<SurfaceHabitatWeights>(rocks.HabitatWeights);
        Assert.True(weight.For("rocky_reefs") > weight.For("gravel_banks"));
        Assert.True(weight.For("gravel_banks") > weight.For("sand_flats"));
    }

    [Fact]
    public void DefaultStructurePackParsesAndMatchesActiveOverworldRoots()
    {
        var blocks =
            BlockRegistry.FromJson(
                ReadJsonDirectory(
                    "blocks"));
        var structures =
            StructureRegistry.FromJson(
                ReadJsonDirectory(
                    "structures"));
        var fluids =
            FluidRegistry.FromJson(
                ReadJsonDirectory(
                    "fluids"));
        var dimensions =
            DimensionRegistry.FromJson(
                ReadJsonDirectory(
                    "dimensions"));
        var overworld =
            dimensions.Get(
                DimensionId.Overworld);

        structures.ValidateBlocks(
            blocks);
        structures.ValidateFluids(
            fluids);
        var structureSets =
            StructureSetRegistry.FromJson(
                ReadJsonDirectory("structure_sets"));
        dimensions.ValidateStructures(
            structures, structureSets);

        Assert.Equal(
            130,
            structures.Count);
        Assert.Equal(4, structures.ResolveReference("asteria:ocean_rock").Count);
        Assert.Equal(2, structures.ResolveReference("asteria:basalt_outcrop").Count);
        Assert.Equal(2, structures.ResolveReference("asteria:basalt_boulder").Count);
        Assert.Equal(2, structures.ResolveReference("asteria:basalt_spire").Count);
        Assert.Equal(2, structures.ResolveReference("asteria:talus_outcrop").Count);
        Assert.Equal(3, structures.ResolveReference("asteria:bush_oak").Count);
        Assert.Equal(7, structureSets.Count);
        Assert.Equal("asteria:ocean_rock",
            structureSets.Get("asteria:ocean_rock_cluster").Elements[0].Structure);
        Assert.Equal("asteria:basalt_outcrop",
            structureSets.Get("asteria:basalt_cluster").Elements[0].Structure);
        Assert.Equal(2, structures.ResolveReference("asteria:fallen_log_wraith").Count);
        Assert.Equal(3, structures.ResolveReference("asteria:tree_wraith").Count);
        Assert.Equal(2, structures.ResolveReference("asteria:wraith_snag").Count);
        Assert.Equal("asteria:tree_wraith",
            structureSets.Get("asteria:wraith_grove").Elements[0].Structure);
        Assert.Equal(4, structures.ResolveReference("asteria:tree_enchanted").Count);
        Assert.Equal("asteria:tree_enchanted",
            structureSets.Get("asteria:enchanted_grove").Elements[0].Structure);
        Assert.Equal(4, structures.ResolveReference("asteria:fallen_log_oak").Count);
        Assert.Equal(3, structures.ResolveReference("asteria:oak_stump").Count);
        Assert.Equal("asteria:boulder_small",
            structureSets.Get("asteria:rock_cluster").Elements[0].Structure);
        Assert.Equal("asteria:bush_oak",
            structureSets.Get("asteria:thicket_oak").Elements[0].Structure);
        Assert.Equal("asteria:tree_oak",
            structureSets.Get("asteria:oak_grove").Elements[0].Structure);
        Assert.Equal(
            10,
            structures.ResolveReference(
                "asteria:river_segment")
                .Count);
        Assert.Equal(
            4,
            structures.ResolveReference(
                "asteria:lake")
                .Count);
        Assert.Equal(
            4,
            structures.ResolveReference(
                "asteria:mountain_pond")
                .Count);
        Assert.Equal(
            4,
            structures.ResolveReference(
                "asteria:mountain_waterfall")
                .Count);
        Assert.Equal(
            4,
            structures.ResolveReference(
                "asteria:river_lake")
                .Count);
        Assert.True(
            structures.ResolvesReference(
                "asteria:river_ocean_mouth"));
        Assert.Equal(
            74,
            overworld
                .GeneratedSurfaceStructures
                .Count);
        var plainsRoots = overworld.GeneratedSurfaceStructures
            .Where(root => root.Biome == "asteria:overworld/plains")
            .ToArray();
        Assert.Equal(13, plainsRoots.Length);
        Assert.All(plainsRoots.Where(root => root.Structure != "asteria:lake"),
            root => Assert.NotNull(root.HabitatWeights));
        Assert.Equal(2.5f, Assert.Single(plainsRoots,
            root => root.Structure == "asteria:rock_cluster")
            .HabitatWeights!.For("rocky"));
        Assert.Equal(0f, Assert.Single(plainsRoots,
            root => root.Structure == "asteria:oak_grove")
            .HabitatWeights!.For("rocky"));
        Assert.All(
            overworld
                .GeneratedSurfaceStructures,
            generated =>
            {
                Assert.Contains(
                    generated.Biome,
                    overworld.SurfaceBiomes);
                Assert.True(
                    structures.ResolvesReference(generated.Structure) ||
                    structureSets.ResolvesReference(generated.Structure));
                Assert.True(generated.Chance is >= 0f and <= 1f);
            });

        foreach (var reference in new[] { "asteria:fallen_log_oak", "asteria:oak_stump" })
        {
            Assert.All(structures.ResolveReference(reference), detail =>
            {
                Assert.True(detail.Rotation);
                Assert.Equal(0, detail.GroundAnchorY);
                Assert.Equal(0, detail.Restrictions.MaxSlope);
                Assert.Equal(1f, detail.Restrictions.RequiredBiomeCoverage);
                Assert.True(detail.Restrictions.RequiresDryGround);
                Assert.Equal(StructureFluidPolicy.Forbid, detail.Generation.FluidPolicy);
                Assert.True(detail.Voxels.All(voxel => voxel.Y >= 1));
                Assert.Contains("asteria:gravel", detail.Restrictions.GroundBlocks);
            });
        }

        Assert.All(
            structures.ResolveReference("asteria:bush_oak"),
            bush =>
            {
                Assert.True(bush.Restrictions.RequiresDryGround);
                Assert.Equal(1f, bush.Restrictions.RequiredBiomeCoverage);
                Assert.Equal(StructureFluidPolicy.Forbid, bush.Generation.FluidPolicy);
                Assert.False(bush.Generation.ReserveSpace);
                Assert.Contains("asteria:leaf_oak", bush.Voxels.Select(v => v.Block));
            });
        Assert.Equal(
            10,
            structures
                .Get(
                    "asteria:boulder_small")
                .Voxels
                .Count);
        Assert.Equal(
            168,
            structures
                .Get(
                    "asteria:boulder_huge")
                .Voxels
                .Count);
    }

    [Fact]
    public void EnchantedForestTreesPortTheFourMinecloneTemplatesWithGroundFit()
    {
        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var structures = StructureRegistry.FromJson(ReadJsonDirectory("structures"));
        var variants = structures.ResolveReference("asteria:tree_enchanted");

        Assert.Equal(4, variants.Count);
        Assert.Equal(4, variants.Select(tree => tree.Id).Distinct().Count());
        foreach (var tree in variants)
        {
            Assert.False(tree.Locatable);
            Assert.True(tree.Rotation);
            Assert.Equal(0, tree.Priority);
            Assert.Equal(StructureReplacePolicy.Terrain, tree.Generation.ReplacePolicy);
            Assert.Equal(StructureFluidPolicy.Forbid, tree.Generation.FluidPolicy);
            Assert.False(tree.Generation.ReserveSpace);
            Assert.Contains("tree", tree.ConflictGroups);
            Assert.Equal(1, tree.Restrictions.MaxSlope);
            Assert.Equal(1f, tree.Restrictions.RequiredBiomeCoverage);
            Assert.True(tree.Restrictions.RequiresDryGround);
            Assert.Equal(new[] { "asteria:grass_block", "asteria:dirt" },
                tree.Restrictions.GroundBlocks);
            Assert.Equal(0, tree.GroundAnchorYOffset);
            Assert.True(tree.Voxels.Count >= 20);
            Assert.Contains(tree.Voxels, voxel => voxel.Block == "asteria:log_enchanted" && voxel.Orientation == BlockOrientation.Y);
            Assert.Contains(tree.Voxels, voxel => voxel.Block == "asteria:leaf_enchanted");
            Assert.Contains(tree.Voxels, voxel => voxel.Block == "asteria:log_enchanted" && voxel.Orientation != BlockOrientation.Y);
            Assert.All(tree.Voxels, voxel => Assert.True(voxel.Y is >= 0 and <= 9));
        }

        structures.ValidateBlocks(blocks);
    }

    [Fact]
    public void EnchantedGroveUsesBoundedSetsAndAuthoredHabitats()
    {
        var structures = StructureRegistry.FromJson(ReadJsonDirectory("structures"));
        var sets = StructureSetRegistry.FromJson(ReadJsonDirectory("structure_sets"));
        sets.ValidateStructures(structures);
        var grove = sets.Get("asteria:enchanted_grove");
        Assert.True(grove.Locatable);
        Assert.Contains("tree", grove.ConflictGroups);
        Assert.Equal("asteria:tree_enchanted", grove.Elements[0].Structure);
        var companions = grove.Elements[1];
        Assert.Equal("asteria:tree_enchanted", companions.Structure);
        Assert.Equal(2, companions.Count.Min);
        Assert.Equal(5, companions.Count.Max);
        Assert.True(companions.Placement.MaxDistance <= 32);
        Assert.True(companions.Placement.MinSeparation >= 14);

        var dimensions = DimensionRegistry.FromJson(ReadJsonDirectory("dimensions"));
        var overworld = dimensions.Get(DimensionId.Overworld);
        var rules = overworld.GeneratedSurfaceStructures.Where(
            rule => rule.Biome == "asteria:overworld/enchanted_forest").ToArray();
        Assert.Equal(2, rules.Length);
        Assert.Equal(34, Assert.Single(rules,
            rule => rule.Structure == "asteria:tree_enchanted").Spacing);
        foreach (var rule in rules)
        {
            Assert.NotNull(rule.HabitatWeights);
            Assert.True(rule.HabitatWeights.For("violet_undergrowth") >
                        rule.HabitatWeights.For("pink_glade"));
            Assert.True(rule.HabitatWeights.For("pink_glade") >
                        rule.HabitatWeights.For("luminous_clearing"));
        }
        Assert.Equal(0f, Assert.Single(rules,
            rule => rule.Structure == "asteria:enchanted_grove")
            .HabitatWeights!.For("luminous_clearing"));
    }

    [Fact]
    public void WraithGroveTemplatesUseExistingPaleWoodAndTintedFoliage()
    {
        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var structures = StructureRegistry.FromJson(ReadJsonDirectory("structures"));
        var living = structures.ResolveReference("asteria:tree_wraith");
        var dead = structures.ResolveReference("asteria:wraith_snag");

        Assert.Equal(3, living.Count);
        Assert.Equal(2, dead.Count);
        Assert.All(living.Concat(dead), tree =>
        {
            Assert.False(tree.Locatable);
            Assert.True(tree.Rotation);
            Assert.Equal(0, tree.GroundAnchorYOffset);
            Assert.Equal(1, tree.Restrictions.MaxSlope);
            Assert.Equal(1f, tree.Restrictions.RequiredBiomeCoverage);
            Assert.True(tree.Restrictions.RequiresDryGround);
            Assert.Equal(StructureReplacePolicy.Terrain, tree.Generation.ReplacePolicy);
            Assert.Equal(StructureFluidPolicy.Forbid, tree.Generation.FluidPolicy);
            Assert.False(tree.Generation.ReserveSpace);
            Assert.Empty(tree.Restrictions.Proximity);
            Assert.Contains("tree", tree.ConflictGroups);
            Assert.Contains("asteria:grass_block", tree.Restrictions.GroundBlocks);
            Assert.Contains("asteria:mud", tree.Restrictions.GroundBlocks);
            Assert.Contains(tree.Voxels, voxel =>
                voxel.Block == "asteria:log_enchanted_stripped_hollow");
            Assert.Contains(tree.Voxels, voxel =>
                voxel.Block == "asteria:leaf_willow");
            Assert.Contains(tree.Voxels, voxel =>
                voxel.Block == "asteria:log_enchanted_stripped"
                && voxel.Orientation != BlockOrientation.Y);
        });
        Assert.True(living.Min(tree => tree.Voxels.Count(voxel =>
            voxel.Block == "asteria:leaf_willow")) >
            dead.Max(tree => tree.Voxels.Count(voxel =>
                voxel.Block == "asteria:leaf_willow")));

        structures.ValidateBlocks(blocks);
    }

    [Fact]
    public void WraithGroveSetHasBoundedCompanionsAndConflicts()
    {
        var structures = StructureRegistry.FromJson(ReadJsonDirectory("structures"));
        var sets = StructureSetRegistry.FromJson(ReadJsonDirectory("structure_sets"));
        sets.ValidateStructures(structures);

        var grove = sets.Get("asteria:wraith_grove");
        Assert.True(grove.Locatable);
        Assert.Contains("tree", grove.ConflictGroups);
        Assert.False(grove.ReserveSpace);
        Assert.Equal("asteria:tree_wraith", grove.Elements[0].Structure);
        Assert.Equal("asteria:tree_wraith", grove.Elements[1].Structure);
        Assert.Equal(2, grove.Elements[1].Count.Min);
        Assert.Equal(4, grove.Elements[1].Count.Max);
        Assert.Equal(14, grove.Elements[1].Placement.MinSeparation);
        Assert.InRange(grove.Elements[1].Placement.MaxDistance, 15, 32);
        Assert.InRange(grove.Elements[1].Placement.Attempts, 1, 64);
    }

    [Fact]
    public void OakGroupAuthorsGroundAndGenerationPolicies()
    {
        var structures =
            StructureRegistry.FromJson(
                ReadJsonDirectory(
                    "structures"));
        var oak =
            structures.ResolveReference(
                "asteria:tree_oak");

        Assert.Equal(
            4,
            oak.Count);

        Assert.All(
            oak,
            definition =>
            {
                Assert.Equal(
                    new[]
                    {
                        "asteria:grass_block",
                        "asteria:dirt",
                    },
                    definition.Restrictions
                        .GroundBlocks);
                Assert.Equal(
                    StructureReplacePolicy.Terrain,
                    definition.Generation.ReplacePolicy);
                Assert.Equal(
                    StructureFluidPolicy.Forbid,
                    definition.Generation.FluidPolicy);
                Assert.False(
                    definition.Generation.ReserveSpace);
                Assert.Contains(
                    "tree",
                    definition.ConflictGroups);
            });
    }

    [Fact]
    public void WillowGroupAuthorsBoundedWaterProximity()
    {
        var structures =
            StructureRegistry.FromJson(
                ReadJsonDirectory(
                    "structures"));
        var willow =
            structures.ResolveReference(
                "asteria:tree_willow");

        Assert.Equal(
            3,
            willow.Count);

        Assert.All(
            willow,
            definition =>
            {
                Assert.Equal(
                    new[]
                    {
                        "asteria:grass_block",
                        "asteria:dirt",
                        "asteria:mud",
                    },
                    definition.Restrictions
                        .GroundBlocks);
                var proximity =
                    Assert.Single(
                        definition.Restrictions
                            .Proximity);
                Assert.Equal(
                    StructureProximityMode.Required,
                    proximity.Mode);
                Assert.Equal(
                    "asteria:water",
                    proximity.Target.Fluid);
                Assert.Null(
                    proximity.Target.Block);
                Assert.Equal(
                    1,
                    proximity.MinDistance);
                Assert.Equal(
                    12,
                    proximity.MaxDistance);
                Assert.Equal(
                    StructureReplacePolicy.Terrain,
                    definition.Generation.ReplacePolicy);
                Assert.Equal(
                    StructureFluidPolicy.Forbid,
                    definition.Generation.FluidPolicy);
                Assert.Contains(
                    "tree",
                    definition.ConflictGroups);
            });
    }

    [Fact]
    public void RequiredFluidProximityUsesActualGeneratedFluid()
    {
        var accepted =
            FluidProximityGenerator(
                "asteria:water",
                out var blocks);
        var rejected =
            FluidProximityGenerator(
                "asteria:lava",
                out _);
        var marker =
            blocks.GetId(
                "asteria:marker");
        var coord =
            new ChunkCoord(
                0,
                1,
                0);

        Assert.True(
            ChunkContains(
                accepted.Materialize(
                    coord),
                marker));
        Assert.False(
            ChunkContains(
                rejected.Materialize(
                    coord),
                marker));
    }

    [Fact]
    public void OakGroupMaterializesOnlyOnAllowedGround()
    {
        var blocks =
            BlockRegistry.FromJson(
                ReadJsonDirectory(
                    "blocks"));
        var structures =
            StructureRegistry.FromJson(
                ReadJsonDirectory(
                    "structures"));
        var oakLog =
            blocks.GetId(
                "asteria:log_oak");

        var allowed =
            FlatStructureGenerator(
                blocks,
                structures,
                "asteria:grass_block",
                "asteria:tree_oak");
        var rejected =
            FlatStructureGenerator(
                blocks,
                structures,
                "asteria:stone",
                "asteria:tree_oak");

        Assert.True(
            ChunkContains(
                allowed.Materialize(
                    new ChunkCoord(
                        0,
                        2,
                        0)),
                oakLog));
        Assert.False(
            ChunkContains(
                rejected.Materialize(
                    new ChunkCoord(
                        0,
                        2,
                        0)),
                oakLog));
    }

    [Fact]
    public void HigherPriorityConflictRejectsWholeLowerCandidate()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:grass_block"),
                new BlockDefinition(
                    "asteria:stone"),
                new BlockDefinition(
                    "asteria:marker_high"),
                new BlockDefinition(
                    "asteria:marker_low"),
            ]);
        var sharedRestrictions =
            new StructureRestrictionsDefinition(
                maxSlope: 0,
                requiresDryGround: true,
                requiredBiomeCoverage: 1f);
        var structures =
            new StructureRegistry(
            [
                new StructureDefinition(
                    "asteria:high",
                    rotation: false,
                    anchor: default,
                    voxels:
                    [
                        new StructureVoxelDefinition(
                            0,
                            0,
                            0,
                            "asteria:marker_high",
                            BlockOrientation.Y),
                    ],
                    restrictions:
                        sharedRestrictions,
                    priority: 10,
                    conflictGroups:
                    [
                        "test",
                    ]),
                new StructureDefinition(
                    "asteria:low",
                    rotation: false,
                    anchor: default,
                    voxels:
                    [
                        new StructureVoxelDefinition(
                            0,
                            0,
                            0,
                            "asteria:marker_low",
                            BlockOrientation.Y),
                    ],
                    restrictions:
                        sharedRestrictions,
                    priority: 0,
                    conflictGroups:
                    [
                        "test",
                    ]),
            ]);
        var generator =
            FlatStructureGenerator(
                blocks,
                structures,
                "asteria:grass_block",
                "asteria:high",
                "asteria:low");
        var chunk =
            generator.Materialize(
                new ChunkCoord(
                    0,
                    2,
                    0));

        Assert.Equal(
            blocks.GetId(
                "asteria:marker_high"),
            chunk.GetBlock(
                8,
                0,
                8));
    }

    [Fact]
    public void TerrainReplacementDoesNotOverwritePreviouslyClaimedStructureVoxel()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:grass_block"),
                new BlockDefinition(
                    "asteria:stone"),
                new BlockDefinition(
                    "asteria:marker_any"),
                new BlockDefinition(
                    "asteria:marker_terrain"),
            ]);
        var restrictions =
            new StructureRestrictionsDefinition(
                maxSlope: 0,
                requiresDryGround: true,
                requiredBiomeCoverage: 1f);
        var structures =
            new StructureRegistry(
            [
                new StructureDefinition(
                    "asteria:a_any",
                    rotation: false,
                    anchor: default,
                    voxels:
                    [
                        new StructureVoxelDefinition(
                            0,
                            0,
                            0,
                            "asteria:marker_any",
                            BlockOrientation.Y),
                    ],
                    restrictions:
                        restrictions),
                new StructureDefinition(
                    "asteria:z_terrain",
                    rotation: false,
                    anchor: default,
                    voxels:
                    [
                        new StructureVoxelDefinition(
                            0,
                            0,
                            0,
                            "asteria:marker_terrain",
                            BlockOrientation.Y),
                    ],
                    restrictions:
                        restrictions,
                    generation:
                        new StructureGenerationDefinition(
                            StructureReplacePolicy.Terrain,
                            StructureFluidPolicy.Displace,
                            false)),
            ]);
        var generator =
            FlatStructureGenerator(
                blocks,
                structures,
                "asteria:grass_block",
                "asteria:a_any",
                "asteria:z_terrain");
        var chunk =
            generator.Materialize(
                new ChunkCoord(
                    0,
                    2,
                    0));

        Assert.Equal(
            blocks.GetId(
                "asteria:marker_any"),
            chunk.GetBlock(
                8,
                0,
                8));
    }

    [Fact]
    public void BlockTemplateRejectsUnsupportedPaletteCapabilities()
    {
        Assert.Throws<FormatException>(
            () =>
                StructureDefinitionJson.Parse(
                    """
                    {
                      "id":"asteria:test_structure",
                      "rotation":false,
                      "palette":{
                        "S":{
                          "block":"asteria:stone",
                          "objects":[{"object":"asteria:pebble"}]
                        }
                      },
                      "layers":[
                        {"y":0,"rows":["S"]}
                      ]
                    }
                    """));
    }

    [Fact]
    public void RotatingTemplateValidatesEveryProducedBlockOrientation()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:log",
                    orientations:
                    [
                        BlockOrientation.X,
                    ]),
            ]);
        var structures =
            new StructureRegistry(
            [
                new StructureDefinition(
                    "asteria:test_structure",
                    rotation: true,
                    anchor: default,
                    voxels:
                    [
                        new StructureVoxelDefinition(
                            0,
                            0,
                            0,
                            "asteria:log",
                            BlockOrientation.X),
                    ]),
            ]);

        Assert.Throws<ArgumentException>(
            () =>
                structures.ValidateBlocks(
                    blocks));
    }

    [Fact]
    public void StructureCrossesHorizontalAndVerticalChunkBoundariesWithoutClipping()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:grass_block"),
                new BlockDefinition(
                    "asteria:dirt"),
                new BlockDefinition(
                    "asteria:stone"),
                new BlockDefinition(
                    "asteria:marker"),
            ]);
        var biome =
            new BiomeDefinition(
                "asteria:test/flat",
                new BiomeSurfaceLayoutDefinition(),
                new BiomeTerrainDefinition(
                    0f,
                    0f,
                    64,
                    0f,
                    32),
                [
                    new BiomeSurfaceLayerDefinition(
                        "asteria:grass_block",
                        1),
                    new BiomeSurfaceLayerDefinition(
                        "asteria:dirt",
                        4),
                    new BiomeSurfaceLayerDefinition(
                        "asteria:stone"),
                ]);
        var structures =
            new StructureRegistry(
            [
                new StructureDefinition(
                    "asteria:test_structure",
                    rotation: false,
                    anchor: default,
                    voxels:
                    [
                        new StructureVoxelDefinition(
                            0,
                            0,
                            0,
                            "asteria:marker",
                            BlockOrientation.Y),
                        new StructureVoxelDefinition(
                            9,
                            1,
                            0,
                            "asteria:marker",
                            BlockOrientation.Y),
                        new StructureVoxelDefinition(
                            0,
                            17,
                            0,
                            "asteria:marker",
                            BlockOrientation.Y),
                    ],
                    restrictions:
                        new StructureRestrictionsDefinition(
                            maxSlope: 0,
                            requiresDryGround: true,
                            requiredBiomeCoverage: 1f)),
            ]);
        var dimension =
            new DimensionDefinition(
                new DimensionId(
                    "asteria:test"),
                [
                    biome.Id,
                ],
                32,
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
                    0f),
                generatedSurfaceStructures:
                [
                    new DimensionGeneratedSurfaceStructureDefinition(
                        biome.Id,
                        "asteria:test_structure",
                        spacing: 16,
                        chance: 1f,
                        jitter: 0),
                ]);
        var biomes =
            new BiomeRegistry(
            [
                biome,
            ]);
        var generator =
            new BiomeWorldGenerator(
                77UL,
                dimension,
                blocks,
                new FluidRegistry(
                    Array.Empty<FluidDefinition>()),
                biomes,
                structures);
        var marker =
            blocks.GetId(
                "asteria:marker");

        var leftRange =
            generator.GetSurfaceRange(
                0,
                0);
        var rightRange =
            generator.GetSurfaceRange(
                1,
                0);

        Assert.True(
            leftRange.MaximumWorldY >=
            49);
        Assert.True(
            rightRange.MaximumWorldY >=
            33);

        var leftBase =
            generator.Materialize(
                new ChunkCoord(
                    0,
                    2,
                    0));
        var rightBase =
            generator.Materialize(
                new ChunkCoord(
                    1,
                    2,
                    0));
        var leftTop =
            generator.Materialize(
                new ChunkCoord(
                    0,
                    3,
                    0));

        Assert.Equal(
            marker,
            leftBase.GetBlock(
                8,
                0,
                8));
        Assert.Equal(
            marker,
            rightBase.GetBlock(
                1,
                1,
                8));
        Assert.Equal(
            marker,
            leftTop.GetBlock(
                8,
                1,
                8));

        // Vertical bands outside the entire payload retain normal
        // terrain/air even with horizontal structure overlap.
        var underground = generator.Materialize(
            new ChunkCoord(0, 0, 0));
        var above = generator.Materialize(
            new ChunkCoord(0, 5, 0));
        Assert.Equal(
            blocks.GetId("asteria:stone"),
            underground.GetBlock(8, 0, 8));
        Assert.True(above.IsEmpty);
    }

    [Fact]
    public void StructureQueriesReturnTheSameAcceptedPlacementUsedByMaterialization()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
                new BlockDefinition(
                    "asteria:marker"),
            ]);
        var structure =
            new StructureDefinition(
                "asteria:test_marker",
                rotation: false,
                anchor: default,
                voxels:
                [
                    new StructureVoxelDefinition(
                        0,
                        0,
                        0,
                        "asteria:marker",
                        BlockOrientation.Y),
                ],
                restrictions:
                    new StructureRestrictionsDefinition(
                        maxSlope: 0,
                        requiresDryGround: true,
                        requiredBiomeCoverage: 1f));
        var generator =
            FlatStructureGenerator(
                blocks,
                new StructureRegistry(
                [
                    structure,
                ]),
                "asteria:stone",
                structure.Id);

        var area =
            generator.SurfaceStructuresIntersecting(
                0,
                0,
                Chunk.Size,
                Chunk.Size);
        var placement =
            Assert.Single(
                area);
        var nearest =
            generator.FindNearestSurfaceStructure(
                structure.Id,
                0,
                0,
                64);

        Assert.True(
            nearest.HasValue);
        Assert.Equal(
            -8,
            nearest.Value.PlacementAnchorX);
        Assert.Equal(
            -8,
            nearest.Value.PlacementAnchorZ);
        Assert.Equal(
            -8,
            nearest.Value.AnchorX);
        Assert.Equal(
            -8,
            nearest.Value.AnchorZ);
        Assert.Equal(
            8,
            placement.AnchorX);
        Assert.Equal(
            8,
            placement.AnchorZ);

        var chunk =
            generator.Materialize(
                new ChunkCoord(
                    0,
                    placement.AnchorY /
                    Chunk.Size,
                    0));
        Assert.Equal(
            blocks.GetId(
                "asteria:marker"),
            chunk.GetBlock(
                placement.AnchorX,
                placement.AnchorY %
                Chunk.Size,
                placement.AnchorZ));
    }

    [Fact]
    public void StructureSearchIsBoundedByReferenceAndDistance()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
                new BlockDefinition(
                    "asteria:marker"),
            ]);
        var structure =
            new StructureDefinition(
                "asteria:test_marker",
                rotation: false,
                anchor: default,
                voxels:
                [
                    new StructureVoxelDefinition(
                        0,
                        0,
                        0,
                        "asteria:marker",
                        BlockOrientation.Y),
                ]);
        var generator =
            FlatStructureGenerator(
                blocks,
                new StructureRegistry(
                [
                    structure,
                ]),
                "asteria:stone",
                structure.Id);

        Assert.Null(
            generator.FindNearestSurfaceStructure(
                "asteria:missing",
                0,
                0,
                64));
        Assert.Null(
            generator.FindNearestSurfaceStructure(
                structure.Id,
                0,
                0,
                4));
        Assert.NotNull(
            generator.FindNearestSurfaceStructure(
                structure.Id,
                0,
                0,
                12));
        Assert.NotNull(
            generator.FindNearestSurfaceStructure(
                structure.Id,
                0,
                0,
                12,
                structureId: structure.Id));
        Assert.Null(
            generator.FindNearestSurfaceStructure(
                structure.Id,
                0,
                0,
                12,
                structureId: "asteria:other"));

    }

    [Fact]
    public void ManualStructureUsesAuthoredGroundAndMutationRuntime()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:marker"),
        ]);
        var definition = new StructureDefinition(
            "asteria:test_manual",
            rotation: false,
            anchor: default,
            voxels:
            [
                new StructureVoxelDefinition(0, 0, 0,
                    "asteria:marker", BlockOrientation.Y),
            ],
            restrictions: new StructureRestrictionsDefinition(
                maxSlope: 0, requiresDryGround: false));
        var registry = new StructureRegistry([definition]);
        // No procedural structure roots: this is a manual-only definition.
        var generator = FlatStructureGenerator(
            blocks, registry, "asteria:stone");
        var height = generator.SurfaceHeight(8, 8);
        var coordinate = VoxelCoordinates.FromWorld(8, height, 8).Chunk;
        var world = new VoxelWorld();
        world.InsertChunk(coordinate, generator.Materialize(coordinate));

        var mutations = new VoxelMutationRuntime(
            world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var runtime = new ManualStructurePlacementRuntime(
            generator, world, mutations, blocks, registry,
            StructureSetRegistry.Empty, new ManualStructurePlacementLedger());
        var playerBounds = new WorldAabb(
            new System.Numerics.Vector3(12, height + 2, 12),
            new System.Numerics.Vector3(13, height + 4, 13));

        Assert.Equal(ManualStructurePlacementResult.Placed,
            runtime.TryPlace(definition.Id, null, 8, 8, playerBounds));
        Assert.Equal(blocks.GetId("asteria:marker"),
            world.GetCellOrEmpty(new WorldVoxelCoord(8, height, 8)).Block);
        Assert.Equal(ManualStructurePlacementResult.UnknownReference,
            runtime.TryPlace("asteria:missing", null, 8, 8, playerBounds));
    }

    [Fact]
    public void ManualStructureReservationSurvivesChunkArchiveAndSessionReconstruction()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:marker"),
        ]);
        var structure = new StructureDefinition("asteria:manual_marker",
            rotation: false, anchor: default,
            voxels: [new StructureVoxelDefinition(
                0, 0, 0, "asteria:marker", BlockOrientation.Y)],
            restrictions: new StructureRestrictionsDefinition(
                maxSlope: 0, requiresDryGround: false));
        var structures = new StructureRegistry([structure]);
        var generator = FlatStructureGenerator(
            blocks, structures, "asteria:stone");
        var y = generator.SurfaceHeight(8, 8);
        var chunk = VoxelCoordinates.FromWorld(8, y, 8).Chunk;
        var world = new VoxelWorld();
        world.InsertChunk(chunk, generator.Materialize(chunk));
        var journal = new ManualStructurePlacementLedger();
        var player = new WorldAabb(
            new System.Numerics.Vector3(12, y + 2, 12),
            new System.Numerics.Vector3(13, y + 4, 13));

        var before = ManualRuntime(
            generator, world, blocks, structures, StructureSetRegistry.Empty,
            journal);
        Assert.Equal(ManualStructurePlacementResult.Placed,
            before.TryPlace(structure.Id, null, 8, 8, player));
        Assert.Single(journal.Snapshot());
        world.ArchiveChunk(chunk);
        Assert.Equal(ChunkRestoreResult.Restored, world.RestoreChunk(chunk));

        var after = ManualRuntime(
            generator, world, blocks, structures, StructureSetRegistry.Empty,
            journal);
        var revision = world.Revision;
        Assert.Equal(ManualStructurePlacementResult.InvalidPlacement,
            after.TryPlace(structure.Id, null, 8, 8, player));
        Assert.Equal(revision, world.Revision);
        Assert.Single(journal.Snapshot());
        Assert.Equal(blocks.GetId("asteria:marker"),
            world.GetCellOrEmpty(new WorldVoxelCoord(8, y, 8)).Block);
    }

    [Fact]
    public void ManualStructureNeverChangesUnloadedWorld()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:marker"),
        ]);
        var definition = new StructureDefinition(
            "asteria:test_manual", rotation: false,
            anchor: default,
            voxels:
            [
                new StructureVoxelDefinition(0, 0, 0,
                    "asteria:marker", BlockOrientation.Y),
            ]);
        var registry = new StructureRegistry([definition]);
        var world = new VoxelWorld();
        var generator = FlatStructureGenerator(
            blocks, registry, "asteria:stone");
        var mutations = new VoxelMutationRuntime(
            world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var runtime = new ManualStructurePlacementRuntime(
            generator, world, mutations, blocks, registry,
            StructureSetRegistry.Empty, new ManualStructurePlacementLedger());
        var bounds = new WorldAabb(
            new System.Numerics.Vector3(12, 70, 12),
            new System.Numerics.Vector3(13, 72, 13));

        Assert.Equal(ManualStructurePlacementResult.NonResident,
            runtime.TryPlace(definition.Id, null, 8, 8, bounds));
        Assert.Equal(0, world.ChunkCount);
        Assert.Equal(0UL, world.Revision);
    }

    [Fact]
    public void ManualStructureSetPlacesRequiredElementsTogether()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:root_marker"),
            new BlockDefinition("asteria:child_marker"),
        ]);
        var root = ManualMarker("asteria:root", "asteria:root_marker");
        var child = ManualMarker("asteria:child", "asteria:child_marker");
        var structures = new StructureRegistry([root, child]);
        var set = new StructureSetDefinition("asteria:test_set",
        [
            new StructureSetElementDefinition("root", root.Id, required: true,
                placement: new StructureSetElementPlacementDefinition(
                    minDistance: 0, maxDistance: 0, attempts: 1)),
            new StructureSetElementDefinition("child", child.Id, required: true,
                placement: new StructureSetElementPlacementDefinition(
                    minDistance: 4, maxDistance: 4, attempts: 48)),
        ]);
        var sets = new StructureSetRegistry([set]);
        sets.ValidateStructures(structures);
        var generator = FlatStructureGenerator(
            blocks, structures, "asteria:stone", sets);
        var height = generator.SurfaceHeight(8, 8);
        var world = new VoxelWorld();
        var chunk = VoxelCoordinates.FromWorld(8, height, 8).Chunk;
        world.InsertChunk(chunk, generator.Materialize(chunk));
        var runtime = ManualRuntime(generator, world, blocks, structures, sets);
        var bounds = new WorldAabb(
            new System.Numerics.Vector3(19, height, 19),
            new System.Numerics.Vector3(20, height + 2, 20));

        Assert.Equal(ManualStructurePlacementResult.Placed,
            runtime.TryPlace(set.Id, null, 8, 8, bounds));
        var rootCount = 0;
        var childCount = 0;
        for (var x = 0; x < Chunk.Size; x++)
        for (var z = 0; z < Chunk.Size; z++)
        {
            var block = world.GetCellOrEmpty(
                new WorldVoxelCoord(x, height, z)).Block;
            rootCount += block == blocks.GetId("asteria:root_marker") ? 1 : 0;
            childCount += block == blocks.GetId("asteria:child_marker") ? 1 : 0;
        }
        Assert.Equal(1, rootCount);
        Assert.Equal(1, childCount);
    }

    [Fact]
    public void FailedMultiPieceSetPlanningNeverChangesWorld()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:root_marker"),
            new BlockDefinition("asteria:child_marker"),
        ]);
        var root = ManualMarker("asteria:root", "asteria:root_marker");
        var child = ManualMarker("asteria:child", "asteria:child_marker");
        var structures = new StructureRegistry([root, child]);
        var set = new StructureSetDefinition("asteria:test_set",
        [
            new StructureSetElementDefinition("root", root.Id, required: true),
            new StructureSetElementDefinition("child", child.Id, required: true,
                placement: new StructureSetElementPlacementDefinition(
                    minDistance: 32, maxDistance: 32, attempts: 48)),
        ]);
        var sets = new StructureSetRegistry([set]);
        var generator = FlatStructureGenerator(
            blocks, structures, "asteria:stone", sets);
        var height = generator.SurfaceHeight(16, 16);
        var world = new VoxelWorld();
        var chunk = VoxelCoordinates.FromWorld(16, height, 16).Chunk;
        world.InsertChunk(chunk, generator.Materialize(chunk));
        var revision = world.Revision;
        var original = world.GetCellOrEmpty(new WorldVoxelCoord(16, height, 16));
        var runtime = ManualRuntime(generator, world, blocks, structures, sets);
        var bounds = new WorldAabb(
            new System.Numerics.Vector3(18, height, 18),
            new System.Numerics.Vector3(19, height + 2, 19));

        Assert.NotEqual(ManualStructurePlacementResult.Placed,
            runtime.TryPlace(set.Id, null, 16, 16, bounds));
        Assert.Equal(revision, world.Revision);
        Assert.Equal(original,
            world.GetCellOrEmpty(new WorldVoxelCoord(16, height, 16)));
    }

    [Fact]
    public void ManualConnectorUsesAuthoredExpansionAndPlacesChild()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:root_marker"),
            new BlockDefinition("asteria:child_marker"),
        ]);
        var root = new StructureDefinition("asteria:root",
            rotation: false, anchor: default,
            voxels:
            [
                new StructureVoxelDefinition(0, 0, 0,
                    "asteria:root_marker", BlockOrientation.Y),
            ],
            connectors:
            [
                new StructureConnectorDefinition(0, 0, 0,
                    StructureConnectorFace.Right, target: "asteria:child",
                    minDistance: 1, maxDistance: 1),
            ]);
        var child = new StructureDefinition("asteria:child",
            rotation: false, anchor: default,
            voxels:
            [
                new StructureVoxelDefinition(0, 0, 0,
                    "asteria:child_marker", BlockOrientation.Y),
            ],
            connectors:
            [
                new StructureConnectorDefinition(0, 0, 0,
                    StructureConnectorFace.Left),
            ]);
        var structures = new StructureRegistry([root, child]);
        var generator = FlatStructureGenerator(
            blocks, structures, "asteria:stone");
        var height = generator.SurfaceHeight(8, 8);
        var world = new VoxelWorld();
        var chunk = VoxelCoordinates.FromWorld(8, height, 8).Chunk;
        world.InsertChunk(chunk, generator.Materialize(chunk));
        var runtime = ManualRuntime(
            generator, world, blocks, structures, StructureSetRegistry.Empty);
        var bounds = new WorldAabb(
            new System.Numerics.Vector3(16, height, 16),
            new System.Numerics.Vector3(17, height + 2, 17));

        Assert.Equal(ManualStructurePlacementResult.Placed,
            runtime.TryPlace(root.Id, null, 8, 8, bounds));
        Assert.Equal(blocks.GetId("asteria:root_marker"),
            world.GetCellOrEmpty(new WorldVoxelCoord(8, height, 8)).Block);
        Assert.Equal(blocks.GetId("asteria:child_marker"),
            world.GetCellOrEmpty(new WorldVoxelCoord(9, height, 8)).Block);
    }

    private static StructureDefinition ManualMarker(string id, string block) =>
        new(id, rotation: false, anchor: default,
            voxels: [new StructureVoxelDefinition(
                0, 0, 0, block, BlockOrientation.Y)]);

    private static ManualStructurePlacementRuntime ManualRuntime(
        BiomeWorldGenerator generator, VoxelWorld world,
        BlockRegistry blocks, StructureRegistry structures,
        StructureSetRegistry sets,
        ManualStructurePlacementLedger? journal = null) =>
        new(generator, world,
            new VoxelMutationRuntime(
                world, new WorldUpdateQueue(), new FluidUpdateQueue(),
                new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
                new MeshletContentRevisions(), new MeshletContentRevisions()),
            blocks, structures, sets,
            journal ?? new ManualStructurePlacementLedger());

    [Theory]
    [InlineData(0, 0)]
    [InlineData(31, 17)]
    [InlineData(-13, -20)]
    [InlineData(47, -33)]
    public void NearestStructureMatchesExhaustivePlacementQuery(
        int originX,
        int originZ)
    {
        const int radius = 48;
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:marker"),
        ]);
        var structure = new StructureDefinition(
            "asteria:test_marker",
            rotation: false,
            anchor: default,
            voxels:
            [
                new StructureVoxelDefinition(
                    0, 0, 0, "asteria:marker", BlockOrientation.Y),
            ]);
        var generator = FlatStructureGenerator(
            blocks,
            new StructureRegistry([structure]),
            "asteria:stone",
            structure.Id);

        var expected = generator.SurfaceStructuresIntersecting(
                originX - radius,
                originZ - radius,
                radius * 2 + 1,
                radius * 2 + 1)
            .Where(placement =>
            {
                var dx = (long)placement.PlacementAnchorX - originX;
                var dz = (long)placement.PlacementAnchorZ - originZ;
                return dx * dx + dz * dz <= (long)radius * radius;
            })
            .OrderBy(placement =>
            {
                var dx = (long)placement.PlacementAnchorX - originX;
                var dz = (long)placement.PlacementAnchorZ - originZ;
                return dx * dx + dz * dz;
            })
            .ThenBy(placement => placement.PlacementAnchorX)
            .ThenBy(placement => placement.PlacementAnchorZ)
            .FirstOrDefault();

        var nearest = generator.FindNearestSurfaceStructure(
            structure.Id,
            originX,
            originZ,
            radius);

        if (expected == default)
        {
            Assert.Null(nearest);
        }
        else
        {
            Assert.True(nearest.HasValue);
            Assert.Equal(
                expected.PlacementAnchorX,
                nearest.Value.PlacementAnchorX);
            Assert.Equal(
                expected.PlacementAnchorZ,
                nearest.Value.PlacementAnchorZ);
        }
    }

    [Fact]
    public void StructureRotationRotatesOffsetsAndHorizontalBlockOrientation()
    {
        Assert.Equal(
            (
                -3,
                2,
                1),
            StructureDefinition.RotateOffset(
                StructureRotation.Degrees90,
                1,
                2,
                3));
        Assert.Equal(
            BlockOrientation.Z,
            StructureDefinition.RotateOrientation(
                StructureRotation.Degrees90,
                BlockOrientation.X));
        Assert.Equal(
            BlockOrientation.Y,
            StructureDefinition.RotateOrientation(
                StructureRotation.Degrees270,
                BlockOrientation.Y));
    } 
    private static BiomeWorldGenerator FluidProximityGenerator(
        string targetFluid,
        out BlockRegistry blocks)
    {
        blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
                new BlockDefinition(
                    "asteria:marker"),
            ]);
        var fluids =
            new FluidRegistry(
            [
                new FluidDefinition(
                    "asteria:water",
                    new FluidColor(
                        0,
                        96,
                        192),
                    0.7f),
                new FluidDefinition(
                    "asteria:lava",
                    new FluidColor(
                        255,
                        96,
                        0),
                    0.9f),
            ]);
        var biome =
            new BiomeDefinition(
                "asteria:test/ocean",
                new BiomeSurfaceLayoutDefinition(),
                new BiomeTerrainDefinition(
                    -4f,
                    0f,
                    64,
                    0f,
                    32),
                [
                    new BiomeSurfaceLayerDefinition(
                        "asteria:stone"),
                ]);
        var structure =
            new StructureDefinition(
                "asteria:test_marker",
                rotation: false,
                anchor: default,
                voxels:
                [
                    new StructureVoxelDefinition(
                        0,
                        0,
                        0,
                        "asteria:marker",
                        BlockOrientation.Y),
                ],
                restrictions:
                    new StructureRestrictionsDefinition(
                        maxSlope: 0,
                        requiresDryGround: false,
                        requiredBiomeCoverage: 1f,
                        proximity:
                        [
                            new StructureProximityRestrictionDefinition(
                                new StructureProximityTargetDefinition(
                                    fluid:
                                        targetFluid),
                                StructureProximityMode.Required,
                                maxDistance: 2,
                                minDistance: 1),
                        ]));
        var structures =
            new StructureRegistry(
            [
                structure,
            ]);
        var dimension =
            new DimensionDefinition(
                new DimensionId(
                    "asteria:test"),
                [
                    biome.Id,
                ],
                32,
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
                    0f),
                generatedOcean:
                    new DimensionGeneratedOceanDefinition(
                        biome.Id,
                        "asteria:water"),
                generatedSurfaceStructures:
                [
                    new DimensionGeneratedSurfaceStructureDefinition(
                        biome.Id,
                        structure.Id,
                        spacing: 16,
                        chance: 1f,
                        jitter: 0),
                ]);

        return new BiomeWorldGenerator(
            91UL,
            dimension,
            blocks,
            fluids,
            new BiomeRegistry(
            [
                biome,
            ]),
            structures);
    }

    private static BiomeWorldGenerator FlatStructureGenerator(
        BlockRegistry blocks,
        StructureRegistry structures,
        string surfaceBlock,
        params string[] structureReferences) =>
        FlatStructureGenerator(
            blocks, structures, surfaceBlock,
            StructureSetRegistry.Empty, structureReferences);

    private static BiomeWorldGenerator FlatStructureGenerator(
        BlockRegistry blocks,
        StructureRegistry structures,
        string surfaceBlock,
        StructureSetRegistry structureSets,
        params string[] structureReferences)
    {
        var biome =
            new BiomeDefinition(
                "asteria:test/flat",
                new BiomeSurfaceLayoutDefinition(),
                new BiomeTerrainDefinition(
                    0f,
                    0f,
                    64,
                    0f,
                    32),
                surfaceBlock ==
                    "asteria:grass_block"
                    ?
                    [
                        new BiomeSurfaceLayerDefinition(
                            "asteria:grass_block",
                            1),
                        new BiomeSurfaceLayerDefinition(
                            blocks.TryGetId(
                                    "asteria:dirt",
                                    out _)
                                ? "asteria:dirt"
                                : "asteria:stone",
                            4),
                        new BiomeSurfaceLayerDefinition(
                            "asteria:stone"),
                    ]
                    :
                    [
                        new BiomeSurfaceLayerDefinition(
                            surfaceBlock),
                    ]);
        var dimension =
            new DimensionDefinition(
                new DimensionId(
                    "asteria:test"),
                [
                    biome.Id,
                ],
                32,
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
                    0f),
                generatedSurfaceStructures:
                    structureReferences
                        .Select(reference =>
                            new DimensionGeneratedSurfaceStructureDefinition(
                                biome.Id,
                                reference,
                                spacing: 16,
                                chance: 1f,
                                jitter: 0))
                        .ToArray());

        return new BiomeWorldGenerator(
            77UL,
            dimension,
            blocks,
            FluidRegistry.FromJson(
                ReadJsonDirectory(
                    "fluids")),
            new BiomeRegistry(
            [
                biome,
            ]),
            structures,
            structureSets);
    }

    private static bool ChunkContains(
        Chunk chunk,
        BlockRuntimeId block)
    {
        var found =
            false;

        chunk.VisitBlockCells(
            (_, _, _, candidate) =>
            {
                if (candidate.Block ==
                    block)
                {
                    found =
                        true;
                }
            });

        return found;
    }

    private static IEnumerable<string> ReadJsonDirectory(
        string category)
    {
        var directory =
            Path.Combine(
                AppContext.BaseDirectory,
                "packs",
                "default",
                "data",
                category);

        return Directory
            .EnumerateFiles(
                directory,
                "*.json")
            .OrderBy(
                path => path,
                StringComparer.Ordinal)
            .Select(
                File.ReadAllText);
    }

}

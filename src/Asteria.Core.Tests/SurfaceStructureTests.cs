using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class SurfaceStructureTests
{
    private static BlockRegistry PortalArrivalBlocks(bool includeShell = false)
    {
        var definitions = new List<BlockDefinition>
        {
            new("asteria:stone"),
            new("asteria:glass", tags: [
                "is_portal_frame",
                "accept_dimension:asteria:overworld",
                "accept_dimension:asteria:umbral",
            ]),
            new("asteria:portal",
                shape: BlockShapeDefinition.CenteredLayer(0.10f),
                orientations: [BlockOrientation.X, BlockOrientation.Y, BlockOrientation.Z],
                isCollidable: false, lightDampening: 0,
                mining: new BlockMiningDefinition(unbreakable: true)),
        };
        if (includeShell)
            definitions.Add(new BlockDefinition("asteria:shell_sphere",
                mining: new BlockMiningDefinition(unbreakable: true)));
        return new BlockRegistry(definitions);
    }

    [Fact]
    public void AuthoredPortalArrivalUsesExactYAndCanonicalStructureMutations()
    {
        var blocks = PortalArrivalBlocks();
        var json = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "packs", "default", "data",
            "structures", "platform_portal_arrival.json"));
        var structure = StructureDefinitionJson.Parse(json);
        var registry = PortalArrivalStructures();
        registry.ValidateBlocks(blocks);
        var generator = FlatStructureGenerator(blocks, registry, "asteria:stone");
        var world = new VoxelWorld();
        // Include the upper chunks needed by an underground access stair.
        for (var y = 0; y <= 8; y++)
            world.InsertChunk(new ChunkCoord(0, y, 0), new Chunk());
        // The authored Structure, not the generated surface, determines Y.
        var feet = new WorldVoxelCoord(8, 16, 8);
        Assert.True(world.SetBlockAt(feet, blocks.GetId("asteria:stone"), out _));
        var mutations = new VoxelMutationRuntime(
            world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var ledger = new ManualStructurePlacementLedger();
        var runtime = new ManualStructurePlacementRuntime(
            generator, world, mutations, blocks, registry,
            StructureSetRegistry.Empty, ledger);

        Assert.Equal(ManualStructurePlacementResult.Placed,
            runtime.TryPlaceExact(structure.Id, 8, 16, 8,
                DimensionId.Overworld));
        var portal = new PortalActivationRuntime(world, blocks, mutations);
        Assert.True(portal.TryGetDestinationAt(
            new WorldVoxelCoord(9, 16, 8), out var returnTarget));
        Assert.Equal(DimensionId.Overworld.Value, returnTarget);
        Assert.True(portal.TryGetDestinationAt(
            new WorldVoxelCoord(9, 17, 8), out returnTarget));
        Assert.Equal(DimensionId.Overworld.Value, returnTarget);
        Assert.Equal(BlockOrientation.X,
            world.GetCellOrEmpty(new WorldVoxelCoord(9, 16, 8)).Orientation);
        Assert.Equal(blocks.GetId("asteria:glass"),
            world.GetCellOrEmpty(new WorldVoxelCoord(9, 15, 8)).Block);
        Assert.True(ResidentWarpDestinationQuery.IsExactSafe(
            world, new System.Numerics.Vector3(8.5f, 16, 8.5f)));
        Assert.Equal(1, ledger.Count);
        Assert.Equal(ManualStructurePlacementResult.InvalidPlacement,
            runtime.TryPlaceExact(structure.Id, 8, 16, 8));
        Assert.Equal(blocks.GetId("asteria:stone"),
            world.GetCellOrEmpty(new WorldVoxelCoord(8, 15, 8)).Block);
    }

    [Fact]
    public void ExactPortalArrivalCannotOverwriteSphereShell()
    {
        var blocks = PortalArrivalBlocks(includeShell: true);
        var structure = StructureDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "packs", "default",
                "data", "structures", "platform_portal_arrival.json")));
        var registry = PortalArrivalStructures();
        var generator = FlatStructureGenerator(blocks, registry, "asteria:stone");
        var world = new VoxelWorld();
        world.InsertChunk(new ChunkCoord(0, 0, 0), new Chunk());
        world.InsertChunk(new ChunkCoord(0, 1, 0), new Chunk());
        var feet = new WorldVoxelCoord(8, 16, 8);
        Assert.True(world.SetBlockAt(feet,
            blocks.GetId("asteria:shell_sphere"), out _));
        var before = world.Revision;
        var mutations = new VoxelMutationRuntime(
            world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var ledger = new ManualStructurePlacementLedger();
        var runtime = new ManualStructurePlacementRuntime(
            generator, world, mutations, blocks, registry,
            StructureSetRegistry.Empty, ledger);

        Assert.Equal(ManualStructurePlacementResult.ProtectedVoxel,
            runtime.TryPlaceExact(structure.Id, 8, 16, 8));
        Assert.Equal(before, world.Revision);
        Assert.Equal(0, ledger.Count);
        Assert.Equal(blocks.GetId("asteria:shell_sphere"),
            world.GetCellOrEmpty(feet).Block);
    }

    [Fact]
    public void PortalArrivalRefusesUnloadedStructurePayload()
    {
        var blocks = PortalArrivalBlocks();
        var structure = StructureDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "packs", "default",
                "data", "structures", "platform_portal_arrival.json")));
        var registry = PortalArrivalStructures();
        var generator = FlatStructureGenerator(blocks, registry, "asteria:stone");
        var world = new VoxelWorld();
        var mutations = new VoxelMutationRuntime(
            world, new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var runtime = new ManualStructurePlacementRuntime(
            generator, world, mutations, blocks, registry,
            StructureSetRegistry.Empty, new ManualStructurePlacementLedger());
        Assert.Equal(ManualStructurePlacementResult.NonResident,
            runtime.TryPlaceExact(structure.Id, 8, 16, 8));
        Assert.Equal(0, world.ChunkCount);
    }

    [Fact]
    public void AuthoredSurfaceSpiralStairsRiseContinuouslyAndStopAtSurface()
    {
        var blocks = PortalArrivalBlocks();
        var structures = PortalArrivalStructures();
        structures.ValidateBlocks(blocks);
        var generator = FlatStructureGenerator(blocks, structures, "asteria:stone");
        var ground = generator.SurfaceHeight(8, 8);
        var entryY = ground - 10;
        Assert.True(entryY >= 2);
        Assert.True(generator.TryPrepareExactStructure(
            "asteria:platform_portal_arrival", 8, entryY, 8,
            new ManualStructurePlacementLedger(), out var plan));
        Assert.NotNull(plan);
        var pieces = plan.Pieces;
        Assert.Equal(2, pieces.Count);
        Assert.Equal("asteria:platform_portal_arrival", pieces[0].StructureId);
        Assert.Equal("asteria:stair_segment_surface_spiral", pieces[1].StructureId);
        var steps = pieces[1].Voxels.OrderBy(voxel => voxel.Y).ToArray();
        Assert.Equal(16, steps.Length);
        Assert.Equal(entryY - 1, steps[0].Y);
        for (var index = 1; index < steps.Length; index++)
        {
            Assert.Equal(steps[index - 1].Y + 1, steps[index].Y);
            Assert.Equal(1,
                Math.Abs(steps[index].X - steps[index - 1].X) +
                Math.Abs(steps[index].Z - steps[index - 1].Z));
        }
        Assert.True(steps[^1].Y >= generator.SurfaceHeight(steps[^1].X, steps[^1].Z));
        Assert.True(pieces[^1].MaximumY - 2 >=
            generator.SurfaceHeight(steps[^1].X, steps[^1].Z));
    }

    [Fact]
    public void PortalAccessMustReachSurfaceAtConnectorRatherThanTallUnrelatedVoxel()
    {
        var blocks = PortalArrivalBlocks();
        var directory = Path.Combine(AppContext.BaseDirectory,
            "packs", "default", "data", "structures");
        var root = File.ReadAllText(Path.Combine(
            directory, "platform_portal_arrival.json"));
        // This distant top block raises MaximumY, but does not raise the
        // walkable exit at E. A height-only stop would wrongly omit stairs.
        var raised = root.Replace(
            "\"layers\": [",
            "\"layers\": [{\"y\": 55, \"rows\": [\"S..\", \"...\", \"...\"]},",
            StringComparison.Ordinal);
        Assert.NotEqual(root, raised);
        var stair = File.ReadAllText(Path.Combine(
            directory, "stair_segment_surface_spiral.json"));
        var structures = StructureRegistry.FromJson([raised, stair]);
        structures.ValidateBlocks(blocks);
        var generator = FlatStructureGenerator(
            blocks, structures, "asteria:stone");
        var entryY = generator.SurfaceHeight(8, 8) - 10;
        Assert.True(generator.TryPrepareExactStructure(
            "asteria:platform_portal_arrival", 8, entryY, 8,
            new ManualStructurePlacementLedger(), out var plan));
        Assert.NotNull(plan);
        Assert.Equal(2, plan.Pieces.Count);
        Assert.Equal("asteria:stair_segment_surface_spiral",
            plan.Pieces[^1].StructureId);
    }

    [Fact]
    public void AboveSurfacePortalDoesNotBuildUnnecessaryStaircase()
    {
        var blocks = PortalArrivalBlocks();
        var structures = PortalArrivalStructures();
        var generator = FlatStructureGenerator(
            blocks, structures, "asteria:stone");
        var entryY = generator.SurfaceHeight(8, 8) + 3;
        Assert.True(generator.TryPrepareExactStructure(
            "asteria:platform_portal_arrival", 8, entryY, 8,
            new ManualStructurePlacementLedger(), out var plan));
        Assert.NotNull(plan);
        Assert.Single(plan.Pieces);
    }

    [Fact]
    public void DeepPortalArrivalExpandsMultipleStairSegmentsToSurface()
    {
        var blocks = PortalArrivalBlocks();
        var structures = PortalArrivalStructures();
        // The default flat fixture is too shallow for a 45-block climb.
        var generator = FlatStructureGenerator(blocks, structures, "asteria:stone",
            StructureSetRegistry.Empty, 48f);
        var entryY = generator.SurfaceHeight(8, 8) - 45;
        Assert.True(entryY > 1);
        Assert.True(generator.TryPrepareExactStructure(
            "asteria:platform_portal_arrival", 8, entryY, 8,
            new ManualStructurePlacementLedger(), out var plan));
        Assert.NotNull(plan);
        var stairs = plan.Pieces.Skip(1).ToArray();
        Assert.True(stairs.Length >= 3);
        Assert.All(stairs, piece => Assert.Equal(
            "asteria:stair_segment_surface_spiral", piece.StructureId));
        for (var i = 1; i < stairs.Length; i++)
            Assert.Equal(stairs[i - 1].AnchorY + 15, stairs[i].AnchorY);
        Assert.True(stairs[^1].MaximumY - 2 >=
            generator.SurfaceHeight(8, 8));
    }

    [Fact]
    public void DeepPortalAndEveryStairAreCommittedAtomicallyAtExactCoordinates()
    {
        var blocks = PortalArrivalBlocks();
        var structures = PortalArrivalStructures();
        structures.ValidateBlocks(blocks);
        var generator = FlatStructureGenerator(
            blocks, structures, "asteria:stone",
            StructureSetRegistry.Empty, 48f);
        var entryY = generator.SurfaceHeight(8, 8) - 45;
        var origin = new DimensionId("asteria:overworld");
        var ledger = new ManualStructurePlacementLedger();
        Assert.True(generator.TryPrepareExactStructure(
            "asteria:platform_portal_arrival", 8, entryY, 8, ledger,
            out var expected));
        Assert.NotNull(expected);
        Assert.True(expected.Pieces.Count >= 4);

        var world = new VoxelWorld();
        // Simulate the existing vertical-access streaming selection:
        // every chunk touched by the authored connector chain is resident.
        var coords = expected.Pieces
            .SelectMany(piece => piece.PayloadPositions())
            .Select(position => VoxelCoordinates.FromWorld(
                position.X, position.Y, position.Z).Chunk)
            .Distinct().ToArray();
        foreach (var coord in coords)
            world.InsertChunk(coord, new Chunk());

        var mutations = new VoxelMutationRuntime(world,
            new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var runtime = new ManualStructurePlacementRuntime(
            generator, world, mutations, blocks, structures,
            StructureSetRegistry.Empty, ledger);
        Assert.Equal(ManualStructurePlacementResult.Placed,
            runtime.TryPlaceExact(
                "asteria:platform_portal_arrival", 8, entryY, 8, origin));

        var portal = new PortalActivationRuntime(world, blocks, mutations);
        Assert.True(portal.TryGetDestinationAt(
            new WorldVoxelCoord(9, entryY, 8), out var destination));
        Assert.Equal(origin.Value, destination);
        Assert.True(ResidentWarpDestinationQuery.IsExactSafe(
            world, new System.Numerics.Vector3(8.5f, entryY, 8.5f)));
        Assert.All(expected.Pieces.Skip(1), piece =>
            Assert.Contains(piece.Voxels, voxel =>
                world.GetCellOrEmpty(new WorldVoxelCoord(
                    voxel.X, voxel.Y, voxel.Z)).Block ==
                    blocks.GetId("asteria:stone")));
        Assert.Equal(1, ledger.Count);
    }

    [Fact]
    public void SurfaceConnectionRejectsUnfinishedAuthoredStaircase()
    {
        var blocks = PortalArrivalBlocks();
        var directory = Path.Combine(AppContext.BaseDirectory,
            "packs", "default", "data", "structures");
        var root = File.ReadAllText(Path.Combine(
            directory, "platform_portal_arrival.json"));
        var stair = File.ReadAllText(Path.Combine(
            directory, "stair_segment_surface_spiral.json"));
        var exhausted = stair.Replace(
            "\"strengthLossOnEachLoop\": 0.05",
            "\"strengthLossOnEachLoop\": 1",
            StringComparison.Ordinal);
        Assert.NotEqual(stair, exhausted);
        var registry = StructureRegistry.FromJson([root, exhausted]);
        registry.ValidateBlocks(blocks);
        var generator = FlatStructureGenerator(blocks, registry, "asteria:stone");
        Assert.True(generator.SurfaceHeight(8, 8) > 20);

        Assert.False(generator.TryPrepareExactStructure(
            "asteria:platform_portal_arrival", 8, 2, 8,
            new ManualStructurePlacementLedger(), out var plan));
        Assert.Null(plan);
    }

    private static StructureRegistry PortalArrivalStructures() =>
        StructureRegistry.FromJson(new[]
        {
            "platform_portal_arrival.json",
            "stair_segment_surface_spiral.json",
        }.Select(name => File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "packs", "default", "data",
            "structures", name))));

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
                Assert.Equal($"textures/blocks/coral_{id["asteria:coral_".Length..]}.png",
                    layer.Texture));
        }

        var variants = structures.ResolveReference("asteria:reef_coral");
        Assert.Equal(2, variants.Count);
        Assert.Equal(new[] { "asteria:reef_coral_01", "asteria:reef_coral_02" },
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
                    rule.Structure == "asteria:reef_coral");
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
        var members = structures.ResolveReference("asteria:rock_ocean");
        Assert.Equal(4, members.Count);
        Assert.Equal(
            new[] {
                "asteria:rock_ocean_01", "asteria:rock_ocean_02",
                "asteria:rock_ocean_03", "asteria:rock_ocean_04",
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
                voxel.Block == "asteria:cobble_stone");
            Assert.All(reef.Voxels, voxel =>
                Assert.Contains(voxel.Block,
                    new[] {
                        "asteria:stone", "asteria:cobble_stone",
                        "asteria:gravel", "asteria:clay",
                    }));
        }

        var dimensions = DimensionRegistry.FromJson(
            ReadJsonDirectory("dimensions"));
        var overworld = dimensions.Get(DimensionId.Overworld);
        var rocks = Assert.Single(overworld.GeneratedSurfaceStructures,
            rule => rule.Biome == "asteria:overworld/ocean" &&
                    rule.Structure == "asteria:rock_ocean");
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

        // The pack intentionally gains structures as biome habitats grow;
        // validate the essential references below rather than freezing
        // the total number of authored templates.
        Assert.True(structures.Count >= 130);
        Assert.Equal(4, structures.ResolveReference("asteria:rock_ocean").Count);
        Assert.Equal(2, structures.ResolveReference("asteria:outcrop_basalt").Count);
        Assert.Equal(2, structures.ResolveReference("asteria:boulder_basalt").Count);
        Assert.Equal(2, structures.ResolveReference("asteria:spire_basalt").Count);
        Assert.Equal(2, structures.ResolveReference("asteria:outcrop_talus").Count);
        Assert.Equal(3, structures.ResolveReference("asteria:bush_oak").Count);
        Assert.Equal(5, structureSets.Count);
        Assert.Equal("asteria:rock_ocean",
            structureSets.Get("asteria:cluster_rock_ocean").Elements[0].Structure);
        Assert.Equal("asteria:outcrop_basalt",
            structureSets.Get("asteria:cluster_basalt").Elements[0].Structure);
        Assert.Equal(4, structures.ResolveReference("asteria:log_oak_fallen").Count);
        Assert.Equal(3, structures.ResolveReference("asteria:stump_oak").Count);
        Assert.Equal("asteria:boulder_small",
            structureSets.Get("asteria:cluster_rock").Elements[0].Structure);
        Assert.Equal("asteria:bush_oak",
            structureSets.Get("asteria:thicket_oak").Elements[0].Structure);
        Assert.Equal("asteria:tree_oak",
            structureSets.Get("asteria:grove_oak").Elements[0].Structure);
        Assert.Equal(
            10,
            structures.ResolveReference(
                "asteria:segment_river")
                .Count);
        Assert.Equal(
            4,
            structures.ResolveReference(
                "asteria:lake")
                .Count);
        Assert.Equal(
            4,
            structures.ResolveReference(
                "asteria:pond_mountain")
                .Count);
        Assert.Equal(
            4,
            structures.ResolveReference(
                "asteria:waterfall_mountain")
                .Count);
        Assert.Equal(
            4,
            structures.ResolveReference(
                "asteria:lake_river")
                .Count);
        Assert.True(
            structures.ResolvesReference(
                "asteria:mouth_river_ocean"));
        // New habitats add roots without invalidating required originals.
        Assert.True(overworld.GeneratedSurfaceStructures.Count >= 74);
        var plainsRoots = overworld.GeneratedSurfaceStructures
            .Where(root => root.Biome == "asteria:overworld/plains")
            .ToArray();
        Assert.Equal(17, plainsRoots.Length);
        Assert.All(plainsRoots.Where(root => root.Structure != "asteria:lake"),
            root => Assert.NotNull(root.HabitatWeights));
        Assert.Equal(2.5f, Assert.Single(plainsRoots,
            root => root.Structure == "asteria:cluster_rock")
            .HabitatWeights!.For("rocky"));
        Assert.Equal(0f, Assert.Single(plainsRoots,
            root => root.Structure == "asteria:grove_oak")
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

        foreach (var reference in new[] { "asteria:log_oak_fallen", "asteria:stump_oak" })
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
    public void EnchantedForestUsesRetainedTreeFamiliesAndAuthoredHabitats()
    {
        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var structures = StructureRegistry.FromJson(ReadJsonDirectory("structures"));
        var dimensions = DimensionRegistry.FromJson(ReadJsonDirectory("dimensions"));
        var overworld = dimensions.Get(DimensionId.Overworld);
        var roots = overworld.GeneratedSurfaceStructures
            .Where(root =>
                root.Biome == "asteria:overworld/enchanted_forest" &&
                root.Structure.StartsWith("asteria:tree_", StringComparison.Ordinal))
            .ToArray();
        var expectedFamilies = new[]
        {
            "asteria:tree_aurorasil",
            "asteria:tree_dawnlace",
            "asteria:tree_prismara",
            "asteria:tree_solmire",
            "asteria:tree_mycelora"
        };

        Assert.Equal(expectedFamilies.OrderBy(id => id, StringComparer.Ordinal),
            roots.Select(root => root.Structure).OrderBy(id => id, StringComparer.Ordinal));
        foreach (var root in roots)
        {
            Assert.NotNull(root.HabitatWeights);
            var variants = structures.ResolveReference(root.Structure);
            Assert.NotEmpty(variants);
            Assert.All(variants, tree =>
            {
                Assert.True(tree.Rotation);
                Assert.Equal(0, tree.GroundAnchorYOffset);
                Assert.Equal(1f, tree.Restrictions.RequiredBiomeCoverage);
                Assert.True(tree.Restrictions.RequiresDryGround);
                Assert.Equal(StructureReplacePolicy.Terrain, tree.Generation.ReplacePolicy);
                Assert.Equal(StructureFluidPolicy.Forbid, tree.Generation.FluidPolicy);
                Assert.Contains(tree.Voxels, voxel =>
                    blocks.TryGetId(voxel.Block, out var blockId) &&
                    blocks.GetDefinition(blockId).Category == "foliage");
            });
        }
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
                        "asteria:block_grass",
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
                        "asteria:block_grass",
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
                "asteria:block_grass",
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
                    "asteria:block_grass"),
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
                "asteria:block_grass",
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
                    "asteria:block_grass"),
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
                "asteria:block_grass",
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
                    "asteria:block_grass"),
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
                        "asteria:block_grass",
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
        params string[] structureReferences) =>
        FlatStructureGenerator(
            blocks, structures, surfaceBlock, structureSets,
            0f, structureReferences);

    private static BiomeWorldGenerator FlatStructureGenerator(
        BlockRegistry blocks,
        StructureRegistry structures,
        string surfaceBlock,
        StructureSetRegistry structureSets,
        float surfaceHeightOffset,
        params string[] structureReferences)
    {
        var biome =
            new BiomeDefinition(
                "asteria:test/flat",
                new BiomeSurfaceLayoutDefinition(),
                new BiomeTerrainDefinition(
                    surfaceHeightOffset,
                    0f,
                    64,
                    0f,
                    32),
                surfaceBlock ==
                    "asteria:block_grass"
                    ?
                    [
                        new BiomeSurfaceLayerDefinition(
                            "asteria:block_grass",
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

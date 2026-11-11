using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class CavernPaletteStreamingDivergenceTests
{
    private const ulong Seed = 181960897289965UL;
    private const string CavernBiomeId = "asteria:overworld/caverns";

    private static readonly (WorldVoxelCoord Position, string BaselineBlock)[] DivergentVoxels =
    [
        (new WorldVoxelCoord(156, 56, -35), "asteria:stone"),
        (new WorldVoxelCoord(156, 57, -35), "asteria:stone"),
        (new WorldVoxelCoord(156, 58, -35), "asteria:stone"),
        (new WorldVoxelCoord(156, 59, -35), "asteria:stone"),
        (new WorldVoxelCoord(159, 59, -34), "asteria:stone"),
        (new WorldVoxelCoord(156, 60, -35), "asteria:cobble_stone"),
        (new WorldVoxelCoord(159, 60, -34), "asteria:cobble_stone"),
        (new WorldVoxelCoord(156, 61, -35), "asteria:cobble_stone"),
        (new WorldVoxelCoord(156, 61, -34), "asteria:cobble_stone"),
        (new WorldVoxelCoord(159, 61, -34), "asteria:cobble_stone"),
        (new WorldVoxelCoord(156, 61, -33), "asteria:cobble_stone"),
        (new WorldVoxelCoord(159, 61, -33), "asteria:cobble_stone"),
    ];

    [Fact]
    public void StreamingBenchmarkDivergenceIsAuthoredByTheCavernWallPalette()
    {
        var dataRoot = Path.Combine(
            AppContext.BaseDirectory, "packs", "default", "data");
        static IEnumerable<string> Documents(string root, string kind) =>
            Directory.EnumerateFiles(Path.Combine(root, kind), "*.json")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText);
        static IEnumerable<string> OptionalDocuments(string root, string kind)
        {
            var directory = Path.Combine(root, kind);
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*.json")
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .Select(File.ReadAllText)
                : Array.Empty<string>();
        }

        var blocks = BlockRegistry.FromJson(Documents(dataRoot, "blocks"));
        var fluids = FluidRegistry.FromJson(Documents(dataRoot, "fluids"));
        var biomes = new BiomeRegistry(
            Documents(dataRoot, "biomes").Select(BiomeDefinitionJson.Parse));
        var structures = StructureRegistry.FromJson(
            OptionalDocuments(dataRoot, "structures"));
        var structureSets = StructureSetRegistry.FromJson(
            OptionalDocuments(dataRoot, "structure_sets"));
        var dimensions = DimensionRegistry.FromJson(
            Documents(dataRoot, "dimensions"));
        var dimension = dimensions.Get(DimensionId.Overworld);
        var dimensionSeed = DimensionSeed.Derive(Seed, dimension.Id);
        Assert.Equal(14813712133479341624UL, dimensionSeed);

        var generator = new BiomeWorldGenerator(
            dimensionSeed,
            dimension,
            blocks,
            fluids,
            biomes,
            structures,
            structureSets);
        var materials = new BiomeSurfaceMaterialField(
            dimensionSeed,
            dimension.SurfaceBiomes
                .Concat(dimension.VolumeBiomes)
                .Distinct(StringComparer.Ordinal)
                .Select(biomes.Get),
            blocks,
            generator.Terrain);
        var columns = new SurfaceTerrainColumnCache(generator.Terrain);
        var chunkCoord = new ChunkCoord(9, 3, -3);
        var origin = VoxelCoordinates.ChunkOrigin(chunkCoord);
        var sourceColumn = columns.Get(chunkCoord.X, chunkCoord.Z);
        var density = generator.SampleDensityVolume(
            origin.X, origin.Y, origin.Z,
            Chunk.Size, Chunk.Size, Chunk.Size);
        var chunk = generator.Materialize(chunkCoord);
        var world = new VoxelWorld();
        world.InsertChunk(chunkCoord, chunk);

        // The baseline materials are present when this chunk first arrives.
        // The divergence is introduced only when its +Z neighbor becomes
        // resident and the arrival-time face reconciliation runs.
        foreach (var (position, baselineBlock) in DivergentVoxels)
        {
            var (_, local) = VoxelCoordinates.FromWorld(
                position.X, position.Y, position.Z);
            Assert.Equal(
                blocks.GetId(baselineBlock),
                chunk.GetBlock(local.X, local.Y, local.Z));
        }

        var arrivingNeighbor = new ChunkCoord(9, 3, -2);
        world.InsertChunk(
            arrivingNeighbor,
            generator.Materialize(arrivingNeighbor));
        var changedPositions = generator.ReconcileChunkArrival(
            world, arrivingNeighbor).ToHashSet();
        var gravel = blocks.GetId("asteria:gravel");

        var decisions = new List<PaletteDecision>(DivergentVoxels.Length);
        foreach (var (position, baselineBlock) in DivergentVoxels)
        {
            var (_, local) = VoxelCoordinates.FromWorld(
                position.X, position.Y, position.Z);
            var currentBlock = chunk.GetBlock(local.X, local.Y, local.Z);
            Assert.Equal(gravel, currentBlock);
            Assert.Contains(position, changedPositions);

            var linearIndex = local.X +
                Chunk.Size * (local.Z + Chunk.Size * local.Y);
            var faceData = chunk.DirectionalFaceDataAtLinearIndex(linearIndex);
            var decision = FindAppliedPaletteDecision(
                generator,
                blocks,
                materials,
                columns,
                sourceColumn,
                density,
                position,
                faceData,
                dimension.Shell);
            var description = decision is null
                ? $"voxel={position.X},{position.Y},{position.Z}; baseline={baselineBlock}; current=asteria:gravel; no eligible directional palette decision"
                : $"voxel={position.X},{position.Y},{position.Z}; baseline={baselineBlock}; current=asteria:gravel; " +
                  $"owner={decision.OwnerBiome} ({decision.OwnerKind}); face={decision.Face}; depth={decision.Depth}; " +
                  $"air-neighbor={decision.EmptyNeighbor.X},{decision.EmptyNeighbor.Y},{decision.EmptyNeighbor.Z}; " +
                  $"cave-void={decision.IsCaveVoid}; selected={decision.ReplacementBlock}";

            Assert.NotNull(decision);
            Assert.True(decision.OwnerKind == BiomeRegionOwnerKind.Volume, description);
            Assert.Equal(CavernBiomeId, decision.OwnerBiome);
            Assert.Equal(BiomePaletteFace.Walls, decision.Face);
            Assert.True(decision.IsCaveVoid, description);
            Assert.Equal("asteria:gravel", decision.ReplacementBlock);
            decisions.Add(decision);
        }

        Assert.Equal(12, decisions.Count);
        Assert.All(decisions, decision =>
        {
            Assert.Equal(CavernBiomeId, decision.OwnerBiome);
            Assert.Equal(BiomePaletteFace.Walls, decision.Face);
            Assert.Equal("asteria:gravel", decision.ReplacementBlock);
        });
    }

    private static PaletteDecision? FindAppliedPaletteDecision(
        BiomeWorldGenerator generator,
        BlockRegistry blocks,
        BiomeSurfaceMaterialField materials,
        SurfaceTerrainColumnCache columns,
        SurfaceTerrainColumn sourceColumn,
        TerrainDensityVolume density,
        WorldVoxelCoord position,
        DirectionalFaceVoxelData data,
        DimensionShellDefinition? shell)
    {
        if (!data.IsTerrainVoxel || !data.HasCandidates)
            return null;

        var (_, sourceLocal) = VoxelCoordinates.FromWorld(
            position.X, position.Y, position.Z);
        var baseY = sourceColumn.BaseHeightAt(
            sourceLocal.X, sourceLocal.Z);
        var volumeBiome = position.Y > baseY
            ? density.VolumeBiomeAt(
                sourceLocal.X, position.Y, sourceLocal.Z)
            : null;
        var surfaceOwner = new BiomeRegionOwner(
            sourceColumn.BiomeAt(sourceLocal.X, sourceLocal.Z),
            BiomeRegionOwnerKind.Surface);
        var solidOwner = volumeBiome is null
            ? surfaceOwner
            : new BiomeRegionOwner(volumeBiome, BiomeRegionOwnerKind.Volume);
        var solidPalette = materials.DirectionalPaletteFor(solidOwner);

        var maximumDepth = checked((int)materials.MaximumDirectionalPaintDepth);
        if (maximumDepth == 0)
            return null;
        var up = data.Up;
        var negativeX = data.NegativeX;
        var positiveX = data.PositiveX;
        var negativeZ = data.NegativeZ;
        var positiveZ = data.PositiveZ;
        var down = data.Down;
        var depthMask = maximumDepth == 64
            ? ulong.MaxValue
            : (1UL << maximumDepth) - 1UL;

        static int FirstCandidateDistance(ulong candidates, ulong validDepthMask)
        {
            candidates &= validDepthMask;
            return candidates == 0
                ? int.MaxValue
                : System.Numerics.BitOperations.TrailingZeroCount(candidates) + 1;
        }

        while (true)
        {
            var nearestDistance = int.MaxValue;
            var direction = -1;

            // Match the production strict-comparison tie order:
            // Up, -X, +X, -Z, +Z, Down.
            var candidateDistance = FirstCandidateDistance(up, depthMask);
            if (candidateDistance < nearestDistance)
            {
                nearestDistance = candidateDistance;
                direction = 0;
            }
            candidateDistance = FirstCandidateDistance(negativeX, depthMask);
            if (candidateDistance < nearestDistance)
            {
                nearestDistance = candidateDistance;
                direction = 1;
            }
            candidateDistance = FirstCandidateDistance(positiveX, depthMask);
            if (candidateDistance < nearestDistance)
            {
                nearestDistance = candidateDistance;
                direction = 2;
            }
            candidateDistance = FirstCandidateDistance(negativeZ, depthMask);
            if (candidateDistance < nearestDistance)
            {
                nearestDistance = candidateDistance;
                direction = 3;
            }
            candidateDistance = FirstCandidateDistance(positiveZ, depthMask);
            if (candidateDistance < nearestDistance)
            {
                nearestDistance = candidateDistance;
                direction = 4;
            }
            candidateDistance = FirstCandidateDistance(down, depthMask);
            if (candidateDistance < nearestDistance)
            {
                nearestDistance = candidateDistance;
                direction = 5;
            }

            if (direction < 0)
                return null;

            var distance = nearestDistance;
            var candidateBit = 1UL << (distance - 1);
            switch (direction)
            {
                case 0: up &= ~candidateBit; break;
                case 1: negativeX &= ~candidateBit; break;
                case 2: positiveX &= ~candidateBit; break;
                case 3: negativeZ &= ~candidateBit; break;
                case 4: positiveZ &= ~candidateBit; break;
                case 5: down &= ~candidateBit; break;
                default: throw new ArgumentOutOfRangeException(nameof(direction));
            }

            var dx = direction switch
            {
                1 => -distance,
                2 => distance,
                _ => 0,
            };
            var dy = direction switch
            {
                0 => distance,
                5 => -distance,
                _ => 0,
            };
            var dz = direction switch
            {
                3 => -distance,
                4 => distance,
                _ => 0,
            };
            var neighborX = checked(position.X + dx);
            var neighborY = checked(position.Y + dy);
            var neighborZ = checked(position.Z + dz);
            if (neighborY < 0)
                continue;

            var face = direction switch
            {
                0 => BiomePaletteFace.Floor,
                1 or 2 or 3 or 4 => BiomePaletteFace.Walls,
                5 => BiomePaletteFace.Ceiling,
                _ => throw new ArgumentOutOfRangeException(nameof(direction)),
            };
            var neighborChunk = VoxelCoordinates.FromWorld(
                neighborX, 0, neighborZ).Chunk;
            var (_, neighborLocal) = VoxelCoordinates.FromWorld(
                neighborX, neighborY, neighborZ);
            var targetColumn = columns.Get(
                neighborChunk.X, neighborChunk.Z);
            var emptyBiome = targetColumn.CaveBiomeAt(
                neighborLocal.X, neighborLocal.Z);
            var emptyPalette = emptyBiome is null
                ? default
                : materials.DirectionalPaletteFor(
                    new BiomeRegionOwner(
                        emptyBiome, BiomeRegionOwnerKind.Volume));
            var targetBaseY = targetColumn.BaseHeightAt(
                neighborLocal.X, neighborLocal.Z);
            var surfaceFluidCutDepth = targetColumn.SurfaceFluidCutDepthAt(
                neighborLocal.X, neighborLocal.Z);
            var shellBoundary = IsSphereShellBoundaryOrLining(
                neighborY, shell);
            var emptyCanOwn =
                emptyPalette.Has(face) &&
                emptyBiome is not null &&
                neighborY > 0 &&
                (long)neighborY <= (long)targetBaseY - surfaceFluidCutDepth;
            if (emptyCanOwn)
                emptyCanOwn = !shellBoundary;

            if (!solidPalette.Has(face) && !emptyCanOwn)
                continue;

            var owner = solidOwner;
            var palette = solidPalette;
            if (emptyCanOwn)
            {
                var resolved = SurfaceTerrainField.ResolveRegionOwner(
                    targetColumn.BiomeAt(neighborLocal.X, neighborLocal.Z),
                    additiveVolumeBiome: null,
                    emptyRegionVolumeBiome: emptyBiome,
                    baseY: targetBaseY,
                    surfaceFluidCutDepth: surfaceFluidCutDepth,
                    worldY: neighborY,
                    isEmpty: true,
                    isSphereShellBoundaryOrLining: shellBoundary);
                if (resolved.Kind == BiomeRegionOwnerKind.Volume)
                {
                    owner = resolved;
                    palette = emptyPalette;
                }
            }

            var depth = checked((uint)distance - 1u);
            if (!palette.Has(face) || depth >= palette.MaxDepth)
                continue;

            var replacement = materials.VolumeBlockAt(
                owner.Biome,
                position.X,
                position.Y,
                position.Z,
                face,
                depth);
            return new PaletteDecision(
                owner.Biome.Primary,
                owner.Kind,
                face,
                depth,
                new WorldVoxelCoord(neighborX, neighborY, neighborZ),
                generator.IsCaveVoidAt(neighborX, neighborY, neighborZ),
                blocks.GetDefinition(replacement).Id);
        }
    }

    private static bool IsSphereShellBoundaryOrLining(
        int worldY,
        DimensionShellDefinition? shell) =>
        (shell?.FloorY is { } floorY &&
         (worldY == floorY || (long)worldY == (long)floorY + 1L)) ||
        (shell?.RoofY is { } roofY &&
         (worldY == roofY || (long)worldY == (long)roofY - 1L));

    private sealed record PaletteDecision(
        string OwnerBiome,
        BiomeRegionOwnerKind OwnerKind,
        BiomePaletteFace Face,
        uint Depth,
        WorldVoxelCoord EmptyNeighbor,
        bool IsCaveVoid,
        string ReplacementBlock);
}

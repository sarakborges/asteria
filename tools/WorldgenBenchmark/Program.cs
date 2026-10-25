using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Asteria.Core.Content;
using Asteria.Core.Diagnostics;
using Asteria.Core.World;

var options = Arguments.Parse(args);
var selection = new PackSelection(options.Pack);
var root = Path.Combine(options.ProjectRoot, "packs", selection.Name, "data");

static IEnumerable<string> Documents(string root, string kind) =>
    Directory.EnumerateFiles(
            Path.Combine(root, kind),
            "*.json")
        .OrderBy(path => path, StringComparer.Ordinal)
        .Select(File.ReadAllText);

static IEnumerable<string> OptionalDocuments(
    string root,
    string kind)
{
    var directory =
        Path.Combine(
            root,
            kind);

    return Directory.Exists(
            directory)
        ? Directory
            .EnumerateFiles(
                directory,
                "*.json")
            .OrderBy(
                path => path,
                StringComparer.Ordinal)
            .Select(
                File.ReadAllText)
        : Array.Empty<string>();
}

var blocks = BlockRegistry.FromJson(Documents(root, "blocks"));
var fluids = FluidRegistry.FromJson(Documents(root, "fluids"));
var biomes = BiomeRegistry.FromJson(Documents(root, "biomes"));
var structures = StructureRegistry.FromJson(OptionalDocuments(root, "structures"));
var structureSets =
    StructureSetRegistry.FromJson(
        OptionalDocuments(
            root,
            "structure_sets"));
var dimensions = DimensionRegistry.FromJson(Documents(root, "dimensions"));
dimensions.ValidateBlocks(blocks);
dimensions.ValidateFluids(fluids);
dimensions.ValidateBiomes(biomes);
dimensions.ValidateStructures(
    structures,
    structureSets);
structureSets.ValidateStructures(
    structures);
biomes.ValidateBlocks(blocks);
structures.ValidateBlocks(blocks);
structures.ValidateFluids(fluids);

var dimension = dimensions.Get(new DimensionId(options.Dimension));
var dimensionSeed = DimensionSeed.Derive(options.Seed, dimension.Id);
BiomeWorldGenerator NewGenerator(
    WorldGenerationOptions? generation = null) => new(
    dimensionSeed,
    dimension,
    blocks,
    fluids,
    biomes,
    structures,
    structureSets,
    generation);

var targetProbeGenerator =
    NewGenerator();
var centerBiome =
    targetProbeGenerator.Biomes.Sample(
        options.CenterX,
        options.CenterZ)
    .Primary;
var biomeSearchTarget =
    dimension.SurfaceBiomes
        .OrderBy(
            id => id,
            StringComparer.Ordinal)
        .FirstOrDefault(id =>
            !string.Equals(
                id,
                centerBiome,
                StringComparison.Ordinal)) ??
    centerBiome;
var structureSearchTarget =
    dimension.GeneratedSurfaceStructures
        .Select(definition =>
            definition.Structure)
        .OrderBy(
            reference => reference,
            StringComparer.Ordinal)
        .FirstOrDefault();
var farX =
    OffsetWorldAxis(
        options.CenterX,
        1_000_000);
var farZ =
    OffsetWorldAxis(
        options.CenterZ,
        -1_000_000);
var searchRadius =
    options.SearchRadius;

var metrics = new SortedDictionary<string, Measurement>(
    StringComparer.Ordinal);

var initialAreaDiagnostics = new
{
    SpawnX = 0,
    SpawnY = 0,
    SpawnZ = 0,
    CenterChunkX = 0,
    CenterChunkY = 0,
    CenterChunkZ = 0,
    DesiredChunkCount = 0,
    SurfaceColumnCount = 0,
    ChunksByY = new SortedDictionary<int, int>(),
};

object? coldMaterializationDiagnostics = null;
object? warmMaterializationDiagnostics = null;
var initialAreaInvocation = 0;

metrics.Add("biomeScalar", Measure(
    options.Samples,
    generator =>
    {
        ulong digest = 14695981039346656037UL;
        for (var i = 0; i < options.Samples; i++)
        {
            var (x, z) = Position(options, i);
            digest = HashText(digest, generator.Biomes.Sample(x, z).Primary);
        }

        return digest;
    }));

metrics.Add("biomeArea", Measure(
    1,
    generator =>
    {
        var sample = generator.Biomes.SampleGrid(
            options.CenterX,
            options.CenterZ,
            options.AreaSize,
            options.AreaSize);
        ulong digest = 14695981039346656037UL;
        for (var z = 0; z < sample.Depth; z++)
        {
            for (var x = 0; x < sample.Width; x++)
            {
                digest = HashText(digest, sample[x, z].Primary);
            }
        }

        return digest;
    }));

metrics.Add("biomeMap", Measure(
    checked(
        options.MapSize *
        options.MapSize),
    generator =>
    {
        var raster =
            BiomeMapDiagnostic.Render(
                generator.Biomes,
                options.CenterX,
                options.CenterZ,
                options.MapSize,
                options.MapSize,
                options.MapStep,
                BiomeMapMode.Influences);
        ulong digest =
            14695981039346656037UL;

        foreach (var pixel in
                 raster.Pixels)
        {
            digest =
                HashNumber(
                    digest,
                    pixel.Red);
            digest =
                HashNumber(
                    digest,
                    pixel.Green);
            digest =
                HashNumber(
                    digest,
                    pixel.Blue);
        }

        return digest;
    }));

metrics.Add("surfaceScalar", Measure(
    options.Samples,
    generator =>
    {
        ulong digest = 14695981039346656037UL;
        for (var i = 0; i < options.Samples; i++)
        {
            var (x, z) = Position(options, i);
            digest = HashNumber(
                digest,
                unchecked((ulong)generator.SurfaceHeight(x, z)));
        }

        return digest;
    }));

metrics.Add("surfaceArea", Measure(
    options.Chunks,
    generator =>
    {
        ulong digest = 14695981039346656037UL;
        for (var i = 0; i < options.Chunks; i++)
        {
            var range = generator.GetSurfaceRange(
                options.CenterX / Chunk.Size + i,
                options.CenterZ / Chunk.Size);
            digest = HashNumber(
                digest,
                unchecked((ulong)range.MinimumWorldY));
            digest = HashNumber(
                digest,
                unchecked((ulong)range.MaximumWorldY));
        }

        return digest;
    }));

metrics.Add("densityVolume", Measure(
    4 * 8 * 4,
    generator =>
    {
        var surfaceY = generator.SurfaceHeight(
            options.CenterX,
            options.CenterZ);
        var volume = generator.SampleDensityVolume(
            options.CenterX,
            Math.Max(0, surfaceY - 24),
            options.CenterZ,
            width: 4,
            height: 8,
            depth: 4);
        ulong digest = 14695981039346656037UL;
        for (var z = 0; z < volume.Depth; z++)
        {
            for (var y = 0; y < volume.Height; y++)
            {
                for (var x = 0; x < volume.Width; x++)
                {
                    digest = HashNumber(
                        digest,
                        unchecked((ulong)BitConverter.DoubleToInt64Bits(
                            volume.DensityAt(x, y, z))));
                }
            }
        }

        return digest;
    }));

metrics.Add("biomeSearchNear", Measure(
    1,
    generator =>
        HashBiomeSearch(
            generator.FindNearestSurfaceBiome(
                biomeSearchTarget,
                options.CenterX,
                options.CenterZ,
                searchRadius))));

metrics.Add("biomeSearchFar", Measure(
    1,
    generator =>
        HashBiomeSearch(
            generator.FindNearestSurfaceBiome(
                biomeSearchTarget,
                farX,
                farZ,
                searchRadius))));

metrics.Add("destinationNear", Measure(
    1,
    generator =>
        HashDestination(
            generator.FindGeneratedSurfaceDestination(
                options.CenterX,
                options.CenterZ,
                maxRadius: 64))));

metrics.Add("destinationFar", Measure(
    1,
    generator =>
        HashDestination(
            generator.FindGeneratedSurfaceDestination(
                farX,
                farZ,
                maxRadius: 64))));

if (structureSearchTarget is not null)
{
    metrics.Add("structureSearchNear", Measure(
        1,
        generator =>
            HashStructureSearch(
                generator.FindNearestSurfaceStructure(
                    structureSearchTarget,
                    options.CenterX,
                    options.CenterZ,
                    searchRadius))));

    metrics.Add("structureSearchFar", Measure(
        1,
        generator =>
            HashStructureSearch(
                generator.FindNearestSurfaceStructure(
                    structureSearchTarget,
                    farX,
                    farZ,
                    searchRadius))));
}

metrics.Add("initialSpawnSearch", Measure(
    1,
    generator =>
    {
        var spawn =
            generator.FindGeneratedSurfaceDestination(
                dimension.Spawn.X,
                dimension.Spawn.Z,
                maxRadius: 64);
        return HashDestination(spawn);
    }));

metrics.Add("initialDesiredChunks", Measure(
    1,
    generator =>
    {
        var spawn =
            generator.FindGeneratedSurfaceDestination(
                dimension.Spawn.X,
                dimension.Spawn.Z,
                maxRadius: 64) ??
            throw new InvalidOperationException(
                "Benchmark dimension has no safe generated spawn within 64 blocks.");
        var center =
            VoxelCoordinates.FromWorld(
                    spawn.X,
                    spawn.Y,
                    spawn.Z)
                .Chunk;
        var desired =
            ChunkStreamingSelection.DesiredSurfaceChunks(
                center,
                horizontalRadius: 2,
                generator);
        var chunksByY = new SortedDictionary<int, int>();
        foreach (var coord in desired)
        {
            chunksByY[coord.Y] = chunksByY.GetValueOrDefault(coord.Y) + 1;
        }

        initialAreaDiagnostics = new
        {
            SpawnX = spawn.X,
            SpawnY = spawn.Y,
            SpawnZ = spawn.Z,
            CenterChunkX = center.X,
            CenterChunkY = center.Y,
            CenterChunkZ = center.Z,
            DesiredChunkCount = desired.Count,
            SurfaceColumnCount = desired.Select(coord => (coord.X, coord.Z)).Distinct().Count(),
            ChunksByY = chunksByY,
        };

        var digest = 14695981039346656037UL;
        foreach (var coord in desired
                     .OrderBy(coord => coord.Z)
                     .ThenBy(coord => coord.X)
                     .ThenBy(coord => coord.Y))
        {
            digest = HashNumber(digest, unchecked((ulong)coord.X));
            digest = HashNumber(digest, unchecked((ulong)coord.Y));
            digest = HashNumber(digest, unchecked((ulong)coord.Z));
        }

        return HashNumber(digest, (ulong)desired.Count);
    }));

metrics.Add("chunkSynthesis", Measure(
    options.Chunks,
    generator =>
    {
        ulong digest = 14695981039346656037UL;
        var chunkX = Math.DivRem(
            options.CenterX,
            Chunk.Size,
            out var remainderX);
        var chunkZ = Math.DivRem(
            options.CenterZ,
            Chunk.Size,
            out var remainderZ);
        if (remainderX < 0) chunkX--;
        if (remainderZ < 0) chunkZ--;

        for (var i = 0; i < options.Chunks; i++)
        {
            var x = checked(chunkX + i);
            var range = generator.GetSurfaceRange(x, chunkZ);
            var topY = Math.Max(0, range.MaximumWorldY);
            var chunkY = topY / Chunk.Size;
            var chunk = generator.Materialize(
                new ChunkCoord(x, chunkY, chunkZ));
            chunk.VisitBlockCells(
                (localX, localY, localZ, cell) =>
                {
                    digest = HashNumber(digest, (ulong)localX);
                    digest = HashNumber(digest, (ulong)localY);
                    digest = HashNumber(digest, (ulong)localZ);
                    digest = HashNumber(digest, cell.Block.Value);
                });
        }

        return digest;
    }));

metrics.Add("initialAreaSynthesis", Measure(
    1,
    generator =>
    {
        var invocation = ++initialAreaInvocation;
        var spawn =
            generator.FindGeneratedSurfaceDestination(
                dimension.Spawn.X,
                dimension.Spawn.Z,
                maxRadius: 64) ??
            throw new InvalidOperationException(
                "Benchmark dimension has no safe generated spawn within 64 blocks.");
        var center =
            VoxelCoordinates.FromWorld(
                    spawn.X,
                    spawn.Y,
                    spawn.Z)
                .Chunk;
        var desired =
            ChunkStreamingSelection
                .DesiredSurfaceChunks(
                    center,
                    horizontalRadius: 2,
                    generator)
                .OrderBy(
                    coord =>
                        coord.Z)
                .ThenBy(
                    coord =>
                        coord.X)
                .ThenBy(
                    coord =>
                        coord.Y)
                .ToArray();
        ulong digest =
            14695981039346656037UL;
        var materializeMsByY = new SortedDictionary<int, double>();
        var materializeBytesByY = new SortedDictionary<int, long>();
        var chunkCountByY = new SortedDictionary<int, int>();
        var totalMaterializeMs = 0d;
        var totalMaterializeBytes = 0L;

        foreach (var coord in
                 desired)
        {
            var beforeChunkBytes = GC.GetAllocatedBytesForCurrentThread();
            var chunkTimer = Stopwatch.StartNew();
            var chunk =
                generator.Materialize(
                    coord);
            chunkTimer.Stop();
            var chunkMs = chunkTimer.Elapsed.TotalMilliseconds;
            var chunkBytes = checked(
                GC.GetAllocatedBytesForCurrentThread() - beforeChunkBytes);
            totalMaterializeMs += chunkMs;
            totalMaterializeBytes += chunkBytes;
            materializeMsByY[coord.Y] =
                materializeMsByY.GetValueOrDefault(coord.Y) + chunkMs;
            materializeBytesByY[coord.Y] =
                materializeBytesByY.GetValueOrDefault(coord.Y) + chunkBytes;
            chunkCountByY[coord.Y] =
                chunkCountByY.GetValueOrDefault(coord.Y) + 1;

            digest =
                HashNumber(
                    digest,
                    unchecked(
                        (ulong)coord.X));
            digest =
                HashNumber(
                    digest,
                    unchecked(
                        (ulong)coord.Y));
            digest =
                HashNumber(
                    digest,
                    unchecked(
                        (ulong)coord.Z));
            chunk.VisitBlockCells(
                (localX, localY, localZ, cell) =>
                {
                    digest =
                        HashNumber(
                            digest,
                            (ulong)localX);
                    digest =
                        HashNumber(
                            digest,
                            (ulong)localY);
                    digest =
                        HashNumber(
                            digest,
                            (ulong)localZ);
                    digest =
                        HashNumber(
                            digest,
                            cell.Block.Value);
                });
        }

        var materializationByY = new SortedDictionary<int, object>();
        foreach (var pair in chunkCountByY)
        {
            var y = pair.Key;
            materializationByY[y] = new
            {
                ChunkCount = pair.Value,
                MaterializeMs = materializeMsByY[y],
                AllocatedBytes = materializeBytesByY[y],
            };
        }

        var materializationSnapshot = new
        {
            ChunkCount = desired.Length,
            TotalMaterializeMs = totalMaterializeMs,
            TotalAllocatedBytes = totalMaterializeBytes,
            ByY = materializationByY,
        };
        if (invocation == 1)
            coldMaterializationDiagnostics = materializationSnapshot;
        else
            warmMaterializationDiagnostics = materializationSnapshot;

        digest =
            HashNumber(
                digest,
                (ulong)desired.Length);
        return digest;
    }));

if (options.EnforceCiBudgets)
{
    EnforceCiBudgets(
        options,
        metrics);
}

var result = new
{
    options.Seed,
    Dimension = options.Dimension,
    DimensionSeed = dimensionSeed,
    Center = new[] { options.CenterX, options.CenterZ },
    options.Samples,
    options.AreaSize,
    options.MapSize,
    options.MapStep,
    options.Chunks,
    options.SearchRadius,
    Metrics = metrics,
    InitialAreaDiagnostics = initialAreaDiagnostics,
    InitialAreaMaterializationDiagnostics = new
    {
        Cold = coldMaterializationDiagnostics,
        Warm = warmMaterializationDiagnostics,
    },
    Notes = new[]
    {
        "Cold and warm passes use identical coordinates and independent generator instances per metric.",
        "Elapsed times include immutable query/cache cost, not Godot meshing, physics or GPU publication.",
        "Allocation counters use the current benchmark thread and are comparative diagnostics, not retained-memory truth.",
        "Every metric records its generated-result digest so determinism can be compared across runs.",
        "Search/destination metrics include near and far world coordinates without chunk materialization.",
        options.EnforceCiBudgets
            ? "CI regression budgets are enforced only for the canonical fixed-seed fixture and include deliberate headroom over the measured baseline."
            : "No time thresholds are enforced unless --enforce-ci-budgets true is supplied.",
    },
};

var json = JsonSerializer.Serialize(
    result,
    new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    });
Console.WriteLine(json);
if (options.Output is { } output)
{
    File.WriteAllText(output, json);
}

if (options.MaterialMapOutput is { } materialMapOutput)
{
    RenderGeologyMaterialMap(
        NewGenerator(new WorldGenerationOptions(
            spawnStructures: false,
            spawnCaves: false)),
        blocks,
        options,
        materialMapOutput);
}

Measurement Measure(
    int operations,
    Func<BiomeWorldGenerator, ulong> work)
{
    var generator = NewGenerator();
    var beforeCold =
        GC.GetAllocatedBytesForCurrentThread();
    var timer =
        Stopwatch.StartNew();
    var coldDigest =
        work(
            generator);
    timer.Stop();
    var coldMs =
        timer.Elapsed.TotalMilliseconds;
    var coldAllocated =
        checked(
            GC.GetAllocatedBytesForCurrentThread() -
            beforeCold);

    var beforeWarm =
        GC.GetAllocatedBytesForCurrentThread();
    timer.Restart();
    var warmDigest =
        work(
            generator);
    timer.Stop();
    var warmMs =
        timer.Elapsed.TotalMilliseconds;
    var warmAllocated =
        checked(
            GC.GetAllocatedBytesForCurrentThread() -
            beforeWarm);

    if (coldDigest != warmDigest)
    {
        throw new InvalidOperationException(
            "Worldgen cold and warm queries produced different generated results.");
    }

    return new Measurement(
        operations,
        coldMs,
        warmMs,
        coldAllocated,
        warmAllocated,
        coldDigest);
}

static void RenderGeologyMaterialMap(
    BiomeWorldGenerator generator,
    BlockRegistry blocks,
    Arguments options,
    string outputPath)
{
    const int cellSize = 4;
    const int margin = 16;
    const int headingHeight = 32;
    const int panelGap = 20;
    var size = options.MapSize;
    var panelPixels = checked(size * cellSize);
    var panelTop = margin + headingHeight;
    const int panelCount = 4;
    var panelLefts = Enumerable.Range(0, panelCount)
        .Select(index => margin + index * (panelPixels + panelGap))
        .ToArray();
    var imageWidth = margin * 2 + panelPixels * panelCount + panelGap * (panelCount - 1);
    var imageHeight = panelTop + panelPixels + 76;
    var centerX = options.CenterX;
    var centerZ = options.CenterZ;
    if (options.MaterialMapBiome is { } targetBiome)
    {
        var target = generator.FindNearestSurfaceBiome(
            targetBiome, centerX, centerZ, options.SearchRadius)
            ?? throw new InvalidOperationException(
                $"Could not locate surface biome {targetBiome} within {options.SearchRadius} blocks.");

        // Locate an elevated representative inside the requested biome instead
        // of accidentally centering on a low-relief border cell.
        centerX = target.X;
        centerZ = target.Z;
        var highestSurface = generator.SurfaceHeight(centerX, centerZ);
        const int peakSearchRadius = 256;
        const int peakSearchStep = 8;
        for (var dz = -peakSearchRadius; dz <= peakSearchRadius; dz += peakSearchStep)
        for (var dx = -peakSearchRadius; dx <= peakSearchRadius; dx += peakSearchStep)
        {
            var x = checked(target.X + dx);
            var z = checked(target.Z + dz);
            if (!string.Equals(
                    generator.Biomes.Sample(x, z).Primary,
                    targetBiome,
                    StringComparison.Ordinal))
                continue;

            var height = generator.SurfaceHeight(x, z);
            if (height > highestSurface)
            {
                highestSurface = height;
                centerX = x;
                centerZ = z;
            }
        }
    }

    var centerY = Math.Max(0, generator.SurfaceHeight(centerX, centerZ));
    var subsurfaceY = Math.Max(0, centerY - 32);
    var verticalRadius = Math.Min(64, Math.Max(16, centerY));
    var startX = checked(centerX - ((size - 1) / 2) * options.MapStep);
    var startZ = checked(centerZ - ((size - 1) / 2) * options.MapStep);
    var chunkCache = new Dictionary<ChunkCoord, Chunk>();

    BlockRuntimeId SampleBlock(int x, int y, int z)
    {
        var (coord, local) = VoxelCoordinates.FromWorld(x, Math.Max(0, y), z);
        if (!chunkCache.TryGetValue(coord, out var chunk))
        {
            chunk = generator.Materialize(coord);
            chunkCache.Add(coord, chunk);
        }

        return chunk.GetBlock(local.X, local.Y, local.Z);
    }

    var surface = new BlockRuntimeId[size, size];
    var horizontal = new BlockRuntimeId[size, size];
    var subsurface = new BlockRuntimeId[size, size];
    var vertical = new BlockRuntimeId[size, size];
    for (var row = 0; row < size; row++)
    {
        var z = checked(startZ + row * options.MapStep);
        var worldY = Math.Max(0,
            centerY + verticalRadius -
            (int)Math.Round(row * (2d * verticalRadius) / Math.Max(1, size - 1)));
        for (var column = 0; column < size; column++)
        {
            var x = checked(startX + column * options.MapStep);
            var topY = Math.Max(0, generator.SurfaceHeight(x, z));
            var verticalX = checked(centerX - verticalRadius +
                (int)Math.Round(column * (2d * verticalRadius) /
                    Math.Max(1, size - 1)));
            surface[row, column] = SampleBlock(x, topY, z);
            horizontal[row, column] = SampleBlock(x, centerY, z);
            subsurface[row, column] = SampleBlock(x, subsurfaceY, z);
            vertical[row, column] = SampleBlock(verticalX, worldY, centerZ);
        }
    }

    var legendBlocks = new[]
    {
        "asteria:stone", "asteria:sand", "asteria:gravel", "asteria:clay",
        "asteria:cobble_stone", "asteria:dirt", "asteria:sandstone",
        "asteria:basalt", "asteria:cobble_basalt", "asteria:ice",
        "asteria:snow", "asteria:mud", "asteria:block_grass",
    };
    var svg = new StringBuilder();
    svg.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{imageWidth}\" height=\"{imageHeight}\" viewBox=\"0 0 {imageWidth} {imageHeight}\">");
    svg.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"#10141a\"/>");
    svg.AppendLine("<style>text{font-family:monospace;fill:#e8edf2}.label{font-size:12px}.title{font-size:15px;font-weight:bold}</style>");
    var targetLabel = options.MaterialMapBiome is { } requestedBiome
        ? $" · target {requestedBiome}"
        : string.Empty;
    svg.AppendLine($"<text x=\"{margin}\" y=\"19\" class=\"title\">Asteria geology materialization · seed {options.Seed} · center {centerX},{centerZ}{targetLabel} · surface Y {centerY}</text>");

    AppendPanel(svg, "Exposed surface", surface, panelLefts[0], panelTop);
    AppendPanel(svg, $"Horizontal slice Y={centerY}", horizontal, panelLefts[1], panelTop);
    AppendPanel(svg, $"Subsurface slice Y={subsurfaceY}", subsurface, panelLefts[2], panelTop);
    AppendPanel(svg, $"Local vertical slice X={centerX}±{verticalRadius}, Z={centerZ}", vertical, panelLefts[3], panelTop);

    var legendY = panelTop + panelPixels + 28;
    var legendX = margin;
    foreach (var id in legendBlocks)
    {
        var color = MaterialColor(id);
        svg.AppendLine($"<rect x=\"{legendX}\" y=\"{legendY - 10}\" width=\"10\" height=\"10\" fill=\"{color}\"/>");
        svg.AppendLine($"<text x=\"{legendX + 14}\" y=\"{legendY - 1}\" class=\"label\">{id.Replace("asteria:", "")}</text>");
        legendX += 92;
    }

    svg.AppendLine("</svg>");
    var fullPath = Path.GetFullPath(outputPath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    File.WriteAllText(fullPath, svg.ToString());
    Console.WriteLine($"Geology materialization map: {fullPath} ({chunkCache.Count} generated chunks)");

    void AppendPanel(
        StringBuilder builder, string label, BlockRuntimeId[,] values,
        int left, int top)
    {
        builder.AppendLine($"<text x=\"{left}\" y=\"{top - 7}\" class=\"label\">{label}</text>");
        for (var row = 0; row < size; row++)
        for (var column = 0; column < size; column++)
        {
            var block = values[row, column];
            var color = block.IsAir
                ? "#10141a"
                : MaterialColor(blocks.GetDefinition(block).Id);
            var x = left + column * cellSize;
            var y = top + row * cellSize;
            builder.AppendLine($"<rect x=\"{x}\" y=\"{y}\" width=\"{cellSize}\" height=\"{cellSize}\" fill=\"{color}\"/>");
        }
    }
}

static string MaterialColor(string id) => id switch
{
    "asteria:stone" => "#858b92",
    "asteria:sand" => "#d6bd79",
    "asteria:gravel" => "#9a958a",
    "asteria:clay" => "#b38b72",
    "asteria:cobble_stone" => "#62666d",
    "asteria:dirt" => "#79543a",
    "asteria:sandstone" => "#bd8d58",
    "asteria:basalt" => "#3c4148",
    "asteria:cobble_basalt" => "#66605a",
    "asteria:ice" => "#8dd9e8",
    "asteria:snow" => "#eef2f5",
    "asteria:mud" => "#584939",
    "asteria:block_grass" => "#54884a",
    _ => "#d85bb8",
};

static (int X, int Z) Position(Arguments options, int index) =>
(
    checked(options.CenterX + (index % 8) * 11),
    checked(options.CenterZ - (index / 8) * 13)
);

static void EnforceCiBudgets(
    Arguments options,
    IReadOnlyDictionary<string, Measurement> metrics)
{
    const ulong ciSeed =
        181960897289965UL;

    if (options.Seed != ciSeed ||
        !string.Equals(
            options.Dimension,
            "asteria:overworld",
            StringComparison.Ordinal) ||
        options.CenterX != 0 ||
        options.CenterZ != 0 ||
        options.Samples != 2 ||
        options.AreaSize != 4 ||
        options.MapSize != 64 ||
        options.MapStep != 4 ||
        options.Chunks != 1 ||
        options.SearchRadius != 1024)
    {
        throw new InvalidOperationException(
            "--enforce-ci-budgets requires the canonical fixed-seed CI fixture.");
    }

    var budgets =
        new[]
        {
            new BenchmarkBudget(
                "biomeMap",
                MaximumColdMs: 250d,
                MaximumWarmMs: 150d,
                MaximumColdAllocatedBytes: 64L * 1024L * 1024L,
                MaximumWarmAllocatedBytes: 32L * 1024L * 1024L),
            new BenchmarkBudget(
                "biomeSearchNear",
                MaximumColdMs: 250d,
                MaximumWarmMs: 25d,
                MaximumColdAllocatedBytes: 64L * 1024L * 1024L,
                MaximumWarmAllocatedBytes: 8L * 1024L * 1024L),
            new BenchmarkBudget(
                "biomeSearchFar",
                MaximumColdMs: 250d,
                MaximumWarmMs: 25d,
                MaximumColdAllocatedBytes: 64L * 1024L * 1024L,
                MaximumWarmAllocatedBytes: 8L * 1024L * 1024L),
            new BenchmarkBudget(
                "destinationNear",
                MaximumColdMs: 1000d,
                MaximumWarmMs: 25d,
                MaximumColdAllocatedBytes: 512L * 1024L * 1024L,
                MaximumWarmAllocatedBytes: 16L * 1024L * 1024L),
            new BenchmarkBudget(
                "destinationFar",
                MaximumColdMs: 1000d,
                MaximumWarmMs: 25d,
                MaximumColdAllocatedBytes: 768L * 1024L * 1024L,
                MaximumWarmAllocatedBytes: 16L * 1024L * 1024L),
            new BenchmarkBudget(
                "structureSearchNear",
                MaximumColdMs: 2000d,
                MaximumWarmMs: 50d,
                MaximumColdAllocatedBytes: 1536L * 1024L * 1024L,
                MaximumWarmAllocatedBytes: 32L * 1024L * 1024L),
            new BenchmarkBudget(
                "structureSearchFar",
                MaximumColdMs: 2000d,
                MaximumWarmMs: 50d,
                MaximumColdAllocatedBytes: 1792L * 1024L * 1024L,
                MaximumWarmAllocatedBytes: 32L * 1024L * 1024L),
            new BenchmarkBudget(
                "chunkSynthesis",
                MaximumColdMs: 1000d,
                MaximumWarmMs: 50d,
                MaximumColdAllocatedBytes: 768L * 1024L * 1024L,
                MaximumWarmAllocatedBytes: 32L * 1024L * 1024L),
        };

    foreach (var budget in budgets)
    {
        if (!metrics.TryGetValue(
                budget.Metric,
                out var measurement))
        {
            throw new InvalidOperationException(
                $"Missing benchmark metric {budget.Metric}.");
        }

        var failures =
            new List<string>();

        if (measurement.ColdMs >
            budget.MaximumColdMs)
        {
            failures.Add(
                $"cold {measurement.ColdMs:F3}ms > {budget.MaximumColdMs:F3}ms");
        }

        if (measurement.WarmMs >
            budget.MaximumWarmMs)
        {
            failures.Add(
                $"warm {measurement.WarmMs:F3}ms > {budget.MaximumWarmMs:F3}ms");
        }

        if (measurement.ColdAllocatedBytes >
            budget.MaximumColdAllocatedBytes)
        {
            failures.Add(
                $"cold alloc {measurement.ColdAllocatedBytes} > {budget.MaximumColdAllocatedBytes}");
        }

        if (measurement.WarmAllocatedBytes >
            budget.MaximumWarmAllocatedBytes)
        {
            failures.Add(
                $"warm alloc {measurement.WarmAllocatedBytes} > {budget.MaximumWarmAllocatedBytes}");
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                $"Worldgen benchmark budget failed for {budget.Metric}: {string.Join(", ", failures)}.");
        }
    }
}

static ulong HashBiomeSearch(
    SurfaceBiomeSearchResult? result)
{
    if (result is null)
    {
        return 0UL;
    }

    var digest = 14695981039346656037UL;
    digest = HashNumber(
        digest,
        unchecked((ulong)result.X));
    digest = HashNumber(
        digest,
        unchecked((ulong)result.Z));
    return HashText(
        digest,
        result.Sample.Primary);
}

static ulong HashDestination(
    GeneratedSurfaceDestination? result)
{
    if (result is null)
    {
        return 0UL;
    }

    var digest = 14695981039346656037UL;
    digest = HashNumber(
        digest,
        unchecked((ulong)result.Value.X));
    digest = HashNumber(
        digest,
        unchecked((ulong)result.Value.Y));
    return HashNumber(
        digest,
        unchecked((ulong)result.Value.Z));
}

static ulong HashStructureSearch(
    SurfaceStructureQueryResult? result)
{
    if (result is null)
    {
        return 0UL;
    }

    var digest = 14695981039346656037UL;
    digest = HashText(
        digest,
        result.Value.Reference);
    digest = HashText(
        digest,
        result.Value.StructureId);
    digest = HashNumber(
        digest,
        unchecked((ulong)result.Value.AnchorX));
    digest = HashNumber(
        digest,
        unchecked((ulong)result.Value.AnchorY));
    return HashNumber(
        digest,
        unchecked((ulong)result.Value.AnchorZ));
}

static int OffsetWorldAxis(
    int value,
    int offset)
{
    var result =
        (long)value +
        offset;
    return result switch
    {
        <= int.MinValue =>
            int.MinValue,
        >= int.MaxValue =>
            int.MaxValue,
        _ =>
            (int)result,
    };
}

static ulong HashText(ulong current, string value)
{
    foreach (var character in value)
    {
        current = HashNumber(current, character);
    }

    return current;
}

static ulong HashNumber(ulong current, ulong value) =>
    unchecked((current ^ value) * 1099511628211UL);

internal sealed record Measurement(
    int Operations,
    double ColdMs,
    double WarmMs,
    long ColdAllocatedBytes,
    long WarmAllocatedBytes,
    ulong Digest);

internal sealed record BenchmarkBudget(
    string Metric,
    double MaximumColdMs,
    double MaximumWarmMs,
    long MaximumColdAllocatedBytes,
    long MaximumWarmAllocatedBytes);

internal sealed record Arguments(
    string ProjectRoot,
    string Pack,
    string Dimension,
    ulong Seed,
    int CenterX,
    int CenterZ,
    int Samples,
    int AreaSize,
    int MapSize,
    int MapStep,
    int Chunks,
    int SearchRadius,
    bool EnforceCiBudgets,
    string? Output,
    string? MaterialMapOutput,
    string? MaterialMapBiome)
{
    public static Arguments Parse(string[] input)
    {
        var root = Directory.GetCurrentDirectory();
        var pack = "default";
        var dimension = "asteria:overworld";
        var seed = 181960897289965UL;
        var x = 0;
        var z = 0;
        var samples = 16;
        var areaSize = 16;
        var mapSize = 64;
        var mapStep = 4;
        var chunks = 2;
        var searchRadius = 1024;
        var enforceCiBudgets = false;
        string? output = null;
        string? materialMapOutput = null;
        string? materialMapBiome = null;

        if (input.Length % 2 != 0)
        {
            throw new ArgumentException(
                "CLI flags require values.");
        }

        for (var i = 0; i < input.Length; i += 2)
        {
            var value = input[i + 1];
            switch (input[i])
            {
                case "--project-root":
                    root = value;
                    break;
                case "--pack":
                    pack = value;
                    break;
                case "--dimension":
                    dimension = value;
                    break;
                case "--seed":
                    if (!WorldCreationSeed.TryParse(value, out seed))
                    {
                        throw new ArgumentException("Invalid unsigned 64-bit seed.");
                    }

                    break;
                case "--center-x":
                    x = int.Parse(value);
                    break;
                case "--center-z":
                    z = int.Parse(value);
                    break;
                case "--samples":
                    samples = int.Parse(value);
                    break;
                case "--area-size":
                    areaSize = int.Parse(value);
                    break;
                case "--map-size":
                    mapSize = int.Parse(value);
                    break;
                case "--map-step":
                    mapStep = int.Parse(value);
                    break;
                case "--chunks":
                    chunks = int.Parse(value);
                    break;
                case "--search-radius":
                    searchRadius = int.Parse(value);
                    break;
                case "--enforce-ci-budgets":
                    enforceCiBudgets =
                        bool.Parse(
                            value);
                    break;
                case "--output":
                    output = value;
                    break;
                case "--material-map-output":
                    materialMapOutput = value;
                    break;
                case "--material-map-biome":
                    materialMapBiome = value;
                    break;
                default:
                    throw new ArgumentException($"Unknown option {input[i]}.");
            }
        }

        if (samples is < 1 or > 4096 ||
            areaSize is < 1 or > 128 ||
            mapSize is < 1 or > 512 ||
            mapStep is < 1 or > 4096 ||
            chunks is < 1 or > 16 ||
            searchRadius is < 1 or > 8192)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                "Samples must be 1–4096, area-size 1–128, map-size 1–512, map-step 1–4096, chunks 1–16 and search-radius 1–8192.");
        }

        return new Arguments(
            root,
            pack,
            dimension,
            seed,
            x,
            z,
            samples,
            areaSize,
            mapSize,
            mapStep,
            chunks,
            searchRadius,
            enforceCiBudgets,
            output,
            materialMapOutput,
            materialMapBiome);
    }
}

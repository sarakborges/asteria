using System.Diagnostics;
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
BiomeWorldGenerator NewGenerator() => new(
    dimensionSeed,
    dimension,
    blocks,
    fluids,
    biomes,
    structures,
    structureSets);

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
        var spawn =
            generator.FindGeneratedSurfaceDestination(
                dimension.Spawn.X,
                dimension.Spawn.Z,
                maxRadius: 64) ??
            throw new InvalidOperationException(
                "Benchmark dimension has no safe generated spawn within 64 blocks.");
        var center =
            VoxelCoordinates.FromWorld(
                    spawn.Value.X,
                    spawn.Value.Y,
                    spawn.Value.Z)
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

        foreach (var coord in
                 desired)
        {
            var chunk =
                generator.Materialize(
                    coord);
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
    string? Output)
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
            output);
    }
}

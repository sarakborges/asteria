using System.Diagnostics;
using System.Text.Json;
using Asteria.Core.Content;
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
var dimensions = DimensionRegistry.FromJson(Documents(root, "dimensions"));
dimensions.ValidateBlocks(blocks);
dimensions.ValidateFluids(fluids);
dimensions.ValidateBiomes(biomes);
dimensions.ValidateStructures(structures);
biomes.ValidateBlocks(blocks);
structures.ValidateBlocks(blocks);

var dimension = dimensions.Get(new DimensionId(options.Dimension));
var dimensionSeed = DimensionSeed.Derive(options.Seed, dimension.Id);
BiomeWorldGenerator NewGenerator() => new(
    dimensionSeed,
    dimension,
    blocks,
    fluids,
    biomes,
    structures);

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

var result = new
{
    options.Seed,
    Dimension = options.Dimension,
    DimensionSeed = dimensionSeed,
    Center = new[] { options.CenterX, options.CenterZ },
    options.Samples,
    options.AreaSize,
    options.Chunks,
    Metrics = metrics,
    Notes = new[]
    {
        "Cold and warm passes use identical coordinates and independent generator instances per metric.",
        "Elapsed times include immutable query/cache cost, not Godot meshing, physics or GPU publication.",
        "No time thresholds: compare representative builds on the same machine and configuration.",
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
    var timer = Stopwatch.StartNew();
    var coldDigest = work(generator);
    timer.Stop();
    var coldMs = timer.Elapsed.TotalMilliseconds;

    timer.Restart();
    var warmDigest = work(generator);
    timer.Stop();
    if (coldDigest != warmDigest)
    {
        throw new InvalidOperationException(
            "Worldgen cold and warm queries produced different generated results.");
    }

    return new Measurement(
        operations,
        coldMs,
        timer.Elapsed.TotalMilliseconds);
}

static (int X, int Z) Position(Arguments options, int index) =>
(
    checked(options.CenterX + (index % 8) * 11),
    checked(options.CenterZ - (index / 8) * 13)
);

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
    double WarmMs);

internal sealed record Arguments(
    string ProjectRoot,
    string Pack,
    string Dimension,
    ulong Seed,
    int CenterX,
    int CenterZ,
    int Samples,
    int AreaSize,
    int Chunks,
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
        var chunks = 2;
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
                case "--chunks":
                    chunks = int.Parse(value);
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
            chunks is < 1 or > 16)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                "Samples must be 1–4096, area-size 1–128 and chunks 1–16.");
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
            chunks,
            output);
    }
}

using System.Text.Json;
using Asteria.Core.Content;
using Asteria.Core.World;

var options = Arguments.Parse(args);
var selection = new PackSelection(options.Pack);
var dataRoot = Path.Combine(
    options.ProjectRoot,
    "packs",
    selection.Name,
    "data");

static IEnumerable<string> Documents(string root, string kind) =>
    Directory.EnumerateFiles(
            Path.Combine(root, kind),
            "*.json")
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
var sourceBiomeDefinitions = Documents(dataRoot, "biomes")
    .Select(BiomeDefinitionJson.Parse)
    .ToArray();
var biomes = new BiomeRegistry(sourceBiomeDefinitions);
var structures = StructureRegistry.FromJson(
    OptionalDocuments(dataRoot, "structures"));
var structureSets = StructureSetRegistry.FromJson(
    OptionalDocuments(dataRoot, "structure_sets"));
var dimensions = DimensionRegistry.FromJson(
    Documents(dataRoot, "dimensions"));

dimensions.ValidateBlocks(blocks);
dimensions.ValidateFluids(fluids);
dimensions.ValidateBiomes(biomes);
dimensions.ValidateStructures(structures, structureSets);
structureSets.ValidateStructures(structures);
biomes.ValidateBlocks(blocks);
structures.ValidateBlocks(blocks);
structures.ValidateFluids(fluids);

var dimension = dimensions.Get(new DimensionId(options.Dimension));
var dimensionSeed = DimensionSeed.Derive(options.Seed, dimension.Id);
var generator = new BiomeWorldGenerator(
    dimensionSeed,
    dimension,
    blocks,
    fluids,
    biomes,
    structures,
    structureSets);
var spawn = generator.FindGeneratedSurfaceDestination(
    dimension.Spawn.X,
    dimension.Spawn.Z,
    maxRadius: 64) ??
    throw new InvalidOperationException(
        "Benchmark dimension has no safe generated spawn within 64 blocks.");
var start = VoxelCoordinates.FromWorld(
    spawn.X, spawn.Y, spawn.Z).Chunk;

var streamingRoute = ChunkStreamingRouteBenchmark.Run(
    generator,
    blocks,
    fluids,
    start,
    options.RouteSteps,
    options.Radius);
var result = new
{
    options.Seed,
    Dimension = options.Dimension,
    DimensionSeed = dimensionSeed,
    StartChunk = new[] { start.X, start.Y, start.Z },
    StreamingRouteDiagnostics = streamingRoute,
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
    var fullPath = Path.GetFullPath(output);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    File.WriteAllText(fullPath, json);
}

internal sealed record Arguments(
    string ProjectRoot,
    string Pack,
    string Dimension,
    ulong Seed,
    int RouteSteps,
    int Radius,
    string? Output)
{
    public static Arguments Parse(string[] input)
    {
        var root = Directory.GetCurrentDirectory();
        var pack = "default";
        var dimension = "asteria:overworld";
        var seed = 181960897289965UL;
        var routeSteps = 8;
        var radius = 4;
        string? output = null;
        if (input.Length % 2 != 0)
            throw new ArgumentException("CLI flags require values.");

        for (var index = 0; index < input.Length; index += 2)
        {
            var value = input[index + 1];
            switch (input[index])
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
                        throw new ArgumentException("Invalid unsigned 64-bit seed.");
                    break;
                case "--route-steps":
                    routeSteps = int.Parse(value);
                    break;
                case "--radius":
                    radius = int.Parse(value);
                    break;
                case "--output":
                    output = value;
                    break;
                default:
                    throw new ArgumentException($"Unknown option {input[index]}.");
            }
        }

        if (routeSteps is < 2 or > 64 || radius is < 1 or > 8)
            throw new ArgumentOutOfRangeException(
                nameof(input),
                "Route steps must be 2–64 and radius must be 1–8.");

        return new Arguments(
            root, pack, dimension, seed, routeSteps, radius, output);
    }
}

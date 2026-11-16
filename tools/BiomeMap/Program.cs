using System.Diagnostics;
using System.Security;
using System.Text;
using Asteria.Core.Content;
using Asteria.Core.Diagnostics;
using Asteria.Core.World;

var options =
    Arguments.Parse(
        args);
var selection =
    new PackSelection(
        options.Pack);
var dataRoot =
    Path.Combine(
        options.ProjectRoot,
        "packs",
        selection.Name,
        "data");

static IEnumerable<string> Documents(
    string root,
    string kind) =>
    Directory
        .EnumerateFiles(
            Path.Combine(
                root,
                kind),
            "*.json")
        .OrderBy(
            path => path,
            StringComparer.Ordinal)
        .Select(
            File.ReadAllText);

var biomes =
    BiomeRegistry.FromJson(
        Documents(
            dataRoot,
            "biomes"));
var dimensions =
    DimensionRegistry.FromJson(
        Documents(
            dataRoot,
            "dimensions"));
dimensions.ValidateBiomes(
    biomes);

var dimension =
    dimensions.Get(
        new DimensionId(
            options.Dimension));
var dimensionSeed =
    DimensionSeed.Derive(
        options.Seed,
        dimension.Id);
var field =
    new BiomeField(
        dimensionSeed,
        dimension,
        biomes);
var originX =
    AxisOrigin(
        options.CenterX,
        options.Width,
        options.Step);
var originZ =
    AxisOrigin(
        options.CenterZ,
        options.Depth,
        options.Step);

var timer =
    Stopwatch.StartNew();
var raster =
    BiomeMapDiagnostic.Render(
        field,
        originX,
        originZ,
        options.Width,
        options.Depth,
        options.Step,
        options.Mode);
timer.Stop();

var svg =
    Svg(
        raster,
        options,
        originX,
        originZ,
        dimensionSeed);
var output =
    Path.GetFullPath(
        options.Output,
        options.ProjectRoot);
Directory.CreateDirectory(
    Path.GetDirectoryName(
        output) ??
    options.ProjectRoot);
File.WriteAllText(
    output,
    svg,
    new UTF8Encoding(
        encoderShouldEmitUTF8Identifier:
            false));

Console.WriteLine(
    $"biome-map dimension={dimension.Id} seed={options.Seed} " +
    $"dimension_seed={dimensionSeed} mode={options.Mode} " +
    $"samples={options.Width}x{options.Depth} step={options.Step} " +
    $"elapsed_ms={timer.Elapsed.TotalMilliseconds:F3} output={output}");

static int AxisOrigin(
    int center,
    int count,
    int step)
{
    var span =
        checked(
            (long)(
                count -
                1) *
            step);
    var origin =
        (long)center -
        span /
        2L;

    return origin is <
            int.MinValue or >
            int.MaxValue
        ? throw new ArgumentOutOfRangeException(
            nameof(center),
            "Biome-map extent exceeds world coordinates.")
        : (int)origin;
}

static string Svg(
    BiomeMapRaster raster,
    Arguments options,
    int originX,
    int originZ,
    ulong dimensionSeed)
{
    const int pixelScale = 2;
    const int legendWidth = 230;
    const int legendRowHeight = 12;
    const int legendPadding = 10;

    var mapWidth =
        checked(
            raster.Width *
            pixelScale);
    var mapHeight =
        checked(
            raster.Depth *
            pixelScale);
    var legendHeight =
        checked(
            raster.Legend.Count *
            legendRowHeight +
            legendPadding *
            2 +
            48);
    var height =
        Math.Max(
            mapHeight,
            legendHeight);
    var width =
        checked(
            mapWidth +
            legendWidth);
    var builder =
        new StringBuilder(
            checked(
                raster.Width *
                raster.Depth *
                16 +
            4096));

    builder.AppendLine(
        $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {width} {height}\" " +
        $"width=\"{width}\" height=\"{height}\" shape-rendering=\"crispEdges\">");
    builder.AppendLine(
        "<rect width=\"100%\" height=\"100%\" fill=\"#101018\"/>");

    for (var z = 0;
         z < raster.Depth;
         z++)
    {
        var runStart =
            0;
        var runColor =
            raster[
                0,
                z];

        for (var x = 1;
             x <= raster.Width;
             x++)
        {
            if (x <
                    raster.Width &&
                raster[
                    x,
                    z] ==
                runColor)
            {
                continue;
            }

            var runWidth =
                x -
                runStart;
            builder.Append(
                $"<rect x=\"{runStart * pixelScale}\" y=\"{z * pixelScale}\" " +
                $"width=\"{runWidth * pixelScale}\" height=\"{pixelScale}\" fill=\"#{runColor.Hex}\"/>");

            if (x <
                raster.Width)
            {
                runStart =
                    x;
                runColor =
                    raster[
                        x,
                        z];
            }
        }

        builder.AppendLine();
    }

    var legendX =
        mapWidth +
        legendPadding;
    builder.AppendLine(
        $"<text x=\"{legendX}\" y=\"18\" fill=\"#f2efff\" " +
        "font-family=\"ui-monospace, monospace\" font-size=\"10\" font-weight=\"700\">Asteria biome map</text>");
    builder.AppendLine(
        $"<text x=\"{legendX}\" y=\"31\" fill=\"#aaa4bd\" " +
        "font-family=\"ui-monospace, monospace\" font-size=\"6\">" +
        $"{Escape(options.Dimension)} · seed {options.Seed} · dim {dimensionSeed}</text>");
    builder.AppendLine(
        $"<text x=\"{legendX}\" y=\"40\" fill=\"#aaa4bd\" " +
        "font-family=\"ui-monospace, monospace\" font-size=\"6\">" +
        $"origin ({originX}, {originZ}) · step {options.Step} · {options.Mode}</text>");

    var y =
        54;
    foreach (var entry in
             raster.Legend)
    {
        builder.AppendLine(
            $"<rect x=\"{legendX}\" y=\"{y - 7}\" width=\"8\" height=\"8\" fill=\"#{entry.Color.Hex}\"/>");
        builder.AppendLine(
            $"<text x=\"{legendX + 13}\" y=\"{y}\" fill=\"#ded9ea\" " +
            "font-family=\"ui-monospace, monospace\" font-size=\"6\">" +
            $"{Escape(entry.BiomeId)}</text>");
        y +=
            legendRowHeight;
    }

    builder.AppendLine(
        "</svg>");
    return builder.ToString();
}

static string Escape(
    string value) =>
    SecurityElement.Escape(
        value) ??
    string.Empty;

internal sealed record Arguments(
    string ProjectRoot,
    string Pack,
    string Dimension,
    ulong Seed,
    int CenterX,
    int CenterZ,
    int Width,
    int Depth,
    int Step,
    BiomeMapMode Mode,
    string Output)
{
    public static Arguments Parse(
        string[] input)
    {
        var root =
            Directory.GetCurrentDirectory();
        var pack =
            "default";
        var dimension =
            "asteria:overworld";
        var seed =
            181960897289965UL;
        var centerX =
            0;
        var centerZ =
            0;
        var width =
            256;
        var depth =
            256;
        var step =
            4;
        var mode =
            BiomeMapMode.Influences;
        var output =
            "biome-map.svg";

        if (input.Length %
            2 !=
            0)
        {
            throw new ArgumentException(
                "CLI flags require values.");
        }

        for (var index = 0;
             index < input.Length;
             index += 2)
        {
            var value =
                input[
                    index +
                    1];

            switch (input[index])
            {
                case "--project-root":
                    root =
                        value;
                    break;
                case "--pack":
                    pack =
                        value;
                    break;
                case "--dimension":
                    dimension =
                        value;
                    break;
                case "--seed":
                    if (!WorldCreationSeed.TryParse(
                            value,
                            out seed))
                    {
                        throw new ArgumentException(
                            "Invalid unsigned 64-bit seed.");
                    }

                    break;
                case "--center-x":
                    centerX =
                        int.Parse(
                            value);
                    break;
                case "--center-z":
                    centerZ =
                        int.Parse(
                            value);
                    break;
                case "--width":
                    width =
                        int.Parse(
                            value);
                    break;
                case "--depth":
                    depth =
                        int.Parse(
                            value);
                    break;
                case "--step":
                    step =
                        int.Parse(
                            value);
                    break;
                case "--mode":
                    mode =
                        value switch
                        {
                            "primary" =>
                                BiomeMapMode.Primary,
                            "influences" =>
                                BiomeMapMode.Influences,
                            _ =>
                                throw new ArgumentException(
                                    "Biome-map mode must be primary or influences."),
                        };
                    break;
                case "--output":
                    output =
                        value;
                    break;
                default:
                    throw new ArgumentException(
                        $"Unknown option {input[index]}.");
            }
        }

        if (width is <
                1 or >
                1024 ||
            depth is <
                1 or >
                1024 ||
            step is <
                1 or >
                4096)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                "Width/depth must be 1–1024 and step 1–4096.");
        }

        return new Arguments(
            root,
            pack,
            dimension,
            seed,
            centerX,
            centerZ,
            width,
            depth,
            step,
            mode,
            output);
    }
}

using System.Text.Json;

namespace Asteria.Core.World;

/// <summary>
/// Converts a bounded loaded-world selection to the actual Asteria structure
/// schema. Unsupported transient voxel details are rejected, never lost silently.
/// </summary>
public static class StructureSelectionExporter
{
    private const string PaletteSymbols =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!#$%&()*+,-/:;<=>?@[]^_{}~";

    public static bool TryExport(
        VoxelWorld world,
        BlockRegistry blocks,
        StructureSelectionBounds bounds,
        string id,
        out StructureSelectionExport? export,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        BlockDefinition.ValidateId(id);
        export = null;
        error = "";

        // Do not trust caller-created struct bounds, including default.
        if (!StructureSelectionBounds.TryCreate(bounds.Minimum, bounds.Maximum,
                out var normalized, out error) ||
            normalized != bounds)
        {
            error = error.Length == 0 ? "Selection bounds are invalid." : error;
            return false;
        }

        var symbols = new Dictionary<(string Id, BlockOrientation Orientation), char>();
        var palette = new SortedDictionary<string, object>(StringComparer.Ordinal);
        var layers = new List<object>(bounds.Height);
        var filled = 0;

        for (var y = bounds.Minimum.Y; y <= bounds.Maximum.Y; y++)
        {
            var rows = new List<string>(bounds.Depth);
            for (var z = bounds.Minimum.Z; z <= bounds.Maximum.Z; z++)
            {
                var row = new char[bounds.Width];
                for (var index = 0; index < bounds.Width; index++)
                {
                    var x = bounds.Minimum.X + index;
                    var position = new WorldVoxelCoord(x, y, z);
                    if (!world.TryGetCell(position, out var cell))
                    {
                        error = $"Unloaded voxel in selection: ({x}, {y}, {z}).";
                        return false;
                    }
                    if (!world.TryGetFluid(position, out var fluid))
                    {
                        error = $"Unloaded fluid voxel in selection: ({x}, {y}, {z}).";
                        return false;
                    }
                    if (!fluid.IsEmpty)
                    {
                        error = $"Source/spread fluid at ({x}, {y}, {z}) cannot be exported losslessly.";
                        return false;
                    }
                    if (cell.IsEmpty)
                    {
                        row[index] = '.';
                        continue;
                    }

                    if (cell.HasMicroblockGeometry ||
                        cell.State != 0 ||
                        cell.TextureRotation != TextureRotation.Degrees0 ||
                        cell.Facing != HorizontalFacing.South ||
                        !world.GetBlockSurfaceStateOrEmpty(position).IsEmpty)
                    {
                        error = $"Block ({x}, {y}, {z}) has sculpting, tint, attached layers or state not supported by structure JSON.";
                        return false;
                    }

                    var definition = blocks.GetDefinition(cell.Block);
                    var key = (definition.Id, cell.Orientation);
                    if (!symbols.TryGetValue(key, out var symbol))
                    {
                        if (symbols.Count >= 128)
                        {
                            error = "Selection has more than 128 block/orientation palette entries.";
                            return false;
                        }

                        var symbolIndex = symbols.Count;
                        symbol = symbolIndex < PaletteSymbols.Length
                            ? PaletteSymbols[symbolIndex]
                            : (char)(0xE000 + symbolIndex - PaletteSymbols.Length);
                        symbols.Add(key, symbol);
                        palette.Add(symbol.ToString(), new
                        {
                            block = definition.Id,
                            orientation = cell.Orientation switch
                            {
                                BlockOrientation.X => "x",
                                BlockOrientation.Y => "y",
                                BlockOrientation.Z => "z",
                                _ => throw new InvalidOperationException("Unknown block orientation."),
                            }
                        });
                    }
                    row[index] = symbol;
                    filled++;
                }
                rows.Add(new string(row));
            }
            layers.Add(new { y = y - bounds.Minimum.Y, rows });
        }

        if (filled == 0)
        {
            error = "Selection contains no blocks.";
            return false;
        }

        var model = new
        {
            id,
            locatable = false,
            rotation = false,
            anchor = new { x = 0, y = 0, z = 0 },
            palette,
            layers
        };
        var json = JsonSerializer.Serialize(
            model, new JsonSerializerOptions { WriteIndented = true }) + "\n";

        // The exporter is not allowed to invent a second structure format.
        var definitionResult = StructureDefinitionJson.Parse(json);
        new StructureRegistry([definitionResult]).ValidateBlocks(blocks);
        export = new StructureSelectionExport(json, bounds.Volume, filled);
        return true;
    }
}

public sealed record StructureSelectionExport(
    string Json, int Volume, int OccupiedBlocks);

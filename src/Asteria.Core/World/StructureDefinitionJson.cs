using System.Text.Json;

namespace Asteria.Core.World;

public static class StructureDefinitionJson
{
    private const int MaximumLayerCount = 256;
    private const int MaximumGridWidth = 128;
    private const int MaximumGridDepth = 128;
    private const int MaximumPaletteEntries = 128;

    public static StructureDefinition Parse(
        string json)
    {
        ArgumentNullException.ThrowIfNull(
            json);

        using var document =
            JsonDocument.Parse(
                json);
        var root =
            document.RootElement;

        if (root.ValueKind !=
            JsonValueKind.Object)
        {
            throw new FormatException(
                "Structure definition root must be an object.");
        }

        EnsureKnownProperties(
            root,
            "structure",
            "id",
            "groupId",
            "locatable",
            "rotation",
            "priority",
            "conflictGroups",
            "generation",
            "restrictions",
            "anchor",
            "groundAnchorY",
            "clearAbove",
            "palette",
            "layers");

        var id =
            RequiredString(
                root,
                "id");
        var anchor =
            ParseAnchor(
                root);
        var palette =
            ParsePalette(
                root);
        var content =
            ParseLayers(
                root,
                anchor,
                palette);

        return new StructureDefinition(
            id,
            OptionalBoolean(
                root,
                "rotation") ??
            false,
            anchor,
            content.Voxels,
            ParseRestrictions(
                root),
            OptionalString(
                root,
                "groupId"),
            OptionalInt32(
                root,
                "priority") ??
            0,
            OptionalStringArray(
                root,
                "conflictGroups"),
            ParseGeneration(
                root),
            content.Connectors,
            content.FluidVoxels,
            content.ClearVoxels,
            OptionalInt32(
                root,
                "groundAnchorY"),
            OptionalInt32(
                root,
                "clearAbove") ??
            0,
            OptionalBoolean(
                root,
                "locatable") ??
            true);
    }

    private static StructureAnchor ParseAnchor(
        JsonElement root)
    {
        if (!root.TryGetProperty(
                "anchor",
                out var value) ||
            value.ValueKind ==
                JsonValueKind.Null)
        {
            return default;
        }

        value =
            RequiredObject(
                root,
                "anchor");
        EnsureKnownProperties(
            value,
            "anchor",
            "x",
            "y",
            "z");
        return new StructureAnchor(
            OptionalInt32(
                value,
                "x") ??
            0,
            OptionalInt32(
                value,
                "y") ??
            0,
            OptionalInt32(
                value,
                "z") ??
            0);
    }

    private static StructureRestrictionsDefinition
        ParseRestrictions(
            JsonElement root)
    {
        if (!root.TryGetProperty(
                "restrictions",
                out var value) ||
            value.ValueKind ==
                JsonValueKind.Null)
        {
            return new StructureRestrictionsDefinition();
        }

        value =
            RequiredObject(
                root,
                "restrictions");
        EnsureKnownProperties(
            value,
            "restrictions",
            "minSlope",
            "maxSlope",
            "requiresDryGround",
            "requiredBiomeCoverage",
            "groundBlocks",
            "proximity");
        return new StructureRestrictionsDefinition(
            OptionalInt32(
                value,
                "maxSlope") ??
            1,
            OptionalBoolean(
                value,
                "requiresDryGround") ??
            true,
            OptionalSingle(
                value,
                "requiredBiomeCoverage") ??
            0f,
            OptionalStringArray(
                value,
                "groundBlocks"),
            ParseProximity(
                value),
            OptionalInt32(
                value,
                "minSlope") ??
            0);
    }

    private static IReadOnlyList<StructureProximityRestrictionDefinition>
        ParseProximity(
            JsonElement restrictions)
    {
        if (!restrictions.TryGetProperty(
                "proximity",
                out var value) ||
            value.ValueKind ==
                JsonValueKind.Null)
        {
            return Array.Empty<StructureProximityRestrictionDefinition>();
        }

        if (value.ValueKind !=
            JsonValueKind.Array)
        {
            throw new FormatException(
                "restrictions.proximity must be an array.");
        }

        return value
            .EnumerateArray()
            .Select(
                item =>
                {
                    if (item.ValueKind !=
                        JsonValueKind.Object)
                    {
                        throw new FormatException(
                            "restrictions.proximity entries must be objects.");
                    }

                    EnsureKnownProperties(
                        item,
                        "restrictions.proximity entry",
                        "target",
                        "mode",
                        "minDistance",
                        "maxDistance");
                    var target =
                        RequiredObject(
                            item,
                            "target");
                    EnsureKnownProperties(
                        target,
                        "restrictions.proximity target",
                        "block",
                        "fluid");

                    var mode =
                        RequiredString(
                            item,
                            "mode") switch
                        {
                            "required" =>
                                StructureProximityMode.Required,
                            "forbidden" =>
                                StructureProximityMode.Forbidden,
                            var authored =>
                                throw new FormatException(
                                    $"Unsupported structure proximity mode: {authored}."),
                        };

                    return new StructureProximityRestrictionDefinition(
                        new StructureProximityTargetDefinition(
                            OptionalString(
                                target,
                                "block"),
                            OptionalString(
                                target,
                                "fluid")),
                        mode,
                        RequiredInt32(
                            item,
                            "maxDistance"),
                        OptionalInt32(
                            item,
                            "minDistance"));
                })
            .ToArray();
    }

    private static StructureGenerationDefinition
        ParseGeneration(
            JsonElement root)
    {
        if (!root.TryGetProperty(
                "generation",
                out var value) ||
            value.ValueKind ==
                JsonValueKind.Null)
        {
            return StructureGenerationDefinition.Default;
        }

        if (value.ValueKind !=
            JsonValueKind.Object)
        {
            throw new FormatException(
                "generation must be an object.");
        }

        EnsureKnownProperties(
            value,
            "generation",
            "replacePolicy",
            "fluidPolicy",
            "reserveSpace");

        var replacePolicy =
            OptionalString(
                value,
                "replacePolicy") switch
            {
                null or "any" =>
                    StructureReplacePolicy.Any,
                "air_only" =>
                    StructureReplacePolicy.AirOnly,
                "terrain" =>
                    StructureReplacePolicy.Terrain,
                var authored =>
                    throw new FormatException(
                        $"Unsupported structure replacePolicy: {authored}."),
            };
        var fluidPolicy =
            OptionalString(
                value,
                "fluidPolicy") switch
            {
                null or "displace" =>
                    StructureFluidPolicy.Displace,
                "preserve" =>
                    StructureFluidPolicy.Preserve,
                "forbid" =>
                    StructureFluidPolicy.Forbid,
                var authored =>
                    throw new FormatException(
                        $"Unsupported structure fluidPolicy: {authored}."),
            };

        return new StructureGenerationDefinition(
            replacePolicy,
            fluidPolicy,
            OptionalBoolean(
                value,
                "reserveSpace") ??
            false);
    }

    private static IReadOnlyDictionary<
        char,
        PaletteEntry> ParsePalette(
            JsonElement root)
    {
        var value =
            RequiredObject(
                root,
                "palette");
        if (value.EnumerateObject().Count() >
            MaximumPaletteEntries)
        {
            throw new FormatException(
                $"Structure palette may contain at most {MaximumPaletteEntries} entries.");
        }

        var entries =
            new Dictionary<
                char,
                PaletteEntry>();

        foreach (var property in
                 value.EnumerateObject())
        {
            if (property.Name.Length != 1 ||
                property.Name[0] == '.')
            {
                throw new FormatException(
                    "Structure palette keys must be one non-dot character.");
            }

            if (property.Value.ValueKind !=
                JsonValueKind.Object)
            {
                throw new FormatException(
                    $"Structure palette {property.Name} must be an object.");
            }

            EnsureKnownProperties(
                property.Value,
                $"structure palette {property.Name}",
                "block",
                "fluid",
                "clear",
                "orientation",
                "connector");

            var block =
                OptionalString(
                    property.Value,
                    "block");
            var fluid =
                OptionalString(
                    property.Value,
                    "fluid");
            var clear =
                OptionalBoolean(
                    property.Value,
                    "clear") ??
                false;
            var primaryPayloads =
                (block is null ? 0 : 1) +
                (fluid is null ? 0 : 1) +
                (clear ? 1 : 0);
            if (primaryPayloads > 1)
            {
                throw new FormatException(
                    $"Structure palette {property.Name} cannot define multiple primary payloads.");
            }

            var connector =
                ParseConnector(
                    property.Value);

            if (primaryPayloads == 0 &&
                connector is null)
            {
                throw new FormatException(
                    $"Structure palette {property.Name} must define payload and/or connector.");
            }

            if (connector is
                    { Target: null } &&
                primaryPayloads != 0)
            {
                throw new FormatException(
                    $"Structure palette {property.Name} input connector must be connector-only.");
            }

            var orientation =
                OptionalString(
                    property.Value,
                    "orientation") switch
                {
                    null or "y" =>
                        BlockOrientation.Y,
                    "x" =>
                        BlockOrientation.X,
                    "z" =>
                        BlockOrientation.Z,
                    var authored =>
                        throw new FormatException(
                            $"Unknown structure block orientation: {authored}"),
                };

            if (block is null &&
                property.Value.TryGetProperty(
                    "orientation",
                    out _))
            {
                throw new FormatException(
                    $"Structure palette {property.Name} orientation requires a block.");
            }

            entries.Add(
                property.Name[0],
                new PaletteEntry(
                    block,
                    fluid,
                    clear,
                    orientation,
                    connector));
        }

        if (entries.Count == 0)
        {
            throw new FormatException(
                "Structure palette cannot be empty.");
        }

        return entries;
    }

    private static PaletteConnector?
        ParseConnector(
            JsonElement paletteEntry)
    {
        if (!paletteEntry.TryGetProperty(
                "connector",
                out var value) ||
            value.ValueKind ==
                JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind !=
            JsonValueKind.Object)
        {
            throw new FormatException(
                "Structure connector must be an object.");
        }

        EnsureKnownProperties(
            value,
            "structure connector",
            "target",
            "face",
            "strength",
            "strengthLossOnEachLoop",
            "minDistance",
            "maxDistance");

        var face =
            RequiredString(
                value,
                "face") switch
            {
                "right" =>
                    StructureConnectorFace.Right,
                "left" =>
                    StructureConnectorFace.Left,
                "top" =>
                    StructureConnectorFace.Top,
                "bottom" =>
                    StructureConnectorFace.Bottom,
                "front" =>
                    StructureConnectorFace.Front,
                "back" =>
                    StructureConnectorFace.Back,
                var authored =>
                    throw new FormatException(
                        $"Unsupported structure connector face: {authored}."),
            };

        return new PaletteConnector(
            OptionalString(
                value,
                "target"),
            face,
            OptionalSingle(
                value,
                "strength") ??
            1f,
            OptionalSingle(
                value,
                "strengthLossOnEachLoop") ??
            0f,
            OptionalInt32(
                value,
                "minDistance") ??
            0,
            OptionalInt32(
                value,
                "maxDistance") ??
            0);
    }

    private static StructureTemplateContent
        ParseLayers(
            JsonElement root,
            StructureAnchor anchor,
            IReadOnlyDictionary<
                char,
                PaletteEntry> palette)
    {
        if (!root.TryGetProperty(
                "layers",
                out var array) ||
            array.ValueKind !=
                JsonValueKind.Array)
        {
            throw new FormatException(
                "Structure layers must be an array.");
        }

        if (array.GetArrayLength() >
            MaximumLayerCount)
        {
            throw new FormatException(
                $"Structure may contain at most {MaximumLayerCount} layers.");
        }

        var voxels =
            new List<StructureVoxelDefinition>();
        var fluidVoxels =
            new List<StructureFluidVoxelDefinition>();
        var clearVoxels =
            new List<StructureClearVoxelDefinition>();
        var connectors =
            new List<StructureConnectorDefinition>();
        int? width = null;
        int? depth = null;
        var authoredY =
            new HashSet<int>();

        foreach (var layer in
                 array.EnumerateArray())
        {
            if (layer.ValueKind !=
                JsonValueKind.Object)
            {
                throw new FormatException(
                    "Structure layer entries must be objects.");
            }

            EnsureKnownProperties(
                layer,
                "structure layer",
                "y",
                "rows");

            var y =
                RequiredInt32(
                    layer,
                    "y");
            if (!authoredY.Add(
                    y))
            {
                throw new FormatException(
                    $"Structure repeats layer Y={y}.");
            }

            var rows =
                RequiredStringArray(
                    layer,
                    "rows");
            if (rows.Count == 0)
            {
                throw new FormatException(
                    "Structure layer rows cannot be empty.");
            }

            if (rows.Count >
                MaximumGridDepth)
            {
                throw new FormatException(
                    $"Structure layer depth may not exceed {MaximumGridDepth} rows.");
            }

            depth ??=
                rows.Count;
            if (depth !=
                rows.Count)
            {
                throw new FormatException(
                    "Structure layers must have the same row count.");
            }

            foreach (var row in rows)
            {
                width ??=
                    row.Length;
                if (row.Length == 0 ||
                    row.Length !=
                        width)
                {
                    throw new FormatException(
                        "Structure rows must be non-empty and share one width.");
                }

                if (row.Length >
                    MaximumGridWidth)
                {
                    throw new FormatException(
                        $"Structure row width may not exceed {MaximumGridWidth} symbols.");
                }
            }

            for (var z = 0;
                 z < rows.Count;
                 z++)
            {
                var row =
                    rows[z];

                for (var x = 0;
                     x < row.Length;
                     x++)
                {
                    var symbol =
                        row[x];
                    if (symbol == '.')
                    {
                        continue;
                    }

                    if (!palette.TryGetValue(
                            symbol,
                            out var entry))
                    {
                        throw new FormatException(
                            $"Structure layer uses undefined palette symbol: {symbol}.");
                    }

                    var offsetX =
                        checked(
                            x -
                            anchor.X);
                    var offsetY =
                        checked(
                            y -
                            anchor.Y);
                    var offsetZ =
                        checked(
                            z -
                            anchor.Z);

                    if (entry.Connector is
                        { } connector)
                    {
                        connectors.Add(
                            new StructureConnectorDefinition(
                                offsetX,
                                offsetY,
                                offsetZ,
                                connector.Face,
                                connector.Target,
                                connector.Strength,
                                connector.StrengthLossOnEachLoop,
                                connector.MinDistance,
                                connector.MaxDistance));
                    }

                    if (entry.Block is
                        { } block)
                    {
                        voxels.Add(
                            new StructureVoxelDefinition(
                                offsetX,
                                offsetY,
                                offsetZ,
                                block,
                                entry.Orientation));
                    }
                    else if (entry.Fluid is
                             { } fluid)
                    {
                        fluidVoxels.Add(
                            new StructureFluidVoxelDefinition(
                                offsetX,
                                offsetY,
                                offsetZ,
                                fluid));
                    }
                    else if (entry.Clear)
                    {
                        clearVoxels.Add(
                            new StructureClearVoxelDefinition(
                                offsetX,
                                offsetY,
                                offsetZ));
                    }
                }
            }
        }

        return new StructureTemplateContent(
            Array.AsReadOnly(
                voxels.ToArray()),
            Array.AsReadOnly(
                fluidVoxels.ToArray()),
            Array.AsReadOnly(
                clearVoxels.ToArray()),
            Array.AsReadOnly(
                connectors.ToArray()));
    }

    private static void EnsureKnownProperties(
        JsonElement value,
        string context,
        params string[] allowed)
    {
        var known =
            new HashSet<string>(
                allowed,
                StringComparer.Ordinal);

        foreach (var property in
                 value.EnumerateObject())
        {
            if (!known.Contains(
                    property.Name))
            {
                throw new FormatException(
                    $"Unsupported {context} field: {property.Name}.");
            }
        }
    }

    private static JsonElement RequiredObject(
        JsonElement parent,
        string name)
    {
        if (!parent.TryGetProperty(
                name,
                out var value) ||
            value.ValueKind !=
                JsonValueKind.Object)
        {
            throw new FormatException(
                $"{name} must be an object.");
        }

        return value;
    }

    private static IReadOnlyList<string>
        OptionalStringArray(
            JsonElement parent,
            string name)
    {
        if (!parent.TryGetProperty(
                name,
                out var value) ||
            value.ValueKind ==
                JsonValueKind.Null)
        {
            return Array.Empty<string>();
        }

        if (value.ValueKind !=
            JsonValueKind.Array)
        {
            throw new FormatException(
                $"{name} must be an array.");
        }

        return value
            .EnumerateArray()
            .Select(item =>
                item.ValueKind ==
                    JsonValueKind.String
                    ? item.GetString()!
                    : throw new FormatException(
                        $"{name} can contain only strings."))
            .ToArray();
    }

    private static IReadOnlyList<string>
        RequiredStringArray(
            JsonElement parent,
            string name)
    {
        if (!parent.TryGetProperty(
                name,
                out var value) ||
            value.ValueKind !=
                JsonValueKind.Array)
        {
            throw new FormatException(
                $"{name} must be an array.");
        }

        return value
            .EnumerateArray()
            .Select(item =>
                item.ValueKind ==
                    JsonValueKind.String
                    ? item.GetString()!
                    : throw new FormatException(
                        $"{name} can contain only strings."))
            .ToArray();
    }

    private static string RequiredString(
        JsonElement parent,
        string name)
    {
        if (!parent.TryGetProperty(
                name,
                out var value) ||
            value.ValueKind !=
                JsonValueKind.String)
        {
            throw new FormatException(
                $"{name} must be a string.");
        }

        return value.GetString()!;
    }

    private static string? OptionalString(
        JsonElement parent,
        string name)
    {
        if (!parent.TryGetProperty(
                name,
                out var value) ||
            value.ValueKind ==
                JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind ==
            JsonValueKind.String
            ? value.GetString()
            : throw new FormatException(
                $"{name} must be a string.");
    }

    private static bool? OptionalBoolean(
        JsonElement parent,
        string name)
    {
        if (!parent.TryGetProperty(
                name,
                out var value) ||
            value.ValueKind ==
                JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind is
            JsonValueKind.True or
            JsonValueKind.False
            ? value.GetBoolean()
            : throw new FormatException(
                $"{name} must be a boolean.");
    }

    private static float? OptionalSingle(
        JsonElement parent,
        string name)
    {
        if (!parent.TryGetProperty(
                name,
                out var value) ||
            value.ValueKind ==
                JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind !=
                JsonValueKind.Number ||
            !value.TryGetSingle(
                out var result))
        {
            throw new FormatException(
                $"{name} must be a number.");
        }

        return result;
    }

    private static int? OptionalInt32(
        JsonElement parent,
        string name)
    {
        if (!parent.TryGetProperty(
                name,
                out var value) ||
            value.ValueKind ==
                JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind !=
                JsonValueKind.Number ||
            !value.TryGetInt32(
                out var result))
        {
            throw new FormatException(
                $"{name} must be an integer.");
        }

        return result;
    }

    private static int RequiredInt32(
        JsonElement parent,
        string name) =>
        OptionalInt32(
            parent,
            name) ??
        throw new FormatException(
            $"{name} must be an integer.");

    private sealed record PaletteConnector(
        string? Target,
        StructureConnectorFace Face,
        float Strength,
        float StrengthLossOnEachLoop,
        int MinDistance,
        int MaxDistance);

    private sealed record PaletteEntry(
        string? Block,
        string? Fluid,
        bool Clear,
        BlockOrientation Orientation,
        PaletteConnector? Connector);

    private sealed record StructureTemplateContent(
        IReadOnlyList<StructureVoxelDefinition> Voxels,
        IReadOnlyList<StructureFluidVoxelDefinition> FluidVoxels,
        IReadOnlyList<StructureClearVoxelDefinition> ClearVoxels,
        IReadOnlyList<StructureConnectorDefinition> Connectors);
}

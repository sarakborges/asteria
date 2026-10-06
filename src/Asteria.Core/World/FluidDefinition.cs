using System.Globalization;
using System.Text.Json;

namespace Asteria.Core.World;

public readonly record struct FluidColor(
    byte Red,
    byte Green,
    byte Blue)
{
    public static FluidColor Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var hex =
            value.StartsWith('#')
                ? value[1..]
                : value;

        if (hex.Length != 6 ||
            !byte.TryParse(
                hex.AsSpan(0, 2),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var red) ||
            !byte.TryParse(
                hex.AsSpan(2, 2),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var green) ||
            !byte.TryParse(
                hex.AsSpan(4, 2),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var blue))
        {
            throw new FormatException(
                $"Fluid color must be a six-digit RGB hex value: {value}");
        }

        return new FluidColor(red, green, blue);
    }
}

public readonly record struct FluidMotionDefinition
{
    public static FluidMotionDefinition Default { get; } =
        new(
            horizontalSpeedMultiplier: 0.45f,
            horizontalAcceleration: 10f,
            sinkSpeed: 0.9f,
            ascendSpeed: 2.4f,
            surfaceExitSpeed: 5f,
            verticalAcceleration: 10f,
            surfaceExitMargin: 0.35f);

    public FluidMotionDefinition(
        float horizontalSpeedMultiplier,
        float horizontalAcceleration,
        float sinkSpeed,
        float ascendSpeed,
        float surfaceExitSpeed,
        float verticalAcceleration,
        float surfaceExitMargin)
    {
        HorizontalSpeedMultiplier =
            RequiredFiniteNonNegative(
                horizontalSpeedMultiplier,
                nameof(horizontalSpeedMultiplier));
        HorizontalAcceleration =
            RequiredFiniteNonNegative(
                horizontalAcceleration,
                nameof(horizontalAcceleration));
        SinkSpeed =
            RequiredFiniteNonNegative(
                sinkSpeed,
                nameof(sinkSpeed));
        AscendSpeed =
            RequiredFiniteNonNegative(
                ascendSpeed,
                nameof(ascendSpeed));
        SurfaceExitSpeed =
            RequiredFiniteNonNegative(
                surfaceExitSpeed,
                nameof(surfaceExitSpeed));
        VerticalAcceleration =
            RequiredFiniteNonNegative(
                verticalAcceleration,
                nameof(verticalAcceleration));
        SurfaceExitMargin =
            RequiredFiniteNonNegative(
                surfaceExitMargin,
                nameof(surfaceExitMargin));
    }

    public float HorizontalSpeedMultiplier { get; }

    public float HorizontalAcceleration { get; }

    public float SinkSpeed { get; }

    public float AscendSpeed { get; }

    public float SurfaceExitSpeed { get; }

    public float VerticalAcceleration { get; }

    public float SurfaceExitMargin { get; }

    private static float RequiredFiniteNonNegative(
        float value,
        string name)
    {
        if (!float.IsFinite(value) ||
            value < 0f)
        {
            throw new ArgumentOutOfRangeException(
                name,
                "Fluid motion values must be finite and non-negative.");
        }

        return value;
    }
}

public sealed class FluidDefinition
{
    public FluidDefinition(
        string id,
        FluidColor color,
        float opacity,
        float roughness = 1f,
        byte lightDampening = 0,
        float spreadSpeed = 1f,
        ushort maxSpread = 7,
        BlockLightEmission lightEmission = default,
        FluidMotionDefinition? motion = null,
        string? texture = null)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            id != id.Trim() ||
            id.Count(character => character == ':') != 1)
        {
            throw new ArgumentException(
                "Fluid id must use the namespaced form namespace:name.",
                nameof(id));
        }

        if (!float.IsFinite(opacity) ||
            opacity is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(opacity),
                "Fluid opacity must be within 0..1.");
        }

        if (!float.IsFinite(roughness) ||
            roughness is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(roughness),
                "Fluid roughness must be within 0..1.");
        }

        if (lightDampening > VoxelLight.MaxLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lightDampening),
                "Fluid light dampening must be within 0..15.");
        }

        if (!float.IsFinite(spreadSpeed) ||
            spreadSpeed < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(spreadSpeed),
                "Fluid spread speed must be finite and non-negative.");
        }

        if (texture is not null &&
            (string.IsNullOrWhiteSpace(texture) ||
             texture != texture.Trim()))
        {
            throw new ArgumentException(
                "Fluid texture path must be non-empty and trimmed when provided.",
                nameof(texture));
        }

        Id = id;
        Color = color;
        Opacity = opacity;
        Roughness = roughness;
        LightDampening = lightDampening;
        SpreadSpeed = spreadSpeed;
        MaxSpread = maxSpread;
        LightEmission = lightEmission;
        Motion =
            motion ??
            FluidMotionDefinition.Default;
        Texture = texture;
    }

    public string Id { get; }

    public FluidColor Color { get; }

    public float Opacity { get; }

    public float Roughness { get; }

    public byte LightDampening { get; }

    public float SpreadSpeed { get; }

    public ushort MaxSpread { get; }

    public BlockLightEmission LightEmission { get; }

    public FluidMotionDefinition Motion { get; }

    public string? Texture { get; }
}

public static class FluidDefinitionJson
{
    public static FluidDefinition Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        return new FluidDefinition(
            RequiredString(root, "id"),
            FluidColor.Parse(
                RequiredString(root, "color")),
            RequiredSingle(root, "opacity"),
            OptionalSingle(root, "roughness") ?? 1f,
            OptionalByte(root, "lightDampening") ?? 0,
            OptionalSingle(root, "spreadSpeed") ?? 1f,
            OptionalUShort(root, "maxSpread") ?? 7,
            ParseLightEmission(root),
            ParseMotion(root),
            OptionalString(root, "texture"));
    }

    private static FluidMotionDefinition ParseMotion(
        JsonElement root)
    {
        if (!root.TryGetProperty(
                "motion",
                out var motion) ||
            motion.ValueKind ==
                JsonValueKind.Null)
        {
            return FluidMotionDefinition.Default;
        }

        if (motion.ValueKind !=
            JsonValueKind.Object)
        {
            throw new FormatException(
                "Fluid motion must be an object.");
        }

        var defaults =
            FluidMotionDefinition.Default;

        return new FluidMotionDefinition(
            OptionalSingle(
                motion,
                "horizontalSpeedMultiplier") ??
                defaults.HorizontalSpeedMultiplier,
            OptionalSingle(
                motion,
                "horizontalAcceleration") ??
                defaults.HorizontalAcceleration,
            OptionalSingle(
                motion,
                "sinkSpeed") ??
                defaults.SinkSpeed,
            OptionalSingle(
                motion,
                "ascendSpeed") ??
                defaults.AscendSpeed,
            OptionalSingle(
                motion,
                "surfaceExitSpeed") ??
                defaults.SurfaceExitSpeed,
            OptionalSingle(
                motion,
                "verticalAcceleration") ??
                defaults.VerticalAcceleration,
            OptionalSingle(
                motion,
                "surfaceExitMargin") ??
                defaults.SurfaceExitMargin);
    }

    private static BlockLightEmission ParseLightEmission(
        JsonElement root)
    {
        if (!root.TryGetProperty(
                "lightEmission",
                out var emission) ||
            emission.ValueKind == JsonValueKind.Null)
        {
            return default;
        }

        if (emission.ValueKind ==
            JsonValueKind.Number)
        {
            if (!emission.TryGetByte(
                    out var level) ||
                level > VoxelLight.MaxLevel)
            {
                throw new FormatException(
                    "Fluid lightEmission must be within 0..15.");
            }

            return new BlockLightEmission(
                level,
                level,
                level);
        }

        if (emission.ValueKind !=
            JsonValueKind.Object)
        {
            throw new FormatException(
                "Fluid lightEmission must be a 0..15 number or RGB object.");
        }

        return new BlockLightEmission(
            RequiredLightChannel(
                emission,
                "red"),
            RequiredLightChannel(
                emission,
                "green"),
            RequiredLightChannel(
                emission,
                "blue"));
    }

    private static byte RequiredLightChannel(
        JsonElement emission,
        string channel)
    {
        if (!emission.TryGetProperty(
                channel,
                out var value) ||
            value.ValueKind !=
                JsonValueKind.Number ||
            !value.TryGetByte(
                out var level) ||
            level > VoxelLight.MaxLevel)
        {
            throw new FormatException(
                $"Fluid lightEmission.{channel} must be within 0..15.");
        }

        return level;
    }

    private static string RequiredString(
        JsonElement element,
        string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            throw new FormatException(
                $"Missing required string property: {name}");
        }

        return value.GetString()!;
    }

    private static string? OptionalString(
        JsonElement element,
        string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new FormatException(
                $"{name} must be a string.");
        }

        return value.GetString();
    }

    private static float RequiredSingle(
        JsonElement element,
        string name) =>
        OptionalSingle(element, name) ??
        throw new FormatException(
            $"Missing required number property: {name}");

    private static float? OptionalSingle(
        JsonElement element,
        string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetSingle(out var result))
        {
            throw new FormatException(
                $"{name} must be a number.");
        }

        return result;
    }

    private static byte? OptionalByte(
        JsonElement element,
        string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetByte(out var result))
        {
            throw new FormatException(
                $"{name} must be an unsigned byte.");
        }

        return result;
    }

    private static ushort? OptionalUShort(
        JsonElement element,
        string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetUInt16(out var result))
        {
            throw new FormatException(
                $"{name} must be an unsigned 16-bit integer.");
        }

        return result;
    }
}

public sealed class FluidRegistry
{
    private readonly FluidDefinition[] _definitions;
    private readonly Dictionary<string, FluidRuntimeId> _idsByName;

    public FluidRegistry(IEnumerable<FluidDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var authored = definitions.ToArray();
        if (authored.Length >= ushort.MaxValue)
        {
            throw new ArgumentException(
                $"A fluid registry supports at most {ushort.MaxValue - 1} authored fluids.",
                nameof(definitions));
        }

        _definitions =
            new FluidDefinition[authored.Length + 1];
        _idsByName =
            new Dictionary<string, FluidRuntimeId>(
                authored.Length,
                StringComparer.Ordinal);

        for (var index = 0;
             index < authored.Length;
             index++)
        {
            var definition =
                authored[index] ??
                throw new ArgumentException(
                    "Fluid definitions cannot contain null entries.",
                    nameof(definitions));
            var runtimeId =
                new FluidRuntimeId(
                    checked((ushort)(index + 1)));

            if (!_idsByName.TryAdd(
                    definition.Id,
                    runtimeId))
            {
                throw new ArgumentException(
                    $"Duplicate fluid id: {definition.Id}",
                    nameof(definitions));
            }

            _definitions[runtimeId.Value] =
                definition;
        }
    }

    public int AuthoredCount =>
        _definitions.Length - 1;

    public ushort MaximumSpread =>
        _definitions
            .Skip(1)
            .Where(definition => definition is not null)
            .Select(definition => definition!.MaxSpread)
            .DefaultIfEmpty((ushort)0)
            .Max();

    public static FluidRegistry FromJson(
        IEnumerable<string> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        return new FluidRegistry(
            documents
                .Select(FluidDefinitionJson.Parse)
                .OrderBy(
                    definition => definition.Id,
                    StringComparer.Ordinal));
    }

    public FluidRuntimeId GetId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        return _idsByName.TryGetValue(
            id,
            out var runtimeId)
            ? runtimeId
            : throw new KeyNotFoundException(
                $"Unknown fluid id: {id}");
    }

    public FluidDefinition GetDefinition(
        FluidRuntimeId runtimeId)
    {
        if (runtimeId.IsNone ||
            runtimeId.Value >= _definitions.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(runtimeId),
                $"Unknown runtime fluid id: {runtimeId.Value}");
        }

        return _definitions[runtimeId.Value];
    }

    public IEnumerable<(
        FluidRuntimeId RuntimeId,
        FluidDefinition Definition)> AuthoredDefinitions()
    {
        for (var index = 1;
             index < _definitions.Length;
             index++)
        {
            yield return (
                new FluidRuntimeId(
                    checked((ushort)index)),
                _definitions[index]);
        }
    }
}

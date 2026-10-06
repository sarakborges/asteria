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

public sealed class FluidDefinition
{
    public FluidDefinition(
        string id,
        FluidColor color,
        float opacity,
        float roughness = 1f,
        byte lightDampening = 0,
        float spreadSpeed = 1f,
        ushort maxSpread = 7)
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

        Id = id;
        Color = color;
        Opacity = opacity;
        Roughness = roughness;
        LightDampening = lightDampening;
        SpreadSpeed = spreadSpeed;
        MaxSpread = maxSpread;
    }

    public string Id { get; }

    public FluidColor Color { get; }

    public float Opacity { get; }

    public float Roughness { get; }

    public byte LightDampening { get; }

    public float SpreadSpeed { get; }

    public ushort MaxSpread { get; }
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
            OptionalUShort(root, "maxSpread") ?? 7);
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

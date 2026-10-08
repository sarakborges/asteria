namespace Asteria.Core.World;

public enum BlockShapeKind : byte
{
    Cube = 0,
    Layer = 1,
    Hollow = 2,
    Spike = 3,
}

public enum BlockLayerPlacement : byte
{
    Surface = 0,
    Center = 1,
}

public sealed class BlockShapeDefinition
{
    private BlockShapeDefinition(
        BlockShapeKind kind,
        float thickness,
        BlockLayerPlacement layerPlacement,
        string? stackToBlockId,
        float wallThickness,
        float spikeBaseRadius = 0f,
        float spikeTipRadius = 0f,
        int spikeSides = 0,
        float spikeIrregularity = 0f,
        float spikeTaperPower = 1f)
    {
        Kind = kind;
        Thickness = thickness;
        LayerPlacement = layerPlacement;
        StackToBlockId = stackToBlockId;
        WallThickness = wallThickness;
        SpikeBaseRadius = spikeBaseRadius;
        SpikeTipRadius = spikeTipRadius;
        SpikeSides = spikeSides;
        SpikeIrregularity = spikeIrregularity;
        SpikeTaperPower = spikeTaperPower;
    }

    public BlockShapeKind Kind { get; }

    public float Thickness { get; }

    public BlockLayerPlacement LayerPlacement { get; }

    public string? StackToBlockId { get; }

    public float WallThickness { get; }
    public float SpikeBaseRadius { get; }
    public float SpikeTipRadius { get; }
    public int SpikeSides { get; }
    public float SpikeIrregularity { get; }
    public float SpikeTaperPower { get; }

    public bool IsStackableLayer =>
        Kind == BlockShapeKind.Layer &&
        LayerPlacement == BlockLayerPlacement.Surface &&
        StackToBlockId is not null;

    public bool IsCenteredLayer =>
        Kind == BlockShapeKind.Layer &&
        LayerPlacement == BlockLayerPlacement.Center;

    public static BlockShapeDefinition Cube { get; } =
        new(BlockShapeKind.Cube, 1f, BlockLayerPlacement.Surface, null, 0f);

    public static BlockShapeDefinition SurfaceLayer(float thickness, string stackToBlockId)
    {
        ValidateThickness(thickness);
        ValidateNamespacedId(stackToBlockId, nameof(stackToBlockId));

        return new BlockShapeDefinition(
            BlockShapeKind.Layer,
            thickness,
            BlockLayerPlacement.Surface,
            stackToBlockId,
            0f);
    }

    public static BlockShapeDefinition CenteredLayer(float thickness)
    {
        ValidateThickness(thickness);

        return new BlockShapeDefinition(
            BlockShapeKind.Layer,
            thickness,
            BlockLayerPlacement.Center,
            null,
            0f);
    }

    public static BlockShapeDefinition Hollow(float wallThickness)
    {
        if (!float.IsFinite(wallThickness) || wallThickness <= 0f || wallThickness >= 0.5f)
        {
            throw new ArgumentOutOfRangeException(nameof(wallThickness), "Hollow wall thickness must be within (0, 0.5).");
        }

        return new BlockShapeDefinition(
            BlockShapeKind.Hollow,
            1f,
            BlockLayerPlacement.Surface,
            null,
            wallThickness);
    }

    public static BlockShapeDefinition Spike(
        float baseRadius = 0.46f,
        float tipRadius = 0.025f,
        int sides = 6,
        float irregularity = 0f,
        float taperPower = 1f)
    {
        if (!float.IsFinite(baseRadius) ||
            !float.IsFinite(tipRadius) ||
            baseRadius is <= 0f or > 0.5f ||
            tipRadius < 0f ||
            tipRadius >= baseRadius ||
            sides is < 4 or > 12 ||
            !float.IsFinite(irregularity) ||
            irregularity is < 0f or > 0.25f ||
            !float.IsFinite(taperPower) ||
            taperPower is < 0.5f or > 2.5f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(baseRadius),
                "Spikes require 4..12 sides, 0 <= tip radius < base radius <= 0.5, " +
                "irregularity in 0..0.25 and taper power in 0.5..2.5.");
        }

        return new BlockShapeDefinition(
            BlockShapeKind.Spike, 1f,
            BlockLayerPlacement.Surface, null, 0f,
            baseRadius, tipRadius, sides,
            irregularity, taperPower);
    }

    private static void ValidateThickness(float thickness)
    {
        if (!float.IsFinite(thickness) || thickness <= 0f || thickness > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(thickness), "Layer thickness must be within (0, 1].");
        }
    }

    private static void ValidateNamespacedId(string id, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(id) || id != id.Trim())
        {
            throw new ArgumentException("Block id must be non-empty and trimmed.", parameterName);
        }

        var separator = id.IndexOf(':');
        if (separator <= 0 || separator == id.Length - 1 || id.IndexOf(':', separator + 1) >= 0)
        {
            throw new ArgumentException("Block id must use the namespaced form namespace:name.", parameterName);
        }
    }
}

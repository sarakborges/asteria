namespace Asteria.Core.World;

public abstract class BiomeTerrainShapeDefinition
{
    public abstract float MaximumHeightOffset { get; }
}

public sealed class BiomeNoiseTerrainShapeDefinition :
    BiomeTerrainShapeDefinition
{
    public BiomeNoiseTerrainShapeDefinition(
        float baseHeightOffset,
        float macroAmplitude,
        uint macroScale,
        float detailAmplitude,
        uint detailScale)
    {
        if (!float.IsFinite(baseHeightOffset))
        {
            throw new ArgumentOutOfRangeException(nameof(baseHeightOffset));
        }

        ValidateNoise(macroAmplitude, macroScale, nameof(macroAmplitude), nameof(macroScale));
        ValidateNoise(detailAmplitude, detailScale, nameof(detailAmplitude), nameof(detailScale));

        if (detailScale > macroScale)
        {
            throw new ArgumentException(
                "Biome detail scale cannot exceed macro scale.",
                nameof(detailScale));
        }

        BaseHeightOffset = baseHeightOffset;
        MacroAmplitude = macroAmplitude;
        MacroScale = macroScale;
        DetailAmplitude = detailAmplitude;
        DetailScale = detailScale;
    }

    public float BaseHeightOffset { get; }
    public float MacroAmplitude { get; }
    public uint MacroScale { get; }
    public float DetailAmplitude { get; }
    public uint DetailScale { get; }

    public override float MaximumHeightOffset =>
        BaseHeightOffset +
        Math.Abs(MacroAmplitude) +
        Math.Abs(DetailAmplitude);

    private static void ValidateNoise(
        float amplitude,
        uint scale,
        string amplitudeName,
        string scaleName)
    {
        if (!float.IsFinite(amplitude) ||
            amplitude < 0f ||
            amplitude > 512f)
        {
            throw new ArgumentOutOfRangeException(
                amplitudeName,
                "Terrain amplitude must be finite and within 0..512.");
        }

        if (scale is < 2 or > 16_384)
        {
            throw new ArgumentOutOfRangeException(
                scaleName,
                "Terrain scale must be within 2..16384.");
        }
    }
}

public sealed class BiomeRollingTerrainShapeDefinition :
    BiomeTerrainShapeDefinition
{
    public BiomeRollingTerrainShapeDefinition(
        float baseHeight,
        float amplitude,
        float scale,
        float detailAmplitude,
        float detailScale)
    {
        BaseHeight = TerrainValue.Finite(baseHeight, nameof(baseHeight));
        Amplitude = TerrainValue.NonNegative(amplitude, nameof(amplitude));
        Scale = TerrainValue.Positive(scale, nameof(scale));
        DetailAmplitude = TerrainValue.NonNegative(detailAmplitude, nameof(detailAmplitude));
        DetailScale = TerrainValue.Positive(detailScale, nameof(detailScale));
    }

    public float BaseHeight { get; }
    public float Amplitude { get; }
    public float Scale { get; }
    public float DetailAmplitude { get; }
    public float DetailScale { get; }

    public override float MaximumHeightOffset =>
        BaseHeight + Amplitude + DetailAmplitude;
}

public sealed class BiomeDunesTerrainShapeDefinition :
    BiomeTerrainShapeDefinition
{
    public BiomeDunesTerrainShapeDefinition(
        float baseHeight,
        float amplitude,
        float scale,
        float sharpness,
        float warpScale,
        float warpStrength,
        float detailAmplitude,
        float detailScale)
    {
        BaseHeight = TerrainValue.Finite(baseHeight, nameof(baseHeight));
        Amplitude = TerrainValue.NonNegative(amplitude, nameof(amplitude));
        Scale = TerrainValue.Positive(scale, nameof(scale));
        Sharpness = TerrainValue.Positive(sharpness, nameof(sharpness));
        WarpScale = TerrainValue.Positive(warpScale, nameof(warpScale));
        WarpStrength = TerrainValue.NonNegative(warpStrength, nameof(warpStrength));
        DetailAmplitude = TerrainValue.NonNegative(detailAmplitude, nameof(detailAmplitude));
        DetailScale = TerrainValue.Positive(detailScale, nameof(detailScale));
    }

    public float BaseHeight { get; }
    public float Amplitude { get; }
    public float Scale { get; }
    public float Sharpness { get; }
    public float WarpScale { get; }
    public float WarpStrength { get; }
    public float DetailAmplitude { get; }
    public float DetailScale { get; }

    public override float MaximumHeightOffset =>
        BaseHeight + Amplitude + DetailAmplitude;
}

public sealed class BiomeOceanTerrainShapeDefinition :
    BiomeTerrainShapeDefinition
{
    public BiomeOceanTerrainShapeDefinition(
        float depth,
        float amplitude,
        float scale,
        float detailAmplitude,
        float detailScale)
    {
        Depth = TerrainValue.Positive(depth, nameof(depth));
        Amplitude = TerrainValue.NonNegative(amplitude, nameof(amplitude));
        Scale = TerrainValue.Positive(scale, nameof(scale));
        DetailAmplitude = TerrainValue.NonNegative(detailAmplitude, nameof(detailAmplitude));
        DetailScale = TerrainValue.Positive(detailScale, nameof(detailScale));
    }

    public float Depth { get; }
    public float Amplitude { get; }
    public float Scale { get; }
    public float DetailAmplitude { get; }
    public float DetailScale { get; }

    public override float MaximumHeightOffset =>
        -Depth + Amplitude + DetailAmplitude;
}

public sealed class BiomeSwampTerrainShapeDefinition :
    BiomeTerrainShapeDefinition
{
    public BiomeSwampTerrainShapeDefinition(
        float baseHeight,
        float depth,
        float amplitude,
        float scale,
        float detailAmplitude,
        float detailScale)
    {
        BaseHeight = TerrainValue.Finite(baseHeight, nameof(baseHeight));
        Depth = TerrainValue.Positive(depth, nameof(depth));
        Amplitude = TerrainValue.NonNegative(amplitude, nameof(amplitude));
        Scale = TerrainValue.Positive(scale, nameof(scale));
        DetailAmplitude = TerrainValue.NonNegative(detailAmplitude, nameof(detailAmplitude));
        DetailScale = TerrainValue.Positive(detailScale, nameof(detailScale));
    }

    public float BaseHeight { get; }
    public float Depth { get; }
    public float Amplitude { get; }
    public float Scale { get; }
    public float DetailAmplitude { get; }
    public float DetailScale { get; }

    public override float MaximumHeightOffset =>
        BaseHeight + Amplitude + DetailAmplitude;
}

public sealed class BiomeMountainsTerrainShapeDefinition :
    BiomeTerrainShapeDefinition
{
    public BiomeMountainsTerrainShapeDefinition(
        float baseHeight,
        float amplitude,
        float scale,
        float sharpness)
    {
        BaseHeight = TerrainValue.Finite(baseHeight, nameof(baseHeight));
        Amplitude = TerrainValue.NonNegative(amplitude, nameof(amplitude));
        Scale = TerrainValue.Positive(scale, nameof(scale));
        Sharpness = TerrainValue.Positive(sharpness, nameof(sharpness));
    }

    public float BaseHeight { get; }
    public float Amplitude { get; }
    public float Scale { get; }
    public float Sharpness { get; }

    public override float MaximumHeightOffset =>
        BaseHeight + Amplitude;
}

public sealed class BiomeGorgeTerrainShapeDefinition :
    BiomeTerrainShapeDefinition
{
    public BiomeGorgeTerrainShapeDefinition(
        float baseHeight,
        float depth,
        float wallHeight,
        float topAmplitude,
        float topScale,
        float floorAmplitude,
        float floorScale)
    {
        BaseHeight = TerrainValue.Finite(baseHeight, nameof(baseHeight));
        Depth = TerrainValue.NonNegative(depth, nameof(depth));
        WallHeight = TerrainValue.NonNegative(wallHeight, nameof(wallHeight));
        TopAmplitude = TerrainValue.NonNegative(topAmplitude, nameof(topAmplitude));
        TopScale = TerrainValue.Positive(topScale, nameof(topScale));
        FloorAmplitude = TerrainValue.NonNegative(floorAmplitude, nameof(floorAmplitude));
        FloorScale = TerrainValue.Positive(floorScale, nameof(floorScale));
    }

    public float BaseHeight { get; }
    public float Depth { get; }
    public float WallHeight { get; }
    public float TopAmplitude { get; }
    public float TopScale { get; }
    public float FloorAmplitude { get; }
    public float FloorScale { get; }

    public override float MaximumHeightOffset =>
        BaseHeight + WallHeight + TopAmplitude + FloorAmplitude;
}

public sealed class BiomeAlpsTerrainShapeDefinition :
    BiomeTerrainShapeDefinition
{
    public BiomeAlpsTerrainShapeDefinition(
        float baseHeight,
        float amplitude,
        float scale,
        float sharpness,
        float detailAmplitude,
        float detailScale)
    {
        BaseHeight = TerrainValue.Finite(baseHeight, nameof(baseHeight));
        Amplitude = TerrainValue.NonNegative(amplitude, nameof(amplitude));
        Scale = TerrainValue.Positive(scale, nameof(scale));
        Sharpness = TerrainValue.Positive(sharpness, nameof(sharpness));
        DetailAmplitude = TerrainValue.NonNegative(detailAmplitude, nameof(detailAmplitude));
        DetailScale = TerrainValue.Positive(detailScale, nameof(detailScale));
    }

    public float BaseHeight { get; }
    public float Amplitude { get; }
    public float Scale { get; }
    public float Sharpness { get; }
    public float DetailAmplitude { get; }
    public float DetailScale { get; }

    public override float MaximumHeightOffset =>
        BaseHeight + Amplitude + DetailAmplitude;
}

public sealed class BiomeMountainBeltTerrainShapeDefinition :
    BiomeTerrainShapeDefinition
{
    public BiomeMountainBeltTerrainShapeDefinition(
        float baseHeight,
        float amplitude,
        float scale,
        float sharpness,
        float detailAmplitude,
        float detailScale)
    {
        BaseHeight = TerrainValue.Finite(baseHeight, nameof(baseHeight));
        Amplitude = TerrainValue.NonNegative(amplitude, nameof(amplitude));
        Scale = TerrainValue.Positive(scale, nameof(scale));
        Sharpness = TerrainValue.Positive(sharpness, nameof(sharpness));
        DetailAmplitude = TerrainValue.NonNegative(detailAmplitude, nameof(detailAmplitude));
        DetailScale = TerrainValue.Positive(detailScale, nameof(detailScale));
    }

    public float BaseHeight { get; }
    public float Amplitude { get; }
    public float Scale { get; }
    public float Sharpness { get; }
    public float DetailAmplitude { get; }
    public float DetailScale { get; }

    public override float MaximumHeightOffset =>
        BaseHeight + Amplitude + DetailAmplitude;
}

public sealed class BiomeVolcanoTerrainShapeDefinition :
    BiomeTerrainShapeDefinition
{
    public BiomeVolcanoTerrainShapeDefinition(
        float baseHeight,
        float height,
        float craterDepth,
        float craterRadius,
        float irregularity,
        float irregularityScale,
        float detailIrregularity,
        float detailScale,
        float craterIrregularity)
    {
        BaseHeight = TerrainValue.Finite(baseHeight, nameof(baseHeight));
        Height = TerrainValue.Positive(height, nameof(height));
        CraterDepth = TerrainValue.NonNegative(craterDepth, nameof(craterDepth));
        CraterRadius = TerrainValue.OpenUnit(craterRadius, nameof(craterRadius));
        Irregularity = TerrainValue.NonNegative(irregularity, nameof(irregularity));
        IrregularityScale = TerrainValue.Positive(irregularityScale, nameof(irregularityScale));
        DetailIrregularity = TerrainValue.NonNegative(detailIrregularity, nameof(detailIrregularity));
        DetailScale = TerrainValue.Positive(detailScale, nameof(detailScale));
        CraterIrregularity = TerrainValue.NonNegative(craterIrregularity, nameof(craterIrregularity));
    }

    public float BaseHeight { get; }
    public float Height { get; }
    public float CraterDepth { get; }
    public float CraterRadius { get; }
    public float Irregularity { get; }
    public float IrregularityScale { get; }
    public float DetailIrregularity { get; }
    public float DetailScale { get; }
    public float CraterIrregularity { get; }

    public override float MaximumHeightOffset =>
        BaseHeight + Height;
}

public abstract class BiomeTerrainModifierDefinition
{
    public abstract float MaximumHeightOffset { get; }
}

public sealed class BiomeHeightOffsetTerrainModifierDefinition :
    BiomeTerrainModifierDefinition
{
    public BiomeHeightOffsetTerrainModifierDefinition(float height)
    {
        Height = TerrainValue.Finite(height, nameof(height));
    }

    public float Height { get; }

    public override float MaximumHeightOffset =>
        Math.Max(0f, Height);
}

public sealed class BiomeCliffsTerrainModifierDefinition :
    BiomeTerrainModifierDefinition
{
    public BiomeCliffsTerrainModifierDefinition(
        float scale,
        float threshold,
        float height,
        float edgeWidth,
        float warpScale,
        float warpStrength)
    {
        Scale = TerrainValue.Positive(scale, nameof(scale));
        Threshold = TerrainValue.ClosedUnit(threshold, nameof(threshold));
        Height = TerrainValue.NonNegative(height, nameof(height));
        EdgeWidth = TerrainValue.OpenClosedUnit(edgeWidth, nameof(edgeWidth));
        WarpScale = TerrainValue.Positive(warpScale, nameof(warpScale));
        WarpStrength = TerrainValue.NonNegative(warpStrength, nameof(warpStrength));
    }

    public float Scale { get; }
    public float Threshold { get; }
    public float Height { get; }
    public float EdgeWidth { get; }
    public float WarpScale { get; }
    public float WarpStrength { get; }

    public override float MaximumHeightOffset =>
        Height;
}

internal static class TerrainValue
{
    public static float Finite(float value, string name)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name);
        }

        return value;
    }

    public static float NonNegative(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0f)
        {
            throw new ArgumentOutOfRangeException(name);
        }

        return value;
    }

    public static float Positive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new ArgumentOutOfRangeException(name);
        }

        return value;
    }

    public static float ClosedUnit(float value, string name)
    {
        if (!float.IsFinite(value) || value is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(name);
        }

        return value;
    }

    public static float OpenUnit(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0f || value >= 1f)
        {
            throw new ArgumentOutOfRangeException(name);
        }

        return value;
    }

    public static float OpenClosedUnit(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0f || value > 1f)
        {
            throw new ArgumentOutOfRangeException(name);
        }

        return value;
    }
}

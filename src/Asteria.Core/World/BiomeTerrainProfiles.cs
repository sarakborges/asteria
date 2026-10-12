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
        float detailScale,
        float waveDirectionZ = 0.35f,
        float broadScaleMultiplier = 0.55f,
        float waveWeight = 0.72f)
    {
        WaveDirectionZ = TerrainValue.Finite(waveDirectionZ, nameof(waveDirectionZ));
        BroadScaleMultiplier = TerrainValue.Positive(broadScaleMultiplier, nameof(broadScaleMultiplier));
        WaveWeight = TerrainValue.ClosedUnit(waveWeight, nameof(waveWeight));
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
    public float WaveDirectionZ { get; }
    public float BroadScaleMultiplier { get; }
    public float WaveWeight { get; }

    public override float MaximumHeightOffset =>
        BaseHeight + Amplitude + DetailAmplitude;
}

public enum BiomeRidgeDetailMode
{
    None,
    Ridged,
    Modulated,
}

public sealed class BiomeRidgesTerrainShapeDefinition :
    BiomeTerrainShapeDefinition
{
    public BiomeRidgesTerrainShapeDefinition(
        float baseHeight,
        float amplitude,
        float scale,
        float sharpness,
        float detailAmplitude = 0f,
        float detailScale = 0.02f,
        BiomeRidgeDetailMode detailMode = BiomeRidgeDetailMode.None,
        float detailSharpness = 1.35f)
    {
        BaseHeight = TerrainValue.Finite(baseHeight, nameof(baseHeight));
        Amplitude = TerrainValue.NonNegative(amplitude, nameof(amplitude));
        Scale = TerrainValue.Positive(scale, nameof(scale));
        Sharpness = TerrainValue.Positive(sharpness, nameof(sharpness));
        DetailAmplitude = TerrainValue.NonNegative(detailAmplitude, nameof(detailAmplitude));
        DetailScale = TerrainValue.Positive(detailScale, nameof(detailScale));
        DetailSharpness = TerrainValue.Positive(detailSharpness, nameof(detailSharpness));
        if (!Enum.IsDefined(detailMode) ||
            (detailAmplitude > 0f && detailMode == BiomeRidgeDetailMode.None))
        {
            throw new ArgumentException(
                "A nonzero ridge detail amplitude requires a valid detail mode.",
                nameof(detailMode));
        }

        DetailMode = detailMode;
    }

    public float BaseHeight { get; }
    public float Amplitude { get; }
    public float Scale { get; }
    public float Sharpness { get; }
    public float DetailAmplitude { get; }
    public float DetailScale { get; }
    public BiomeRidgeDetailMode DetailMode { get; }
    public float DetailSharpness { get; }

    public override float MaximumHeightOffset =>
        BaseHeight + Amplitude + DetailAmplitude;
}

public sealed class BiomeValleyTerrainShapeDefinition :
    BiomeTerrainShapeDefinition
{
    public BiomeValleyTerrainShapeDefinition(
        float baseHeight,
        float depth,
        float wallHeight,
        float topAmplitude,
        float topScale,
        float floorAmplitude,
        float floorScale,
        float rimFalloff = 0.8f,
        float floorFalloff = 1.35f)
    {
        RimFalloff = TerrainValue.Positive(rimFalloff, nameof(rimFalloff));
        FloorFalloff = TerrainValue.Positive(floorFalloff, nameof(floorFalloff));
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
    public float RimFalloff { get; }
    public float FloorFalloff { get; }

    public override float MaximumHeightOffset =>
        BaseHeight + WallHeight + TopAmplitude + FloorAmplitude;
}

/// <summary>
/// A radial mountain and its enclosed caldera, sharing the authoritative
/// biome formation center. Blocks and fluid are selected by authored data.
/// </summary>
public sealed class BiomeCalderaTerrainShapeDefinition :
    BiomeTerrainShapeDefinition
{
    public BiomeCalderaTerrainShapeDefinition(
        float baseHeight,
        float height,
        float craterRadius,
        float floorRadius,
        float craterDepth,
        float flankRoughness,
        float flankNoiseScale,
        float detailRoughness,
        float detailScale,
        BiomeCalderaFluidFillDefinition? fluidFill = null)
    {
        BaseHeight = TerrainValue.Finite(baseHeight, nameof(baseHeight));
        Height = TerrainValue.Positive(height, nameof(height));
        CraterRadius = TerrainValue.OpenUnit(craterRadius, nameof(craterRadius));
        FloorRadius = TerrainValue.OpenUnit(floorRadius, nameof(floorRadius));
        CraterDepth = TerrainValue.Positive(craterDepth, nameof(craterDepth));
        FlankRoughness = TerrainValue.NonNegative(flankRoughness, nameof(flankRoughness));
        FlankNoiseScale = TerrainValue.Positive(flankNoiseScale, nameof(flankNoiseScale));
        DetailRoughness = TerrainValue.NonNegative(detailRoughness, nameof(detailRoughness));
        DetailScale = TerrainValue.Positive(detailScale, nameof(detailScale));

        if (FloorRadius >= CraterRadius)
            throw new ArgumentException("Caldera floor radius must be smaller than crater radius.");
        if (FlankRoughness + DetailRoughness > Height * 0.18f)
            throw new ArgumentException("Caldera flank noise must remain subordinate to mountain height.");

        // BiomeField samples a smooth radial strength. Terrain and fluid
        // use the same thresholds, independent of chunk or world position.
        RimStrength = WorldGenerationEntropy.SmoothStep(1d - CraterRadius);
        FloorStrength = WorldGenerationEntropy.SmoothStep(1d - FloorRadius);
        if (fluidFill is { } fill &&
            (fill.TopLevel <= BaseHeight + Height - CraterDepth + 1f ||
             fill.TopLevel >= BaseHeight + Height - 2f))
            throw new ArgumentException(
                "Caldera fluid must be above the floor and below the entire rim.",
                nameof(fluidFill));

        FluidFill = fluidFill;
    }

    public float BaseHeight { get; }
    public float Height { get; }
    public float CraterRadius { get; }
    public float FloorRadius { get; }
    public float CraterDepth { get; }
    public float FlankRoughness { get; }
    public float FlankNoiseScale { get; }
    public float DetailRoughness { get; }
    public float DetailScale { get; }
    public double RimStrength { get; }
    public double FloorStrength { get; }
    public BiomeCalderaFluidFillDefinition? FluidFill { get; }

    public override float MaximumHeightOffset =>
        BaseHeight + Height + FlankRoughness + DetailRoughness;
}

public sealed class BiomeCalderaFluidFillDefinition
{
    public BiomeCalderaFluidFillDefinition(string fluid, float topLevel)
    {
        FluidDefinition.ValidateId(fluid);
        Fluid = fluid;
        TopLevel = TerrainValue.Finite(topLevel, nameof(topLevel));
    }

    public string Fluid { get; }
    /// <summary>Height relative to the dimension sea level.</summary>
    public float TopLevel { get; }
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

public sealed class BiomeDepressionsTerrainModifierDefinition :
    BiomeTerrainModifierDefinition
{
    public BiomeDepressionsTerrainModifierDefinition(
        float depth,
        float broadScale,
        float detailScale,
        float broadWeight,
        float bias,
        float transitionWidth,
        float sharpness)
    {
        Depth = TerrainValue.NonNegative(depth, nameof(depth));
        BroadScale = TerrainValue.Positive(broadScale, nameof(broadScale));
        DetailScale = TerrainValue.Positive(detailScale, nameof(detailScale));
        BroadWeight = TerrainValue.ClosedUnit(broadWeight, nameof(broadWeight));
        Bias = TerrainValue.Finite(bias, nameof(bias));
        TransitionWidth = TerrainValue.Positive(transitionWidth, nameof(transitionWidth));
        Sharpness = TerrainValue.Positive(sharpness, nameof(sharpness));
    }

    public float Depth { get; }
    public float BroadScale { get; }
    public float DetailScale { get; }
    public float BroadWeight { get; }
    public float Bias { get; }
    public float TransitionWidth { get; }
    public float Sharpness { get; }

    // Depressions only subtract terrain height.
    public override float MaximumHeightOffset => 0f;
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

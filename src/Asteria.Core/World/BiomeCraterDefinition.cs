namespace Asteria.Core.World;

/// <summary>
/// Formation-centered depression, independent of the base surface shape.
/// Its fluid fill is optional and uses a sea-level-relative top surface.
/// </summary>
public sealed class BiomeCraterDefinition
{
    public BiomeCraterDefinition(
        float depth,
        float radius,
        float irregularity,
        float noiseScale,
        float transitionWidth,
        BiomeCraterFluidFillDefinition? fluidFill = null)
    {
        Depth = TerrainValue.NonNegative(depth, nameof(depth));
        Radius = TerrainValue.OpenUnit(radius, nameof(radius));
        Irregularity = TerrainValue.NonNegative(irregularity, nameof(irregularity));
        NoiseScale = TerrainValue.Positive(noiseScale, nameof(noiseScale));
        TransitionWidth = TerrainValue.OpenClosedUnit(transitionWidth, nameof(transitionWidth));
        FluidFill = fluidFill;
    }

    public float Depth { get; }
    public float Radius { get; }
    public float Irregularity { get; }
    public float NoiseScale { get; }
    public float TransitionWidth { get; }
    public BiomeCraterFluidFillDefinition? FluidFill { get; }
}

public sealed class BiomeCraterFluidFillDefinition
{
    public BiomeCraterFluidFillDefinition(
        string fluid,
        float minimumStrength,
        float topLevel,
        BiomeCraterSpillDefinition? spill = null)
    {
        FluidDefinition.ValidateId(fluid);
        Fluid = fluid;
        MinimumStrength = TerrainValue.ClosedUnit(minimumStrength, nameof(minimumStrength));
        TopLevel = TerrainValue.Finite(topLevel, nameof(topLevel));
        if (spill is not null && spill.MaximumStrength > MinimumStrength)
        {
            throw new ArgumentException(
                "Crater spill maximum strength must not exceed fill minimum strength.",
                nameof(spill));
        }

        Spill = spill;
    }

    public string Fluid { get; }
    public float MinimumStrength { get; }
    public float TopLevel { get; }
    public BiomeCraterSpillDefinition? Spill { get; }
}

public sealed class BiomeCraterSpillDefinition
{
    public BiomeCraterSpillDefinition(
        float minimumStrength,
        float maximumStrength,
        float scale,
        float width,
        byte level)
    {
        MinimumStrength = TerrainValue.ClosedUnit(minimumStrength, nameof(minimumStrength));
        MaximumStrength = TerrainValue.ClosedUnit(maximumStrength, nameof(maximumStrength));
        if (MaximumStrength < MinimumStrength)
        {
            throw new ArgumentException("Crater spill maximum strength must be >= minimum strength.");
        }

        Scale = TerrainValue.Positive(scale, nameof(scale));
        Width = TerrainValue.OpenClosedUnit(width, nameof(width));
        if (level is < FluidCell.MinLevel or > FluidCell.MaxLevel)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        Level = level;
    }

    public float MinimumStrength { get; }
    public float MaximumStrength { get; }
    public float Scale { get; }
    public float Width { get; }
    public byte Level { get; }
}

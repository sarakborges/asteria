namespace Asteria.Core.World;

public abstract class BiomeSurfaceFluidDefinition
{
    protected BiomeSurfaceFluidDefinition(string fluid)
    {
        FluidDefinition.ValidateId(fluid);
        Fluid = fluid;
    }

    public string Fluid { get; }
}

public sealed class BiomeVolcanoCraterFluidDefinition :
    BiomeSurfaceFluidDefinition
{
    public BiomeVolcanoCraterFluidDefinition(
        string fluid,
        float minimumStrength,
        float levelOffset,
        float spillMinimumStrength,
        float spillMaximumStrength,
        float spillScale,
        float spillWidth,
        byte spillLevel)
        : base(fluid)
    {
        MinimumStrength = TerrainValue.ClosedUnit(minimumStrength, nameof(minimumStrength));
        LevelOffset = TerrainValue.NonNegative(levelOffset, nameof(levelOffset));
        SpillMinimumStrength = TerrainValue.ClosedUnit(spillMinimumStrength, nameof(spillMinimumStrength));
        SpillMaximumStrength = TerrainValue.ClosedUnit(spillMaximumStrength, nameof(spillMaximumStrength));
        SpillScale = TerrainValue.Positive(spillScale, nameof(spillScale));
        SpillWidth = TerrainValue.OpenClosedUnit(spillWidth, nameof(spillWidth));

        if (SpillMaximumStrength < SpillMinimumStrength ||
            SpillMaximumStrength > MinimumStrength)
        {
            throw new ArgumentException(
                "Volcano spill strength must satisfy minimum <= maximum <= crater minimum.");
        }

        if (spillLevel is < FluidCell.MinLevel or > FluidCell.MaxLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(spillLevel),
                $"Volcano spill level must be within {FluidCell.MinLevel}..{FluidCell.MaxLevel}.");
        }

        SpillLevel = spillLevel;
    }

    public float MinimumStrength { get; }
    public float LevelOffset { get; }
    public float SpillMinimumStrength { get; }
    public float SpillMaximumStrength { get; }
    public float SpillScale { get; }
    public float SpillWidth { get; }
    public byte SpillLevel { get; }
}

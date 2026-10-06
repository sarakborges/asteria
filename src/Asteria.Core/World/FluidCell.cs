namespace Asteria.Core.World;

public readonly record struct FluidCell
{
    public const byte MinLevel = 1;
    public const byte MaxLevel = 8;

    private FluidCell(
        FluidRuntimeId fluid,
        byte level,
        bool source,
        ushort spreadDistance)
    {
        if (fluid.IsNone)
        {
            throw new ArgumentException(
                "A non-empty fluid cell requires a fluid id.",
                nameof(fluid));
        }

        if (level is < MinLevel or > MaxLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                $"Fluid level must be within {MinLevel}..{MaxLevel}.");
        }

        Fluid = fluid;
        Level = level;
        IsSource = source;
        SpreadDistance = spreadDistance;
    }

    public FluidRuntimeId Fluid { get; }

    public byte Level { get; }

    public bool IsSource { get; }

    public ushort SpreadDistance { get; }

    public bool IsEmpty => Fluid.IsNone;

    public float Height =>
        IsEmpty
            ? 0f
            : Level / (float)MaxLevel;

    public static FluidCell Empty => default;

    public static FluidCell Source(
        FluidRuntimeId fluid,
        byte level = MaxLevel) =>
        new(
            fluid,
            level,
            source: true,
            spreadDistance: 0);

    public static FluidCell Spreading(
        FluidRuntimeId fluid,
        byte level,
        ushort spreadDistance) =>
        new(
            fluid,
            level,
            source: false,
            spreadDistance);
}

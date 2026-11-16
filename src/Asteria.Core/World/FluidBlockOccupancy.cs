namespace Asteria.Core.World;

/// <summary>
/// Shared occupancy rule for fluid generation, editing, simulation and meshing.
/// </summary>
public static class FluidBlockOccupancy
{
    public static bool AllowsFluid(BlockDefinition block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return !block.IsCollidable &&
               block.Visual.Kind is
                   BlockVisualKind.CrossedSprite or BlockVisualKind.GroundSprite;
    }
}

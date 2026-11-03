namespace Asteria.Core.World;

/// <summary>
/// Selects the spatial model used to assign authored geological materials.
/// The noise-field model remains the default; weighted cellular is an
/// experimental alternative for profiling and visual validation.
/// </summary>
public enum BiomeGeologySelectionAlgorithm : byte
{
    NoiseFields,
    WeightedCellular,
}

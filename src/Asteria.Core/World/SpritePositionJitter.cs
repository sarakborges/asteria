using System.Numerics;

namespace Asteria.Core.World;

/// <summary>
/// Stable visual-only horizontal scatter for voxel-backed decorative sprites.
/// Vertices remain inside the owning voxel, preserving meshlet bounds.
/// </summary>
internal static class SpritePositionJitter
{
    public static Vector2 For(BlockDefinition definition, WorldVoxelCoord position)
    {
        var visual = definition.Visual;
        if (visual.Kind == BlockVisualKind.Geometry)
            return Vector2.Zero;

        var margin = (1f - visual.Width) * 0.5f;
        var maximumX = MathF.Min(visual.PositionJitter.X, margin);
        var maximumZ = MathF.Min(visual.PositionJitter.Y, margin);
        if (maximumX == 0f && maximumZ == 0f)
            return Vector2.Zero;

        var domain = definition.SpriteJitterDomain;
        var x = WorldGenerationEntropy.Sample3D(
            0UL, domain, position.X, position.Y, position.Z);
        var z = WorldGenerationEntropy.Sample3D(
            1UL, domain, position.X, position.Y, position.Z);
        return new Vector2(
            (float)WorldGenerationEntropy.SignedUnit(x) * maximumX,
            (float)WorldGenerationEntropy.SignedUnit(z) * maximumZ);
    }
}

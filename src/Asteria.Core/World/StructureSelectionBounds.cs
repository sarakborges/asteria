namespace Asteria.Core.World;

/// <summary>Inclusive voxel bounds; checked sizing before visiting any voxel.</summary>
public readonly record struct StructureSelectionBounds(
    WorldVoxelCoord Minimum,
    WorldVoxelCoord Maximum,
    int Width,
    int Height,
    int Depth,
    int Volume)
{
    public const int MaximumAxisLength = 128;
    public const int MaximumHeight = 256;
    public const int MaximumSelectionVolume = 16_384;

    public static bool TryCreate(
        WorldVoxelCoord a, WorldVoxelCoord b,
        out StructureSelectionBounds bounds, out string error)
    {
        bounds = default;
        error = "";
        var min = new WorldVoxelCoord(
            Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
        var max = new WorldVoxelCoord(
            Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
        if (min.Y < 0)
        {
            error = "Selection cannot contain negative world Y.";
            return false;
        }
        var width = (long)max.X - min.X + 1;
        var height = (long)max.Y - min.Y + 1;
        var depth = (long)max.Z - min.Z + 1;
        if (width > MaximumAxisLength || height > MaximumHeight ||
            depth > MaximumAxisLength ||
            width * height * depth > MaximumSelectionVolume)
        {
            error = $"Selection exceeds the supported {MaximumAxisLength}x{MaximumHeight}x{MaximumAxisLength} axis limits or {MaximumSelectionVolume} voxel budget.";
            return false;
        }

        bounds = new StructureSelectionBounds(
            min, max, (int)width, (int)height, (int)depth,
            checked((int)(width * height * depth)));
        return true;
    }
}

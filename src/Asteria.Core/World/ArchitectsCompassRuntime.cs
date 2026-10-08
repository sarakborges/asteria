using Asteria.Core.Content;

namespace Asteria.Core.World;

public enum ArchitectsCompassResult : byte
{
    Rejected,
    Started,
    Exported,
    Failed
}

/// <summary>
/// Session-owned two-point selection. All exported data is read from loaded
/// authoritative voxels; no generator queries or world writes are performed.
/// </summary>
public sealed class ArchitectsCompassRuntime
{
    private readonly VoxelWorld _world;
    private readonly BlockRegistry _blocks;
    private readonly PackContentRegistry<ToolDefinition> _tools;
    private readonly DyeRegistry? _dyes;
    private readonly AttachedLayerRegistry? _layers;
    private int? _selectionSlot;

    public ArchitectsCompassRuntime(
        VoxelWorld world, BlockRegistry blocks,
        PackContentRegistry<ToolDefinition> tools,
        DyeRegistry? dyes = null,
        AttachedLayerRegistry? layers = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _dyes = dyes;
        _layers = layers;
    }

    public WorldVoxelCoord? SelectionStart { get; private set; }

    public bool IsEquipped(InventoryStack? selected) =>
        selected?.Kind == InventoryEntryKind.Tool &&
        _tools.TryGet(selected.Id, out var tool) &&
        tool?.RightBehavior == "asteria:structure/select";

    public bool ClearUnlessEquipped(InventoryStack? selected, int slot)
    {
        if (SelectionStart is null)
            return false;
        if (_selectionSlot == slot && IsEquipped(selected))
            return false;
        Clear();
        return true;
    }

    public void Clear()
    {
        SelectionStart = null;
        _selectionSlot = null;
    }

    public ArchitectsCompassResult Select(
        InventoryStack? selected,
        int selectedSlot,
        VoxelWorldHit hit,
        string exportId,
        out StructureSelectionExport? export,
        out string message)
    {
        export = null;
        message = "";
        if (!IsEquipped(selected) ||
            !VoxelHitFace.TryResolve(hit, out _) ||
            !TryPlacementPosition(hit, out var point))
            return ArchitectsCompassResult.Rejected;

        if (SelectionStart is null || _selectionSlot != selectedSlot)
        {
            SelectionStart = point;
            _selectionSlot = selectedSlot;
            message = $"Selection started at X={point.X}, Y={point.Y}, Z={point.Z}.";
            return ArchitectsCompassResult.Started;
        }

        if (!StructureSelectionBounds.TryCreate(SelectionStart.Value, point,
                out var bounds, out message) ||
            !StructureSelectionExporter.TryExport(
                _world, _blocks, bounds, exportId, out export, out message,
                _dyes, _layers))
            return ArchitectsCompassResult.Failed;

        Clear();
        message = $"Exported {export.OccupiedBlocks} blocks across {export.Volume} selected voxels.";
        return ArchitectsCompassResult.Exported;
    }

    public bool TryPreviewBounds(
        VoxelWorldHit? hit,
        out StructureSelectionBounds bounds)
    {
        bounds = default;
        if (SelectionStart is not { } first)
            return false;
        var second = hit is { } target &&
            TryPlacementPosition(target, out var point)
            ? point : first;
        return StructureSelectionBounds.TryCreate(first, second, out bounds, out _);
    }

    private bool TryPlacementPosition(VoxelWorldHit hit, out WorldVoxelCoord point)
    {
        point = default;
        var x = (long)hit.Voxel.X + hit.NormalX;
        var y = (long)hit.Voxel.Y + hit.NormalY;
        var z = (long)hit.Voxel.Z + hit.NormalZ;
        if (x is < int.MinValue or > int.MaxValue ||
            y is < 0 or > int.MaxValue ||
            z is < int.MinValue or > int.MaxValue)
            return false;
        point = new WorldVoxelCoord((int)x, (int)y, (int)z);
        return _world.TryGetCell(point, out var cell) && cell.IsEmpty;
    }
}

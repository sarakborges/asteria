using System.Numerics;

namespace Asteria.Core.World;

/// <summary>Connects committed block removal to a Sphere's authoritative
/// container inventory and physical item simulation. Chunk eviction is not removal.</summary>
public sealed class StorageBoxBlockLifecycle
{
    private readonly StorageBoxRuntime _storage;
    private readonly DroppedBlockRuntime _drops;
    private readonly BlockRuntimeId? _storageBlock;

    public StorageBoxBlockLifecycle(
        StorageBoxRuntime storage,
        BlockRegistry blocks,
        DroppedBlockRuntime drops)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        ArgumentNullException.ThrowIfNull(blocks);
        _drops = drops ?? throw new ArgumentNullException(nameof(drops));
        _storageBlock = blocks.TryGetId(StorageBoxRuntime.BlockId, out var id)
            ? id
            : null;
    }

    public void OnBlockCellChanged(VoxelWorldEdit edit)
    {
        if (_storageBlock is not { } boxId ||
            edit.Previous.Block != boxId ||
            edit.Current.Block == boxId)
            return;

        var center = new Vector3(
            edit.Position.X + 0.5f,
            edit.Position.Y + 0.5f,
            edit.Position.Z + 0.5f);

        // Physical drops currently represent one item each. Keep the complete
        // portable entry (block state/metadata) when splitting stored stacks.
        foreach (var stack in _storage.Drain(edit.Position))
        {
            var single = stack.WithQuantity(1);
            for (var count = 0; count < stack.Quantity; count++)
                _drops.Spawn(single, center);
        }
    }
}

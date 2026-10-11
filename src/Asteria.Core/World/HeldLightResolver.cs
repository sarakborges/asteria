namespace Asteria.Core.World;

/// <summary>
/// Maps the authoritative selected portable block to its authored RGB
/// emission. This never creates a secondary lighting state or mutates voxels.
/// </summary>
public static class HeldLightResolver
{
    public static BlockLightEmission Resolve(
        InventoryStack? selected,
        BlockRegistry blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        if (selected is not { Kind: InventoryEntryKind.Block, Block: { } state } ||
            !blocks.TryGetId(selected.Id, out var id) ||
            state.Cell.Block != id)
            return default;

        return blocks.GetDefinition(id).LightEmission;
    }
}

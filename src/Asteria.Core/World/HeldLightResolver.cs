using Asteria.Core.Content;

namespace Asteria.Core.World;

/// <summary>
/// Derives held light from the selected portable content definition.
/// No secondary light state is stored by inventory or presentation.
/// </summary>
public static class HeldLightResolver
{
    public static BlockLightEmission Resolve(
        InventoryStack? selected,
        BlockRegistry blocks,
        PackContentRegistry<ObjectDefinition>? objects = null)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        if (selected is null) return default;

        if (selected.Kind == InventoryEntryKind.Object)
            return objects?.TryGet(selected.Id, out var definition) == true &&
                definition is not null
                ? definition.LightEmission : default;

        if (selected is not { Kind: InventoryEntryKind.Block, Block: { } state } ||
            !blocks.TryGetId(selected.Id, out var id) ||
            state.Cell.Block != id)
            return default;

        return blocks.GetDefinition(id).LightEmission;
    }
}

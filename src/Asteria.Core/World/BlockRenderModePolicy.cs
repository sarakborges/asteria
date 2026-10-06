namespace Asteria.Core.World;

public static class BlockRenderModePolicy
{
    public static bool CanGreedyMerge(
        BlockRenderMode renderMode) =>
        renderMode is
            BlockRenderMode.Opaque or
            BlockRenderMode.Cutout;

    public static bool IsOrderIndependent(
        BlockRenderMode renderMode) =>
        renderMode != BlockRenderMode.Translucent;
}

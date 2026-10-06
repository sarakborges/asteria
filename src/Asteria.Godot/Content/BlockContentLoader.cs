using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Client.Content;

public static class BlockContentLoader
{
    public static BlockRegistry LoadProjectBlocks(
        PackSelection selection) =>
        BlockRegistry.FromJson(
            ProjectDataDocuments.Load(
                selection,
                "blocks"));
}

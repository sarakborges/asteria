using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Client.Content;

public static class StructureContentLoader
{
    public static StructureRegistry LoadProjectStructures(
        PackSelection selection) =>
        StructureRegistry.FromJson(
            ProjectDataDocuments.Load(
                selection,
                "structures"));
}

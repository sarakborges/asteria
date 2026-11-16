using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Client.Content;

public static class StructureSetContentLoader
{
    public static StructureSetRegistry LoadProjectStructureSets(
        PackSelection selection) =>
        StructureSetRegistry.FromJson(
            ProjectDataDocuments.LoadOptional(
                selection,
                "structure_sets"));
}

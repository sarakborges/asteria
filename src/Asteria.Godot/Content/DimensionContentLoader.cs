using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Client.Content;

public static class DimensionContentLoader
{
    public static DimensionRegistry LoadProjectDimensions(
        PackSelection selection) =>
        DimensionRegistry.FromJson(
            ProjectDataDocuments.Load(
                selection,
                "dimensions"));
}

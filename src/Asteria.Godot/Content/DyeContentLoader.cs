using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Client.Content;

public static class DyeContentLoader
{
    public static DyeRegistry LoadProjectDyes(PackSelection selection) =>
        DyeRegistry.FromJson(ProjectDataDocuments.LoadOptional(selection, "dyes"));
}

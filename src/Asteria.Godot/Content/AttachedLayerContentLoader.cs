using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Client.Content;

public static class AttachedLayerContentLoader
{
    public static AttachedLayerRegistry LoadProjectLayers(PackSelection selection) =>
        AttachedLayerRegistry.FromJson(ProjectDataDocuments.LoadOptional(selection, "layers"));
}

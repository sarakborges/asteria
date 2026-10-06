using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Client.Content;

public static class BiomeContentLoader
{
    public static BiomeRegistry LoadProjectBiomes(
        PackSelection selection) =>
        BiomeRegistry.FromJson(
            ProjectDataDocuments.Load(
                selection,
                "biomes"));
}

using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Client.Content;

public static class FluidContentLoader
{
    public static FluidRegistry LoadProjectFluids(
        PackSelection selection) =>
        FluidRegistry.FromJson(
            ProjectDataDocuments.Load(
                selection,
                "fluids"));
}

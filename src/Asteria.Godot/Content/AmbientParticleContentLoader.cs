using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Client.Content;

public static class AmbientParticleContentLoader
{
    public static AmbientParticleRegistry LoadProjectParticles(PackSelection selection) =>
        AmbientParticleRegistry.FromJson(
            ProjectDataDocuments.LoadOptional(selection, "ambient_particles"));
}

using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Client.Content;

public static class DayNightCycleContentLoader
{
    public static DayNightCycleRegistry LoadProjectCycles(
        PackSelection selection) =>
        DayNightCycleRegistry.FromJson(
            ProjectDataDocuments.Load(
                selection,
                "day_night_cycles"));
}

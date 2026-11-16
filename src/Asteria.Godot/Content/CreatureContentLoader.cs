using Asteria.Core.Content;

namespace Asteria.Client.Content;

public static class CreatureContentLoader
{
    public static PackContentRegistry<CreatureDefinition> LoadProjectCreatures(PackSelection selection) =>
        PackContentRegistry<CreatureDefinition>.FromJson(
            ProjectDataDocuments.Load(selection, "creatures"),
            CreatureDefinition.Parse,
            creature => creature.Id);
}

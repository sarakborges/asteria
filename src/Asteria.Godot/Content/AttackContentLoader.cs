using Asteria.Core.Content;

namespace Asteria.Client.Content;

public static class AttackContentLoader
{
    public static PackContentRegistry<AttackDefinition> LoadProjectAttacks(
        PackSelection selection) =>
        PackContentRegistry<AttackDefinition>.FromJson(
            ProjectDataDocuments.Load(selection, "attacks"),
            AttackDefinition.Parse,
            attack => attack.Id);
}

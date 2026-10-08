using Asteria.Core.Content;

namespace Asteria.Client.Content;

public static class ToolContentLoader
{
    public static PackContentRegistry<ToolDefinition> LoadProjectTools(PackSelection selection) =>
        PackContentRegistry<ToolDefinition>.FromJson(
            ProjectDataDocuments.Load(selection, "tools"),
            ToolDefinition.Parse,
            tool => tool.Id);
}

using Asteria.Core.Content;

namespace Asteria.Client.Content;

/// <summary>Reads and composes immutable definitions before registries parse them.</summary>
internal static class ProjectDataDocuments
{
    public static IReadOnlyList<string> Load(PackSelection selection, string category)
    {
        var documents = ProjectPackFiles.ReadData(selection, category, required: true);
        if (documents.Count == 0)
            throw new InvalidDataException($"No active definitions remain in {category}.");
        return documents;
    }

    public static IReadOnlyList<string> LoadOptional(PackSelection selection, string category) =>
        ProjectPackFiles.ReadData(selection, category, required: false);
}

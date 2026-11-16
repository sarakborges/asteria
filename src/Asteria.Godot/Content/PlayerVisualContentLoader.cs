using Asteria.Core.Content;

namespace Asteria.Client.Content;

internal static class PlayerVisualContentLoader
{
    public static PlayerVisualDefinition Load(PackSelection selection)
    {
        var documents = ProjectDataDocuments.Load(selection, "entities");
        if (documents.Count != 1)
            throw new InvalidDataException("Exactly one local player model must be authored.");
        return PlayerVisualDefinition.Parse(documents[0]);
    }
}

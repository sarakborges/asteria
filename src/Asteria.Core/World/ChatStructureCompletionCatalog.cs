namespace Asteria.Core.World;

/// <summary>
/// Command-completion candidates derived from the same authored structure
/// registries used for placement and locate validation. The optional generated
/// root predicate comes from the active Sphere's world generator; absent a
/// world session, no structure may be suggested as currently locatable.
/// </summary>
public sealed record ChatStructureCompletionCatalog(
    IReadOnlyList<string> Structures,
    IReadOnlyList<string> LocatableStructures)
{
    public const int MaximumCandidates = 512;

    public static ChatStructureCompletionCatalog Create(
        StructureRegistry structures,
        StructureSetRegistry sets,
        Func<string, bool>? isGeneratedRoot)
    {
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(sets);

        // Grouped variants share one command reference, while authored
        // structure sets are independently placeable references.
        var references = structures.Definitions()
            .Select(definition => definition.GroupReference ?? definition.Id)
            .Concat(sets.Definitions().Select(definition => definition.Id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .Take(MaximumCandidates)
            .ToArray();

        var locatable = isGeneratedRoot is null
            ? Array.Empty<string>()
            : references.Where(id =>
            {
                // Match the command dispatcher: sets take precedence when a
                // set and a structure happen to share an authored reference.
                var enabled = sets.TryGet(id, out var set)
                    ? set.Locatable
                    : structures.ResolveReference(id)[0].Locatable;
                return enabled && isGeneratedRoot(id);
            }).ToArray();

        return new ChatStructureCompletionCatalog(references, locatable);
    }
}

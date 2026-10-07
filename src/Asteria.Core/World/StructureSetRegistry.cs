namespace Asteria.Core.World;

public sealed class StructureSetRegistry
{
    public static StructureSetRegistry Empty { get; } =
        new(
            Array.Empty<StructureSetDefinition>());

    private readonly StructureSetDefinition[] _definitions;
    private readonly Dictionary<string, StructureSetDefinition>
        _definitionsById;

    public StructureSetRegistry(
        IEnumerable<StructureSetDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(
            definitions);

        _definitions =
            definitions
                .OrderBy(
                    definition =>
                        definition.Id,
                    StringComparer.Ordinal)
                .ToArray();
        _definitionsById =
            new Dictionary<string, StructureSetDefinition>(
                _definitions.Length,
                StringComparer.Ordinal);

        foreach (var definition in
                 _definitions)
        {
            if (!_definitionsById.TryAdd(
                    definition.Id,
                    definition))
            {
                throw new ArgumentException(
                    $"Duplicate structure-set id: {definition.Id}",
                    nameof(definitions));
            }
        }
    }

    public int Count =>
        _definitions.Length;

    public IEnumerable<StructureSetDefinition> Definitions() =>
        _definitions;

    public static StructureSetRegistry FromJson(
        IEnumerable<string> documents)
    {
        ArgumentNullException.ThrowIfNull(
            documents);

        return new StructureSetRegistry(
            documents.Select(
                StructureSetDefinitionJson.Parse));
    }

    public bool TryGet(
        string id,
        out StructureSetDefinition definition) =>
        _definitionsById.TryGetValue(
            id,
            out definition!);

    public StructureSetDefinition Get(
        string id) =>
        TryGet(
            id,
            out var definition)
            ? definition
            : throw new KeyNotFoundException(
                $"Unknown structure-set id: {id}");

    public bool ResolvesReference(
        string reference) =>
        _definitionsById.ContainsKey(
            reference);

    public void ValidateStructures(
        StructureRegistry structures)
    {
        ArgumentNullException.ThrowIfNull(
            structures);

        foreach (var definition in
                 _definitions)
        {
            if (structures.ResolvesReference(
                    definition.Id))
            {
                throw new ArgumentException(
                    $"Structure-set id {definition.Id} collides with a structure/group reference.");
            }

            foreach (var element in
                     definition.Elements)
            {
                if (!structures.ResolvesReference(
                        element.Structure))
                {
                    throw new ArgumentException(
                        $"Structure set {definition.Id} element {element.Id} references missing structure/group {element.Structure}.");
                }
            }
        }
    }
}

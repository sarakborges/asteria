namespace Asteria.Core.World;

public sealed class BlockRegistry
{
    private readonly BlockDefinition[] _definitions;
    private readonly Dictionary<string, BlockRuntimeId> _idsByName;
    private readonly Dictionary<(string Family, string Key), BlockRuntimeId> _variantIds = [];

    public BlockRegistry(IEnumerable<BlockDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var authored = definitions.ToArray();
        if (authored.Length > ushort.MaxValue)
        {
            throw new ArgumentException($"A block registry supports at most {ushort.MaxValue} authored blocks.", nameof(definitions));
        }

        _definitions = new BlockDefinition[authored.Length + 1];
        _definitions[0] = BlockDefinition.Air;
        _idsByName = new Dictionary<string, BlockRuntimeId>(authored.Length + 1, StringComparer.Ordinal)
        {
            [BlockDefinition.Air.Id] = BlockRuntimeId.Air,
        };

        for (var index = 0; index < authored.Length; index++)
        {
            var definition = authored[index] ?? throw new ArgumentException("Block definitions cannot contain null entries.", nameof(definitions));
            if (definition.Id == BlockDefinition.Air.Id)
            {
                throw new ArgumentException($"{BlockDefinition.Air.Id} is reserved by the runtime.", nameof(definitions));
            }

            var runtimeId = new BlockRuntimeId(checked((ushort)(index + 1)));
            if (!_idsByName.TryAdd(definition.Id, runtimeId))
            {
                throw new ArgumentException($"Duplicate block id: {definition.Id}", nameof(definitions));
            }

            _definitions[runtimeId.Value] = definition;
        }

        for (var index = 1; index < _definitions.Length; index++)
        {
            var definition = _definitions[index];
            var runtimeId = new BlockRuntimeId(checked((ushort)index));

            if (definition.Shape.StackToBlockId is { } stackTo && !_idsByName.ContainsKey(stackTo))
            {
                throw new ArgumentException(
                    $"Layer block {definition.Id} references missing stack target {stackTo}.",
                    nameof(definitions));
            }

            if (definition.Variant is { } variant &&
                !_variantIds.TryAdd((variant.Family, variant.Key), runtimeId))
            {
                throw new ArgumentException(
                    $"Duplicate block variant {variant.Family}:{variant.Key}.",
                    nameof(definitions));
            }
        }
    }

    public int AuthoredCount => _definitions.Length - 1;

    public static BlockRegistry FromJson(IEnumerable<string> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var definitions = documents
            .Select(BlockDefinitionJson.Parse)
            .OrderBy(definition => definition.Id, StringComparer.Ordinal)
            .ToArray();

        return new BlockRegistry(definitions);
    }

    public BlockRuntimeId GetId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _idsByName.TryGetValue(id, out var runtimeId)
            ? runtimeId
            : throw new KeyNotFoundException($"Unknown block id: {id}");
    }

    public bool TryGetId(string id, out BlockRuntimeId runtimeId)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _idsByName.TryGetValue(id, out runtimeId);
    }

    public BlockDefinition GetDefinition(BlockRuntimeId runtimeId)
    {
        if (runtimeId.Value >= _definitions.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(runtimeId), $"Unknown runtime block id: {runtimeId.Value}");
        }

        return _definitions[runtimeId.Value];
    }

    public bool TryGetVariant(BlockRuntimeId source, string key, out BlockRuntimeId variantId)
    {
        ArgumentNullException.ThrowIfNull(key);

        var definition = GetDefinition(source);
        if (definition.Variant is null)
        {
            variantId = default;
            return false;
        }

        return _variantIds.TryGetValue((definition.Variant.Family, key), out variantId);
    }

    public IEnumerable<(BlockRuntimeId RuntimeId, BlockDefinition Definition)> AuthoredDefinitions()
    {
        for (var index = 1; index < _definitions.Length; index++)
        {
            var runtimeId = new BlockRuntimeId(checked((ushort)index));
            yield return (runtimeId, _definitions[index]);
        }
    }
}

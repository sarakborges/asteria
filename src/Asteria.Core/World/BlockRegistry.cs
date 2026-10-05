namespace Asteria.Core.World;

public sealed class BlockRegistry
{
    private readonly BlockDefinition[] _definitions;
    private readonly Dictionary<string, BlockRuntimeId> _idsByName;

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
    }

    public int AuthoredCount => _definitions.Length - 1;

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

    public IEnumerable<(BlockRuntimeId RuntimeId, BlockDefinition Definition)> AuthoredDefinitions()
    {
        for (ushort value = 1; value < _definitions.Length; value++)
        {
            yield return (new BlockRuntimeId(value), _definitions[value]);
        }
    }
}

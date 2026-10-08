namespace Asteria.Core.World;

public enum CreatureMetaTagAction : byte
{
    Add,
    Remove,
    Edit,
}

public enum CreatureMetaTagError : byte
{
    None,
    UnknownTag,
    AlreadyPresent,
    NotSet,
    InvalidValue,
}

/// <summary>
/// Authored MineClone creature metadata, intentionally limited to the two
/// supported tags. Presence controls behavior; optional values are persisted
/// verbatim without changing the meaning of a tag.
/// Immutable so captured creature snapshots cannot be mutated by consumers.
/// </summary>
public readonly record struct CreatureMetaTags
{
    public const string NoAiTag = "NO_AI";
    public const string PersistentTag = "PERSISTENT";
    public const int MaximumValueLength = 256;

    public bool NoAi { get; private init; }
    public string? NoAiValue { get; private init; }
    public bool Persistent { get; private init; }
    public string? PersistentValue { get; private init; }

    public bool Contains(string tag) =>
        TryNormalize(tag, out var normalized) &&
        (normalized == NoAiTag ? NoAi : Persistent);

    public bool TryChange(
        CreatureMetaTagAction action, string tag, string? value,
        out CreatureMetaTags updated, out CreatureMetaTagError error)
    {
        updated = this;
        if (!TryNormalize(tag, out var normalized))
        {
            error = CreatureMetaTagError.UnknownTag;
            return false;
        }
        if ((value?.Length ?? 0) > MaximumValueLength ||
            (action == CreatureMetaTagAction.Remove && value is not null))
        {
            error = CreatureMetaTagError.InvalidValue;
            return false;
        }

        var present = normalized == NoAiTag ? NoAi : Persistent;
        if (action == CreatureMetaTagAction.Add && present)
        {
            error = CreatureMetaTagError.AlreadyPresent;
            return false;
        }
        if (action is CreatureMetaTagAction.Remove or CreatureMetaTagAction.Edit &&
            !present)
        {
            error = CreatureMetaTagError.NotSet;
            return false;
        }

        var enabled = action != CreatureMetaTagAction.Remove;
        var storedValue = enabled ? value : null;
        updated = normalized == NoAiTag
            ? this with { NoAi = enabled, NoAiValue = storedValue }
            : this with { Persistent = enabled, PersistentValue = storedValue };
        error = CreatureMetaTagError.None;
        return true;
    }

    public static bool TryNormalize(string? tag, out string normalized)
    {
        if (string.Equals(tag, NoAiTag, StringComparison.OrdinalIgnoreCase))
        {
            normalized = NoAiTag;
            return true;
        }
        if (string.Equals(tag, PersistentTag, StringComparison.OrdinalIgnoreCase))
        {
            normalized = PersistentTag;
            return true;
        }
        normalized = "";
        return false;
    }

    public bool IsValid =>
        (!NoAi || (NoAiValue?.Length ?? 0) <= MaximumValueLength) &&
        (!Persistent || (PersistentValue?.Length ?? 0) <= MaximumValueLength) &&
        (NoAi || NoAiValue is null) &&
        (Persistent || PersistentValue is null);
}

using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class CreatureMetaTagsTests
{
    [Theory]
    [InlineData("NO_AI", true, false)]
    [InlineData("no_ai", true, false)]
    [InlineData("PERSISTENT", false, true)]
    [InlineData("persistent", false, true)]
    public void SupportedTagsNormalizeAndPreserveAuthoredValues(
        string input, bool noAi, bool persistent)
    {
        var tags = default(CreatureMetaTags);
        Assert.True(tags.TryChange(CreatureMetaTagAction.Add, input, "first",
            out var added, out var error));
        Assert.Equal(CreatureMetaTagError.None, error);
        Assert.Equal(noAi, added.NoAi);
        Assert.Equal(persistent, added.Persistent);

        Assert.True(added.TryChange(CreatureMetaTagAction.Edit, input, "second",
            out var edited, out error));
        Assert.Equal(CreatureMetaTagError.None, error);
        Assert.Equal(noAi ? "second" : null, edited.NoAiValue);
        Assert.Equal(persistent ? "second" : null, edited.PersistentValue);
        Assert.Equal(noAi ? "first" : null, added.NoAiValue);

        Assert.True(edited.TryChange(CreatureMetaTagAction.Remove, input, null,
            out var removed, out error));
        Assert.Equal(default(CreatureMetaTags), removed);
    }

    [Fact]
    public void InvalidTagMutationsDoNotChangeOriginal()
    {
        var empty = default(CreatureMetaTags);
        Assert.False(empty.TryChange(CreatureMetaTagAction.Add, "UNKNOWN", null,
            out var unchanged, out var error));
        Assert.Equal(CreatureMetaTagError.UnknownTag, error);
        Assert.Equal(empty, unchanged);

        Assert.False(empty.TryChange(CreatureMetaTagAction.Remove, "NO_AI", null,
            out unchanged, out error));
        Assert.Equal(CreatureMetaTagError.NotSet, error);

        Assert.True(empty.TryChange(CreatureMetaTagAction.Add, "PERSISTENT", null,
            out var tagged, out _));
        Assert.False(tagged.TryChange(CreatureMetaTagAction.Add, "persistent", null,
            out unchanged, out error));
        Assert.Equal(CreatureMetaTagError.AlreadyPresent, error);
        Assert.Equal(tagged, unchanged);

        Assert.False(tagged.TryChange(CreatureMetaTagAction.Edit, "PERSISTENT",
            new string('a', CreatureMetaTags.MaximumValueLength + 1),
            out unchanged, out error));
        Assert.Equal(CreatureMetaTagError.InvalidValue, error);
    }
}

using Asteria.Core.Content;

namespace Asteria.Core.Tests;

public sealed class PackSelectionTests
{
    [Fact]
    public void DefaultSelectionUsesDefaultPack()
    {
        var selection =
            PackSelection.Default;

        Assert.Equal(
            "default",
            selection.Name);
    }

    [Theory]
    [InlineData("../other")]
    [InlineData("Other")]
    [InlineData("example:pack")]
    [InlineData("")]
    public void PackFolderNamesRejectUnsafeOrNonPortableValues(
        string value)
    {
        Assert.Throws<ArgumentException>(
            () =>
                new PackSelection(
                    value));
    }
}

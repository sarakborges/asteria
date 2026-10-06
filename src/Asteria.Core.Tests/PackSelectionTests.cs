using Asteria.Core.Content;

namespace Asteria.Core.Tests;

public sealed class PackSelectionTests
{
    [Fact]
    public void DefaultSelectionUsesDefaultPackForBothKinds()
    {
        var selection =
            PackSelection.Default;

        Assert.Equal(
            "default",
            selection.ResourcePack);
        Assert.Equal(
            "default",
            selection.DataPack);
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
                    value,
                    PackSelection.DefaultPackName));
    }
}

using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class DyeDefinitionTests
{
    [Fact]
    public void HsiColorsConvertAndPaletteOrderIsDeterministic()
    {
        var dyes = DyeRegistry.FromJson([
            "{\"id\":\"asteria:blue\",\"color\":{\"hue\":240,\"saturation\":1,\"intensity\":0.33333334}}",
            "{\"id\":\"asteria:red\",\"color\":{\"hue\":0,\"saturation\":1,\"intensity\":0.33333334}}"
        ]);
        Assert.Equal(new[] { "asteria:red", "asteria:blue" }, dyes.Palette.Select(x => x.Id));
        Assert.InRange(dyes.Get("asteria:red").Rgb.X, .99f, 1f);
        Assert.InRange(dyes.Get("asteria:blue").Rgb.Z, .99f, 1f);
        Assert.Throws<ArgumentException>(() => new DyeRegistry([
            dyes.Get("asteria:red"), dyes.Get("asteria:red")]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DyeDefinition("asteria:bad", 0, 2, 1));
    }
}

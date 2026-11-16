using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class DyeDefinitionTests
{
    [Fact]
    public void HsiColorsConvertAndPaletteOrderIsDeterministic()
    {
        var dyes = DyeRegistry.FromJson([
            "{\"id\":\"asteria:dye_blue\",\"color\":{\"hue\":240,\"saturation\":1,\"intensity\":0.33333334}}",
            "{\"id\":\"asteria:dye_red\",\"color\":{\"hue\":0,\"saturation\":1,\"intensity\":0.33333334}}"
        ]);
        Assert.Equal(new[] { "asteria:dye_red", "asteria:dye_blue" }, dyes.Palette.Select(x => x.Id));
        Assert.InRange(dyes.Get("asteria:dye_red").Rgb.X, .99f, 1f);
        Assert.InRange(dyes.Get("asteria:dye_blue").Rgb.Z, .99f, 1f);
        Assert.Throws<ArgumentException>(() => new DyeRegistry([
            dyes.Get("asteria:dye_red"), dyes.Get("asteria:dye_red")]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DyeDefinition("asteria:bad", 0, 2, 1));
    }
}

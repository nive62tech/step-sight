using DesktopGuide.Core;
using Xunit;

namespace DesktopGuide.Tests;

public sealed class GeometryTests
{
    private static Geometry Make(double dpi = 96) => new(Guid.NewGuid(), Guid.NewGuid(), new(-1920, -200, 1920, 1080),
        1600, 900, dpi, "secondary", Guid.NewGuid(), new(0, 0, 1, 1), new(0, 0, 1, 1));

    [Theory]
    [InlineData(96)] [InlineData(120)] [InlineData(144)] [InlineData(192)]
    public void MappingUsesPhysicalPixelsAcrossDpi(double dpi)
    {
        var geometry = Make(dpi);
        Assert.Equal((-1920, -200), geometry.Map(0, 0));
        Assert.Equal((-1, 879), geometry.Map(1, 1));
        Assert.Equal((-960, 340), geometry.Map(.5, .5));
    }
    [Fact]
    public void CropAndPaddingAreInverted()
    {
        var geometry = Make() with { ContentBounds = new(.1, .1, .9, .9), CropBounds = new(.25, .25, .75, .75) };
        Assert.Equal((-960, 340), geometry.Map(.5, .5));
        Assert.Throws<SafetyException>(() => geometry.Map(.05, .5));
    }
    [Theory]
    [InlineData(-.01)] [InlineData(1.01)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidPointsNeverClamp(double x)
        => Assert.Throws<SafetyException>(() => Make().Map(x, .5));
}

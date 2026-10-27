using MESharp.ViewModels;
using Xunit;

namespace MESharpNavTool.Tests;

public sealed class TravelDestinationTests
{
    [Theory]
    [InlineData("3227,3429,0", 3227, 3429, 0)]
    [InlineData("3227 3429", 3227, 3429, 0)]
    [InlineData(" 3227, 3429, 2 ", 3227, 3429, 2)]
    public void CoordinateDestinationsResolveToWorldTiles(string value, int x, int y, int z)
    {
        Assert.True(NavigationViewModel.TryParseCoordinateDestination(value, out var point));
        Assert.Equal(x, point.X);
        Assert.Equal(y, point.Y);
        Assert.Equal(z, point.Z);
    }

    [Theory]
    [InlineData("lodestone.varrock")]
    [InlineData("Varrock Lodestone [lodestone.varrock]")]
    [InlineData("0,0,0")]
    [InlineData("3227,3429,4")]
    [InlineData("3227,3429,0,1")]
    public void NonCoordinatesAndInvalidTilesDoNotResolveAsCoordinates(string value)
        => Assert.False(NavigationViewModel.TryParseCoordinateDestination(value, out _));
}

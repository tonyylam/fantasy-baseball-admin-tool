using FantasyKeeper.Api.Services;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class A1RangeTests
{
    [Theory]
    [InlineData("C8:F13", 6, 4)]
    [InlineData("A1:A1", 1, 1)]
    [InlineData("AA1:AB2", 2, 2)]
    public void GetDimensions_ValidRange_ReturnsRowsAndCols(string range, int expectedRows, int expectedCols)
    {
        var (rows, cols) = A1Range.GetDimensions(range);
        Assert.Equal(expectedRows, rows);
        Assert.Equal(expectedCols, cols);
    }

    [Fact]
    public void GetDimensions_NoColon_Throws()
    {
        Assert.Throws<ArgumentException>(() => A1Range.GetDimensions("C8"));
    }
}

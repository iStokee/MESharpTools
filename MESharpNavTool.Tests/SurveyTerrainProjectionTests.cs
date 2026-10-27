using MESharp.API;
using MESharp.Services;
using Xunit;

namespace MESharpNavTool.Tests;

public sealed class SurveyTerrainProjectionTests
{
    [Fact]
    public void MapUsesCurrentTerrainForBothStatusChangesAndDropsLostCoverage()
    {
        var cells = new[]
        {
            new SurveyCell { X = 2983, Y = 3397, Status = SurveyStatus.Blocked, Count = 10 },
            new SurveyCell { X = 2984, Y = 3397, Status = SurveyStatus.Clear },
            new SurveyCell { X = 2985, Y = 3397, Status = SurveyStatus.Clear }
        };
        var projected = CoverageMapServer.ProjectSurveyTerrain(cells, new bool?[] { true, false, null });
        Assert.Equal(2, projected.Length);
        Assert.Equal(SurveyStatus.Clear, projected[0].Status);
        Assert.Equal(10, projected[0].Count);
        Assert.Equal(SurveyStatus.Blocked, projected[1].Status);
        Assert.Equal(SurveyStatus.Blocked, cells[0].Status); // preserve historical capture
    }

    [Theory]
    [InlineData(10053, true)] // connection aborted by the browser
    [InlineData(10054, true)] // reset by peer
    [InlineData(10060, false)] // timeouts remain visible
    public void OnlyExpectedClientDisconnectsAreQuiet(int code, bool expected)
    {
        var error = new System.IO.IOException("write failed", new System.Net.Sockets.SocketException(code));
        Assert.Equal(expected, CoverageMapServer.IsClientDisconnect(error));
        Assert.False(CoverageMapServer.IsClientDisconnect(new System.IO.IOException("unexpected I/O failure")));
    }
}

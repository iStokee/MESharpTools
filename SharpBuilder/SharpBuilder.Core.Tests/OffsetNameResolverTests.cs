using System.Linq;
using MESharp.API;
using SharpBuilder.Core.Services;
using Xunit;

namespace SharpBuilder.Core.Tests;

public class OffsetNameResolverTests
{
	[Fact]
	public void TryResolve_KnownName_ReturnsCurrentTableValue_IgnoringStaleNumber()
	{
		Assert.True(OffsetNameResolver.TryResolve("InteractNPC_route = 4928", out var value));
		Assert.Equal(Npcs.InteractNPC_route, value);
	}

	[Fact]
	public void TryResolve_KnownName_WithoutNumber_Resolves()
	{
		Assert.True(OffsetNameResolver.TryResolve("Walk_route", out var value));
		Assert.Equal(Objects.Offsets.WalkRoute, value);
	}

	[Fact]
	public void TryResolve_UnknownName_FallsBackToNumericTail()
	{
		Assert.True(OffsetNameResolver.TryResolve("SomeCapturedRoute = 5392", out var value));
		Assert.Equal(5392, value);
	}

	[Fact]
	public void TryResolve_UnknownName_WithoutNumber_Fails()
	{
		Assert.False(OffsetNameResolver.TryResolve("NotARealOffset", out _));
	}

	[Fact]
	public void DetectDrift_StaleValue_ReportsCurrent()
	{
		var drift = OffsetNameResolver.DetectDrift("GeneralObject_route0 = 99999");
		Assert.NotNull(drift);
		Assert.Equal("GeneralObject_route0", drift!.Value.Name);
		Assert.Equal(99999, drift.Value.SavedValue);
		Assert.Equal(Objects.Offsets.GeneralRoute0, drift.Value.CurrentValue);
	}

	[Fact]
	public void DetectDrift_CurrentValue_ReturnsNull()
	{
		Assert.Null(OffsetNameResolver.DetectDrift($"Walk_route = {Objects.Offsets.WalkRoute}"));
	}

	[Fact]
	public void DetectDrift_PlainNumberOrUnknownName_ReturnsNull()
	{
		Assert.Null(OffsetNameResolver.DetectDrift("2304"));
		Assert.Null(OffsetNameResolver.DetectDrift("SomeCapturedRoute = 5392"));
	}
}

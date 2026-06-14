using SharpBuilder.Core.Models;
using Xunit;

namespace SharpBuilder.Core.Tests;

public class NodeParameterValueTests
{
	[Fact]
	public void SplitValues_TrimsCommaSemicolonAndNewLineSeparatedValues()
	{
		var value = new NodeParameterValue
		{
			RawValue = " Raw shark, 385;\n Sailfish \n\n"
		};

		Assert.Equal(new[] { "Raw shark", "385", "Sailfish" }, value.SplitValues());
	}

	[Fact]
	public void GetTypedValue_ReturnsBooleanFromBoolStorage()
	{
		var value = new NodeParameterValue
		{
			Type = NodeParamType.Bool,
			BoolValue = true,
			RawValue = "false"
		};

		Assert.Equal(true, value.GetTypedValue());
	}

	[Theory]
	[InlineData("12.5", 12.5)]
	[InlineData("-4", -4d)]
	public void GetTypedValue_ReturnsDoubleForValidNumbers(string raw, double expected)
	{
		var value = new NodeParameterValue
		{
			Type = NodeParamType.Number,
			RawValue = raw
		};

		Assert.Equal(expected, value.GetTypedValue());
	}

	[Fact]
	public void GetTypedValue_ReturnsNullForInvalidNumbers()
	{
		var value = new NodeParameterValue
		{
			Type = NodeParamType.Number,
			RawValue = "not a number"
		};

		Assert.Null(value.GetTypedValue());
	}

	[Fact]
	public void GetTypedValue_ReturnsSplitValuesForMultiValueParameters()
	{
		var value = new NodeParameterValue
		{
			Type = NodeParamType.List,
			AllowMultiple = true,
			RawValue = "one, two\nthree"
		};

		var typed = Assert.IsAssignableFrom<IEnumerable<string>>(value.GetTypedValue());
		Assert.Equal(new[] { "one", "two", "three" }, typed);
	}
}

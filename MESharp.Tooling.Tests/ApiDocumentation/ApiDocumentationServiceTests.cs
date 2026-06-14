using System.Linq;
using csharp_interop.Documentation.Services;
using Xunit;

namespace csharp_interop.Tests;

public sealed class ApiDocumentationServiceTests
{
    [Fact]
    public void GetAllApiClasses_discovers_public_mesharp_static_api_classes()
    {
        var service = new ApiDocumentationService();

        var classes = service.GetAllApiClasses();

        Assert.NotEmpty(classes);
        Assert.All(classes, apiClass =>
        {
            Assert.StartsWith("MESharp.API.", apiClass.FullName);
            Assert.False(string.IsNullOrWhiteSpace(apiClass.Name));
        });
        Assert.Contains(classes, apiClass => apiClass.Name == "Inventory");
        Assert.Contains(classes, apiClass => apiClass.Name == "LocalPlayer");
    }

    [Fact]
    public void GetAllApiClasses_returns_classes_sorted_by_name()
    {
        var service = new ApiDocumentationService();

        var names = service.GetAllApiClasses().Select(apiClass => apiClass.Name).ToList();

        Assert.Equal(names.OrderBy(name => name).ToList(), names);
    }

    [Fact]
    public void GetAllApiClasses_excludes_property_accessors_from_methods()
    {
        var service = new ApiDocumentationService();

        var classes = service.GetAllApiClasses();

        Assert.All(classes.SelectMany(apiClass => apiClass.Methods), method =>
        {
            Assert.False(method.Name.StartsWith("get_"), $"Accessor method was included: {method.Name}");
            Assert.False(method.Name.StartsWith("set_"), $"Accessor method was included: {method.Name}");
        });
    }

    [Fact]
    public void ApiMethodInfo_formats_signatures_with_optional_defaults()
    {
        var method = new ApiMethodInfo
        {
            ReturnType = "bool",
            Name = "DoThing"
        };
        method.Parameters.Add(new ApiParameterInfo { Type = "int", Name = "id" });
        method.Parameters.Add(new ApiParameterInfo { Type = "string", Name = "option", IsOptional = true, DefaultValue = "Bank" });

        Assert.Equal("int id, string option = Bank", method.ParametersString);
        Assert.Equal("bool DoThing(int id, string option = Bank)", method.GetSignature());
    }

    [Fact]
    public void ApiPropertyInfo_formats_readonly_and_readwrite_signatures()
    {
        var readonlyProperty = new ApiPropertyInfo
        {
            Type = "int",
            Name = "Level",
            CanRead = true,
            CanWrite = false
        };
        var readWriteProperty = new ApiPropertyInfo
        {
            Type = "string",
            Name = "Name",
            CanRead = true,
            CanWrite = true
        };

        Assert.Equal("int Level { get; }", readonlyProperty.GetSignature());
        Assert.Equal("string Name { get; set; }", readWriteProperty.GetSignature());
    }
}

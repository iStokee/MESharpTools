using System.Linq;
using csharp_interop.Documentation.ViewModels;
using Xunit;

namespace csharp_interop.Tests;

public sealed class ApiDocBrowserViewModelTests
{
    [Fact]
    public void Constructor_loads_api_classes()
    {
        var viewModel = new ApiDocBrowserViewModel();

        Assert.NotEmpty(viewModel.AllClasses);
        Assert.Equal(viewModel.AllClasses.Count, viewModel.DisplayedClasses.Count);
    }

    [Fact]
    public void SearchText_filters_by_class_name()
    {
        var viewModel = new ApiDocBrowserViewModel();

        viewModel.SearchText = "Inventory";

        Assert.NotEmpty(viewModel.DisplayedClasses);
        Assert.All(viewModel.DisplayedClasses, apiClass =>
        {
            var haystack = string.Join(" ",
                apiClass.Name,
                apiClass.Summary ?? string.Empty,
                string.Join(" ", apiClass.Methods.Select(method => method.Name)),
                string.Join(" ", apiClass.Properties.Select(property => property.Name)))
                .ToLowerInvariant();

            Assert.Contains("inventory", haystack);
        });
        Assert.Contains(viewModel.DisplayedClasses, apiClass => apiClass.Name == "Inventory");
    }

    [Fact]
    public void SearchText_filters_by_method_name()
    {
        var viewModel = new ApiDocBrowserViewModel();
        var searchableMethod = viewModel.AllClasses
            .SelectMany(apiClass => apiClass.Methods.Select(method => new { apiClass, method }))
            .First(item => !string.IsNullOrWhiteSpace(item.method.Name));

        viewModel.SearchText = searchableMethod.method.Name;

        Assert.Contains(viewModel.DisplayedClasses, apiClass => apiClass.Name == searchableMethod.apiClass.Name);
    }

    [Fact]
    public void ClearSearchCommand_restores_all_classes()
    {
        var viewModel = new ApiDocBrowserViewModel();
        viewModel.SearchText = "Inventory";
        Assert.True(viewModel.DisplayedClasses.Count < viewModel.AllClasses.Count);

        viewModel.ClearSearchCommand.Execute(null);

        Assert.Equal(string.Empty, viewModel.SearchText);
        Assert.Equal(viewModel.AllClasses.Count, viewModel.DisplayedClasses.Count);
    }

    [Fact]
    public void IsDarkMode_defaults_to_true()
    {
        var viewModel = new ApiDocBrowserViewModel();

        Assert.True(viewModel.IsDarkMode);
    }

    [Fact]
    public void IsDarkMode_raises_PropertyChanged_when_toggled()
    {
        var viewModel = new ApiDocBrowserViewModel();
        var fired = new System.Collections.Generic.List<string?>();
        viewModel.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        viewModel.IsDarkMode = false;

        Assert.Contains(nameof(ApiDocBrowserViewModel.IsDarkMode), fired);
    }

    [Fact]
    public void IsDarkMode_does_not_raise_PropertyChanged_when_value_unchanged()
    {
        var viewModel = new ApiDocBrowserViewModel();
        var count = 0;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ApiDocBrowserViewModel.IsDarkMode)) count++;
        };

        viewModel.IsDarkMode = true; // already true — no change
        viewModel.IsDarkMode = false;
        viewModel.IsDarkMode = false; // already false — no change

        Assert.Equal(1, count);
    }

    [Fact]
    public void SelectedMember_projects_method_and_property()
    {
        var viewModel = new ApiDocBrowserViewModel();
        var method = new csharp_interop.Documentation.Services.ApiMethodInfo { Name = "Method" };
        var property = new csharp_interop.Documentation.Services.ApiPropertyInfo { Name = "Property" };

        viewModel.SelectedMember = method;
        Assert.Same(method, viewModel.SelectedMethod);
        Assert.Null(viewModel.SelectedProperty);

        viewModel.SelectedMember = property;
        Assert.Same(property, viewModel.SelectedProperty);
        Assert.Null(viewModel.SelectedMethod);
    }
}

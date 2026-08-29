using MESharp.ViewModels;
using Xunit;

namespace MESharp.Tooling.Tests.DoAction;

public sealed class NamedVerbTesterViewModelTests
{
    [Fact]
    public void FireCommand_only_enables_verbs_for_the_selected_target_type()
    {
        using var vm = new NamedVerbTesterViewModel { TargetId = "123", IsNpc = true };

        Assert.True(vm.FireCommand.CanExecute("Attack"));
        Assert.True(vm.FireCommand.CanExecute("Talk"));
        Assert.False(vm.FireCommand.CanExecute("Open"));
        Assert.False(vm.FireCommand.CanExecute("Bank"));

        vm.IsObject = true;

        Assert.False(vm.FireCommand.CanExecute("Attack"));
        Assert.False(vm.FireCommand.CanExecute("Talk"));
        Assert.True(vm.FireCommand.CanExecute("Open"));
        Assert.True(vm.FireCommand.CanExecute("Bank"));
    }

    [Fact]
    public void FireCommand_is_disabled_without_a_valid_id()
    {
        using var vm = new NamedVerbTesterViewModel { IsNpc = true, TargetId = "not-an-id" };

        Assert.False(vm.FireCommand.CanExecute("Attack"));
    }
}

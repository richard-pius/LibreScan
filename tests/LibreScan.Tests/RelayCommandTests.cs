using LibreScan.Helpers;
using Xunit;

namespace LibreScan.Tests;

public class RelayCommandTests
{
    [Fact]
    public void RelayCommand_Execute_InvokesAction()
    {
        bool executed = false;
        var cmd = new RelayCommand(_ => executed = true);

        Assert.True(cmd.CanExecute(null));
        cmd.Execute(null);

        Assert.True(executed);
    }

    [Fact]
    public void RelayCommand_CanExecute_RespectsPredicate()
    {
        bool canRun = false;
        var cmd = new RelayCommand(_ => { }, _ => canRun);

        Assert.False(cmd.CanExecute(null));
        canRun = true;
        Assert.True(cmd.CanExecute(null));
    }

    [Fact]
    public void RelayCommand_NullExecute_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RelayCommand(null!));
    }
}

using System;
using System.Windows.Input;

namespace FitohPhone.Helpers;

public sealed class RelayCommand(Action execute) : ICommand
{
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}

public sealed class RelayCommand<T>(Action<T> execute) : ICommand
{
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute((T)parameter!);
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}

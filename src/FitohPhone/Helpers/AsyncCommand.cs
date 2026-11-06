using System.Windows.Input;

namespace FitohPhone.Helpers;

public sealed class AsyncCommand(Func<Task> execute) : ICommand
{
    private bool running;
    public bool CanExecute(object? parameter) => !running;
    public event EventHandler? CanExecuteChanged;

    public async void Execute(object? parameter)
    {
        if (running) return;
        running = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await execute(); }
        finally { running = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>
    /// يسمح بالاستدعاء مع await من كود خلف الواجهة (Code-behind).
    /// </summary>
    public Task ExecuteAsync(object? parameter)
    {
        if (running) return Task.CompletedTask;
        running = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        return ExecuteCore();

        async Task ExecuteCore()
        {
            try { await execute(); }
            finally { running = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
        }
    }
}

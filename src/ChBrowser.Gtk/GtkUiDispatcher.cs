using System;
using System.Threading;
using System.Threading.Tasks;
using ChBrowser.Services.Ui;

namespace ChBrowser.GtkApp;

public sealed class GtkUiDispatcher : IUiDispatcher
{
    private readonly SynchronizationContext? _syncContext;
    private readonly int _uiThreadId;

    public GtkUiDispatcher()
    {
        _syncContext = SynchronizationContext.Current;
        _uiThreadId = Environment.CurrentManagedThreadId;
    }

    public bool CheckAccess() => Environment.CurrentManagedThreadId == _uiThreadId;

    public void Post(Action action)
    {
        if (CheckAccess())
        {
            action();
            return;
        }

        if (_syncContext != null)
        {
            _syncContext.Post(_ => action(), null);
        }
        else
        {
            GLib.Functions.IdleAdd(0, () =>
            {
                action();
                return false;
            });
        }
    }

    public Task InvokeAsync(Action action)
    {
        if (CheckAccess())
        {
            try
            {
                action();
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        var tcs = new TaskCompletionSource();
        Post(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    public Task<T> InvokeAsync<T>(Func<T> function)
    {
        if (CheckAccess())
        {
            try
            {
                return Task.FromResult(function());
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }

        var tcs = new TaskCompletionSource<T>();
        Post(() =>
        {
            try
            {
                tcs.SetResult(function());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }
}

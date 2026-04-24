using CommunityToolkit.WinUI.Notifications;
using Windows.ApplicationModel;
using Microsoft.UI.Xaml;
using TigangReminder_App.Services;

namespace TigangReminder_App;

public partial class App : Application
{
    private MainWindow? _window;
    private string? _pendingToastArguments;
    private readonly DebugLogService _debugLogService = new();

    public App()
    {
        InitializeComponent();

        UnhandledException += async (_, args) =>
        {
            await _debugLogService.LogAsync("AppUnhandledException", args.Exception.ToString());
        };

        AppDomain.CurrentDomain.UnhandledException += async (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                await _debugLogService.LogAsync("AppDomainUnhandledException", ex.ToString());
            }
        };

        TaskScheduler.UnobservedTaskException += async (_, args) =>
        {
            await _debugLogService.LogAsync("UnobservedTaskException", args.Exception.ToString());
        };

        ToastNotificationManagerCompat.OnActivated += toastArgs =>
        {
            _pendingToastArguments = toastArgs.Argument;

            if (_window is not null)
            {
                _window.DispatcherQueue.TryEnqueue(() =>
                {
                    EnsureWindow();
                    _window.HandleToastArguments(_pendingToastArguments);
                    _pendingToastArguments = null;
                });
            }
        };
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        EnsureWindow();

        if (!string.IsNullOrWhiteSpace(args.Arguments))
        {
            _window?.HandleToastArguments(args.Arguments);
        }
        else if (!string.IsNullOrWhiteSpace(_pendingToastArguments))
        {
            _window?.HandleToastArguments(_pendingToastArguments);
            _pendingToastArguments = null;
        }
    }

    private void EnsureWindow()
    {
        _window ??= new MainWindow();
        _window.Activate();
    }
}

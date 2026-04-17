using System.Runtime.InteropServices;
using CommunityToolkit.WinUI.Notifications;
using Microsoft.UI.Xaml;
using TigangReminder_App.Services;
using TigangReminder_App.ViewModels;
using WinRT.Interop;

namespace TigangReminder_App;

public sealed partial class MainWindow : Window
{
    private const uint WM_APP = 0x8000;
    private const uint WM_TRAYICON = WM_APP + 1;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_SIZE = 0x0005;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const int SIZE_MINIMIZED = 1;
    private const int GWLP_WNDPROC = -4;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_NONOTIFY = 0x0080;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0010;
    private const uint LR_DEFAULTSIZE = 0x0040;
    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;
    private const uint NIIF_INFO = 0x00000001;
    private const int TrayCommandShow = 1001;
    private const int TrayCommandStartTraining = 1002;
    private const int TrayCommandExit = 1003;

    private readonly MainViewModel _viewModel;
    private readonly nint _hwnd;
    private readonly WindowProc _newWindowProc;
    private readonly nint _iconHandle;
    private nint _oldWndProc;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");

        _viewModel = new MainViewModel(
            new StorageService(),
            new NotificationService(),
            new AiPlanService(),
            DispatcherQueue);

        RootLayout.DataContext = _viewModel;
        Activated += OnWindowActivated;

        _hwnd = WindowNative.GetWindowHandle(this);
        _newWindowProc = WndProc;
        _oldWndProc = SetWindowLongPtr(_hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_newWindowProc));
        _iconHandle = LoadImage(nint.Zero, Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"), IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
        AddTrayIcon();

        Closed += (_, _) =>
        {
            RemoveTrayIcon();
            if (_iconHandle != 0)
            {
                DestroyIcon(_iconHandle);
            }
        };
    }

    public void HandleToastArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return;
        }

        RestoreFromTray();

        var parsed = ToastArguments.Parse(arguments);
        if (parsed.TryGetValue("action", out var action) && action == "open-training")
        {
            RootTabs.SelectedItem = TrainingTab;
        }
    }

    private async void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnWindowActivated;
        await _viewModel.InitializeAsync();
    }

    private void OnHideToTrayClick(object sender, RoutedEventArgs e)
    {
        HideToTray("应用已转入后台", "提醒仍会继续按计划触发。");
    }

    private nint WndProc(nint hWnd, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case WM_CLOSE:
                if (!_allowClose && _viewModel.Settings.CloseToTray)
                {
                    HideToTray("已最小化到托盘", "你可以从托盘重新打开应用。");
                    return 0;
                }
                break;
            case WM_SIZE:
                if (_viewModel.Settings.MinimizeToTray && (uint)wParam == SIZE_MINIMIZED)
                {
                    HideToTray("已最小化到托盘", "训练计划和提醒会继续保留。");
                    return 0;
                }
                break;
            case WM_TRAYICON:
                switch ((uint)lParam)
                {
                    case WM_LBUTTONDBLCLK:
                        RestoreFromTray();
                        return 0;
                    case WM_RBUTTONUP:
                        ShowTrayMenu();
                        return 0;
                }
                break;
        }

        return CallWindowProc(_oldWndProc, hWnd, message, wParam, lParam);
    }

    private void AddTrayIcon()
    {
        var data = CreateTrayData();
        Shell_NotifyIcon(NIM_ADD, ref data);
    }

    private void RemoveTrayIcon()
    {
        var data = CreateTrayData();
        Shell_NotifyIcon(NIM_DELETE, ref data);
    }

    private void HideToTray(string title, string message)
    {
        AppWindow.Hide();
        ShowTrayBalloon(title, message);
    }

    private void RestoreFromTray()
    {
        AppWindow.Show();
        ShowWindow(_hwnd, 9);
        Activate();
        SetForegroundWindow(_hwnd);
    }

    private void ShowTrayMenu()
    {
        var menu = CreatePopupMenu();
        try
        {
            AppendMenu(menu, 0, TrayCommandShow, "显示主界面");
            AppendMenu(menu, 0, TrayCommandStartTraining, "开始训练");
            AppendMenu(menu, 0, TrayCommandExit, "退出");

            GetCursorPos(out var point);
            SetForegroundWindow(_hwnd);
            var command = TrackPopupMenu(menu, TPM_NONOTIFY | TPM_RETURNCMD, point.X, point.Y, 0, _hwnd, nint.Zero);

            switch (command)
            {
                case TrayCommandShow:
                    RestoreFromTray();
                    break;
                case TrayCommandStartTraining:
                    RestoreFromTray();
                    RootTabs.SelectedItem = TrainingTab;
                    _viewModel.StartTrainingFromShell();
                    break;
                case TrayCommandExit:
                    _allowClose = true;
                    Close();
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void ShowTrayBalloon(string title, string message)
    {
        var data = CreateTrayData();
        data.uFlags = NIF_INFO;
        data.dwInfoFlags = NIIF_INFO;
        data.szInfoTitle = title;
        data.szInfo = message;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private NOTIFYICONDATA CreateTrayData()
    {
        return new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _iconHandle,
            szTip = "提肛节律"
        };
    }

    private delegate nint WindowProc(nint hWnd, uint message, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, uint msg, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(nint hMenu, uint uFlags, int uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(nint hMenu, uint uFlags, int x, int y, int nReserved, nint hWnd, nint prcRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode)]
    private static extern nint LoadImage(nint hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint hIcon);
}

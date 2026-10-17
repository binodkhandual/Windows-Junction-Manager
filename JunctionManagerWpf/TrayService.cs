using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace JunctionManager;

public sealed class TrayService : IDisposable
{
    private const int WM_USER = 0x0400;
    public const int WM_TRAYICON = WM_USER + 101;

    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int NIF_INFO = 0x00000010;

    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private readonly Window _window;
    private IntPtr _hwnd;
    private IntPtr _hIcon;
    private bool _added;

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public TrayService(Window window)
    {
        _window = window;
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) Hook();
        else _window.SourceInitialized += (_, _) => Hook();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern uint ExtractIconEx(string szFileName, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    private void Hook()
    {
        _hwnd = new WindowInteropHelper(_window).Handle;
        if (_hwnd == IntPtr.Zero) return;
        var source = HwndSource.FromHwnd(_hwnd);
        source?.AddHook(WndProc);

        try
        {
            var exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exe))
            {
                ExtractIconEx(exe, 0, out _, out _hIcon, 1);
            }
            if (_hIcon == IntPtr.Zero)
            {
                var icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (File.Exists(icoPath))
                    _hIcon = LoadImage(IntPtr.Zero, icoPath, 1, 0, 0, 0x0010 | 0x8000);
            }
        }
        catch { }

        EnsureTrayIcon();
    }

    public void EnsureTrayIcon(string tip = "Junction Manager")
    {
        if (_hwnd == IntPtr.Zero) return;

        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1001,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = tip
        };

        if (!_added)
        {
            _added = Shell_NotifyIcon(NIM_ADD, ref data);
        }
        else
        {
            Shell_NotifyIcon(NIM_MODIFY, ref data);
        }
    }

    public void ShowBalloon(string title, string text)
    {
        if (_hwnd == IntPtr.Zero || !_added) return;

        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1001,
            uFlags = NIF_INFO,
            szInfo = text,
            szInfoTitle = title,
            dwInfoFlags = 0x00000001 /* NIIF_INFO */
        };

        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    public void RemoveTrayIcon()
    {
        if (_added && _hwnd != IntPtr.Zero)
        {
            var data = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1001
            };
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYICON)
        {
            int cmd = lParam.ToInt32();
            if (cmd is WM_LBUTTONUP or WM_LBUTTONDBLCLK)
            {
                OpenRequested?.Invoke();
                handled = true;
            }
            else if (cmd == WM_RBUTTONUP)
            {
                ShowContextMenu();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private void ShowContextMenu()
    {
        if (_hwnd == IntPtr.Zero) return;

        SetForegroundWindow(_hwnd);
        IntPtr hMenu = CreatePopupMenu();
        AppendMenu(hMenu, 0, 1, "Open Junction Manager");
        AppendMenu(hMenu, 0x800 /* MF_SEPARATOR */, 0, string.Empty);
        AppendMenu(hMenu, 0, 2, "Exit");

        GetCursorPos(out POINT pt);
        int selected = TrackPopupMenuEx(hMenu, 0x0100 /* TPM_RETURNCMD */, pt.X, pt.Y, _hwnd, IntPtr.Zero);
        DestroyMenu(hMenu);

        if (selected == 1) OpenRequested?.Invoke();
        else if (selected == 2) ExitRequested?.Invoke();
    }

    public void Dispose()
    {
        RemoveTrayIcon();
        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }
}

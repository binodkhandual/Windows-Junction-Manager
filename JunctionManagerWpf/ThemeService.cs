using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace JunctionManager;

public enum AppTheme
{
    System,
    Dark,
    Light
}

public static class ThemeService
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

    public static AppTheme CurrentTheme { get; private set; } = AppTheme.System;
    public static bool IsEffectiveDark { get; private set; } = true;

    static bool _initialized;

    public static void Initialize(AppTheme initialTheme)
    {
        CurrentTheme = initialTheme;
        if (!_initialized)
        {
            _initialized = true;
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (CurrentTheme == AppTheme.System)
                {
                    Application.Current?.Dispatcher.InvokeAsync(() => Apply(AppTheme.System));
                }
            };
        }
        Apply(initialTheme);
    }

    public static bool IsSystemInDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int val)
                return val == 0;
        }
        catch { }
        return true;
    }

    static SolidColorBrush B(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public static void Apply(AppTheme theme)
    {
        CurrentTheme = theme;
        bool dark = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => IsSystemInDarkMode()
        };
        IsEffectiveDark = dark;

        if (Application.Current is not { } app) return;

        var r = app.Resources;
        r["Bg"] = dark ? B(27, 28, 33) : B(244, 245, 248);
        r["Card"] = dark ? B(37, 39, 46) : B(255, 255, 255);
        r["CardHover"] = dark ? B(44, 47, 55) : B(236, 238, 242);
        r["Line"] = dark ? B(58, 61, 71) : B(215, 218, 226);
        r["Text"] = dark ? B(236, 236, 241) : B(26, 29, 36);
        r["Muted"] = dark ? B(154, 158, 171) : B(104, 110, 125);
        r["Accent"] = dark ? B(76, 141, 255) : B(37, 99, 235);
        r["Btn"] = dark ? B(52, 55, 65) : B(226, 229, 235);
        r["SelectedRowBg"] = dark ? B(42, 46, 57) : B(229, 233, 245);
        r["SwitchTrack"] = dark ? B(85, 90, 102) : B(176, 181, 192);
        r["SegmentBg"] = dark ? B(23, 24, 28) : B(235, 237, 242);
        r["SegmentActiveBg"] = dark ? B(52, 55, 65) : B(255, 255, 255);

        foreach (Window w in app.Windows)
        {
            UpdateTitleBar(w, dark);
        }
    }

    public static void UpdateTitleBar(Window w, bool dark)
    {
        var handle = new WindowInteropHelper(w).Handle;
        if (handle == IntPtr.Zero) return;
        int v = dark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, 20, ref v, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref v, sizeof(int));
    }
}

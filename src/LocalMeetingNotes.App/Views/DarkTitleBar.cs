using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LocalMeetingNotes.App.Views;

/// <summary>
/// Paints the native window frame dark so it matches the app theme.
/// </summary>
internal static class DarkTitleBar
{
    private const int UseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window)
    {
        if (!Set(window))
        {
            window.SourceInitialized += (_, _) => Set(window);
        }
    }

    private static bool Set(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var enabled = 1;
        _ = DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref enabled, sizeof(int));
        return true;
    }
}

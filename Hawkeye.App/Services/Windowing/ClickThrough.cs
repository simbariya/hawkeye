using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Hawkeye.App.Services.Windowing
{
    /// <summary>
    /// Makes a window ignore the mouse (clicks go straight to the game underneath) and never take focus.
    /// Used for the tracker icons whenever Settings is closed.
    /// </summary>
    public static class ClickThrough
    {
        private const int GWL_EXSTYLE = -20;
        private const long WS_EX_TRANSPARENT = 0x00000020L;
        private const long WS_EX_NOACTIVATE = 0x08000000L;
        private const long Bits = WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        public static void Set(Window window, bool enabled)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return; // not shown yet

            long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            long updated = enabled ? style | Bits : style & ~Bits;
            if (updated != style) SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(updated));
        }
    }
}

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Automation;

/// <summary>Owns the initial non-activating placement of an automation window on Windows.</summary>
internal static class BackgroundWindowPlacement
{
    #region Window placement

    /// <summary>Shows a hidden window at the bottom of the stack while preserving its size and position.</summary>
    internal static void Show(IntPtr handle)
    {
        const uint flags = 0x0001 | 0x0002 | 0x0010 | 0x0040; // NOSIZE | NOMOVE | NOACTIVATE | SHOWWINDOW
        // Showing and ordering in one operation avoids exposing a raised window before lowering it.
        if (handle == IntPtr.Zero) throw new InvalidOperationException("The automation window has no Win32 handle.");
        if (!SetWindowPos(handle, new IntPtr(1), 0, 0, 0, 0, flags)) // HWND_BOTTOM
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not show the automation window behind existing windows.");
        }
    }

    #endregion

    #region Native API

    /// <summary>Changes native window placement and visibility without requiring activation.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    #endregion
}

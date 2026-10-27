using System;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Controls optional native debug notifications.</summary>
internal static partial class GlDebug
{
    #region Public API
    /// <summary>Suppresses optional debug-group notifications without affecting rendering policy.</summary>
    [System.Diagnostics.Conditional("DEBUG")]
    public static void TrySuppressGroupDebugMessages()
    {
#if DEBUG
        if (!SupportsKhrDebug())
        {
            return;
        }

        // The game can enable GL debug output and log all debug callback events.
        // Notification-severity messages, including push/pop groups, are extremely noisy.
        // Opt-out for deep GPU debugging sessions.
        if (string.Equals(Environment.GetEnvironmentVariable("VGE_GL_DEBUG_GROUP_MESSAGES"), "1", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            GL.DebugMessageControl(
                DebugSourceControl.DontCare,
                DebugTypeControl.DontCare,
                DebugSeverityControl.DebugSeverityNotification,
                0,
                Array.Empty<int>(),
                false);

            GL.DebugMessageControl(
                DebugSourceControl.DontCare,
                DebugTypeControl.DebugTypePushGroup,
                DebugSeverityControl.DontCare,
                0,
                Array.Empty<int>(),
                false);

            GL.DebugMessageControl(
                DebugSourceControl.DontCare,
                DebugTypeControl.DebugTypePopGroup,
                DebugSeverityControl.DontCare,
                0,
                Array.Empty<int>(),
                false);

            // Ensure we don't poison the GL error state if the driver ignores some controls.
            _ = GetErrors();
        }
        catch
        {
            // Best-effort only: depends on context + driver support.
        }
#endif
    }
    #endregion

}

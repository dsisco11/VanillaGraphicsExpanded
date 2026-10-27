using System;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Applies optional native labels without validity or outcome probes.</summary>
internal static partial class GlDebug
{
    #region Public API
    /// <summary>Applies a best-effort label to an admitted native allocation without validity queries.</summary>
    [System.Diagnostics.Conditional("DEBUG")]
    public static void TryLabel(ObjectLabelIdentifier identifier, int id, string? name)
    {
#if DEBUG
        if (id == 0 || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        // Avoid calling ObjectLabel on contexts that don't support KHR_debug.
        // Note: GL_EXT_debug_label uses a different entry point; we skip it here rather than
        // calling glObjectLabel and polluting the GL error state.
        if (!SupportsKhrDebug())
        {
            return;
        }

        try
        {
            int maxLabelLength = GpuSupport.Graphics.MaxLabelLength;

            string label = name;
            if (maxLabelLength > 0 && label.Length >= maxLabelLength)
            {
                // glObjectLabel generates INVALID_VALUE when the label exceeds GL_MAX_LABEL_LENGTH.
                label = label.Substring(0, Math.Max(1, maxLabelLength - 1));
            }

            // Allocation/association admission belongs to the caller. Diagnostics never probe
            // native validity or consume errors belonging to an unrelated operation.
            GL.ObjectLabel(identifier, id, label.Length, label);
        }
        catch
        {
            // Best-effort only: labels depend on context + driver support.
        }
#endif
    }
    #endregion

}

using System;
using System.Collections.Generic;
using System.Text;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

internal static class GlDebug
{
#if DEBUG
    private const bool DebugGroupsEnabled = true;
#else
    private const bool DebugGroupsEnabled = false;
#endif

    private static volatile int cachedContextFlags;
    private static volatile bool cachedContextFlagsValid;

    private static volatile int cachedMaxLabelLength;
    private static volatile string? cachedMaxLabelLengthContextKey;

    private static bool SupportsKhrDebug()
    {
        try
        {
            return GlExtensions.Supports("GL_KHR_debug");
        }
        catch
        {
            return false;
        }
    }

    private static int GetMaxLabelLengthCached()
    {
        if (!GlExtensions.TryGetContextKey(out string contextKey))
        {
            return 0;
        }

        string? keySnapshot = cachedMaxLabelLengthContextKey;
        if (string.Equals(keySnapshot, contextKey, StringComparison.Ordinal))
        {
            return cachedMaxLabelLength;
        }

        int value = 0;
        try
        {
            value = GL.GetInteger(GetPName.MaxLabelLength);
        }
        catch
        {
            value = 0;
        }

        cachedMaxLabelLength = value;
        cachedMaxLabelLengthContextKey = contextKey;
        return value;
    }

    private static bool IsDebugContext()
    {
        if (cachedContextFlagsValid)
        {
            return ((ContextFlagMask)cachedContextFlags & ContextFlagMask.ContextFlagDebugBit) != 0;
        }

        try
        {
            int flags = GL.GetInteger(GetPName.ContextFlags);
            cachedContextFlags = flags;
            cachedContextFlagsValid = true;
            return ((ContextFlagMask)flags & ContextFlagMask.ContextFlagDebugBit) != 0;
        }
        catch
        {
            return false;
        }
    }

    public static void TrySuppressGroupDebugMessages()
    {
        if (!SupportsKhrDebug())
        {
            return;
        }

        // The game can enable GL debug output and log all debug callback events.
        // Push/Pop group messages are extremely noisy and not actionable.
        // Opt-out for deep GPU debugging sessions.
        if (string.Equals(Environment.GetEnvironmentVariable("VGE_GL_DEBUG_GROUP_MESSAGES"), "1", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
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
    }

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

        int maxLabelLength = GetMaxLabelLengthCached();

        string label = name;
        if (maxLabelLength > 0 && label.Length >= maxLabelLength)
        {
            // glObjectLabel generates INVALID_VALUE when the label exceeds GL_MAX_LABEL_LENGTH.
            label = label.Substring(0, Math.Max(1, maxLabelLength - 1));
        }

        try
        {
            // Guard against KHR_debug errors when labeling IDs that aren't valid in the current context
            // (e.g., after disposal, context switches, or driver limitations).
            bool valid = identifier switch
            {
                ObjectLabelIdentifier.VertexArray => GL.IsVertexArray(id),
                ObjectLabelIdentifier.Buffer => GL.IsBuffer(id),
                ObjectLabelIdentifier.Texture => GL.IsTexture(id),
                ObjectLabelIdentifier.Framebuffer => GL.IsFramebuffer(id),
                ObjectLabelIdentifier.Renderbuffer => GL.IsRenderbuffer(id),
                ObjectLabelIdentifier.Program => GL.IsProgram(id),
                ObjectLabelIdentifier.Shader => GL.IsShader(id),
                ObjectLabelIdentifier.Query => GL.IsQuery(id),
                ObjectLabelIdentifier.Sampler => GL.IsSampler(id),
                ObjectLabelIdentifier.ProgramPipeline => GL.IsProgramPipeline(id),
                ObjectLabelIdentifier.TransformFeedback => GL.IsTransformFeedback(id),
                _ => true
            };

            if (!valid)
            {
                return;
            }

            GL.ObjectLabel(identifier, id, label.Length, label);
        }
        catch
        {
            // Best-effort only: labels depend on context + driver support.
        }
#endif
    }

    /// <summary>
    /// Drains and returns any pending OpenGL errors on the current thread/context.
    /// </summary>
    public static ErrorCode[] GetErrors(int max = 64)
    {
        if (max <= 0)
        {
            return [];
        }

        var errors = new List<ErrorCode>(Math.Min(max, 8));
        try
        {
            for (int i = 0; i < max; i++)
            {
                ErrorCode err = GL.GetError();
                if (err == ErrorCode.NoError)
                {
                    break;
                }

                errors.Add(err);
            }
        }
        catch
        {
            // Best-effort only: depends on context + driver support.
        }

        return errors.Count == 0 ? [] : errors.ToArray();
    }

    /// <summary>
    /// Drains pending OpenGL errors and returns a formatted string (empty when no errors).
    /// </summary>
    public static string GetErrorsString(string? context = null, int max = 64)
    {
        ErrorCode[] errors = GetErrors(max);
        if (errors.Length == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(context))
        {
            sb.Append(context);
            sb.Append(": ");
        }

        for (int i = 0; i < errors.Length; i++)
        {
            if (i != 0)
            {
                sb.Append(", ");
            }

            sb.Append(errors[i]);
        }

        return sb.ToString();
    }

    public static void TryLabelFramebuffer(int framebufferId, string? name)
    {
        TryLabel(ObjectLabelIdentifier.Framebuffer, framebufferId, name);
    }

    public static GroupScope Group(string name)
    {
        return new GroupScope(name);
    }

    public readonly struct GroupScope : IDisposable
    {
        private readonly bool active;

        public GroupScope(string name)
        {
#if DEBUG
            if (!DebugGroupsEnabled || string.IsNullOrWhiteSpace(name) || !SupportsKhrDebug())
            {
                active = false;
                return;
            }

            try
            {
                GL.PushDebugGroup(DebugSourceExternal.DebugSourceApplication, 0, -1, name);
                active = true;
            }
            catch
            {
                active = false;
            }
#else
            active = false;
#endif
        }

        public void Dispose()
        {
#if DEBUG
            if (!active)
            {
                return;
            }

            try
            {
                GL.PopDebugGroup();
            }
            catch
            {
            }
#endif
        }
    }
}

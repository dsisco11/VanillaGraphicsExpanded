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

    public static void TrySuppressGroupDebugMessages()
    {
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
                ObjectLabelIdentifier.ProgramPipeline => GL.IsProgramPipeline(id),
                ObjectLabelIdentifier.TransformFeedback => GL.IsTransformFeedback(id),
                _ => true
            };

            if (!valid)
            {
                return;
            }

            GL.ObjectLabel(identifier, id, -1, name);
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
            if (!DebugGroupsEnabled || string.IsNullOrWhiteSpace(name))
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

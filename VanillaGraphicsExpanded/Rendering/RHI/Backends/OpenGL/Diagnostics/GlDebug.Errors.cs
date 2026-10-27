using System;
// Capability capture can run before RHI admission loads the OpenGL4 binding table.
// Read errors through the engine binding already active at that explicit native boundary.
using OpenTK.Graphics.OpenGL;

using System.Collections.Generic;
using System.Text;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Reports native errors only at explicit diagnostic boundaries.</summary>
internal static partial class GlDebug
{
    #region Public API
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

    /// <summary>
    /// Drains (clears) any pending OpenGL errors on the current thread/context.
    /// </summary>
    public static void ClearErrors(int max = 64)
    {
        _ = GetErrors(max);
    }

    /// <summary>
    /// Drains pending OpenGL errors and throws when any are present.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when OpenGL reports one or more errors.</exception>
    public static void ThrowIfErrors(string? context = null, int max = 64)
    {
        string message = GetErrorsString(context, max);
        if (message.Length == 0)
        {
            return;
        }

        throw new InvalidOperationException($"OpenGL error(s): {message}");
    }

    #region Error attribution
    /// <summary>Checks errors at both boundaries so existing errors are not attributed to the enclosed operation.</summary>
    public readonly struct ErrorScope : IDisposable
    {
        private readonly string context;

        /// <summary>Reports pending errors before starting the named operation.</summary>
        public ErrorScope(string context)
        {
            this.context = context;
            ThrowIfErrors("Before " + context);
        }

        /// <summary>Reports errors produced by the enclosed operation at its caller's stack frame.</summary>
        public void Dispose() => ThrowIfErrors("After " + context);
    }
    #endregion
    #endregion

}

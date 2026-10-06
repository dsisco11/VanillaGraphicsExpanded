using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns demand-driven format queries separately from global implementation limits.</summary>
public static partial class GpuSupport
{
    private static readonly Dictionary<(ImageTarget Target, SizedInternalFormat Format), InternalFormatCapabilities> formatCapabilities = new();

    #region Public API
    /// <summary>Returns cached target-specific format support; requires internal-format-query2 support.</summary>
    internal static InternalFormatCapabilities GetInternalFormatCapabilities(ImageTarget target, SizedInternalFormat format)
    {
        EnsureCurrentContext();
        if (!graphics.SupportsInternalFormatQuery2)
            throw new NotSupportedException("Internal format capabilities require OpenGL 4.3 or GL_ARB_internalformat_query2.");
        lock (Sync)
        {
            var key = (target, format);
            if (formatCapabilities.TryGetValue(key, out var cached)) return cached;

            // Never publish query failures as unsupported formats. Warm lookups leave pending GL errors untouched.
            GlDebug.ThrowIfErrors("Before internal format capability query");
            GL.GetInternalformat(target, format, InternalFormatParameter.InternalformatSupported, 1, out int supported);
            GlDebug.ThrowIfErrors("Internal format support");
            int renderable = 0;
            int[] samples = [];
            if (supported != 0)
            {
                GL.GetInternalformat(target, format, InternalFormatParameter.FramebufferRenderable, 1, out renderable);
                if (target is ImageTarget.Renderbuffer or ImageTarget.Texture2DMultisample or ImageTarget.Texture2DMultisampleArray)
                {
                    GL.GetInternalformat(target, format, InternalFormatParameter.NumSampleCounts, 1, out int count);
                    GlDebug.ThrowIfErrors("Internal format sample count");
                    samples = new int[count];
                    if (count > 0) GL.GetInternalformat(target, format, InternalFormatParameter.Samples, count, samples);
                }
            }
            GlDebug.ThrowIfErrors("Internal format capabilities");
            var result = new InternalFormatCapabilities
            {
                Supported = supported != 0,
                FramebufferRenderableSupport = renderable,
                SampleCounts = [.. samples]
            };
            formatCapabilities.Add(key, result);
            return result;
        }
    }
    #endregion
}

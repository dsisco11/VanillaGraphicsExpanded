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
    /// <summary>Returns cached target-specific format support; uses the admitted backend for unknown entries.</summary>
    internal static InternalFormatCapabilities GetInternalFormatCapabilities(ImageTarget target, SizedInternalFormat format)
    {
        var execution = RHI.RHICommandContext.Current;
        lock (Sync)
        {
            var key = (target, format);
            if (formatCapabilities.TryGetValue(key, out var cached)) return cached;
            var result = execution.ReadFormatCapabilities(target, format);
            formatCapabilities.Add(key, result);
            return result;
        }
    }
    #endregion
}

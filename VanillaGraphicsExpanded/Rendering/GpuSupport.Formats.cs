using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns demand-driven format queries separately from global implementation limits.</summary>
public static partial class GpuSupport
{
    private static readonly Dictionary<(GpuResourceKind Kind, TextureDimension Target, TextureFormat Format), InternalFormatCapabilities> formatCapabilities = new();

    #region Public API
    /// <summary>Looks up admitted format facts without initiating native discovery from an observer.</summary>
    internal static bool TryGetCachedFormatCapabilities(TextureDimension target, TextureFormat format,
        out InternalFormatCapabilities capabilities, GpuResourceKind kind = GpuResourceKind.Texture)
    {
        lock (Sync)
        {
            capabilities = null!;
            return HasCachedCapabilitiesForCurrentContext
                && formatCapabilities.TryGetValue((kind, target, format), out capabilities!);
        }
    }

    /// <summary>Returns cached target-specific format support; uses the admitted backend for unknown entries.</summary>
    internal static InternalFormatCapabilities GetInternalFormatCapabilities(TextureDimension target, TextureFormat format, GpuResourceKind kind = GpuResourceKind.Texture)
    {
        var execution = RHI.RHICommandContext.Current;
        lock (Sync)
        {
            var key = (kind, target, format);
            if (formatCapabilities.TryGetValue(key, out var cached)) return cached;
            var result = execution.ReadFormatCapabilities(target, format, kind);
            formatCapabilities.Add(key, result);
            return result;
        }
    }
    #endregion
}

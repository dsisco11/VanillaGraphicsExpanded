using System;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Resolves supported multisample allocation counts using the existing backend format-capability cache.</summary>
internal sealed partial class OpenGLBackend
{
    #region Private
    /// <summary>Selects a guaranteed supported count before allocation; unavailable lists retain the narrow outcome query.</summary>
    private GpuTextureInitialization ResolveTextureSamples(in GpuTextureInitialization initialization)
    {
        var result = initialization;
        if (result.Target is not (TextureDimension.TwoMultisample or TextureDimension.TwoMultisampleArray)) return result;
        RequireContext();
        var support = GpuSupport.GetInternalFormatCapabilities(result.Target, result.InternalFormat);
        if (!support.Supported) throw new ArgumentOutOfRangeException(nameof(initialization), "The multisample format is unsupported.");
        if (support.SampleCounts.IsDefaultOrEmpty) return result;
        int selected = int.MaxValue;
        foreach (int count in support.SampleCounts)
            if (count >= result.Samples && count < selected) selected = count;
        if (selected == int.MaxValue) throw new ArgumentOutOfRangeException(nameof(initialization), "The sample count is unsupported.");
        result.Samples = selected;
        return result;
    }
    #endregion
}

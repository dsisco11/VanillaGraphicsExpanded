using System;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Admits explicit external renderbuffer names without taking native ownership.</summary>
internal static class OpenGLRenderbufferIntegration
{
    #region Internal API
    /// <summary>Reuses covered facts or explicitly acquires unknown external storage before returning a borrower.</summary>
    internal static GpuRenderbuffer Borrow(int nativeName, string? label = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nativeName);
        RHICommandContext.Current.OpenGL.RequireContext();
        var wrapper = new GpuRenderbuffer(0, false, label);
        wrapper.SetCreationIdentity(nativeName, GpuResource.CaptureCreationContext(GpuResourceKind.Renderbuffer));
        try
        {
            GpuResourceManager.AcquireWrapperMetadata(wrapper, GpuRenderbufferMetadataReader.Instance);
            return wrapper;
        }
        catch { wrapper.Dispose(); throw; }
    }
    #endregion
}

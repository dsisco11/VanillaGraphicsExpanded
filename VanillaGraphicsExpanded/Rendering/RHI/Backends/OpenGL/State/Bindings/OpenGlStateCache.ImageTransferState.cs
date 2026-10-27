using System;
using VanillaGraphicsExpanded.Rendering.Pipeline;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Preserves precisely the executed raster values changed by named framebuffer image transfers.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Resolves transfer state from existing knowledge, querying only missing incoming fields before native effects.</summary>
    internal ImageTransferState PreserveImageTransferState()
    {
        ResolveBoundaryRasterizer(RasterizerStateKnowledge.ScissorEnabled);
        ResolveGraphicsCategories(new(output: OutputStateKnowledge.FramebufferSrgb, completeGraphics: true));
        return new(this, rasterizer.ScissorEnabled, output.FramebufferSrgb, GpuResource.CaptureCreationContext(GpuResourceKind.Framebuffer));
    }
    #endregion

    #region Private
    /// <summary>Rejects a changed native namespace before an independent restoration setter can run.</summary>
    private static void RequireImageTransferContext((nint Handle,long Generation) origin)
    {
        if(!VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.IsCurrentResourceContext(origin)) throw new InvalidOperationException("The image-transfer context changed during restoration.");
    }
    #endregion

    /// <summary>Restores independent transfer fields without overwriting unrelated raster or indexed blending state.</summary>
    internal readonly struct ImageTransferState(OpenGlStateCache cache, bool scissor, bool srgb,
        (nint Handle, long Generation) origin) : IDisposable
    {
        #region Internal API
        /// <summary>Restores both values even when one independent restoration fails.</summary>
        public void Dispose()
        {
            if (!VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
                throw new EngineBoundaryRestoreException("Image-transfer restoration requires its originating context.", new InvalidOperationException("The native context changed during transfer."));
            var owner=cache; var nativeOrigin=origin; bool originalScissor=scissor,originalSrgb=srgb;
            var failures = new System.Collections.Generic.List<Exception>();
            owner.RestoreBoundaryField(() =>
            {
                RequireImageTransferContext(nativeOrigin);
                owner.SetCapability(OpenTK.Graphics.OpenGL.EnableCap.ScissorTest, originalScissor);
                RequireImageTransferContext(nativeOrigin);
            }, () => owner.rasterizerKnown &= ~RasterizerStateKnowledge.ScissorEnabled, failures);
            owner.RestoreBoundaryField(() =>
            {
                RequireImageTransferContext(nativeOrigin);
                owner.SetFramebufferSrgbEnabled(originalSrgb);
                RequireImageTransferContext(nativeOrigin);
            }, () => owner.outputKnown &= ~OutputStateKnowledge.FramebufferSrgb, failures);
            if (failures.Count != 0) throw new EngineBoundaryRestoreException("Image-transfer state restoration failed.", new AggregateException(failures));
        }
        #endregion
    }
}

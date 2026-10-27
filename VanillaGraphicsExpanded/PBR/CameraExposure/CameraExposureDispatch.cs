using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.PBR.CameraExposure;

/// <summary>Admits compute metering and preserves engine bindings across its dependent dispatches.</summary>
internal static class CameraExposureDispatch
{
    #region Public API
    /// <summary>Requires the cached runtime compute/storage limits without a raster fallback.</summary>
    internal static void RequireSupport()
    {
        GpuSupport.EnsureCurrentContext();
        if (!Supports(GpuSupport.Graphics))
            throw new InvalidOperationException("Camera exposure requires compute shaders, SPIR-V and shader storage buffers with 256-lane adaptation workgroups.");
    }
    /// <summary>Admits only devices that can execute both metering stages and the graphics consumers.</summary>
    internal static bool Supports(GraphicsCapabilities capabilities) =>
        capabilities.SupportsArbComputeShader && capabilities.SupportsArbShaderImageLoadStore
        && capabilities.SupportsArbShaderStorageBufferObject && capabilities.SupportsArbGlSpirv
        && capabilities.MaxComputeWorkGroupInvocations >= 256
        && !capabilities.MaxComputeWorkGroupSize.IsEmpty && capabilities.MaxComputeWorkGroupSize[0] >= 256
        && !capabilities.MaxComputeWorkGroupCount.IsEmpty && capabilities.MaxComputeWorkGroupCount[0] >= CameraExposureTargets.HistogramGroupCount
        && capabilities.MaxComputeSharedMemorySize >= 2560 && capabilities.MaxShaderStorageBufferBindings >= 3
        && capabilities.MaxFragmentShaderStorageBlocks >= 1 && capabilities.MaxComputeShaderStorageBlocks >= 3
        && capabilities.MaxShaderStorageBlockSize >= CameraExposureTargets.HistogramBytes;
    /// <summary>Admits the frozen read slot and simultaneous display-exposure/frame-scale fragment consumers.</summary>
    internal static bool SupportsLighting(GraphicsCapabilities capabilities) => Supports(capabilities)
        && capabilities.MaxShaderStorageBufferBindings > GpuBindingRegistry.Ssbo.FrameLighting && capabilities.MaxFragmentShaderStorageBlocks >= 2
        && capabilities.MaxUniformBlockSize >= VgeFrameUniformBuffer.PackedSize;
    /// <summary>Restores prepared resource slots and publishes writes to reduction and subsequent graphics consumers.</summary>
    internal static bool TryRun(CameraHistogramShaderProgram histogram, CameraAdaptShaderProgram adaptation)
    {
        var context=RHICommandContext.Current;
        if(!context.TryEnterBoundary(new("Camera.Exposure",true,compute:[histogram.Pipeline,adaptation.Pipeline]),out var boundary)) return false;
        using(boundary) boundary!.Run(()=>
        {
            histogram.Dispatch(CameraExposureTargets.HistogramGroupCount);
            RHICommandContext.Current.Barrier(new(ResourceAccess.ShaderStorage));
            adaptation.Dispatch(1);
            RHICommandContext.Current.Barrier(new(ResourceAccess.ShaderStorage));
        });
        return true;
    }
    /// <summary>Publishes frozen scale writes through the existing prepared resource and restoration boundary.</summary>
    internal static bool TryFreeze(FrameLightingShaderProgram program)
    {
        GpuSupport.EnsureCurrentContext();
        if (!SupportsLighting(GpuSupport.Graphics))
            throw new InvalidOperationException("Physical frame storage requires five SSBO slots, two fragment storage blocks and the shared frame UBO.");
        var context=RHICommandContext.Current;
        if(!context.TryEnterBoundary(new("Camera.FrozenLighting",true,compute:[program.Pipeline]),out var boundary)) return false;
        using(boundary) boundary!.Run(()=>
        {
            program.Dispatch(1);
            RHICommandContext.Current.Barrier(new(ResourceAccess.ShaderStorage));
        });
        return true;
    }
    #endregion
}

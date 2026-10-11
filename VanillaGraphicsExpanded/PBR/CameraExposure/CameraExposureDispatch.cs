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
            throw new InvalidOperationException("Camera exposure requires compute shaders, SPIR-V and shader storage buffers with 64-lane workgroups.");
    }
    /// <summary>Admits only devices that can execute both metering stages and the graphics consumers.</summary>
    internal static bool Supports(GraphicsCapabilities capabilities) =>
        capabilities.SupportsArbComputeShader && capabilities.SupportsArbShaderImageLoadStore
        && capabilities.SupportsArbShaderStorageBufferObject && capabilities.SupportsArbGlSpirv
        && capabilities.MaxComputeWorkGroupInvocations >= 64
        && !capabilities.MaxComputeWorkGroupSize.IsEmpty && capabilities.MaxComputeWorkGroupSize[0] >= 64
        && !capabilities.MaxComputeWorkGroupCount.IsEmpty && capabilities.MaxComputeWorkGroupCount[0] >= 36
        && capabilities.MaxComputeSharedMemorySize >= 512 && capabilities.MaxShaderStorageBufferBindings >= 3
        && capabilities.MaxFragmentShaderStorageBlocks >= 1 && capabilities.MaxComputeShaderStorageBlocks >= 3
        && capabilities.MaxShaderStorageBlockSize >= CameraExposureTargets.HistogramBytes;
    /// <summary>Restores prepared resource slots and publishes writes to reduction and subsequent graphics consumers.</summary>
    internal static bool TryRun(CameraHistogramShaderProgram histogram, CameraAdaptShaderProgram adaptation)
    {
        // Reflection remains executable-owned; the boundary only borrows the prepared footprints.
        var resources = EngineBoundaryResources.From(histogram.ProgramLayout.BinaryInterface!.PreparedBindings)
            .Union(EngineBoundaryResources.From(adaptation.ProgramLayout.BinaryInterface!.PreparedBindings));
        return EngineBoundaryExecution.TryRun(new("Camera.Exposure"), resources, _ =>
        {
            histogram.Dispatch(36);
            GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);
            adaptation.Dispatch(1);
            GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);
        });
    }
    #endregion
}

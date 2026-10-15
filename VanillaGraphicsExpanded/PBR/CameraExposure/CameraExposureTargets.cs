using System;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.CameraExposure;

/// <summary>Owns persistent workgroup statistics and two distinct GPU exposure history buffers.</summary>
internal sealed class CameraExposureTargets : IDisposable
{
    /// <summary>Bounds independent strip tiles while providing enough work to occupy wide GPUs.</summary>
    internal const int HistogramGroupCount = 1024;
    /// <summary>Reserves one complete count/log-sum histogram per workgroup.</summary>
    internal const int HistogramBytes = HistogramGroupCount * 64 * 8;
    private GpuShaderStorageBuffer? histogram;
    private readonly GpuShaderStorageBuffer?[] history = new GpuShaderStorageBuffer?[2];
    private int published;
    #region Public API
    /// <summary>Allocates bounded storage once and retires partial allocations on failure.</summary>
    internal CameraExposureTargets()
    {
        try
        {
            histogram = GpuShaderStorageBuffer.Create(debugName: "Camera.Histogram");
            histogram.Allocate(HistogramBytes);
            for (int i = 0; i < 2; i++)
            {
                history[i] = GpuShaderStorageBuffer.Create(debugName: $"Camera.Exposure.{i}");
                // Reset adaptation does not read undefined initial history.
                history[i]!.Allocate(32);
            }
        }
        catch { Dispose(); throw; }
    }
    /// <summary>Receives the next exposure without overwriting the published history.</summary>
    internal GpuShaderStorageBuffer WriteTarget => history[1 - published]!;
    /// <summary>Supplies disjoint workgroup histograms to the reduction.</summary>
    internal GpuShaderStorageBuffer Histogram => histogram!;
    /// <summary>Supplies the last successfully submitted exposure.</summary>
    internal GpuShaderStorageBuffer Exposure => history[published]!;
    /// <summary>Publishes only after dispatches and their visibility barriers complete submission.</summary>
    internal void Publish() { published = 1 - published; }
    /// <summary>Retires all owned buffers; none borrows a framebuffer or texture.</summary>
    public void Dispose()
    {
        histogram?.Dispose(); histogram = null;
        for (int i = 0; i < 2; i++) { history[i]?.Dispose(); history[i] = null; }
    }
    #endregion
}

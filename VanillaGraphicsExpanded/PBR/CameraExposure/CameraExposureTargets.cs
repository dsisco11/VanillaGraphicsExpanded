using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.CameraExposure;

/// <summary>Owns bounded histogram storage and two distinct temporal exposure images.</summary>
internal sealed class CameraExposureTargets : IDisposable
{
    private DynamicTexture2D? histogram;
    private readonly DynamicTexture2D?[] history = new DynamicTexture2D?[2];
    private readonly GpuFramebuffer?[] historyTargets = new GpuFramebuffer?[2];
    private GpuFramebuffer? histogramTarget;
    private int published;
    #region Public API
    /// <summary>Allocates persistent storage, retiring partially created resources on failure.</summary>
    internal CameraExposureTargets()
    {
        try
        {
            histogram = DynamicTexture2D.Create(64, 1, PixelInternalFormat.Rg32f, debugName: "Camera.Histogram");
            histogramTarget = GpuFramebuffer.CreateSingle(histogram, debugName: "Camera.Histogram");
            for (int i = 0; i < 2; i++)
            {
                history[i] = DynamicTexture2D.Create(1, 1, PixelInternalFormat.R32f, debugName: $"Camera.Exposure.{i}");
                historyTargets[i] = GpuFramebuffer.CreateSingle(history[i], debugName: $"Camera.Exposure.{i}");
            }
        }
        catch { Dispose(); throw; }
    }
    /// <summary>Receives weighted counts and log-luminance sums.</summary>
    internal GpuFramebuffer HistogramTarget => histogramTarget!;
    /// <summary>Receives the next exposure without overwriting the sampled previous exposure.</summary>
    internal GpuFramebuffer WriteTarget => historyTargets[1 - published]!;
    /// <summary>Supplies the histogram to adaptation.</summary>
    internal DynamicTexture2D Histogram => histogram!;
    /// <summary>Supplies the last successfully completed exposure.</summary>
    internal DynamicTexture2D Exposure => history[published]!;
    /// <summary>Publishes the write image only after both passes complete.</summary>
    internal void Publish() { published = 1 - published; }
    /// <summary>Retires borrowing FBOs before the images they reference.</summary>
    public void Dispose()
    {
        histogramTarget?.Dispose(); histogramTarget = null;
        for (int i = 0; i < 2; i++)
        {
            historyTargets[i]?.Dispose(); historyTargets[i] = null;
            history[i]?.Dispose(); history[i] = null;
        }
        histogram?.Dispose(); histogram = null;
    }
    #endregion
}

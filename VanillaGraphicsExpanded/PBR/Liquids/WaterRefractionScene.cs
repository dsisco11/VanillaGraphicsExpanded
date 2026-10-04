using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Owns optional immutable opaque radiance/depth publication for liquid refraction.</summary>
internal sealed class WaterRefractionScene : IDisposable
{
    private GpuFramebuffer? target;
    private GpuFramebuffer? reducedTarget;
    private bool failed;
    private int backgroundScale = 1;
    private DynamicTexture2D? reducedColor;
    private DynamicTexture2D? reducedDepth;

    #region Public API
    #region Publication and resolution
    /// <summary>Supplies the selected background divisor; changing it withdraws current publication.</summary>
    internal int BackgroundScale
    {
        get => backgroundScale;
        set
        {
            int selected = value == 2 ? 2 : 1;
            if (backgroundScale != selected)
            {
                Published = false;
                failed = false;
            }
            backgroundScale = selected;
        }
    }
    /// <summary>Gets restored radiance at the selected background resolution.</summary>
    internal DynamicTexture2D? Color => backgroundScale == 2 ? reducedColor : SourceColor;
    /// <summary>Gets matching depth, with original source UVs in reduced storage.</summary>
    internal DynamicTexture2D? Depth => backgroundScale == 2 ? reducedDepth : SourceDepth;
    /// <summary>Gets the full-resolution composite output before optional reduction.</summary>
    internal DynamicTexture2D? SourceColor { get; private set; }
    /// <summary>Gets full-resolution depth after pre-overlay restoration.</summary>
    internal DynamicTexture2D? SourceDepth { get; private set; }
    /// <summary>Reports only a completely written current-frame receiver pair.</summary>
    internal bool Published { get; private set; }
    #endregion

    #region Frame lifecycle
    /// <summary>Invalidates the preceding frame and prepares borrowed composite plus owned snapshot outputs.</summary>
    internal GpuFramebuffer? BeginFrame(bool enabled, DynamicTexture2D? compositeColor, int backgroundScale = 1)
        => PrepareFrame(enabled && compositeColor?.IsValid == true, compositeColor?.Width ?? 0,
            compositeColor?.Height ?? 0, compositeColor, backgroundScale);

    /// <summary>Prepares full-resolution receiver-only capture without borrowing an ordinary composite image.</summary>
    internal GpuFramebuffer? BeginCapture(int width, int height)
        => PrepareFrame(true, width, height, null, 1);

    /// <summary>Publishes after the owning composite finishes its coherent radiance/depth draw.</summary>
    internal void Publish(Action<GpuFramebuffer, DynamicTexture2D, DynamicTexture2D>? reduce = null)
    {
        Published = false;
        if (target?.IsValid != true || SourceColor?.IsValid != true || SourceDepth?.IsValid != true) return;
        if (BackgroundScale == 2)
        {
            if (reducedTarget?.IsValid != true || reduce is null) return;
            // Publication follows the complete reduction. Exceptions leave the old pair unavailable.
            reduce(reducedTarget, SourceColor, SourceDepth);
        }
        Published = Color?.IsValid == true && Depth?.IsValid == true;
    }

    /// <summary>Invalidates an unfinished frame without requiring resource reallocation.</summary>
    internal void Invalidate() => Published = false;

    /// <summary>Retires owned snapshots while preserving any borrowed composite image.</summary>
    public void Dispose()
    {
        Published = false;
        failed = false;
        target?.Dispose();
        target = null;
        SourceColor?.Dispose();
        SourceColor = null;
        SourceDepth?.Dispose();
        SourceDepth = null;
        RetireReduced();
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Allocates coherent receiver storage and routes its fixed output locations for capture or ordinary composition.</summary>
    private GpuFramebuffer? PrepareFrame(bool enabled, int width, int height, DynamicTexture2D? compositeColor, int backgroundScale)
    {
        Published = false;
        BackgroundScale = backgroundScale;
        if (!enabled || width <= 0 || height <= 0)
        {
            Dispose();
            return null;
        }
        // A failed allocation retains straight-through rendering until toggle, resize or world reset.
        if (failed) return null;
        // Recreate the attachment set together; a resized/replaced composite cannot retain old source storage.
        if (target is not null && (!target.IsValid || SourceColor?.IsValid != true || SourceDepth?.IsValid != true
            || SourceColor.Width != width || SourceColor.Height != height
            || !ReferenceEquals(target.GetAttachment(FramebufferAttachment.ColorAttachment0)?.Resource, compositeColor))) Dispose();
        if (target is null)
        {
            try
            {
                SourceColor = DynamicTexture2D.Create(width, height, PixelInternalFormat.Rgba16f,
                    debugName: "WaterRefraction.Radiance");
                SourceDepth = DynamicTexture2D.Create(width, height, PixelInternalFormat.R32f,
                    debugName: "WaterRefraction.Depth");
                target = compositeColor is null
                    ? GpuFramebuffer.Create([GpuFramebufferAttachment.FromTexture(SourceColor), GpuFramebufferAttachment.FromTexture(SourceDepth)],
                        debugName: "WaterRefraction.Capture", firstColorAttachment: 1)
                    : GpuFramebuffer.CreateMRT([compositeColor, SourceColor, SourceDepth], debugName: "WaterRefraction.Publication");
                if (target is null) throw new InvalidOperationException("Water refraction framebuffer unavailable.");
                if (!target.IsValid) throw new InvalidOperationException("Water refraction framebuffer invalid.");
            }
            catch { Dispose(); failed = true; throw; }
        }
        // The composite always restores overlays at full resolution. Only its final
        // receiver publication is reduced; no attachment is sampled while writable.
        if (BackgroundScale == 1) RetireReduced();
        else if (reducedTarget is null)
        {
            try
            {
                int reducedWidth = (width + 1) / 2;
                int reducedHeight = (height + 1) / 2;
                reducedColor = DynamicTexture2D.Create(reducedWidth, reducedHeight, PixelInternalFormat.Rgba16f,
                    debugName: "WaterRefraction.ReducedRadiance");
                reducedDepth = DynamicTexture2D.Create(reducedWidth, reducedHeight, PixelInternalFormat.Rgba32f,
                    debugName: "WaterRefraction.ReducedDepthAndUv");
                reducedTarget = GpuFramebuffer.CreateMRT([reducedColor, reducedDepth],
                    debugName: "WaterRefraction.Reduction") ?? throw new InvalidOperationException("Water receiver reduction unavailable.");
                if (!reducedTarget.IsValid) throw new InvalidOperationException("Water receiver reduction target invalid.");
            }
            catch { Dispose(); failed = true; throw; }
        }
        return target;
    }

    /// <summary>Retires only reduction storage when full-size backgrounds are selected.</summary>
    private void RetireReduced()
    {
        reducedTarget?.Dispose();
        reducedTarget = null;
        reducedColor?.Dispose();
        reducedColor = null;
        reducedDepth?.Dispose();
        reducedDepth = null;
    }
    #endregion
}

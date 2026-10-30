using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Owns optional immutable opaque radiance/depth publication for liquid refraction.</summary>
internal sealed class WaterRefractionScene : IDisposable
{
    private RHIRenderPassDescription? target;
    private RHIRenderPassDescription? reducedTarget;
    private GpuTextureRef compositeInput;
    private bool failed;
    private int backgroundScale = 1;
    private DynamicTexture2D? reducedColor;
    private DynamicTexture2D? reducedDepth;

    #region Public API
    #region Publication and resolution
    /// <summary>Supplies the fixed reduction declaration for the currently owned reduced allocation.</summary>
    internal RHIRenderPassDescription? ReductionPass => reducedTarget;
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
    internal RHIRenderPassDescription? BeginFrame(bool enabled, DynamicTexture2D? compositeColor, int backgroundScale = 1)
        => PrepareFrame(enabled && compositeColor?.IsValid == true, compositeColor?.Width ?? 0,
            compositeColor?.Height ?? 0, compositeColor, backgroundScale);

    /// <summary>Prepares full-resolution receiver-only capture without borrowing an ordinary composite image.</summary>
    internal RHIRenderPassDescription? BeginCapture(int width, int height)
        => PrepareFrame(true, width, height, null, 1);

    /// <summary>Publishes after the owning composite finishes its coherent radiance/depth draw.</summary>
    internal void Publish(Action<RHIRenderPassDescription, DynamicTexture2D, DynamicTexture2D>? reduce = null)
    {
        Published = false;
        if (target is null || SourceColor?.IsValid != true || SourceDepth?.IsValid != true) return;
        if (BackgroundScale == 2)
        {
            if (reducedTarget is null || reducedColor?.IsValid != true || reducedDepth?.IsValid != true || reduce is null) return;
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
        target = null;
        compositeInput=default;
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
    private RHIRenderPassDescription? PrepareFrame(bool enabled, int width, int height, DynamicTexture2D? compositeColor, int backgroundScale)
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
        if (target is not null && (SourceColor?.IsValid != true || SourceDepth?.IsValid != true
            || SourceColor.Width != width || SourceColor.Height != height
            || compositeInput != (GpuTextureRef)compositeColor)) Dispose();
        if (target is null)
        {
            try
            {
                SourceColor = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, width), Height = System.Math.Max(1, height), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "WaterRefraction.Radiance" }).RequireResource();
                SourceDepth = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, width), Height = System.Math.Max(1, height), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "WaterRefraction.Depth" }).RequireResource();
                compositeInput=compositeColor;
                target = new([
                    compositeColor is null ? new RHIColorAttachment(DiscardOutput:true) : new RHIColorAttachment(Image:RHIAttachment.FromTexture(compositeColor)),
                    new(Image:RHIAttachment.FromTexture(SourceColor)),new(Image:RHIAttachment.FromTexture(SourceDepth))],
                    label:compositeColor is null ? "WaterRefraction.Capture" : "WaterRefraction.Publication");
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
                reducedColor = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, reducedWidth), Height = System.Math.Max(1, reducedHeight), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "WaterRefraction.ReducedRadiance" }).RequireResource();
                reducedDepth = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, reducedWidth), Height = System.Math.Max(1, reducedHeight), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "WaterRefraction.ReducedDepthAndUv" }).RequireResource();
                reducedTarget=new([new(Image:RHIAttachment.FromTexture(reducedColor)),new(Image:RHIAttachment.FromTexture(reducedDepth))],
                    label:"WaterRefraction.Reduction");
            }
            catch { Dispose(); failed = true; throw; }
        }
        return target;
    }

    /// <summary>Retires only reduction storage when full-size backgrounds are selected.</summary>
    private void RetireReduced()
    {
        reducedTarget = null;
        reducedColor?.Dispose();
        reducedColor = null;
        reducedDepth?.Dispose();
        reducedDepth = null;
    }
    #endregion
}

using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Owns optional immutable opaque radiance/depth publication for liquid refraction.</summary>
internal sealed class WaterRefractionScene : IDisposable
{
    private GpuFramebuffer? target;
    private bool failed;
    internal DynamicTexture2D? Color { get; private set; }
    internal DynamicTexture2D? Depth { get; private set; }
    internal bool Published { get; private set; }

    #region Public API
    /// <summary>Invalidates the preceding frame and prepares borrowed composite plus owned snapshot outputs.</summary>
    internal GpuFramebuffer? BeginFrame(bool enabled, DynamicTexture2D? compositeColor)
    {
        Published = false;
        if (!enabled || compositeColor?.IsValid != true)
        {
            Dispose();
            return null;
        }
        // A failed allocation retains straight-through rendering until toggle, resize or world reset.
        if (failed) return null;
        // Recreate the attachment set together; a resized/replaced composite cannot retain old source storage.
        if (target is not null && (!target.IsValid || Color?.IsValid != true || Depth?.IsValid != true
            || Color.Width != compositeColor.Width || Color.Height != compositeColor.Height
            || !ReferenceEquals(target[0], compositeColor))) Dispose();
        if (target is null)
        {
            try
            {
                Color = DynamicTexture2D.Create(compositeColor.Width, compositeColor.Height, PixelInternalFormat.Rgba16f,
                    debugName: "WaterRefraction.Radiance");
                Depth = DynamicTexture2D.Create(compositeColor.Width, compositeColor.Height, PixelInternalFormat.R32f,
                    debugName: "WaterRefraction.Depth");
                target = GpuFramebuffer.CreateMRT([compositeColor, Color, Depth], ownsTextures: false,
                    debugName: "WaterRefraction.Publication") ?? throw new InvalidOperationException("Water refraction framebuffer unavailable.");
                if (!target.IsValid) throw new InvalidOperationException("Water refraction framebuffer invalid.");
            }
            catch { Dispose(); failed = true; throw; }
        }
        return target;
    }

    /// <summary>Publishes after the owning composite finishes its coherent radiance/depth draw.</summary>
    internal void Publish() => Published = target?.IsValid == true && Color?.IsValid == true && Depth?.IsValid == true;

    /// <summary>Invalidates an unfinished frame without requiring resource reallocation.</summary>
    internal void Invalidate() => Published = false;

    /// <summary>Retires owned snapshots while preserving the borrowed composite image.</summary>
    public void Dispose()
    {
        Published = false;
        failed = false;
        target?.Dispose();
        target = null;
        Color?.Dispose();
        Color = null;
        Depth?.Dispose();
        Depth = null;
    }
    #endregion
}

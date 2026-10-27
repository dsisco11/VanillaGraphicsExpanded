using System;
using System.Linq;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Owns compact transport/history images and their pure attachment intentions without creating framebuffer objects.</summary>
internal sealed class CloudVolumeViewTargets : IDisposable
{
    #region Internal API
    /// <summary>Gets stable physical image owners, including optional diagnostic storage.</summary>
    internal DynamicTexture2D[] Images { get; private set; }
    /// <summary>Gets the reduced storage width from its existing texture publication.</summary>
    internal int Width => Images[0].Width;
    /// <summary>Gets the reduced storage height from its existing texture publication.</summary>
    internal int Height => Images[0].Height;
    /// <summary>Gets the three-output raw trace intentions, excluding physical history/diagnostic-only images.</summary>
    internal RHIRenderPassDescription TracePass { get; private set; }
    /// <summary>Gets the current history/optional diagnostic output intentions.</summary>
    internal RHIRenderPassDescription FilterPass { get; private set; }
    /// <summary>Gets the diagnostic image only while diagnostic capture owns its allocation.</summary>
    internal DynamicTexture2D? Diagnostics => Images.Length == 5 ? Images[4] : null;
    /// <summary>Gets authored texel storage without driver padding or allocation queries.</summary>
    internal long Bytes => (long)Width * Height * (Images.Length == 5 ? 54 : Images.Length == 4 ? 46 : 44);

    /// <summary>Allocates ceil-divided images transactionally and freezes their canonical consumer selections.</summary>
    internal CloudVolumeViewTargets(int width, int height, int divisor, bool history = false)
    {
        if (divisor is not (2 or 4 or 8)) throw new ArgumentOutOfRangeException(nameof(divisor));
        Images = new DynamicTexture2D[history ? 4 : 3];
        TextureFormat[] formats = [TextureFormat.Rgba32f, TextureFormat.Rgb32f, TextureFormat.Rgba32f, TextureFormat.R16f];
        try
        {
            for (int i = 0; i < Images.Length; i++)
                Images[i] = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription
                {
                    Dimension = TextureDimension.Two, Format = formats[i], Width = Math.Max(1, (width + divisor - 1) / divisor),
                    Height = Math.Max(1, (height + divisor - 1) / divisor), MipLevels = 1, Storage = ResourceStorageMode.Mutable,
                    Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = 0, Label = $"Cloud.Volume.View.{i}"
                }).RequireResource();
            TracePass = Describe(Images, 3);
            FilterPass = Describe(Images, Images.Length);
        }
        catch { foreach (var image in Images) image?.Dispose(); throw; }
    }

    /// <summary>Installs or retires optional diagnostics without replacing physical history images or acquiring target authority.</summary>
    internal void ConfigureDiagnostics(bool enabled)
    {
        if (Images.Length < 4) throw new InvalidOperationException("Raw cloud traces have no temporal diagnostics.");
        if ((Diagnostics is not null) == enabled) return;
        DynamicTexture2D? added = null;
        try
        {
            if (enabled) added = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription
            {
                Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = Width, Height = Height,
                MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest,
                BaseLevel = 0, MaxLevel = 0, Label = "Cloud.Volume.View.Diagnostics"
            }).RequireResource();
            DynamicTexture2D[] next = enabled ? [..Images, added!] : Images[..4];
            var replacement = Describe(next, next.Length);
            var removed = Diagnostics;
            Images = next; FilterPass = replacement;
            if (!enabled) removed?.Dispose();
        }
        catch { added?.Dispose(); throw; }
    }
    #endregion

    #region Public API
    /// <summary>Retires physical images through their original owners; dependent target retirement belongs to the backend.</summary>
    public void Dispose() => RenderingCleanup.Run(Images.Select(image => (Action)image.Dispose).ToArray());
    #endregion

    #region Private
    /// <summary>Declares disjoint output images and the same per-channel transport/history clear semantics.</summary>
    private static RHIRenderPassDescription Describe(DynamicTexture2D[] images, int count)
        => new(Enumerable.Range(0, count).Select(i => new RHIColorAttachment(Image: RHIAttachment.FromTexture(images[i]),
            Load: AttachmentLoad.Clear, Clear: i == 1 ? ColorClearValue.Float(1, 1, 1, 1) : ColorClearValue.Float(0, 0, 0, 0))),
            label: "Cloud.Volume.View.Reduced");
    #endregion
}

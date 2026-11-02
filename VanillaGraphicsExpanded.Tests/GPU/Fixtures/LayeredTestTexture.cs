using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Assertions;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Builds explicitly layered fixture inputs from existing numerical test images.</summary>
internal static class LayeredTestTexture
{
    #region Public API
    /// <summary>Copies matching fixture images into separate array layers without changing their float precision.</summary>
    internal static Texture3D Create(GpuObservationPrograms programs, params GpuTexture?[] images)
    {
        if (images.Length == 0) throw new ArgumentException("At least one fixture layer is required.", nameof(images));
        int width = images.Max(image => image?.Width ?? 1), height = images.Max(image => image?.Height ?? 1);
        var texture = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, width), Height = System.Math.Max(1, height), Depth = System.Math.Max(1, images.Length), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        try
        {
            GpuLayerCopy? conversion = null;
                for (int layer = 0; layer < images.Length; layer++)
                {
                    var image = images[layer];
                    if (image is null)
                    {
                        RHICommandContext.Current.ClearTexture(texture, new(new(0, layer), 0, 0, 0, width, height, 1),
                            new(PixelChannelLayout.Rgba, PixelComponentType.Float), new byte[16]);
                        continue;
                    }
                    if (image.InternalFormat == TextureFormat.Rgba32f && image.Width == width && image.Height == height && image.Depth == 1)
                        RHICommandContext.Current.CopyTexture(image, new(new(0, 0), 0, 0, 0, width, height, 1),
                            texture, new(new(0, layer), 0, 0, 0, width, height, 1));
                    else
                        (conversion ??= new GpuLayerCopy(programs)).Copy(image, texture, layer);
                }

            return texture;
        }
        catch { texture.Dispose(); throw; }
    }

    /// <summary>Downloads an explicitly requested failure-diagnostic layer; routine assertions retain GPU images or nominated local tuples.</summary>
    internal static float[] ReadDiagnosticLayer(Texture3D texture, int layer)
    {
        using var pixels = texture.ReadPixelsRegion(0, 0, texture.Width, texture.Height, layer);
        return pixels.Span.ToArray();
    }
    #endregion
}

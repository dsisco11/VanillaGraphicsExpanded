using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Builds explicitly layered fixture inputs from existing numerical test images.</summary>
internal static class LayeredTestTexture
{
    #region Public API
    /// <summary>Copies matching fixture images into separate array layers without changing their float precision.</summary>
    internal static Texture3D Create(params GpuTexture?[] images)
    {
        int width = images.Max(image => image?.Width ?? 1), height = images.Max(image => image?.Height ?? 1);
        var texture = Texture3D.Create(width, height, images.Length, PixelInternalFormat.Rgba32f,
            TextureFilterMode.Nearest, TextureTarget.Texture2DArray);
        try
        {
            for (int layer = 0; layer < images.Length; layer++)
            {
                var image = images[layer];
                if (image is null)
                {
                    texture.UploadDataImmediate(new float[width * height * 4], 0, 0, layer, width, height, 1);
                    continue;
                }
                float[] source = image.ReadPixels();
                int channels = source.Length / (image.Width * image.Height);
                float[] data = new float[width * height * 4];
                // Constant fixtures can be broadcast to the dimensions of a spatial test input.
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                {
                    int src = ((y * image.Height / height) * image.Width + x * image.Width / width) * channels;
                    for (int c = 0; c < channels; c++) data[(y * width + x) * 4 + c] = source[src + c];
                }
                texture.UploadDataImmediate(data, 0, 0, layer, width, height, 1);
            }
            return texture;
        }
        catch { texture.Dispose(); throw; }
    }

    /// <summary>Reads one selected array layer through the texture owner's readback API.</summary>
    internal static float[] Read(Texture3D texture, int layer)
    {
        using var pixels = texture.ReadPixelsRegion(0, 0, texture.Width, texture.Height, layer);
        return pixels.Span.ToArray();
    }
    #endregion
}

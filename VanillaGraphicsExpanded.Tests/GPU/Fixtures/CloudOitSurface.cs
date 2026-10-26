using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns the native six-attachment OIT layout independently of reduced cloud history storage.</summary>
internal sealed class CloudOitSurface : IDisposable
{
    internal readonly DynamicTexture2D[] Images = new DynamicTexture2D[6];
    internal readonly GpuFramebuffer Target;

    #region Public API
    /// <summary>Allocates a native OIT output target transactionally for production submission tests.</summary>
    internal CloudOitSurface(int width, int height, string name)
    {
        PixelInternalFormat[] formats = [PixelInternalFormat.Rgb32f, PixelInternalFormat.R32f,
            PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f];
        string[] names = ["RevealageBins", "Transmission", "Glow", "Accumulation0", "Accumulation1", "Accumulation2"];
        try
        {
            for (int i = 0; i < Images.Length; i++)
                Images[i] = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Format((OpenTK.Graphics.OpenGL.PixelInternalFormat)(formats[i])), Width = System.Math.Max(1, width), Height = System.Math.Max(1, height), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = $"{name}.{names[i]}" }).RequireResource();
            Target = DynamicGpuFramebuffer.CreateMRT(Images, debugName: name)!;
        }
        catch { foreach (var image in Images) image?.Dispose(); throw; }
    }
    /// <summary>Releases framebuffer metadata before its owned attachment storage.</summary>
    public void Dispose()
    {
        var actions = new List<Action> { Target.Dispose };
        actions.AddRange(Images.Select(image => (Action)image.Dispose));
        RenderingCleanup.Run(actions.ToArray());
    }
    #endregion
}

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Describes a texture's shader-image binding without allocating a separate OpenGL texture view.</summary>
public readonly record struct GpuTextureBinding(GpuTexture Texture, TextureAccess Access = TextureAccess.ReadOnly,
    int Level = 0, bool Layered = false, int Layer = 0, SizedInternalFormat? Format = null);

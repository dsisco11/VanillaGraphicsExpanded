using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Applies generated typed property assignments through the owning layout's linked interface.</summary>
internal static class ShaderBindingAccess
{
    #region Public API
    /// <summary>Binds a managed texture using the existing sampler activity and unit-resolution policy.</summary>
    internal static void Sampler(GpuProgramLayout layout, int program, string name, GpuTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        layout.TryBindSamplerTextureActive(program, name, texture.TextureTarget, texture.TextureId, 0, null);
    }

    /// <summary>Preserves the image view's explicit access, format and layering while skipping inactive uniforms.</summary>
    internal static void Image(GpuProgramLayout layout, int program, string name, GpuTextureBinding image)
    {
        ArgumentNullException.ThrowIfNull(image.Texture);
        layout.TryBindImageTextureActive(program, name, image.Texture.TextureId, image.Level, image.Layered,
            image.Layer, image.Access, image.Format ?? (SizedInternalFormat)image.Texture.InternalFormat, null);
    }

    /// <summary>Binds a texture with the default read-only image view at mip zero and layer zero.</summary>
    internal static void Image(GpuProgramLayout layout, int program, string name, GpuTexture texture)
    {
        // Direct texture setters share the view's defaults and format inference.
        Image(layout, program, name, new GpuTextureBinding(texture));
    }

    /// <summary>Binds an active uniform buffer through the established layout resolver.</summary>
    internal static void UniformBlock(GpuProgramLayout layout, int program, string name, GpuUniformBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        layout.TryBindUniformBlock(program, name, buffer);
    }

    /// <summary>Binds an active storage buffer through the established layout resolver.</summary>
    internal static void StorageBlock(GpuProgramLayout layout, int program, string name, GpuShaderStorageBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        layout.TryBindShaderStorageBlock(program, name, buffer);
    }
    #endregion
}

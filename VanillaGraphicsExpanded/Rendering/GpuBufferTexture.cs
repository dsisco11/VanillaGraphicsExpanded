using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Preferred name for <see cref="GpuBufferView"/>: a texture buffer (<c>GL_TEXTURE_BUFFER</c>) that exposes a buffer as texels.
/// </summary>
public sealed class GpuBufferTexture : GpuBufferView
{
    /// <summary>Declares an unpublished buffer texture for manager initialization.</summary>
    internal GpuBufferTexture() { }




}

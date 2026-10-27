using OpenTK.Graphics.OpenGL;
using Native = OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns image-unit native dispatch while retaining one cache authority for all view selectors.</summary>
internal static class OpenGLImageSlotBindings
{
    #region Internal API
    /// <summary>Translates a semantic image selection using published facts, retaining no public native-token escape.</summary>
    internal static void BindImageTexture(StateCache cache, int unit, RHIImageBinding image)
    {
        int texture = image.Texture.IsEmpty ? 0 : image.Texture.TextureId;
        var access = image.Access switch
        {
            ImageAccess.ReadOnly => TextureAccess.ReadOnly,
            ImageAccess.WriteOnly => TextureAccess.WriteOnly,
            ImageAccess.ReadWrite => TextureAccess.ReadWrite,
            _ => throw new System.ArgumentOutOfRangeException(nameof(image))
        };
        var format = texture == 0 ? PixelInternalFormat.Rgba8
            : image.Format is { } supplied ? OpenGLImageValues.Format(supplied) : image.Texture.RequireImage(image.Level).Format;
        BindImageTexture(cache, unit, texture, texture == 0 ? 0 : image.Level,
            texture != 0 && image.Layered, texture == 0 ? 0 : image.Layer, access, (SizedInternalFormat)format);
    }
    /// <summary>Suppresses only a known identical view and withdraws the slot before dispatch or interruption.</summary>
    internal static void BindImageTexture(StateCache cache, int unit, int texture, int level, bool layered,
        int layer, TextureAccess access, SizedInternalFormat format)
    {
        var backend = RHICommandContext.Current;
        backend.RequireContext();
        var binding = new StateCache.ImageBinding(texture, level, layered, layer, access, format);
        if (!cache.NeedsImageBinding(unit, binding)) return;
        cache.ForgetImageBinding(unit);
        Native.GL.BindImageTexture(unit, texture, level, layered, layer, (Native.TextureAccess)access, (Native.SizedInternalFormat)format);
        backend.RequireContext();
        cache.EstablishImageBinding(unit, binding);
    }
    #endregion
}

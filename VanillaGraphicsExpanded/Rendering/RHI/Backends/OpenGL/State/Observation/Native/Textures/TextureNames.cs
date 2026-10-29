using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Consumes texture factory outputs without replacing original native identities.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Records GenTextures results without replacing live original identities.</summary>
    [InterceptOpenGL("glGenTextures")]
    [InterceptOpenGL("glGenTexturesEXT")]
    internal static void ObserveGenTextures(int count, uint* names)
    {
        if (count <= 0 || names == null || !OpenGLObservationContext.TryGet(out _, out _, false)) return;
        var manager = RHIResourceManagerSystem.CurrentManager!;
        for (int i = 0; i < count; i++)
            manager.ObserveImageCreation(GpuResourceKind.Texture, unchecked((int)names[i]), false);
    }

    /// <summary>Records CreateTextures results without replacing live original identities.</summary>
    [InterceptOpenGL("glCreateTextures")]
    internal static void ObserveCreateTextures(int target, int count, uint* names)
    {
        if (count <= 0 || names == null || !OpenGLObservationContext.TryGet(out _, out _, false)) return;
        if (!TryImageTarget(target, out var dimension) || dimension == TextureDimension.RenderbufferTexture) return;
        var manager = RHIResourceManagerSystem.CurrentManager!;
        for (int i = 0; i < count; i++)
            manager.ObserveImageCreation(GpuResourceKind.Texture, unchecked((int)names[i]), true, dimension);
    }
    #endregion

    #region Private
    /// <summary>Recognizes object targets without reinterpreting proxy or cube-face selectors.</summary>
    private static bool TryImageTarget(int target, out TextureDimension dimension)
    {
        dimension = TextureDimension.Undefined;
        if (!RepresentsTextureBinding((TextureTarget)target)) return false;
        dimension = OpenGLImageValues.Dimension((TextureTarget)target);
        return dimension != TextureDimension.Undefined;
    }
    /// <summary>Normalizes face selectors used by EXT named queries into their immutable object target.</summary>
    private static bool TryImageSelectorTarget(int selector, out TextureDimension dimension)
    {
        if (selector >= (int)TextureTarget.TextureCubeMapPositiveX && selector <= (int)TextureTarget.TextureCubeMapNegativeZ)
            selector = (int)TextureTarget.TextureCubeMap;
        return TryImageTarget(selector, out dimension);
    }
    #endregion
}

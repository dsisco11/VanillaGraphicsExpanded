using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Maintains explicit-unit EXT binding and its typed object realization.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Retains the selected unit without changing the active texture selector.</summary>
    [InterceptOpenGL("glBindMultiTextureEXT")]
    internal static void ObserveBindMultiTextureEXT(int nativeUnit, int nativeTarget, uint name)
    {
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false)
            || !TryImageTarget(nativeTarget, out var target)) return;
        int unit = nativeUnit - (int)TextureUnit.Texture0;
        if (!GpuSupport.HasCachedCapabilitiesForCurrentContext
            || (uint)unit >= (uint)GpuSupport.Graphics.MaxCombinedTextureImageUnits) return;
        int id = unchecked((int)name);
        var entry = GpuResourceManagerSystem.CurrentManager!.RealizeObservedImage(GpuResourceKind.Texture, id, target, implicitCreation: false)
            as GpuResourceMetadataEntry<GpuTextureMetadata>;
        cache.ForgetImageBinding((TextureTarget)nativeTarget, unit);
        if (id == 0 || entry?.IsRealized == true && entry.Metadata.Target == target)
            cache.ImageBindings(unit)[(TextureTarget)nativeTarget] = id;
    }
    #endregion
}

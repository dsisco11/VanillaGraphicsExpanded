using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Maintains existing texture and image slots after native multi-binding calls.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Publishes a direct unit selection without changing the active texture unit.</summary>
    [InterceptOpenGL("glBindTextureUnit")]
    internal static void ObserveBindTextureUnit(uint unit, uint texture)
    {
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false)
            || !GpuSupport.HasCachedCapabilitiesForCurrentContext
            || unit >= (uint)GpuSupport.Graphics.MaxCombinedTextureImageUnits) return;
        cache.ObserveTextureUnit((int)unit, texture);
    }

    /// <summary>Publishes each independently accepted multi-bind selection.</summary>
    [InterceptOpenGL("glBindTextures")]
    internal static void ObserveBindTextures(uint first, int count, uint* textures)
    {
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false)
            || !GpuSupport.HasCachedCapabilitiesForCurrentContext || count < 0
            || (ulong)first + (uint)count > (uint)GpuSupport.Graphics.MaxCombinedTextureImageUnits) return;
        for (int index = 0; index < count; index++)
            cache.ObserveTextureUnit((int)first + index, textures == null ? 0 : textures[index]);
    }

    /// <summary>Withdraws only image slots affected by a native image multi-bind.</summary>
    [InterceptOpenGL("glBindImageTextures")]
    internal static void ObserveBindImageTextures(uint first, int count, uint* textures)
    {
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false)
            || !GpuSupport.HasCachedCapabilitiesForCurrentContext || count < 0
            || (ulong)first + (uint)count > (uint)GpuSupport.Graphics.MaxImageUnits) return;
        var manager = GpuResourceManagerSystem.CurrentManager!;
        for (int index = 0; index < count; index++)
        {
            int id = textures == null ? 0 : unchecked((int)textures[index]);
            var entry = id == 0 ? null : manager.FindObservedResource(GpuResourceKind.Texture, id);
            if (id == 0 || entry is null || entry.IsRealized)
                cache.ForgetImageBinding((int)first + index);
        }
    }
    #endregion

    #region Private
    /// <summary>Updates one established target or clears all known targets for a zero texture.</summary>
    private void ObserveTextureUnit(int unit, uint name)
    {
        var bindings = ImageBindings(unit);
        if (name == 0)
        {
            foreach (var binding in bindings) bindings[binding.Key] = 0;
            return;
        }
        var entry = GpuResourceManagerSystem.CurrentManager!.FindObservedResource(GpuResourceKind.Texture, unchecked((int)name))
            as GpuResourceMetadataEntry<GpuTextureMetadata>;
        if (entry is null) { bindings.Clear(); return; }
        if (!entry.IsRealized) return;
        if (entry.Metadata.Target == TextureDimension.Undefined) { bindings.Clear(); return; }
        bindings[OpenGLImageValues.Target(entry.Metadata.Target)] = unchecked((int)name);
    }
    #endregion
}

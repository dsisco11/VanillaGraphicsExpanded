using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Acquires one externally changed image while preserving unrelated texture descriptors.</summary>
internal static class GpuTextureImageReader
{
    #region Internal API
    /// <summary>Reads the one driver-selected sample result when cached format sample counts are unavailable.</summary>
    internal static int ReadBoundSamples(TextureTarget target)
    {
        GL.GetTexLevelParameter(target, 0, GetTextureParameter.TextureSamples, out int samples);
        if (samples <= 0) throw new System.InvalidOperationException("Multisample texture allocation has no sample count.");
        return samples;
    }

    /// <summary>Reads the selected level or cube face, including an explicitly unallocated result.</summary>
    internal static bool TryRead(int texture, TextureTarget target, TextureTarget face, int level, out GpuTextureLevelMetadata image)
    {
        image = default;
        bool bound = face != 0 || !GpuSupport.Graphics.SupportsArbDirectStateAccess;
        using var binding = bound ? StateCache.Current.BindTextureScope(target, 0, texture) : (StateCache.TextureScope?)null;
        var queryTarget = face != 0 ? face : target;
        int width = Read(texture, queryTarget, level, GetTextureParameter.TextureWidth, bound);
        if (width < 0) return false;
        if (width == 0) { image = new(level, face, 0, 0, 0, 0, 0, false); return true; }
        int height = Read(texture, queryTarget, level, GetTextureParameter.TextureHeight, bound);
        int depth = Read(texture, queryTarget, level, GetTextureParameter.TextureDepth, bound);
        int format = Read(texture, queryTarget, level, GetTextureParameter.TextureInternalFormat, bound);
        int samples = target is TextureTarget.Texture2DMultisample or TextureTarget.Texture2DMultisampleArray
            ? Read(texture, queryTarget, level, GetTextureParameter.TextureSamples, bound) : 0;
        if (height < 0 || depth < 0 || format <= 0 || samples < 0) return false;
        image = new(level, face, width, System.Math.Max(1, height), System.Math.Max(1, depth), (PixelInternalFormat)format, samples, true);
        return true;
    }
    #endregion

    #region Private
    /// <summary>Reads one descriptor component without consuming the native error queue.</summary>
    private static int Read(int texture, TextureTarget target, int level, GetTextureParameter parameter, bool bound)
    {
        int value = -1;
        if (bound) GL.GetTexLevelParameter(target, level, parameter, out value);
        else GL.GetTextureLevelParameter(texture, level, parameter, out value);
        return value;
    }
    #endregion
}

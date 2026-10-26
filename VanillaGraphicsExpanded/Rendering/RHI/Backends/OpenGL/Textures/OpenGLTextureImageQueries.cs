using OpenTK.Graphics.OpenGL;
using Native = OpenTK.Graphics.OpenGL4;
using GL = OpenTK.Graphics.OpenGL4.GL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Acquires one externally changed image while preserving unrelated texture descriptors.</summary>
internal static class OpenGLTextureImageQueries
{
    #region Internal API


    /// <summary>Reads the selected level or cube face, including an explicitly unallocated result.</summary>
    internal static bool TryRead(int texture, TextureTarget target, TextureTarget face, int level, OpenGLBackend backend, out GpuTextureLevelMetadata image)
    {
        image = default;
        bool bound = face != 0;
        using var binding = bound ? StateCache.Current.BindTextureScope(target, 0, texture) : (StateCache.TextureScope?)null;
        var queryTarget = face != 0 ? face : target;
        int width = Read(texture, queryTarget, level, GetTextureParameter.TextureWidth, backend);
        if (width < 0) return false;
        if (width == 0) { image = new(level, face, 0, 0, 0, 0, 0, false); return true; }
        int height = Read(texture, queryTarget, level, GetTextureParameter.TextureHeight, backend);
        int depth = Read(texture, queryTarget, level, GetTextureParameter.TextureDepth, backend);
        int format = Read(texture, queryTarget, level, GetTextureParameter.TextureInternalFormat, backend);
        int samples = target is TextureTarget.Texture2DMultisample or TextureTarget.Texture2DMultisampleArray
            ? Read(texture, queryTarget, level, GetTextureParameter.TextureSamples, backend) : 0;
        if (height < 0 || depth < 0 || format <= 0 || samples < 0) return false;
        image = new(level, face, width, System.Math.Max(1, height), System.Math.Max(1, depth), (PixelInternalFormat)format, samples, true);
        return true;
    }
    #endregion

    #region Private
    /// <summary>Reads one descriptor component without consuming the native error queue.</summary>
    private static int Read(int texture, TextureTarget target, int level, GetTextureParameter parameter, OpenGLBackend backend)
    {
        int value = -1;
        if (target >= TextureTarget.TextureCubeMapPositiveX && target <= TextureTarget.TextureCubeMapNegativeZ
) GL.GetTexLevelParameter((Native.TextureTarget)target, level, (Native.GetTextureParameter)parameter, out value);
        else GL.GetTextureLevelParameter(texture, level, (Native.GetTextureParameter)parameter, out value);
        backend.RequireContext();
        return value;
    }
    #endregion
}

using System;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;
using Native = OpenTK.Graphics.OpenGL4;
using GL = OpenTK.Graphics.OpenGL4.GL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Reads unknown external image allocation without treating texel contents as metadata.</summary>
internal readonly struct OpenGLTextureQueries
{
    private readonly TextureTarget knownTarget;
    private readonly OpenGLBackend backend;

    #region Internal API
    /// <summary>Retains established shape and original backend for selected external queries.</summary>
    internal OpenGLTextureQueries(TextureTarget knownTarget, OpenGLBackend backend)
    {
        this.backend = backend;
        this.knownTarget = knownTarget >= TextureTarget.TextureCubeMapPositiveX
            && knownTarget <= TextureTarget.TextureCubeMapNegativeZ ? TextureTarget.TextureCubeMap : knownTarget;
    }
    #endregion

    #region Public API
    /// <summary>Checks realization during explicit external discovery only.</summary>
    public bool IsResource(nint id)
    {
        backend.RequireContext();
        bool result = id != 0 && id >= int.MinValue && id <= uint.MaxValue && GL.IsTexture(unchecked((int)id));
        backend.RequireContext();
        return result;
    }

    /// <inheritdoc />
    public bool TryRead(nint id, out GpuTextureMetadata metadata)
    {
        metadata = default;
        if (!IsResource(id)) return false;
        int texture = unchecked((int)id);
        var target = knownTarget;
        if (target == 0)
        {
            // Core named discovery identifies unknown external shape without speculative binds.
            GL.GetTextureParameter(texture, Native.GetTextureParameter.TextureTarget, out int targetValue);
            backend.RequireContext();
            target = (TextureTarget)targetValue;
        }
        if (target == (TextureTarget)All.TextureRenderbufferNv)
            return GpuRenderbufferTextureMetadataReader.TryRead(texture, out metadata);
        // The canonical image descriptor has three spatial axes. Unsupported targets must
        // remain unavailable rather than silently dropping dimensions or associations.
        if (target is not (TextureTarget.Texture1D or TextureTarget.Texture2D or TextureTarget.Texture3D
            or TextureTarget.Texture1DArray or TextureTarget.Texture2DArray or TextureTarget.TextureCubeMap
            or TextureTarget.TextureCubeMapArray or TextureTarget.TextureRectangle
            or TextureTarget.Texture2DMultisample or TextureTarget.Texture2DMultisampleArray)) return false;
        // Query actual allocation rather than inferring mip storage from requested dimensions.
        // Cube faces require face-specific bound queries; the scope restores incoming state.
        bool bound = target == TextureTarget.TextureCubeMap;
        using var binding = bound
            ? OpenGlStateCache.Current.BindTextureScope(target, 0, texture) : (OpenGlStateCache.TextureScope?)null;
        int immutable = ReadParameter(texture, GetTextureParameter.TextureImmutableFormat);
        int immutableLevels = ReadParameter(texture, GetTextureParameter.TextureImmutableLevels);
        if (immutable < 0 || immutableLevels < 0) return false;
        GpuTextureViewMetadata? view = null;
        if (immutable != 0)
        {
            int viewLevel = ReadParameter(texture, GetTextureParameter.TextureViewMinLevel);
            int viewLevels = ReadParameter(texture, GetTextureParameter.TextureViewNumLevels);
            int viewLayer = ReadParameter(texture, GetTextureParameter.TextureViewMinLayer);
            int viewLayers = ReadParameter(texture, GetTextureParameter.TextureViewNumLayers);
            if (viewLevel < 0 || viewLevels < 0 || viewLayer < 0 || viewLayers < 0) return false;
            if (viewLevels > 0 && viewLayers > 0) view = new(viewLevel, viewLevels, viewLayer, viewLayers);
        }
        var levels = ImmutableArray.CreateBuilder<GpuTextureLevelMetadata>();
        int faces = target == TextureTarget.TextureCubeMap ? 6 : 1;
        int levelLimit = target is TextureTarget.TextureRectangle or TextureTarget.TextureBuffer
            or TextureTarget.Texture2DMultisample or TextureTarget.Texture2DMultisampleArray ? 1 : 32;
        if (levelLimit > 1)
        {
            int limit = target == TextureTarget.Texture3D ? backend.NativeCapabilities.Max3DTextureSize
                : target is TextureTarget.TextureCubeMap or TextureTarget.TextureCubeMapArray ? backend.NativeCapabilities.MaxCubeMapTextureSize : backend.NativeCapabilities.MaxTextureSize;
            levelLimit = 1;
            while ((limit >>= 1) > 0) levelLimit++;
        }
        // Native image queries use view-relative levels. Querying beyond the exposed range
        // can reveal source images or exceed the source's legal mip indices.
        if (view is { } selection) levelLimit = Math.Min(levelLimit, selection.LevelCount);
        for (int level = 0; level < levelLimit; level++)
        {
            for (int face = 0; face < faces; face++)
            {
                TextureTarget imageTarget = faces == 6 ? TextureTarget.TextureCubeMapPositiveX + face : target;
                int width = ReadLevel(texture, imageTarget, level, GetTextureParameter.TextureWidth);
                if (width < 0) return false;
                if (width == 0) continue;
                int height = ReadLevel(texture, imageTarget, level, GetTextureParameter.TextureHeight);
                int depth = ReadLevel(texture, imageTarget, level, GetTextureParameter.TextureDepth);
                int format = ReadLevel(texture, imageTarget, level, GetTextureParameter.TextureInternalFormat);
                int samples = target is TextureTarget.Texture2DMultisample or TextureTarget.Texture2DMultisampleArray
                    ? ReadLevel(texture, imageTarget, level, GetTextureParameter.TextureSamples) : 0;
                if (format <= 0 || height < 0 || depth < 0 || samples < 0) return false;
                levels.Add(new(level, faces == 6 ? OpenGLImageValues.Face(imageTarget) : CubeFace.None, width, Math.Max(1, height), Math.Max(1, depth),
                    OpenGLImageValues.Format((PixelInternalFormat)format), samples, true));
            }

        }
        if (levels.Count == 0) return false;
        int baseLevel = ReadParameter(texture, GetTextureParameter.TextureBaseLevel);
        int maxLevel = ReadParameter(texture, GetTextureParameter.TextureMaxLevel);
        if (immutable < 0 || immutableLevels < 0 || baseLevel < 0 || maxLevel < 0) return false;
        metadata = new(OpenGLImageValues.Dimension(target), levels.ToImmutable(), immutable != 0, immutableLevels, baseLevel, maxLevel, View: view);
        return true;
    }
    #endregion

    #region Private
    /// <summary>Reads object parameters through the core named API on the original context.</summary>
    private int ReadParameter(int texture, GetTextureParameter parameter)
    {
        int value = -1;
        GL.GetTextureParameter(texture, (Native.GetTextureParameter)parameter, out value);
        backend.RequireContext();
        return value;
    }
    /// <summary>Reads one image descriptor with an invalid sentinel and preserves the native error queue.</summary>
    private int ReadLevel(int texture, TextureTarget target, int level, GetTextureParameter parameter)
    {
        int value = -1;
        if (target >= TextureTarget.TextureCubeMapPositiveX && target <= TextureTarget.TextureCubeMapNegativeZ)
            GL.GetTexLevelParameter((Native.TextureTarget)target, level, (Native.GetTextureParameter)parameter, out value);
        else GL.GetTextureLevelParameter(texture, level, (Native.GetTextureParameter)parameter, out value);
        backend.RequireContext();
        return value;
    }
    #endregion
}

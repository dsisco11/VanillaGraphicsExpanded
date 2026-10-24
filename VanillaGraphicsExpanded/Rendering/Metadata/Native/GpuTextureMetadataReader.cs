using System;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Captures complete allocated texture images without treating texel contents as metadata.</summary>
internal sealed class GpuTextureMetadataReader : IGpuResourceMetadataReader<GpuTextureMetadata>
{
    internal static readonly GpuTextureMetadataReader Instance = new();
    private readonly TextureTarget knownTarget;

    #region Internal API
    /// <summary>Retains the object's established target for acquisition on APIs without named texture queries.</summary>
    internal GpuTextureMetadataReader(TextureTarget knownTarget = 0)
    {
        this.knownTarget = knownTarget >= TextureTarget.TextureCubeMapPositiveX
            && knownTarget <= TextureTarget.TextureCubeMapNegativeZ ? TextureTarget.TextureCubeMap : knownTarget;
    }
    #endregion

    #region Public API
    /// <inheritdoc />
    public bool IsResource(nint id) => id != 0 && id >= int.MinValue && id <= uint.MaxValue && GL.IsTexture(unchecked((int)id));

    /// <inheritdoc />
    public bool TryRead(nint id, out GpuTextureMetadata metadata)
    {
        metadata = default;
        if (!IsResource(id)) return false;
        int texture = unchecked((int)id);
        var target = knownTarget;
        if (target == 0)
        {
            // Older APIs cannot discover an arbitrary object's target without speculative binds.
            // Callers editing a known target supply it; unsupported external discovery stays unavailable.
            if (!GpuSupport.Graphics.SupportsArbDirectStateAccess) return false;
            GL.GetTextureParameter(texture, GetTextureParameter.TextureTarget, out int targetValue);
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
        bool bound = target == TextureTarget.TextureCubeMap || !GpuSupport.Graphics.SupportsArbDirectStateAccess;
        using var binding = bound
            ? StateCache.Current.BindTextureScope(target, 0, texture) : (StateCache.TextureScope?)null;
        var capabilities = GpuSupport.Graphics;
        int immutable = capabilities.SupportsTextureStorage
            ? ReadParameter(texture, target, GetTextureParameter.TextureImmutableFormat, bound) : 0;
        int immutableLevels = capabilities.SupportsTextureView
            ? ReadParameter(texture, target, GetTextureParameter.TextureImmutableLevels, bound) : 0;
        if (immutable < 0 || immutableLevels < 0) return false;
        GpuTextureViewMetadata? view = null;
        if (immutable != 0 && capabilities.SupportsTextureView)
        {
            int viewLevel = ReadParameter(texture, target, GetTextureParameter.TextureViewMinLevel, bound);
            int viewLevels = ReadParameter(texture, target, GetTextureParameter.TextureViewNumLevels, bound);
            int viewLayer = ReadParameter(texture, target, GetTextureParameter.TextureViewMinLayer, bound);
            int viewLayers = ReadParameter(texture, target, GetTextureParameter.TextureViewNumLayers, bound);
            if (viewLevel < 0 || viewLevels < 0 || viewLayer < 0 || viewLayers < 0) return false;
            if (viewLevels > 0 && viewLayers > 0) view = new(viewLevel, viewLevels, viewLayer, viewLayers);
        }
        var levels = ImmutableArray.CreateBuilder<GpuTextureLevelMetadata>();
        int faces = target == TextureTarget.TextureCubeMap ? 6 : 1;
        int levelLimit = target is TextureTarget.TextureRectangle or TextureTarget.TextureBuffer
            or TextureTarget.Texture2DMultisample or TextureTarget.Texture2DMultisampleArray ? 1 : 32;
        if (levelLimit > 1)
        {
            int limit = target == TextureTarget.Texture3D ? GpuSupport.Graphics.Max3DTextureSize
                : target == TextureTarget.TextureCubeMap ? GpuSupport.Graphics.MaxCubeMapTextureSize : GpuSupport.Graphics.MaxTextureSize;
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
                int width = ReadLevel(texture, imageTarget, level, GetTextureParameter.TextureWidth, bound);
                if (width < 0) return false;
                if (width == 0) continue;
                int height = ReadLevel(texture, imageTarget, level, GetTextureParameter.TextureHeight, bound);
                int depth = ReadLevel(texture, imageTarget, level, GetTextureParameter.TextureDepth, bound);
                int format = ReadLevel(texture, imageTarget, level, GetTextureParameter.TextureInternalFormat, bound);
                int samples = target is TextureTarget.Texture2DMultisample or TextureTarget.Texture2DMultisampleArray
                    ? ReadLevel(texture, imageTarget, level, GetTextureParameter.TextureSamples, bound) : 0;
                if (format <= 0 || height < 0 || depth < 0 || samples < 0) return false;
                levels.Add(new(level, faces == 6 ? imageTarget : 0, width, Math.Max(1, height), Math.Max(1, depth),
                    (PixelInternalFormat)format, samples, true));
            }

        }
        if (levels.Count == 0) return false;
        // Before texture views, immutable storage exposes its allocated image chain but
        // not TEXTURE_IMMUTABLE_LEVELS. Derive that count from the images just queried.
        if (!capabilities.SupportsTextureView && immutable != 0)
            foreach (var image in levels) immutableLevels = Math.Max(immutableLevels, image.Level + 1);
        int baseLevel = ReadParameter(texture, target, GetTextureParameter.TextureBaseLevel, bound);
        int maxLevel = ReadParameter(texture, target, GetTextureParameter.TextureMaxLevel, bound);
        if (immutable < 0 || immutableLevels < 0 || baseLevel < 0 || maxLevel < 0) return false;
        metadata = new(target, levels.ToImmutable(), immutable != 0, immutableLevels, baseLevel, maxLevel, View: view);
        return true;
    }
    #endregion

    #region Private
    /// <summary>Reads object parameters through the available API while a fallback scope preserves bindings.</summary>
    private static int ReadParameter(int texture, TextureTarget target, GetTextureParameter parameter, bool bound)
    {
        int value = -1;
        if (bound) GL.GetTexParameter(target, parameter, out value);
        else GL.GetTextureParameter(texture, parameter, out value);
        return value;
    }
    /// <summary>Reads one image descriptor with an invalid sentinel and preserves the native error queue.</summary>
    private static int ReadLevel(int texture, TextureTarget target, int level, GetTextureParameter parameter, bool bound)
    {
        int value = -1;
        if (bound) GL.GetTexLevelParameter(target, level, parameter, out value);
        else GL.GetTextureLevelParameter(texture, level, parameter, out value);
        return value;
    }
    #endregion
}

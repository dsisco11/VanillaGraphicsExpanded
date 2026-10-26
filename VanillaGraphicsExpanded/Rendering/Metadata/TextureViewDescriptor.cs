using System;
using System.Collections.Immutable;
using System.Linq;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Derives immutable view geometry from an available source and a validated selection.</summary>
internal static class TextureViewDescriptor
{
    #region Internal API
    /// <summary>Rejects incompatible selections and produces descriptors relative to the new view.</summary>
    internal static GpuTextureMetadata Create(in GpuTextureViewInitialization request, GpuTextureMetadata source)
    {
        if (!source.ImmutableStorage || source.Levels.IsDefaultOrEmpty || !TargetsCompatible(source.Target, request.Target))
            throw new ArgumentException("Texture view requires compatible immutable source storage.");
        int exposed = source.View?.LevelCount ?? source.ImmutableLevels;
        if (request.BaseLevel > exposed - request.LevelCount)
            throw new ArgumentOutOfRangeException(nameof(request), "View mip selection exceeds its source.");
        bool cube = request.Target == TextureTarget.TextureCubeMap;
        bool array = request.Target is TextureTarget.Texture1DArray or TextureTarget.Texture2DArray
            or TextureTarget.TextureCubeMapArray or TextureTarget.Texture2DMultisampleArray;
        if (cube ? request.LayerCount != 6 : !array && request.LayerCount != 1
            || request.Target == TextureTarget.TextureCubeMapArray && request.LayerCount % 6 != 0)
            throw new ArgumentException("View layer count is incompatible with its target.");
        var levels = ImmutableArray.CreateBuilder<GpuTextureLevelMetadata>(request.LevelCount * (cube ? 6 : 1));
        for (int level = 0; level < request.LevelCount; level++)
        {
            int sourceLevel = request.BaseLevel + level;
            var image = source.Levels.FirstOrDefault(value => value.Level == sourceLevel && value.Allocated);
            int layers = source.Target == TextureTarget.TextureCubeMap ? 6
                : source.Target == TextureTarget.Texture1DArray ? image.Height
                : source.Target is TextureTarget.Texture2DArray or TextureTarget.TextureCubeMapArray
                    or TextureTarget.Texture2DMultisampleArray ? image.Depth : 1;
            if (!image.Allocated || request.BaseLayer > layers - request.LayerCount
                || !TextureViewFormatCompatibility.AreCompatible(image.Format, request.Format))
                throw new ArgumentException("View format or layer selection is incompatible with its source.");
            if ((cube || request.Target == TextureTarget.TextureCubeMapArray) && image.Width != image.Height)
                throw new ArgumentException("Cube view images must be square.");
            int height = request.Target == TextureTarget.Texture1D ? 1
                : request.Target == TextureTarget.Texture1DArray ? request.LayerCount : image.Height;
            int depth = request.Target == TextureTarget.Texture3D ? image.Depth
                : array && request.Target != TextureTarget.Texture1DArray ? request.LayerCount : 1;
            var caps = RHI.RHICommandContext.Current.OpenGL.NativeCapabilities;
            int limit = cube || request.Target == TextureTarget.TextureCubeMapArray ? caps.MaxCubeMapTextureSize
                : request.Target == TextureTarget.TextureRectangle ? caps.MaxRectangleTextureSize
                : request.Target == TextureTarget.Texture3D ? caps.Max3DTextureSize : caps.MaxTextureSize;
            if (limit > 0 && (image.Width > limit || request.Target != TextureTarget.Texture1DArray && height > limit
                || request.Target == TextureTarget.Texture3D && depth > limit)
                || array && caps.MaxArrayTextureLayers > 0 && request.LayerCount > caps.MaxArrayTextureLayers)
                throw new ArgumentOutOfRangeException(nameof(request), "View geometry exceeds cached target limits.");
            for (int face = 0; face < (cube ? 6 : 1); face++)
                levels.Add(new(level, cube ? TextureTarget.TextureCubeMapPositiveX + face : 0,
                    image.Width, height, depth, request.Format, image.Samples, true));
        }
        bool multisample = request.Target is TextureTarget.Texture2DMultisample or TextureTarget.Texture2DMultisampleArray;
        return new(request.Target, levels.MoveToImmutable(), true, source.ImmutableLevels, 0, multisample ? 0 : 1000,
            View: new((source.View?.BaseLevel ?? 0) + request.BaseLevel, request.LevelCount,
                (source.View?.BaseLayer ?? 0) + request.BaseLayer, request.LayerCount));
    }
    #endregion

    #region Private
    /// <summary>Applies the target compatibility groups specified for immutable texture views.</summary>
    private static bool TargetsCompatible(TextureTarget source, TextureTarget target) => source switch
    {
        TextureTarget.Texture1D or TextureTarget.Texture1DArray => target is TextureTarget.Texture1D or TextureTarget.Texture1DArray,
        TextureTarget.Texture2D => target is TextureTarget.Texture2D or TextureTarget.Texture2DArray,
        TextureTarget.TextureCubeMap or TextureTarget.Texture2DArray or TextureTarget.TextureCubeMapArray => target is TextureTarget.Texture2D
            or TextureTarget.Texture2DArray or TextureTarget.TextureCubeMap or TextureTarget.TextureCubeMapArray,
        TextureTarget.Texture2DMultisample or TextureTarget.Texture2DMultisampleArray => target is TextureTarget.Texture2DMultisample or TextureTarget.Texture2DMultisampleArray,
        TextureTarget.Texture3D or TextureTarget.TextureRectangle => target == source,
        _ => false
    };
    #endregion
}

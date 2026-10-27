using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Collections.Immutable;
using System.Linq;

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
        bool cube = request.Target == TextureDimension.Cube;
        bool array = request.Target is TextureDimension.OneArray or TextureDimension.TwoArray
            or TextureDimension.CubeArray or TextureDimension.TwoMultisampleArray;
        if (cube ? request.LayerCount != 6 : !array && request.LayerCount != 1
            || request.Target == TextureDimension.CubeArray && request.LayerCount % 6 != 0)
            throw new ArgumentException("View layer count is incompatible with its target.");
        var levels = ImmutableArray.CreateBuilder<GpuTextureLevelMetadata>(request.LevelCount * (cube ? 6 : 1));
        for (int level = 0; level < request.LevelCount; level++)
        {
            int sourceLevel = request.BaseLevel + level;
            var image = source.Levels.FirstOrDefault(value => value.Level == sourceLevel && value.Allocated);
            int layers = source.Target == TextureDimension.Cube ? 6
                : source.Target == TextureDimension.OneArray ? image.Height
                : source.Target is TextureDimension.TwoArray or TextureDimension.CubeArray
                    or TextureDimension.TwoMultisampleArray ? image.Depth : 1;
            if (!image.Allocated || request.BaseLayer > layers - request.LayerCount
                || !TextureViewFormatCompatibility.AreCompatible(image.Format, request.Format))
                throw new ArgumentException("View format or layer selection is incompatible with its source.");
            if ((cube || request.Target == TextureDimension.CubeArray) && image.Width != image.Height)
                throw new ArgumentException("Cube view images must be square.");
            int height = request.Target == TextureDimension.One ? 1
                : request.Target == TextureDimension.OneArray ? request.LayerCount : image.Height;
            int depth = request.Target == TextureDimension.Three ? image.Depth
                : array && request.Target != TextureDimension.OneArray ? request.LayerCount : 1;
            var caps = RHI.RHICommandContext.Current.Capabilities;
            int limit = cube || request.Target == TextureDimension.CubeArray ? caps.MaxCubeMapTextureSize
                : request.Target == TextureDimension.Rectangle ? caps.MaxRectangleTextureSize
                : request.Target == TextureDimension.Three ? caps.Max3DTextureSize : caps.MaxTextureSize;
            if (limit > 0 && (image.Width > limit || request.Target != TextureDimension.OneArray && height > limit
                || request.Target == TextureDimension.Three && depth > limit)
                || array && caps.MaxArrayTextureLayers > 0 && request.LayerCount > caps.MaxArrayTextureLayers)
                throw new ArgumentOutOfRangeException(nameof(request), "View geometry exceeds cached target limits.");
            for (int face = 0; face < (cube ? 6 : 1); face++)
                levels.Add(new(level, cube ? CubeFace.PositiveX + face : 0,
                    image.Width, height, depth, request.Format, image.Samples, true));
        }
        bool multisample = request.Target is TextureDimension.TwoMultisample or TextureDimension.TwoMultisampleArray;
        return new(request.Target, levels.MoveToImmutable(), true, source.ImmutableLevels, 0, multisample ? 0 : 1000,
            View: new((source.View?.BaseLevel ?? 0) + request.BaseLevel, request.LevelCount,
                (source.View?.BaseLayer ?? 0) + request.BaseLayer, request.LayerCount));
    }
    #endregion

    #region Private
    /// <summary>Applies the target compatibility groups specified for immutable texture views.</summary>
    private static bool TargetsCompatible(TextureDimension source, TextureDimension target) => source switch
    {
        TextureDimension.One or TextureDimension.OneArray => target is TextureDimension.One or TextureDimension.OneArray,
        TextureDimension.Two => target is TextureDimension.Two or TextureDimension.TwoArray,
        TextureDimension.Cube or TextureDimension.TwoArray or TextureDimension.CubeArray => target is TextureDimension.Two
            or TextureDimension.TwoArray or TextureDimension.Cube or TextureDimension.CubeArray,
        TextureDimension.TwoMultisample or TextureDimension.TwoMultisampleArray => target is TextureDimension.TwoMultisample or TextureDimension.TwoMultisampleArray,
        TextureDimension.Three or TextureDimension.Rectangle => target == source,
        _ => false
    };
    #endregion
}

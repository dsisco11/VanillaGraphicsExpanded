using System;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes complete image allocations and constructs their descriptors from command inputs.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Allocates an unpublished texture whose native name remains private until manager handoff.</summary>
    internal GpuTextureMetadata? CreateTexture(in GpuTextureInitialization request, out nint id)
    {
        id = GenTexture();
        if (id == 0) return null;
        using var binding = BindTextureScope(request.Target, 0, (int)id);
        if (request.Target is TextureTarget.Texture2DMultisample or TextureTarget.Texture2DMultisampleArray)
            return AllocateMultisampleTexture(request);
        var result = AllocateTextureImages(request);
        GL.TexParameter(request.Target, TextureParameterName.TextureBaseLevel, request.BaseLevel);
        GL.TexParameter(request.Target, TextureParameterName.TextureMaxLevel, request.MaxLevel);
        return result;
    }
    #endregion

    #region Private
    /// <summary>Defines the requested complete mip chain, retaining array layers while shrinking spatial axes.</summary>
    private static GpuTextureMetadata AllocateTextureImages(in GpuTextureInitialization request)
    {
        bool immutable = request.AllocationMode == GpuTextureAllocationMode.Immutable;
        bool three = request.Target is TextureTarget.Texture3D or TextureTarget.Texture2DArray or TextureTarget.TextureCubeMapArray;
        if (immutable)
        {
            if (three) GL.TexStorage3D((TextureTarget3d)request.Target, request.Levels, (SizedInternalFormat)request.InternalFormat, request.Width, request.Height, request.Depth);
            else if (request.Target == TextureTarget.Texture1D) GL.TexStorage1D((TextureTarget1d)request.Target, request.Levels, (SizedInternalFormat)request.InternalFormat, request.Width);
            else GL.TexStorage2D((TextureTarget2d)request.Target, request.Levels, (SizedInternalFormat)request.InternalFormat, request.Width, request.Height);
        }
        int faces = request.Target == TextureTarget.TextureCubeMap ? 6 : 1;
        var images = ImmutableArray.CreateBuilder<GpuTextureLevelMetadata>(request.Levels * faces);
        for (int level = 0; level < request.Levels; level++)
        {
            int width = Math.Max(1, request.Width >> level);
            int height = request.Target == TextureTarget.Texture1DArray ? request.Height : Math.Max(1, request.Height >> level);
            int depth = request.Target == TextureTarget.Texture3D ? Math.Max(1, request.Depth >> level) : request.Depth;
            for (int face = 0; face < faces; face++)
            {
                var target = faces == 6 ? TextureTarget.TextureCubeMapPositiveX + face : request.Target;
                if (!immutable)
                {
                    var pixels = TextureFormatHelper.GetPixelFormat(request.InternalFormat);
                    var type = TextureFormatHelper.GetPixelType(request.InternalFormat);
                    if (three) GL.TexImage3D(target, level, request.InternalFormat, width, height, depth, 0, pixels, type, IntPtr.Zero);
                    else if (target == TextureTarget.Texture1D) GL.TexImage1D(target, level, request.InternalFormat, width, 0, pixels, type, IntPtr.Zero);
                    else GL.TexImage2D(target, level, request.InternalFormat, width, height, 0, pixels, type, IntPtr.Zero);
                }
                var format = immutable ? request.InternalFormat : GpuTextureFormatReader.ResolveBoundFormat(target, level, request.InternalFormat);
                images.Add(new(level, faces == 6 ? target : 0, width, height, depth, format, 0, true));
            }
        }
        GpuTextureViewMetadata? view = immutable && GpuSupport.Graphics.SupportsTextureView
            ? new(0, request.Levels, 0, request.Target == TextureTarget.TextureCubeMap ? 6
                : request.Target == TextureTarget.Texture1DArray ? request.Height
                : request.Target is TextureTarget.Texture2DArray or TextureTarget.TextureCubeMapArray ? request.Depth : 1) : null;
        return new(request.Target, images.MoveToImmutable(), immutable, immutable ? request.Levels : 0, request.BaseLevel, request.MaxLevel, View: view);
    }
    #endregion
}

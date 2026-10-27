using System;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;
using Native = OpenTK.Graphics.OpenGL4;
using GL = OpenTK.Graphics.OpenGL4.GL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Realizes complete image allocations without querying controlled descriptors.</summary>
internal sealed partial class OpenGLBackend
{
    #region Internal API
    /// <summary>Creates a realized texture and establishes all images before manager identity handoff.</summary>
    public GpuTextureMetadata? InitializeTexture(in GpuTextureInitialization request, out nint id, RHITextureInitialData[]? initialData = null)
    {
        RequireContext();
        int name = 0;
        try { GL.CreateTextures((Native.TextureTarget)request.Target, 1, out name); }
        finally { id = name; }
        RequireContext();
        if (name == 0) return null;
        var guard = cache.EnterResourceOperation(new(GpuResourceKind.Texture, name));
        try
        {
            var result = DefineTextureImages(name, request, creationGuard: guard);
            if (initialData is not null)
            {
                using var unpack = cache.BindBufferScope(BufferTarget.PixelUnpackBuffer, 0);
                foreach (var item in initialData)
                {
                    using var layout = OpenGLPixelLayouts.UnpackScope(cache, PixelLayoutForTarget(ValidatePixelLayout(item.Region, item.Encoding, item.Bytes.Length, item.RowPitch, item.SlicePitch), request.Target));
                    unsafe { using var pinned = item.Bytes.Pin(); UploadImagePixels(name, request.Target, item.Region, item.Encoding, (nint)pinned.Pointer); }
                    CheckTextureStep(null, guard);
                }
            }
            return result;
        }
        finally
        {
            if (guard.Deleted) id = 0;
            cache.ExitResourceOperation(guard);
        }
    }

    /// <summary>Defines storage while an explicit operation retains the original resource association.</summary>
    internal GpuTextureMetadata DefineTextureImages(int name, in GpuTextureInitialization request,
        GpuResourceManager.MetadataOperation<GpuTextureMetadata>? operation = null, OpenGlStateCache.ResourceOperationGuard? creationGuard = null)
    {
        RequireContext();
        bool immutable = request.AllocationMode == GpuTextureAllocationMode.Immutable;
        bool multisample = request.Target is TextureTarget.Texture2DMultisample or TextureTarget.Texture2DMultisampleArray;
        bool three = request.Target is TextureTarget.Texture3D or TextureTarget.Texture2DArray or TextureTarget.TextureCubeMapArray;
        bool one = request.Target == TextureTarget.Texture1D;
        // Mutable image definition has no core named equivalent. The backend alone owns this
        // temporary selection; immutable allocation never mutates a texture binding.
        using var binding = !immutable ? cache.BindTextureScope(request.Target, 0, name) : (OpenGlStateCache.TextureScope?)null;
        if (immutable)
        {
            if (multisample)
            {
                if (request.Target == TextureTarget.Texture2DMultisampleArray)
                    GL.TextureStorage3DMultisample(name, request.Samples, (Native.SizedInternalFormat)request.InternalFormat,
                        request.Width, request.Height, request.Depth, request.FixedSampleLocations);
                else GL.TextureStorage2DMultisample(name, request.Samples, (Native.SizedInternalFormat)request.InternalFormat,
                    request.Width, request.Height, request.FixedSampleLocations);
            }
            else if (three) GL.TextureStorage3D(name, request.Levels, (Native.SizedInternalFormat)request.InternalFormat, request.Width, request.Height, request.Depth);
            else if (one) GL.TextureStorage1D(name, request.Levels, (Native.SizedInternalFormat)request.InternalFormat, request.Width);
            else GL.TextureStorage2D(name, request.Levels, (Native.SizedInternalFormat)request.InternalFormat, request.Width, request.Height);
            CheckTextureStep(operation, creationGuard);
        }
        int faces = request.Target == TextureTarget.TextureCubeMap ? 6 : 1;
        var images = ImmutableArray.CreateBuilder<GpuTextureLevelMetadata>(request.Levels * faces);
        // Null client data must not become an offset into a caller's pixel-unpack buffer.
        using var unpack = !immutable ? cache.BindBufferScope(BufferTarget.PixelUnpackBuffer, 0) : (OpenGlStateCache.BufferScope?)null;
        for (int level = 0; level < request.Levels; level++)
        {
            int width = Math.Max(1, request.Width >> level);
            int height = request.Target == TextureTarget.Texture1DArray ? request.Height : Math.Max(1, request.Height >> level);
            int depth = request.Target == TextureTarget.Texture3D ? Math.Max(1, request.Depth >> level) : request.Depth;
            for (int face = 0; face < faces; face++)
            {
                TextureTarget target = faces == 6 ? TextureTarget.TextureCubeMapPositiveX + face : request.Target;
                if (!immutable)
                {
                    if (multisample)
                    {
                        if (request.Target == TextureTarget.Texture2DMultisampleArray)
                            GL.TexImage3DMultisample((Native.TextureTargetMultisample)target, request.Samples,
                                (Native.PixelInternalFormat)request.InternalFormat, width, height, depth, request.FixedSampleLocations);
                        else GL.TexImage2DMultisample((Native.TextureTargetMultisample)target, request.Samples,
                            (Native.PixelInternalFormat)request.InternalFormat, width, height, request.FixedSampleLocations);
                    }
                    else
                    {
                        var pixels = (Native.PixelFormat)TextureFormatHelper.GetPixelFormat(request.InternalFormat);
                        var type = (Native.PixelType)TextureFormatHelper.GetPixelType(request.InternalFormat);
                        if (three) GL.TexImage3D((Native.TextureTarget)target, level, (Native.PixelInternalFormat)request.InternalFormat, width, height, depth, 0, pixels, type, IntPtr.Zero);
                        else if (one) GL.TexImage1D((Native.TextureTarget)target, level, (Native.PixelInternalFormat)request.InternalFormat, width, 0, pixels, type, IntPtr.Zero);
                        else GL.TexImage2D((Native.TextureTarget)target, level, (Native.PixelInternalFormat)request.InternalFormat, width, height, 0, pixels, type, IntPtr.Zero);
                    }
                    CheckTextureStep(operation, creationGuard);
                }
                var format = request.InternalFormat;
                if (!GpuTextureFormatReader.IsExplicitFormat(format))
                {
                    GL.GetTexLevelParameter((Native.TextureTarget)target, level, Native.GetTextureParameter.TextureInternalFormat, out int resolved);
                    CheckTextureStep(operation, creationGuard);
                    if (resolved <= 0) throw new InvalidOperationException("Unsized texture format resolution failed.");
                    format = (PixelInternalFormat)resolved;
                }
                int samples = multisample ? request.Samples : 0;
                if (multisample && GpuSupport.GetInternalFormatCapabilities((ImageTarget)request.Target, (SizedInternalFormat)request.InternalFormat).SampleCounts.IsDefaultOrEmpty)
                {
                    GL.GetTextureLevelParameter(name, 0, Native.GetTextureParameter.TextureSamples, out samples);
                    CheckTextureStep(operation, creationGuard);
                    if (samples <= 0) throw new InvalidOperationException("Allocated texture samples are unavailable.");
                }
                images.Add(new(level, faces == 6 ? target : 0, width, height, depth, format, samples, true));
            }
        }
        if (!multisample)
        {
            GL.TextureParameter(name, Native.TextureParameterName.TextureBaseLevel, request.BaseLevel);
            CheckTextureStep(operation, creationGuard);
            GL.TextureParameter(name, Native.TextureParameterName.TextureMaxLevel, request.MaxLevel);
            CheckTextureStep(operation, creationGuard);
        }
        GpuTextureViewMetadata? view = immutable ? new(0, request.Levels, 0,
            request.Target == TextureTarget.TextureCubeMap ? 6 : request.Target == TextureTarget.Texture1DArray ? request.Height
            : request.Target is TextureTarget.Texture2DArray or TextureTarget.TextureCubeMapArray or TextureTarget.Texture2DMultisampleArray ? request.Depth : 1) : null;
        return new(request.Target, images.MoveToImmutable(), immutable, immutable ? request.Levels : 0,
            request.BaseLevel, request.MaxLevel, View: view);
    }
    #endregion

    #region Private
    /// <summary>Stops a compound operation if callbacks retired the original allocation or context.</summary>
    private void CheckTextureStep(GpuResourceManager.MetadataOperation<GpuTextureMetadata>? operation, OpenGlStateCache.ResourceOperationGuard? creationGuard = null)
    {
        RequireContext();
        operation?.RequireLive();
        if (creationGuard?.Cancelled == true) throw new InvalidOperationException("The texture allocation retired during initialization.");
    }
    #endregion
}

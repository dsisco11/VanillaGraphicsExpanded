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
        try { OpenGLDriverCalls.CreateTextures((Native.TextureTarget)OpenGLImageValues.Target(request.Target), 1, out name); }
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
    internal GpuTextureMetadata DefineTextureImages(int name, in GpuTextureInitialization initialization,
        RHIResourceManager.MetadataOperation<GpuTextureMetadata>? operation = null, OpenGlStateCache.ResourceOperationGuard? creationGuard = null)
    {
        RequireContext();
        var request = ResolveTextureSamples(initialization);
        bool immutable = request.AllocationMode == GpuTextureAllocationMode.Immutable;
        bool multisample = request.Target is TextureDimension.TwoMultisample or TextureDimension.TwoMultisampleArray;
        bool three = request.Target is TextureDimension.Three or TextureDimension.TwoArray or TextureDimension.CubeArray;
        bool one = request.Target == TextureDimension.One;
        // Mutable image definition has no core named equivalent. The backend alone owns this
        // temporary selection; immutable allocation never mutates a texture binding.
        using var binding = !immutable ? cache.BindTextureScope(OpenGLImageValues.Target(request.Target), 0, name) : (OpenGlStateCache.TextureScope?)null;
        if (immutable)
        {
            if (multisample)
            {
                if (request.Target == TextureDimension.TwoMultisampleArray)
                    OpenGLDriverCalls.TextureStorage3DMultisample(name, request.Samples, (Native.SizedInternalFormat)OpenGLImageValues.Format(request.InternalFormat),
                        request.Width, request.Height, request.Depth, request.FixedSampleLocations);
                else OpenGLDriverCalls.TextureStorage2DMultisample(name, request.Samples, (Native.SizedInternalFormat)OpenGLImageValues.Format(request.InternalFormat),
                    request.Width, request.Height, request.FixedSampleLocations);
            }
            else if (three) OpenGLDriverCalls.TextureStorage3D(name, request.Levels, (Native.SizedInternalFormat)OpenGLImageValues.Format(request.InternalFormat), request.Width, request.Height, request.Depth);
            else if (one) OpenGLDriverCalls.TextureStorage1D(name, request.Levels, (Native.SizedInternalFormat)OpenGLImageValues.Format(request.InternalFormat), request.Width);
            else OpenGLDriverCalls.TextureStorage2D(name, request.Levels, (Native.SizedInternalFormat)OpenGLImageValues.Format(request.InternalFormat), request.Width, request.Height);
            CheckTextureStep(operation, creationGuard);
        }
        int faces = request.Target == TextureDimension.Cube ? 6 : 1;
        var images = ImmutableArray.CreateBuilder<GpuTextureLevelMetadata>(request.Levels * faces);
        // Null client data must not become an offset into a caller's pixel-unpack buffer.
        using var unpack = !immutable ? cache.BindBufferScope(BufferTarget.PixelUnpackBuffer, 0) : (OpenGlStateCache.BufferScope?)null;
        for (int level = 0; level < request.Levels; level++)
        {
            int width = Math.Max(1, request.Width >> level);
            int height = request.Target == TextureDimension.OneArray ? request.Height : Math.Max(1, request.Height >> level);
            int depth = request.Target == TextureDimension.Three ? Math.Max(1, request.Depth >> level) : request.Depth;
            for (int face = 0; face < faces; face++)
            {
                TextureTarget target = faces == 6 ? TextureTarget.TextureCubeMapPositiveX + face : OpenGLImageValues.Target(request.Target);
                if (!immutable)
                {
                    if (multisample)
                    {
                        if (request.Target == TextureDimension.TwoMultisampleArray)
                            OpenGLDriverCalls.TexImage3DMultisample((Native.TextureTargetMultisample)target, request.Samples,
                                (Native.PixelInternalFormat)OpenGLImageValues.Format(request.InternalFormat), width, height, depth, request.FixedSampleLocations);
                        else OpenGLDriverCalls.TexImage2DMultisample((Native.TextureTargetMultisample)target, request.Samples,
                            (Native.PixelInternalFormat)OpenGLImageValues.Format(request.InternalFormat), width, height, request.FixedSampleLocations);
                    }
                    else
                    {
                        var pixels = OpenGLPixelValues.Channels(TextureFormatHelper.GetPixelFormat(request.InternalFormat));
                        var type = OpenGLPixelValues.Type(TextureFormatHelper.GetPixelType(request.InternalFormat));
                        if (three) OpenGLDriverCalls.TexImage3D((Native.TextureTarget)target, level, (Native.PixelInternalFormat)OpenGLImageValues.Format(request.InternalFormat), width, height, depth, 0, pixels, type, IntPtr.Zero);
                        else if (one) OpenGLDriverCalls.TexImage1D((Native.TextureTarget)target, level, (Native.PixelInternalFormat)OpenGLImageValues.Format(request.InternalFormat), width, 0, pixels, type, IntPtr.Zero);
                        else OpenGLDriverCalls.TexImage2D((Native.TextureTarget)target, level, (Native.PixelInternalFormat)OpenGLImageValues.Format(request.InternalFormat), width, height, 0, pixels, type, IntPtr.Zero);
                    }
                    CheckTextureStep(operation, creationGuard);
                }
                var format = request.InternalFormat;
                if (!GpuTextureFormatReader.IsExplicitFormat(format))
                {
                    GL.GetTexLevelParameter((Native.TextureTarget)target, level, Native.GetTextureParameter.TextureInternalFormat, out int resolved);
                    CheckTextureStep(operation, creationGuard);
                    if (resolved <= 0) throw new InvalidOperationException("Unsized texture format resolution failed.");
                    format = OpenGLImageValues.Format((PixelInternalFormat)resolved);
                }
                int samples = multisample ? request.Samples : 0;
                if (multisample && GpuSupport.GetInternalFormatCapabilities(request.Target, request.InternalFormat).SampleCounts.IsDefaultOrEmpty)
                {
                    GL.GetTextureLevelParameter(name, 0, Native.GetTextureParameter.TextureSamples, out samples);
                    CheckTextureStep(operation, creationGuard);
                    if (samples <= 0) throw new InvalidOperationException("Allocated texture samples are unavailable.");
                }
                images.Add(new(level, faces == 6 ? OpenGLImageValues.Face(target) : CubeFace.None, width, height, depth, format, samples, true));
            }
        }
        if (!multisample)
        {
            OpenGLDriverCalls.TextureParameter(name, Native.TextureParameterName.TextureBaseLevel, request.BaseLevel);
            CheckTextureStep(operation, creationGuard);
            OpenGLDriverCalls.TextureParameter(name, Native.TextureParameterName.TextureMaxLevel, request.MaxLevel);
            CheckTextureStep(operation, creationGuard);
        }
        GpuTextureViewMetadata? view = immutable ? new(0, request.Levels, 0,
            request.Target == TextureDimension.Cube ? 6 : request.Target == TextureDimension.OneArray ? request.Height
            : request.Target is TextureDimension.TwoArray or TextureDimension.CubeArray or TextureDimension.TwoMultisampleArray ? request.Depth : 1) : null;
        return new(request.Target, images.MoveToImmutable(), immutable, immutable ? request.Levels : 0,
            request.BaseLevel, request.MaxLevel, View: view);
    }
    #endregion

    #region Private
    /// <summary>Stops a compound operation if callbacks retired the original allocation or context.</summary>
    private void CheckTextureStep(RHIResourceManager.MetadataOperation<GpuTextureMetadata>? operation, OpenGlStateCache.ResourceOperationGuard? creationGuard = null)
    {
        RequireContext();
        operation?.RequireLive();
        if (creationGuard?.Cancelled == true) throw new InvalidOperationException("The texture allocation retired during initialization.");
    }
    #endregion
}

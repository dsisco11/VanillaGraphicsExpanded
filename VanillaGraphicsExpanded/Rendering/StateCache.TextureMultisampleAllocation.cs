using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Allocates multisample images from resolved target-specific initialization inputs.</summary>
internal sealed partial class StateCache
{
    #region Private
    /// <summary>Defines one multisample level, retaining explicit fixed-location policy and normalized samples.</summary>
    private static GpuTextureMetadata AllocateMultisampleTexture(in GpuTextureInitialization request)
    {
        bool immutable = request.AllocationMode == GpuTextureAllocationMode.Immutable;
        bool array = request.Target == TextureTarget.Texture2DMultisampleArray;
        if (immutable)
        {
            if (array) GL.TexStorage3DMultisample((TextureTargetMultisample3d)request.Target, request.Samples,
                (SizedInternalFormat)request.InternalFormat, request.Width, request.Height, request.Depth, request.FixedSampleLocations);
            else GL.TexStorage2DMultisample((TextureTargetMultisample2d)request.Target, request.Samples,
                (SizedInternalFormat)request.InternalFormat, request.Width, request.Height, request.FixedSampleLocations);
        }
        else
        {
            if (array) GL.TexImage3DMultisample((TextureTargetMultisample)request.Target, request.Samples,
                request.InternalFormat, request.Width, request.Height, request.Depth, request.FixedSampleLocations);
            else GL.TexImage2DMultisample((TextureTargetMultisample)request.Target, request.Samples,
                request.InternalFormat, request.Width, request.Height, request.FixedSampleLocations);
        }
        int samples = GpuSupport.Graphics.SupportsInternalFormatQuery2 ? request.Samples
            : GpuTextureImageReader.ReadBoundSamples(request.Target);
        var image = new GpuTextureLevelMetadata(0, 0, request.Width, request.Height, request.Depth,
            request.InternalFormat, samples, true);
        GpuTextureViewMetadata? view = immutable && GpuSupport.Graphics.SupportsTextureView
            ? new(0, 1, 0, array ? request.Depth : 1) : null;
        return new(request.Target, ImmutableArray.Create(image), immutable, immutable ? 1 : 0, 0, 0, View: view);
    }
    #endregion
}

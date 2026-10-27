using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Creates existing image owners through the single manager-owned handoff.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public RHICreationResult<T> CreateTexture<T>(in RHITextureDescription description, ReadOnlySpan<RHITextureInitialData> initialData) where T : GpuTexture
    {
        var request = description;
        RequireContext();
        ValidateTextureOwner<T>(request);
        var initialization = LowerTexture(request);
        var resolved = initialization.Resolve();
        ValidateTextureInitialData(resolved, initialData);
        GpuTexture owner = typeof(T) == typeof(DynamicTexture2D) ? new DynamicTexture2D()
            : typeof(T) == typeof(DynamicTexture3D) ? new DynamicTexture3D()
            : typeof(T) == typeof(DynamicTextureCube) ? new DynamicTextureCube()
            : typeof(T) == typeof(Texture3D) ? new Texture3D()
            : typeof(T) == typeof(Texture2D) || typeof(T) == typeof(GpuTexture) ? new Texture2D()
            : throw new NotSupportedException("This factory requires an existing image owner type.");
        owner.SetAllocationPolicy(request.Has(11) ? request.Filter : TextureFilterMode.Nearest, request.Label);
        try
        {
            if (!GpuResourceManager.CreateTexture(resolved, owner, out _, initialData.ToArray()).HasValue)
            {
                owner.Dispose();
                return new(null, "Image initialization did not complete.");
            }
            ApplyTextureFilterPolicy(owner, request);
            owner.SetDebugName(request.Label);
            return new((T)owner, null);
        }
        catch { try { owner.Dispose(); } catch { } throw; }
    }




    #endregion

    #region Private
    /// <summary>Rejects requests incompatible with the selected wrapper's owning policy before allocation.</summary>
    private static void ValidateTextureOwner<T>(in RHITextureDescription request) where T : GpuTexture
    {
        if (typeof(T) == typeof(DynamicTextureCube) && request.Dimension != TextureDimension.Cube
            || typeof(T) == typeof(DynamicTexture2D) && request.Dimension != TextureDimension.Two
            || (typeof(T) == typeof(Texture3D) || typeof(T) == typeof(DynamicTexture3D)) && request.Dimension is not (TextureDimension.Three or TextureDimension.TwoArray))
            throw new ArgumentException("Texture shape does not match the selected owning abstraction.");
        if (request.Has(11) && !Enum.IsDefined(request.Filter)) throw new ArgumentOutOfRangeException(nameof(request));
        bool multi = request.Dimension is TextureDimension.TwoMultisample or TextureDimension.TwoMultisampleArray;
        if (multi && request.Has(11)) throw new ArgumentException("Multisample textures have no sampling-filter request.");
        var format = request.Format;
        var pixels = TextureFormatHelper.GetPixelFormat(format);
        if (request.Filter == TextureFilterMode.Linear && pixels is PixelChannelLayout.RedInteger or PixelChannelLayout.RgInteger or PixelChannelLayout.RgbInteger or PixelChannelLayout.RgbaInteger)
            throw new ArgumentException("Integer textures require nearest filtering.");
        if ((typeof(T) == typeof(DynamicTexture2D) || typeof(T) == typeof(DynamicTextureCube)) && (TextureFormatHelper.IsDepthFormat(format) || TextureFormatHelper.IsStencilFormat(format)))
            throw new ArgumentException("Use a depth/stencil owner for this representation.");
    }

    /// <summary>Preserves required fields and omission defaults when lowering semantic allocation intent.</summary>
    private GpuTextureInitialization LowerTexture(in RHITextureDescription description)
    {
        RequireContext();
        if (description.Format is TextureFormat.CompressedRgbS3tcDxt1Ext or TextureFormat.CompressedRgbaS3tcDxt1Ext
            or TextureFormat.CompressedSrgbS3tcDxt1Ext or TextureFormat.CompressedSrgbAlphaS3tcDxt1Ext)
        {
            if (!Capabilities.Dxt1Textures) throw new NotSupportedException("DXT1 texture compression is unavailable.");
        }
        else if (description.Format is TextureFormat.CompressedRgbaS3tcDxt3Ext or TextureFormat.CompressedRgbaS3tcDxt5Ext
            or TextureFormat.CompressedSrgbAlphaS3tcDxt3Ext or TextureFormat.CompressedSrgbAlphaS3tcDxt5Ext)
        {
            if (!Capabilities.S3tcTextures) throw new NotSupportedException("S3TC texture compression is unavailable.");
        }
        if (!description.Has(0) || !description.Has(1) || !description.Has(2) || !description.Has(5) || !description.Has(6))
            throw new ArgumentException("Texture dimension, format, width, mip count and storage mode are required.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(description.Width);
        if (description.Has(3)) ArgumentOutOfRangeException.ThrowIfNegativeOrZero(description.Height);
        if (description.Has(4)) ArgumentOutOfRangeException.ThrowIfNegativeOrZero(description.Depth);
        if (description.Storage is not (ResourceStorageMode.Mutable or ResourceStorageMode.Immutable)) throw new ArgumentException("Image storage must be mutable or immutable.");
        var result = new GpuTextureInitialization { Target = description.Dimension, InternalFormat = description.Format,
            Width = description.Width, Levels = description.MipLevels, AllocationMode = description.Storage == ResourceStorageMode.Immutable ? GpuTextureAllocationMode.Immutable : GpuTextureAllocationMode.Mutable };
        if (description.Has(3)) result.Height = description.Height;
        if (description.Has(4)) result.Depth = description.Depth;
        if (description.Has(7)) result.Samples = description.Samples;
        if (description.Has(8)) result.FixedSampleLocations = description.FixedSampleLocations;
        if (description.Has(9)) result.BaseLevel = description.BaseLevel;
        if (description.Has(10)) result.MaxLevel = description.MaxLevel;
        return result;
    }
    #endregion
}

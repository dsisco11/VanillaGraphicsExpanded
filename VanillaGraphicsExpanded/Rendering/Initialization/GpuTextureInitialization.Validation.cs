using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Validates allocation intent and resolves target-specific omitted axes and mip defaults.</summary>
internal partial struct GpuTextureInitialization
{
    #region Internal API
    /// <summary>Returns a complete allocation request without reading the resource being created.</summary>
    internal readonly GpuTextureInitialization Resolve()
    {
        const Inputs required = Inputs.Target | Inputs.AllocationMode | Inputs.InternalFormat | Inputs.Width | Inputs.Levels;
        if (!IsAssigned(required) || !Enum.IsDefined(allocationMode) || width <= 0 || levels <= 0)
            throw new ArgumentException("Texture allocation requires target, mode, format, width and positive level count.");
        if (!Enum.IsDefined(internalFormat) && internalFormat is not ((PixelInternalFormat)All.Red or (PixelInternalFormat)All.Rg or (PixelInternalFormat)All.StencilIndex8))
            throw new ArgumentOutOfRangeException(nameof(InternalFormat));
        bool one = target == TextureTarget.Texture1D;
        bool multisample = target is TextureTarget.Texture2DMultisample or TextureTarget.Texture2DMultisampleArray;
        bool layered = target is TextureTarget.Texture3D or TextureTarget.Texture2DArray or TextureTarget.TextureCubeMapArray or TextureTarget.Texture2DMultisampleArray;
        if (target is not (TextureTarget.Texture1D or TextureTarget.Texture2D or TextureTarget.Texture3D
            or TextureTarget.Texture1DArray or TextureTarget.Texture2DArray or TextureTarget.TextureCubeMap
            or TextureTarget.TextureCubeMapArray or TextureTarget.TextureRectangle
            or TextureTarget.Texture2DMultisample or TextureTarget.Texture2DMultisampleArray))
            throw new NotSupportedException("This allocation operation requires an image target.");
        if ((!one && (!IsAssigned(Inputs.Height) || height <= 0))
            || (layered && (!IsAssigned(Inputs.Depth) || depth <= 0))
            || (one && IsAssigned(Inputs.Height) && height != 1)
            || (!layered && IsAssigned(Inputs.Depth) && depth != 1)
            || !multisample && (IsAssigned(Inputs.Samples) || IsAssigned(Inputs.FixedSampleLocations)))
            throw new ArgumentException("Texture dimensions or sample inputs do not match the allocation target.");
        if (multisample && (!IsAssigned(Inputs.Samples | Inputs.FixedSampleLocations) || samples <= 0 || levels != 1
            || IsAssigned(Inputs.BaseLevel) || IsAssigned(Inputs.MaxLevel)))
            throw new ArgumentException("Multisample storage requires explicit samples and fixed-location policy, one level and no mip overrides.");
        var result = this;
        if (one) result.Height = 1;
        if (!layered) result.Depth = 1;
        // Resolved defaults are values, not caller assignments; repeated validation must retain omission intent.
        if (!IsAssigned(Inputs.BaseLevel)) result.baseLevel = 0;
        if (!IsAssigned(Inputs.MaxLevel)) result.maxLevel = multisample ? 0 : 1000;
        if (result.BaseLevel < 0 || result.MaxLevel < 0
            || target == TextureTarget.TextureRectangle && (levels != 1 || result.BaseLevel != 0)
            || target is TextureTarget.TextureCubeMap or TextureTarget.TextureCubeMapArray && width != height
            || target == TextureTarget.TextureCubeMapArray && depth % 6 != 0)
            throw new ArgumentException("Invalid mip range or cube geometry.");
        var caps = RHI.RHICommandContext.Current.Capabilities;
        int spatialExtent = Math.Max(width, target == TextureTarget.Texture1DArray ? 1 : result.Height);
        if (target == TextureTarget.Texture3D) spatialExtent = Math.Max(spatialExtent, depth);
        int maximumLevels = 1;
        while ((spatialExtent >>= 1) != 0) maximumLevels++;
        if (levels > maximumLevels) throw new ArgumentOutOfRangeException(nameof(Levels));
        bool sized = GpuTextureFormatReader.IsExplicitFormat(internalFormat);
        if ((allocationMode == GpuTextureAllocationMode.Immutable || multisample) && !sized)
            throw new ArgumentException("Immutable and multisample storage require an explicit sized format.");
        int limit = target == TextureTarget.Texture3D ? caps.Max3DTextureSize
            : target == TextureTarget.TextureRectangle ? caps.MaxRectangleTextureSize
            : target is TextureTarget.TextureCubeMap or TextureTarget.TextureCubeMapArray ? caps.MaxCubeMapTextureSize : caps.MaxTextureSize;
        if (limit > 0 && (width > limit || target != TextureTarget.Texture1DArray && result.Height > limit
            || target == TextureTarget.Texture3D && depth > limit)
            || target is TextureTarget.Texture2DArray or TextureTarget.TextureCubeMapArray or TextureTarget.Texture2DMultisampleArray && caps.MaxArrayTextureLayers > 0 && depth > caps.MaxArrayTextureLayers
            || target == TextureTarget.Texture1DArray && caps.MaxArrayTextureLayers > 0 && height > caps.MaxArrayTextureLayers)
            throw new ArgumentOutOfRangeException(nameof(Width), "Texture dimensions exceed cached limits.");
        if (multisample)
        {
            var support = GpuSupport.GetInternalFormatCapabilities((ImageTarget)target, (SizedInternalFormat)internalFormat);
            if (!support.Supported) throw new ArgumentOutOfRangeException(nameof(Samples));
            if (support.SampleCounts.IsDefaultOrEmpty) return result;
            int selected = int.MaxValue;
            foreach (int count in support.SampleCounts)
                if (count >= samples && count < selected) selected = count;
            if (!support.Supported || selected == int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(Samples));
            result.Samples = selected;
        }
        return result;
    }
    #endregion
}

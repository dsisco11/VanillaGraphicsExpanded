using VanillaGraphicsExpanded.Rendering.RHI;
using System;

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
        if (internalFormat == TextureFormat.Undefined || !Enum.IsDefined(internalFormat) && internalFormat is not (TextureFormat.Red or TextureFormat.Rg or TextureFormat.StencilIndex8))
            throw new ArgumentOutOfRangeException(nameof(InternalFormat));
        bool one = target == TextureDimension.One;
        bool multisample = target is TextureDimension.TwoMultisample or TextureDimension.TwoMultisampleArray;
        bool layered = target is TextureDimension.Three or TextureDimension.TwoArray or TextureDimension.CubeArray or TextureDimension.TwoMultisampleArray;
        if (target is not (TextureDimension.One or TextureDimension.Two or TextureDimension.Three
            or TextureDimension.OneArray or TextureDimension.TwoArray or TextureDimension.Cube
            or TextureDimension.CubeArray or TextureDimension.Rectangle
            or TextureDimension.TwoMultisample or TextureDimension.TwoMultisampleArray))
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
            || target == TextureDimension.Rectangle && (levels != 1 || result.BaseLevel != 0)
            || target is TextureDimension.Cube or TextureDimension.CubeArray && width != height
            || target == TextureDimension.CubeArray && depth % 6 != 0)
            throw new ArgumentException("Invalid mip range or cube geometry.");
        var caps = RHI.RHICommandContext.Current.Capabilities;
        int spatialExtent = Math.Max(width, target == TextureDimension.OneArray ? 1 : result.Height);
        if (target == TextureDimension.Three) spatialExtent = Math.Max(spatialExtent, depth);
        int maximumLevels = 1;
        while ((spatialExtent >>= 1) != 0) maximumLevels++;
        if (levels > maximumLevels) throw new ArgumentOutOfRangeException(nameof(Levels));
        bool sized = GpuTextureFormatReader.IsExplicitFormat(internalFormat);
        if ((allocationMode == GpuTextureAllocationMode.Immutable || multisample) && !sized)
            throw new ArgumentException("Immutable and multisample storage require an explicit sized format.");
        int limit = target == TextureDimension.Three ? caps.Max3DTextureSize
            : target == TextureDimension.Rectangle ? caps.MaxRectangleTextureSize
            : target is TextureDimension.Cube or TextureDimension.CubeArray ? caps.MaxCubeMapTextureSize : caps.MaxTextureSize;
        if (limit > 0 && (width > limit || target != TextureDimension.OneArray && result.Height > limit
            || target == TextureDimension.Three && depth > limit)
            || target is TextureDimension.TwoArray or TextureDimension.CubeArray or TextureDimension.TwoMultisampleArray && caps.MaxArrayTextureLayers > 0 && depth > caps.MaxArrayTextureLayers
            || target == TextureDimension.OneArray && caps.MaxArrayTextureLayers > 0 && height > caps.MaxArrayTextureLayers)
            throw new ArgumentOutOfRangeException(nameof(Width), "Texture dimensions exceed cached limits.");
        return result;
    }
    #endregion
}

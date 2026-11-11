using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Establishes sampled/image compatibility from canonical metadata during initial or relevant-change preparation.</summary>
internal static class ShaderResourcePreparation
{
    #region Internal API
    /// <summary>Checks the current managed allocation only for active samplers.</summary>
    internal static void ValidateSampler(GpuPreparedBindings.Entry binding, GpuTextureRef texture)
    {
        if (!binding.Active) return;
        if (texture.IsEmpty) { Require(binding, false); return; }
        var metadata = texture.RequireMetadata();
        // Buffer textures have a tracked backing association rather than mip images.
        // RequireMetadata already validates that association and its storage lifetime.
        var format = metadata.Target == TextureDimension.Buffer
            ? metadata.Buffer?.Format ?? throw new InvalidOperationException("Texture buffer association is unavailable.")
            : SelectImage(metadata, 0).Format;
        if (GpuPreparedBindings.CompatibleTextureTarget(binding.Type) != (int)metadata.Target ||
            !ShaderResourceFormatCompatibility.MatchesSampler(binding.Type, format))
            throw new InvalidOperationException($"Incompatible texture target for {binding.Contract.Name}.");
    }
    /// <summary>Checks image view bounds before any resource in the input set is published.</summary>
    internal static void ValidateImage(GpuPreparedBindings.Entry binding, RHIImageBinding image)
    {
        if (!binding.Active) return;
        if (image.Texture.IsEmpty) { Require(binding, false); return; }
        var metadata = image.Texture.RequireMetadata();
        var selected = SelectImage(metadata, image.Level);
        var target = metadata.Target;
        int layers = target == TextureDimension.Cube ? 6
            : target == TextureDimension.OneArray ? selected.Height : selected.Depth;
        var viewTarget = image.Layered ? target : target switch
        {
            TextureDimension.OneArray => TextureDimension.One,
            TextureDimension.TwoMultisampleArray => TextureDimension.TwoMultisample,
            TextureDimension.Three or TextureDimension.TwoArray or TextureDimension.Cube
                or TextureDimension.CubeArray => TextureDimension.Two,
            _ => target
        };
        var format = image.Format ?? selected.Format;
        if (image.Layer < 0 || (!image.Layered && image.Layer >= layers) ||
            image.Access is not (ImageAccess.ReadOnly or ImageAccess.WriteOnly or ImageAccess.ReadWrite) ||
            !ImageFormatCompatibility.Matches(selected.Format, format) ||
            !ShaderResourceFormatCompatibility.Matches(binding.Type, format) ||
            GpuPreparedBindings.CompatibleTextureTarget(binding.Type) != (int)viewTarget)
            throw new InvalidOperationException($"Invalid image view for {binding.Contract.Name}.");
    }
    #endregion

    #region Private
    /// <summary>Selects an allocated view-relative mip from the one supplied metadata read.</summary>
    private static GpuTextureLevelMetadata SelectImage(in GpuTextureMetadata metadata, int level)
    {
        foreach (var image in metadata.Levels)
            if (image.Level == level && image.Allocated) return image;
        throw new InvalidOperationException("Texture mip image is not allocated.");
    }
    /// <summary>Distinguishes required absence from a legal optional clear; inactive entries have no obligation.</summary>
    private static void Require(GpuPreparedBindings.Entry binding, bool valid)
    {
        if (binding.Active && binding.Contract.Binding.Required && !valid)
            throw new InvalidOperationException($"Shader requires a valid {binding.Contract.Name} resource.");
    }
    #endregion
}

using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Checks resource compatibility against preparation-owned metadata without executing native commands.</summary>
internal static class ShaderPreparedSubmission
{
    #region Public API
    #region Preparation selection
    /// <summary>Selects the installed executable's entry without resolving shader names or uniform locations.</summary>
    internal static GpuPreparedBindings.Entry Resolve(IShaderSubmissionTarget owner, ulong identity) =>
        (owner.ProgramLayout.BinaryInterface ?? throw new InvalidOperationException("Shader has no prepared executable interface."))
        .PreparedBindings.Resolve(identity);
    #endregion

    #region Validation
    /// <summary>Checks lifetime on each use and compatibility only when the texture or executable facts change.</summary>
    internal static void ValidateSampler(GpuPreparedBindings.Entry binding, GpuTextureRef texture, ref ShaderInputValidation history)
    {
        if (!binding.Active) return;
        bool valid = GpuResourceManager.ValidateTextureReferenceInput(texture.Resource);
        Require(binding, valid);
        if (!valid) return;
        Pipeline.Passes.RenderPass.ValidateSampledTexture(texture);
        var key = ShaderInputValidation.TextureKey(texture);
        if (history.Matches(binding, key)) return;
        ValidateSampler(binding, texture);
        history.Commit(binding, key);
    }

    /// <summary>Normalizes a default view before checking its retained successful compatibility.</summary>
    internal static void ValidateImage(GpuPreparedBindings.Entry binding, GpuTextureRef texture, ref ShaderInputValidation history) =>
        ValidateImage(binding, DefaultImageView(binding, texture), ref history);

    /// <summary>Checks image lifetime every use while revalidating only changed view/allocation facts.</summary>
    internal static void ValidateImage(GpuPreparedBindings.Entry binding, RHIImageBinding image, ref ShaderInputValidation history)
    {
        if (!binding.Active) return;
        bool valid = GpuResourceManager.ValidateTextureReferenceInput(image.Texture.Resource);
        Require(binding, valid);
        if (!valid) return;
        var key = ShaderInputValidation.TextureKey(image.Texture!, image);
        if (history.Matches(binding, key)) return;
        ValidateImage(binding, image);
        history.Commit(binding, key);
    }

    /// <summary>Revalidates range bounds and alignment only after allocation capacity or desired range changes.</summary>
    internal static void ValidateStorageBlock(GpuPreparedBindings.Entry binding, GpuStorageBufferBinding range, ref ShaderInputValidation history)
    {
        if (!binding.Active) return;
        bool valid = range.Buffer?.IsValid == true;
        Require(binding, valid);
        if (!valid) return;
        var key = ShaderInputValidation.StorageKey(range);
        if (history.Matches(binding, key)) return;
        ValidateStorageBlock(binding, range);
        history.Commit(binding, key);
    }
    /// <summary>Validates every active array element before any slot in the input set is published.</summary>
    internal static void ValidateSamplerArray(GpuPreparedBindings.Entry binding, IReadOnlyList<GpuTextureRef>? textures)
    {
        if (!binding.Active) return;
        for (int element = 0; element < binding.ActiveElements.Count; element++)
            ValidateSampler(Element(binding, element), textures is not null && element < textures.Count ? textures[element] : default);
    }
    /// <summary>Checks the current managed allocation only for active samplers.</summary>
    internal static void ValidateSampler(GpuPreparedBindings.Entry binding, GpuTextureRef texture)
    {
        if (!binding.Active) return;
        bool valid = GpuResourceManager.ValidateTextureReferenceInput(texture.Resource);
        Require(binding, valid);
        if (valid) Pipeline.Passes.RenderPass.ValidateSampledTexture(texture);
        if (valid && (GpuPreparedBindings.CompatibleTextureTarget(binding.Type) != (int)texture.Target ||
            !ShaderResourceFormatCompatibility.Matches(binding.Type, texture.RequireImage(0).Format)))
            throw new InvalidOperationException($"Incompatible texture target for {binding.Contract.Name}.");
    }
    /// <summary>Checks a retained default image allocation.</summary>
    internal static void ValidateImage(GpuPreparedBindings.Entry binding, GpuTextureRef texture) => ValidateImage(binding, DefaultImageView(binding, texture));
    /// <summary>Checks image view bounds before any resource in the input set is published.</summary>
    internal static void ValidateImage(GpuPreparedBindings.Entry binding, RHIImageBinding image)
    {
        if (!binding.Active) return;
        bool valid = GpuResourceManager.ValidateTextureReferenceInput(image.Texture.Resource);
        Require(binding, valid);
        if (!valid) return;
        image.Texture.RequireMetadata();
        var selected = image.Texture.RequireImage(image.Level);
        var target = image.Texture.Target;
        int layers = target == TextureTarget.TextureCubeMap ? 6
            : target == TextureTarget.Texture1DArray ? selected.Height : selected.Depth;
        var viewTarget = image.Layered ? target : target switch
        {
            TextureTarget.Texture1DArray => TextureTarget.Texture1D,
            TextureTarget.Texture2DMultisampleArray => TextureTarget.Texture2DMultisample,
            TextureTarget.Texture3D or TextureTarget.Texture2DArray or TextureTarget.TextureCubeMap
                or TextureTarget.TextureCubeMapArray => TextureTarget.Texture2D,
            _ => target
        };
        var format = image.Format is { } supplied ? OpenGLImageValues.Format(supplied) : selected.Format;
        if (image.Layer < 0 || (!image.Layered && image.Layer >= layers) ||
            image.Access is not (ImageAccess.ReadOnly or ImageAccess.WriteOnly or ImageAccess.ReadWrite) ||
            !ImageFormatCompatibility.Matches(image.Texture.RequireImage(image.Level).Format, (SizedInternalFormat)format) ||
            !ShaderResourceFormatCompatibility.Matches(binding.Type, format) ||
            GpuPreparedBindings.CompatibleTextureTarget(binding.Type) != (int)viewTarget)
            throw new InvalidOperationException($"Invalid image view for {binding.Contract.Name}.");
    }
    /// <summary>Checks uniform storage without changing owner-controlled contents.</summary>
    internal static void ValidateUniformBlock(GpuPreparedBindings.Entry binding, GpuUniformBuffer? buffer) => ValidateBuffer(binding, buffer);
    /// <summary>Checks CPU storage and allocator availability without consuming dirty work.</summary>
    internal static void ValidateUniformBlock(GpuPreparedBindings.Entry binding, CpuUniformBuffer? buffer)
    {
        Require(binding, buffer != null);
        if (binding.Active && buffer != null && buffer.SizeBytes < binding.MinimumSize)
            throw new InvalidOperationException($"CPU uniform block is too small for {binding.Contract.Name}.");
        if (binding.Active && buffer != null && !GpuUniformRingSystem.TryGetCurrent(out _))
            throw new InvalidOperationException("An active CPU uniform block requires a uniform ring.");
    }
    /// <summary>Checks the current storage allocation.</summary>
    internal static void ValidateStorageBlock(GpuPreparedBindings.Entry binding, GpuShaderStorageBuffer? buffer) => ValidateBuffer(binding, buffer);
    /// <summary>Checks initialized storage bounds before publication.</summary>
    internal static void ValidateStorageBlock(GpuPreparedBindings.Entry binding, GpuStorageBufferBinding range)
    {
        if (!binding.Active) return;
        Require(binding, range.Buffer?.IsValid == true);
        if (range.Buffer?.IsValid == true) ShaderInputValidation.RequireMetadata(range.Buffer);
        if (range.Buffer?.IsValid == true && (range.OffsetBytes < 0 || range.SizeBytes <= 0 || range.OffsetBytes > range.Buffer.SizeBytes - range.SizeBytes ||
            range.OffsetBytes % StateCache.Current.StorageBufferOffsetAlignment != 0))
            throw new InvalidOperationException($"Invalid storage range for {binding.Contract.Name}.");
    }
    /// <summary>Checks counter storage using retained executable activity.</summary>
    internal static void ValidateAtomicCounter(GpuPreparedBindings.Entry binding, GpuAtomicCounterBuffer? buffer) => ValidateBuffer(binding, buffer);
    #endregion

    #endregion

    #region Private
    /// <summary>Checks the canonical allocation before publishing any active whole-buffer input.</summary>
    private static void ValidateBuffer(GpuPreparedBindings.Entry binding, GpuBufferObject? buffer)
    {
        if (!binding.Active) return;
        Require(binding, buffer?.IsValid == true);
        if (buffer?.IsValid != true) return;
        ShaderInputValidation.RequireMetadata(buffer);
    }

    /// <summary>Selects the whole allocation for layered shader types when the property supplies no explicit view.</summary>
    private static RHIImageBinding DefaultImageView(GpuPreparedBindings.Entry binding, GpuTextureRef texture)
    {
        var target = (TextureTarget)GpuPreparedBindings.CompatibleTextureTarget(binding.Type);
        return new(texture!, Layered: target is TextureTarget.Texture3D or TextureTarget.Texture2DArray or TextureTarget.TextureCubeMap);
    }
    /// <summary>Projects one array element without changing its preparation-owned activity or required policy.</summary>
    private static GpuPreparedBindings.Entry Element(GpuPreparedBindings.Entry binding, int element) => binding with
    {
        Active = binding.ActiveElements[element],
        Contract = binding.Contract with { Binding = binding.Contract.Binding with { Slot = binding.Contract.Binding.Slot + element, ArrayLength = 1 } }
    };
    /// <summary>Fails only required active inputs; inactive entries have no publication obligation.</summary>
    private static void Require(GpuPreparedBindings.Entry binding, bool valid)
    {
        if (binding.Active && binding.Contract.Binding.Required && !valid)
            throw new InvalidOperationException($"Shader requires a valid {binding.Contract.Name} resource.");
    }
    #endregion
}

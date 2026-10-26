using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Validates complete retained inputs and publishes resources using preparation-owned numeric metadata.</summary>
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
    internal static void ValidateImage(GpuPreparedBindings.Entry binding, GpuTextureBinding image, ref ShaderInputValidation history)
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
    /// <summary>Checks borrowed engine texture validity at the ownership boundary.</summary>
    internal static int ValidateSampler(GpuPreparedBindings.Entry binding, int texture)
    {
        if (!binding.Active) return texture;
        bool valid = texture > 0 && GL.IsTexture(texture);
        Require(binding, valid);
        return valid ? texture : 0;
    }
    /// <summary>Checks a retained default image allocation.</summary>
    internal static void ValidateImage(GpuPreparedBindings.Entry binding, GpuTextureRef texture) => ValidateImage(binding, DefaultImageView(binding, texture));
    /// <summary>Checks image view bounds before any resource in the input set is published.</summary>
    internal static void ValidateImage(GpuPreparedBindings.Entry binding, GpuTextureBinding image)
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
        var format = image.Format ?? (SizedInternalFormat)selected.Format;
        if (image.Layer < 0 || (!image.Layered && image.Layer >= layers) ||
            image.Access is not (TextureAccess.ReadOnly or TextureAccess.WriteOnly or TextureAccess.ReadWrite) ||
            !ImageFormatCompatibility.Matches(image.Texture.RequireImage(image.Level).Format, format) ||
            !ShaderResourceFormatCompatibility.Matches(binding.Type, (PixelInternalFormat)format) ||
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

    #region Publication
    /// <summary>Publishes consecutive array slots while skipping optimized-away elements.</summary>
    internal static void SamplerArray(GpuPreparedBindings.Entry binding, IReadOnlyList<GpuTextureRef>? textures)
    {
        if (!binding.Active) return;
        for (int element = 0; element < binding.ActiveElements.Count; element++)
            Sampler(Element(binding, element), textures is not null && element < textures.Count ? textures[element] : default);
    }
    /// <summary>Publishes the managed allocation currently owned by the retained wrapper.</summary>
    internal static void Sampler(GpuPreparedBindings.Entry binding, GpuTextureRef texture)
    {
        if (!binding.Active) return;
        bool valid = GpuResourceManager.ValidateTextureReferenceInput(texture.Resource);
        BindSampler(binding, valid ? texture.TextureId : 0,
            valid ? texture.Target : (TextureTarget)GpuPreparedBindings.CompatibleTextureTarget(binding.Type));
    }
    /// <summary>Publishes an engine handle using the authored target policy.</summary>
    internal static void Sampler(GpuPreparedBindings.Entry binding, int texture)
    {
        if (!binding.Active) return;
        // External allocation identity is unknowable from an integer; force its bind through the cache boundary.
        StateCache.Current.InvalidateTextureUnit(binding.Contract.Binding.Slot);
        BindSampler(binding, texture, (TextureTarget)binding.Contract.Binding.TextureTarget);
    }
    /// <summary>Publishes a default image view through the context-wide cache.</summary>
    internal static void Image(GpuPreparedBindings.Entry binding, GpuTextureRef texture) => Image(binding, DefaultImageView(binding, texture));
    /// <summary>Publishes every image-view parameter, or clears an absent optional allocation.</summary>
    internal static void Image(GpuPreparedBindings.Entry binding, GpuTextureBinding image)
    {
        if (!binding.Active) return;
        bool valid = GpuResourceManager.ValidateTextureReferenceInput(image.Texture.Resource);
        StateCache.Current.BindImageTexture(binding.Contract.Binding.Slot, valid ? image.Texture.TextureId : 0,
            valid ? image.Level : 0, valid && image.Layered, valid ? image.Layer : 0, valid ? image.Access : TextureAccess.ReadOnly,
            valid ? image.Format ?? (SizedInternalFormat)image.Texture.RequireImage(image.Level).Format : SizedInternalFormat.Rgba8);
    }
    /// <summary>Publishes uniform storage directly to its prepared slot.</summary>
    internal static void UniformBlock(GpuPreparedBindings.Entry binding, GpuUniformBuffer? buffer)
    {
        if (!binding.Active) return;
        if (buffer?.IsValid == true) buffer.BindBase(binding.Contract.Binding.Slot);
        else OpenGLBufferBindings.BindBufferBase(StateCache.Current, BufferRangeTarget.UniformBuffer, binding.Contract.Binding.Slot, 0);
    }
    /// <summary>Leaves content upload and dirty-state commitment with the CPU block and ring owners.</summary>
    internal static void UniformBlock(GpuPreparedBindings.Entry binding, CpuUniformBuffer? buffer)
    {
        if (!binding.Active) return;
        if (buffer == null) OpenGLBufferBindings.BindBufferBase(StateCache.Current, BufferRangeTarget.UniformBuffer, binding.Contract.Binding.Slot, 0);
        else if (!buffer.TryBindToSlot(binding.Contract.Binding.Slot)) throw new InvalidOperationException($"Could not publish {binding.Contract.Name}.");
    }
    /// <summary>Publishes or clears a complete storage allocation.</summary>
    internal static void StorageBlock(GpuPreparedBindings.Entry binding, GpuShaderStorageBuffer? buffer)
    {
        if (!binding.Active) return;
        if (buffer?.IsValid == true) buffer.BindBase(binding.Contract.Binding.Slot);
        else OpenGLBufferBindings.BindBufferBase(StateCache.Current, BufferRangeTarget.ShaderStorageBuffer, binding.Contract.Binding.Slot, 0);
    }
    /// <summary>Publishes the initialized storage range or clears optional absent storage.</summary>
    internal static void StorageBlock(GpuPreparedBindings.Entry binding, GpuStorageBufferBinding range)
    {
        if (!binding.Active) return;
        if (range.Buffer?.IsValid == true)
            OpenGLBufferBindings.BindBufferRange(StateCache.Current, BufferRangeTarget.ShaderStorageBuffer, binding.Contract.Binding.Slot, range.Buffer.BufferId, range.OffsetBytes, range.SizeBytes);
        else OpenGLBufferBindings.BindBufferBase(StateCache.Current, BufferRangeTarget.ShaderStorageBuffer, binding.Contract.Binding.Slot, 0);
    }
    /// <summary>Publishes counter storage without repeating executable inspection.</summary>
    internal static void AtomicCounter(GpuPreparedBindings.Entry binding, GpuAtomicCounterBuffer? buffer)
    {
        if (!binding.Active) return;
        if (buffer?.IsValid == true) buffer.BindBase(binding.Contract.Binding.Slot);
        else OpenGLBufferBindings.BindBufferBase(StateCache.Current, BufferRangeTarget.AtomicCounterBuffer, binding.Contract.Binding.Slot, 0);
    }
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
    private static GpuTextureBinding DefaultImageView(GpuPreparedBindings.Entry binding, GpuTextureRef texture)
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
    /// <summary>Restores the shared slot through the existing context cache, independently of local property equality.</summary>
    private static void BindSampler(GpuPreparedBindings.Entry binding, int texture, TextureTarget target)
    {
        int unit = binding.Contract.Binding.Slot;
        int sampler = (ShaderSamplerPolicy)binding.Contract.Binding.Sampler switch
        {
            ShaderSamplerPolicy.Default => 0,
            ShaderSamplerPolicy.NearestClamp => GpuSamplers.NearestClamp.SamplerId,
            ShaderSamplerPolicy.LinearClamp => GpuSamplers.LinearClamp.SamplerId,
            ShaderSamplerPolicy.ShadowCompareLinearClamp => GpuSamplers.ShadowCompareLinearClamp.SamplerId,
            _ => throw new InvalidOperationException("Unknown sampler policy.")
        };
        var cache = StateCache.Current;
        if (!cache.TryGetCachedBoundTexture(target, unit, out int previous) || previous != texture) OpenGLImageBindings.BindTexture(cache, target, unit, texture);
        if (!cache.TryGetCachedBoundSampler(unit, out int previousSampler) || previousSampler != sampler) OpenGLImageBindings.BindSampler(cache, unit, sampler);
    }
    #endregion
}

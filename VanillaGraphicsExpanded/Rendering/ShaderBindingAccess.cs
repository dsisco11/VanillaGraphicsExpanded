using System;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Adapts immediate setters on authored submission owners to their prepared resource entries.</summary>
internal static class ShaderBindingAccess
{
    #region Public API
    /// <summary>Validates a borrowed texture and publishes its fixed sampler unit.</summary>
    internal static void Sampler(GpuProgramLayout layout, int program, string name, GpuTexture texture)
    {
        var binding = Resolve(layout, ShaderBindingKind.Sampler, name);
        ShaderPreparedSubmission.ValidateSampler(binding, texture);
        ShaderPreparedSubmission.Sampler(binding, texture);
    }

    /// <summary>Preserves the explicit image view through shared prepared validation and publication.</summary>
    internal static void Image(GpuProgramLayout layout, int program, string name, GpuTextureBinding image)
    {
        var binding = Resolve(layout, ShaderBindingKind.Image, name);
        ShaderPreparedSubmission.ValidateImage(binding, image);
        ShaderPreparedSubmission.Image(binding, image);
    }

    /// <summary>Resolves the shader-compatible default view for a directly supplied image texture.</summary>
    internal static void Image(GpuProgramLayout layout, int program, string name, GpuTexture texture)
    {
        var binding = Resolve(layout, ShaderBindingKind.Image, name);
        var history = new ShaderInputValidation();
        ShaderPreparedSubmission.ValidateImage(binding, texture, ref history);
        ShaderPreparedSubmission.Image(binding, texture);
    }

    /// <summary>Validates and binds externally owned uniform storage without copying its contents.</summary>
    internal static void UniformBlock(GpuProgramLayout layout, int program, string name, GpuUniformBuffer buffer)
    {
        var binding = Resolve(layout, ShaderBindingKind.UniformBlock, name);
        ShaderPreparedSubmission.ValidateUniformBlock(binding, buffer);
        ShaderPreparedSubmission.UniformBlock(binding, buffer);
    }

    /// <summary>Validates and publishes storage through the existing indexed-buffer cache.</summary>
    internal static void StorageBlock(GpuProgramLayout layout, int program, string name, GpuShaderStorageBuffer buffer)
    {
        var binding = Resolve(layout, ShaderBindingKind.StorageBlock, name);
        ShaderPreparedSubmission.ValidateStorageBlock(binding, buffer);
        ShaderPreparedSubmission.StorageBlock(binding, buffer);
    }
    #endregion

    #region Private
    /// <summary>Requires preparation metadata while preserving the alternate generator's immediate-setter API.</summary>
    private static GpuPreparedBindings.Entry Resolve(GpuProgramLayout layout, ShaderBindingKind kind, string name) =>
        (layout.BinaryInterface ?? throw new InvalidOperationException("Shader has no prepared executable interface."))
        .PreparedBindings.Resolve(GpuBindingEntry.Identity(kind, name));
    #endregion
}
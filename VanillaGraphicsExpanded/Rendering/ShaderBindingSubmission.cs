using System;
using VanillaGraphicsExpanded.Rendering.Contracts;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Validates retained resources and publishes them through the installed binding layout.</summary>
internal static class ShaderBindingSubmission
{
    #region Public API
    #region Validation
    /// <summary>Validates the storage for a declared atomic-counter buffer.</summary>
    internal static void ValidateAtomicCounter(IShaderSubmissionTarget owner, string name, bool required, GpuAtomicCounterBuffer? buffer, int slot)
        => Require(ActiveCounter(owner, slot), required, buffer?.IsValid == true, name);
    /// <summary>Checks borrowed texture lifetime only when the executable consumes the sampler.</summary>
    internal static void ValidateSampler(IShaderSubmissionTarget owner, string name, bool required, GpuTexture? texture)
        => Require(ActiveUniform(owner, name), required, texture?.IsValid == true, name);
    /// <summary>Checks raw texture lifetime at the publication boundary.</summary>
    internal static void ValidateSampler(IShaderSubmissionTarget owner, string name, bool required, int texture)
        => Require(ActiveUniform(owner, name), required, texture > 0 && GL.IsTexture(texture), name);
    /// <summary>Checks the resource underlying an explicit image view.</summary>
    internal static void ValidateImage(IShaderSubmissionTarget owner, string name, bool required, GpuTextureBinding image)
        => Require(ActiveUniform(owner, name), required, image.Texture?.IsValid == true, name);
    /// <summary>Checks a default image view's retained resource.</summary>
    internal static void ValidateImage(IShaderSubmissionTarget owner, string name, bool required, GpuTexture? image)
        => Require(ActiveUniform(owner, name), required, image?.IsValid == true, name);
    /// <summary>Checks an externally owned uniform buffer without uploading it.</summary>
    internal static void ValidateUniformBlock(IShaderSubmissionTarget owner, string name, bool required, GpuUniformBuffer? buffer)
        => Require(ActiveBlock(owner, name), required, buffer?.IsValid == true, name);
    /// <summary>Checks a packed CPU source before allocating a snapshot.</summary>
    internal static void ValidateUniformBlock(IShaderSubmissionTarget owner, string name, bool required, CpuUniformBuffer? buffer)
        => Require(ActiveBlock(owner, name), required, buffer != null, name);
    /// <summary>Checks a retained storage buffer before publication.</summary>
    internal static void ValidateStorageBlock(IShaderSubmissionTarget owner, string name, bool required, GpuShaderStorageBuffer? buffer)
        => Require(ActiveStorage(owner, name), required, buffer?.IsValid == true, name);
    /// <summary>Rejects invalid retained ranges before any binding is published.</summary>
    internal static void ValidateStorageBlock(IShaderSubmissionTarget owner, string name, bool required, GpuStorageBufferBinding range)
    {
        if (!ActiveStorage(owner, name)) return;
        bool valid = range.Buffer?.IsValid == true;
        Require(true, required, valid, name);
        if (valid && (range.OffsetBytes < 0 || range.SizeBytes <= 0 || range.OffsetBytes > range.Buffer!.SizeBytes - range.SizeBytes))
            throw new InvalidOperationException($"Shader requires a valid {name} storage range.");
    }
    #endregion

    #region Publication
    /// <summary>Publishes counter storage at its explicitly declared OpenGL binding.</summary>
    internal static void AtomicCounter(IShaderSubmissionTarget owner, string name, GpuAtomicCounterBuffer? buffer, int slot)
    {
        if (!ActiveCounter(owner, slot)) return;
        if (buffer?.IsValid == true) buffer.BindBase(slot);
        else GlStateCache.Current.BindBufferBase(BufferRangeTarget.AtomicCounterBuffer, slot, 0);
    }
    /// <summary>Binds a managed texture or explicitly clears an unassigned optional sampler.</summary>
    internal static void Sampler(IShaderSubmissionTarget owner, string name, GpuTexture? texture, ShaderTextureTarget target = ShaderTextureTarget.Texture2D, ShaderSamplerPolicy sampler = ShaderSamplerPolicy.Default)
    {
        // A disposed optional resource is treated as unassigned, never as its retired GL name.
        BindSampler(owner, name, texture?.IsValid == true ? texture.TextureId : 0, texture?.TextureTarget ?? (TextureTarget)target, sampler);
    }
    /// <summary>Resolves texture units against the current generation and applies the declared sampler policy.</summary>
    internal static void Sampler(IShaderSubmissionTarget owner, string name, int texture, ShaderTextureTarget target = ShaderTextureTarget.Texture2D, ShaderSamplerPolicy sampler = ShaderSamplerPolicy.Default)
        => BindSampler(owner, name, texture, (TextureTarget)target, sampler);

    /// <summary>Publishes an explicit image view, clearing an absent optional image.</summary>
    internal static void Image(IShaderSubmissionTarget owner, string name, GpuTextureBinding image)
    {
        if (!ActiveUniform(owner, name)) return;
        bool valid = image.Texture?.IsValid == true;
        Published(owner.ProgramLayout.TryBindImageTextureActive(owner.ProgramId, name, valid ? image.Texture!.TextureId : 0,
            image.Level, image.Layered, image.Layer, image.Access,
            image.Format ?? (valid ? (SizedInternalFormat)image.Texture!.InternalFormat : SizedInternalFormat.Rgba8), null), name);
    }
    /// <summary>Supplies the default view for a directly assigned image texture.</summary>
    internal static void Image(IShaderSubmissionTarget owner, string name, GpuTexture? image)
        => Image(owner, name, new GpuTextureBinding(image!));
    /// <summary>Binds external uniform storage, or clears an absent optional block.</summary>
    internal static void UniformBlock(IShaderSubmissionTarget owner, string name, GpuUniformBuffer? buffer)
    {
        if (!ActiveBlock(owner, name)) return;
        if (buffer?.IsValid == true) Published(owner.ProgramLayout.TryBindUniformBlock(owner.ProgramId, name, buffer), name);
        else ClearBlock(owner, name, false);
    }
    /// <summary>Writes exactly one complete ring snapshot for an active CPU-backed uniform block.</summary>
    internal static void UniformBlock(IShaderSubmissionTarget owner, string name, CpuUniformBuffer? buffer)
    {
        if (!ActiveBlock(owner, name)) return;
        if (buffer != null) Published(buffer.TryBindTo(owner, name, name), name);
        else ClearBlock(owner, name, false);
    }
    /// <summary>Binds external storage, or clears an absent optional storage block.</summary>
    internal static void StorageBlock(IShaderSubmissionTarget owner, string name, GpuShaderStorageBuffer? buffer)
    {
        if (!ActiveStorage(owner, name)) return;
        if (buffer?.IsValid == true) Published(owner.ProgramLayout.TryBindShaderStorageBlock(owner.ProgramId, name, buffer), name);
        else ClearBlock(owner, name, true);
    }
    /// <summary>Binds only initialized records, preserving runtime-sized array lengths.</summary>
    internal static void StorageBlock(IShaderSubmissionTarget owner, string name, GpuStorageBufferBinding range)
    {
        if (!ActiveStorage(owner, name)) return;
        Published(owner.ProgramLayout.TryGetShaderStorageBlockBinding(name, out int slot), name);
        if (range.Buffer?.IsValid == true && range.SizeBytes > 0)
            GlStateCache.Current.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, slot, range.Buffer.BufferId, range.OffsetBytes, range.SizeBytes);
        else ClearBlock(owner, name, true);
    }
    #endregion
    #endregion

    #region Private
    #region Texture publication
    /// <summary>Publishes a resolved graphics target using the declared sampler policy.</summary>
    private static void BindSampler(IShaderSubmissionTarget owner, string name, int texture, TextureTarget target, ShaderSamplerPolicy sampler)
    {
        if (!ActiveUniform(owner, name)) return;
        int samplerId = sampler switch
        {
            ShaderSamplerPolicy.NearestClamp => GpuSamplers.NearestClamp.SamplerId,
            ShaderSamplerPolicy.LinearClamp => GpuSamplers.LinearClamp.SamplerId,
            ShaderSamplerPolicy.ShadowCompareLinearClamp => GpuSamplers.ShadowCompareLinearClamp.SamplerId,
            ShaderSamplerPolicy.Default => 0,
            _ => throw new InvalidOperationException($"Unknown sampler policy {sampler}.")
        };
        if (texture != 0 && !GL.IsTexture(texture)) texture = 0;
        Published(owner.ProgramLayout.TryBindSamplerTextureActive(owner.ProgramId, name, target, texture, samplerId, null), name);
    }
    #endregion

    #region Binding resolution and failure handling
    /// <summary>Uses executable counter slots because SPIR-V need not preserve uniform lookup names.</summary>
    private static bool ActiveCounter(IShaderSubmissionTarget owner, int slot)
    {
        GL.GetProgram(owner.ProgramId, GetProgramParameterName.ActiveAtomicCounterBuffers, out int count);
        for (int index = 0; index < count; index++)
        {
            GL.GetActiveAtomicCounterBuffer(owner.ProgramId, index, AtomicCounterBufferParameter.AtomicCounterBufferBinding, out int binding);
            if (binding == slot) return true;
        }
        return false;
    }
    /// <summary>Skips only uniforms proven absent from the installed executable.</summary>
    private static bool ActiveUniform(IShaderSubmissionTarget owner, string name) => owner.ProgramLayout.ResolveUniformLocation(owner.ProgramId, name).State != GpuProgramLayout.ResolutionState.Missing;
    /// <summary>Skips only uniform blocks proven absent from the installed executable.</summary>
    private static bool ActiveBlock(IShaderSubmissionTarget owner, string name) => owner.ProgramLayout.ResolveUniformBlockActive(owner.ProgramId, name).State != GpuProgramLayout.ResolutionState.Missing;
    /// <summary>Skips only storage blocks proven absent from the installed executable.</summary>
    private static bool ActiveStorage(IShaderSubmissionTarget owner, string name) => owner.ProgramLayout.ResolveShaderStorageBlockActive(owner.ProgramId, name).State != GpuProgramLayout.ResolutionState.Missing;
    /// <summary>Fails required active resources before any generated binding operations occur.</summary>
    private static void Require(bool active, bool required, bool valid, string name)
    {
        if (active && required && !valid) throw new InvalidOperationException($"Shader requires a valid {name} resource.");
    }
    /// <summary>Propagates publication failures to Use and TryUse without consuming a failed CPU upload.</summary>
    private static void Published(bool success, string name)
    {
        if (!success) throw new InvalidOperationException($"Shader could not submit {name}.");
    }
    /// <summary>Clears the resolved block slot so optional absence cannot inherit another owner's storage.</summary>
    private static void ClearBlock(IShaderSubmissionTarget owner, string name, bool storage)
    {
        int slot;
        bool found = storage ? owner.ProgramLayout.TryGetShaderStorageBlockBinding(name, out slot) : owner.ProgramLayout.TryGetUniformBlockBinding(name, out slot);
        Published(found, name);
        GlStateCache.Current.BindBufferBase(storage ? BufferRangeTarget.ShaderStorageBuffer : BufferRangeTarget.UniformBuffer, slot, 0);
    }
    #endregion
    #endregion
}

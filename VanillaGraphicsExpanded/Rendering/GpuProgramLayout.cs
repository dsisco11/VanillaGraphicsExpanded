using System;
using System.Collections.Generic;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Captures a snapshot of binding-related program resources after link (UBO/SSBO bindings, sampler/image units).
/// </summary>
internal sealed class GpuProgramLayout
{
    private static readonly IReadOnlyDictionary<string, int> EmptyBindings = new Dictionary<string, int>(StringComparer.Ordinal);

    public static GpuProgramLayout Empty { get; } = new(null, null, null, null);

    /// <summary>
    /// Gets cached uniform block binding points (<c>layout(binding=...)</c> for UBOs).
    /// </summary>
    public IReadOnlyDictionary<string, int> UniformBlockBindings { get; }

    /// <summary>
    /// Gets cached shader storage block binding points (<c>layout(binding=...)</c> for SSBOs).
    /// </summary>
    public IReadOnlyDictionary<string, int> ShaderStorageBlockBindings { get; }

    /// <summary>
    /// Gets cached sampler uniform values (texture units). This is a snapshot at cache build time.
    /// </summary>
    public IReadOnlyDictionary<string, int> SamplerBindings { get; }

    /// <summary>
    /// Gets cached image uniform values (image units). This is a snapshot at cache build time.
    /// </summary>
    public IReadOnlyDictionary<string, int> ImageBindings { get; }

    private GpuProgramLayout(
        IReadOnlyDictionary<string, int>? uniformBlockBindings,
        IReadOnlyDictionary<string, int>? shaderStorageBlockBindings,
        IReadOnlyDictionary<string, int>? samplerBindings,
        IReadOnlyDictionary<string, int>? imageBindings)
    {
        UniformBlockBindings = uniformBlockBindings ?? EmptyBindings;
        ShaderStorageBlockBindings = shaderStorageBlockBindings ?? EmptyBindings;
        SamplerBindings = samplerBindings ?? EmptyBindings;
        ImageBindings = imageBindings ?? EmptyBindings;
    }

    /// <summary>
    /// Attempts to build a binding cache for a successfully linked program.
    /// Returns an empty cache if program interface queries are unavailable or if the GL call fails.
    /// </summary>
    public static GpuProgramLayout TryBuild(int programId)
    {
        if (programId == 0)
        {
            return Empty;
        }

        try
        {
            var uniformBlocks = TryBuildBufferBindingByName(programId, ProgramInterface.UniformBlock);
            var storageBlocks = TryBuildBufferBindingByName(programId, ProgramInterface.ShaderStorageBlock);

            var (samplers, images) = TryBuildTextureUnitBindings(programId);

            return new GpuProgramLayout(uniformBlocks, storageBlocks, samplers, images);
        }
        catch
        {
            return Empty;
        }
    }

    private static IReadOnlyDictionary<string, int> TryBuildBufferBindingByName(int programId, ProgramInterface programInterface)
    {
        try
        {
            GL.GetProgramInterface(programId, programInterface, ProgramInterfaceParameter.ActiveResources, out int count);
            if (count <= 0)
            {
                return EmptyBindings;
            }

            GL.GetProgramInterface(programId, programInterface, ProgramInterfaceParameter.MaxNameLength, out int maxNameLen);
            if (maxNameLen <= 0)
            {
                maxNameLen = 256;
            }

            var bindings = new Dictionary<string, int>(count, StringComparer.Ordinal);
            var props = new[] { ProgramProperty.BufferBinding };
            var values = new int[1];

            for (int i = 0; i < count; i++)
            {
                GL.GetProgramResourceName(programId, programInterface, i, maxNameLen, out _, out string name);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                GL.GetProgramResource(programId, programInterface, i, props.Length, props, values.Length, out _, values);
                bindings[name] = values[0];
            }

            return bindings;
        }
        catch
        {
            return EmptyBindings;
        }
    }

    private static (IReadOnlyDictionary<string, int> Samplers, IReadOnlyDictionary<string, int> Images) TryBuildTextureUnitBindings(int programId)
    {
        try
        {
            GL.GetProgramInterface(programId, ProgramInterface.Uniform, ProgramInterfaceParameter.ActiveResources, out int count);
            if (count <= 0)
            {
                return (EmptyBindings, EmptyBindings);
            }

            GL.GetProgramInterface(programId, ProgramInterface.Uniform, ProgramInterfaceParameter.MaxNameLength, out int maxNameLen);
            if (maxNameLen <= 0)
            {
                maxNameLen = 256;
            }

            // Query uniform type + location. Skip uniforms in blocks (BlockIndex != -1).
            var props = new[] { ProgramProperty.Type, ProgramProperty.Location, ProgramProperty.BlockIndex };
            var values = new int[props.Length];

            Dictionary<string, int>? samplerBindings = null;
            Dictionary<string, int>? imageBindings = null;

            for (int i = 0; i < count; i++)
            {
                GL.GetProgramResource(programId, ProgramInterface.Uniform, i, props.Length, props, values.Length, out _, values);

                int typeValue = values[0];
                int location = values[1];
                int blockIndex = values[2];

                if (blockIndex != -1 || location < 0)
                {
                    continue;
                }

                var type = (ActiveUniformType)typeValue;
                bool isSampler = IsSamplerType(type);
                bool isImage = !isSampler && IsImageType(type);
                if (!isSampler && !isImage)
                {
                    continue;
                }

                GL.GetProgramResourceName(programId, ProgramInterface.Uniform, i, maxNameLen, out _, out string name);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                name = NormalizeUniformName(name);

                GL.GetUniform(programId, location, out int unitOrBinding);

                if (isSampler)
                {
                    samplerBindings ??= new Dictionary<string, int>(StringComparer.Ordinal);
                    samplerBindings[name] = unitOrBinding;
                }
                else
                {
                    imageBindings ??= new Dictionary<string, int>(StringComparer.Ordinal);
                    imageBindings[name] = unitOrBinding;
                }
            }

            return (samplerBindings ?? EmptyBindings, imageBindings ?? EmptyBindings);
        }
        catch
        {
            return (EmptyBindings, EmptyBindings);
        }
    }

    private static string NormalizeUniformName(string name)
    {
        // Drivers often expose arrays as "name[0]" via active uniform enumeration.
        const string array0Suffix = "[0]";
        if (name.EndsWith(array0Suffix, StringComparison.Ordinal))
        {
            return name[..^array0Suffix.Length];
        }

        return name;
    }

    private static bool IsSamplerType(ActiveUniformType type)
    {
        // Keep this allocation-free; the set is stable across GL versions.
        return type is
            ActiveUniformType.Sampler1D
            or ActiveUniformType.Sampler2D
            or ActiveUniformType.Sampler3D
            or ActiveUniformType.SamplerCube
            or ActiveUniformType.Sampler1DShadow
            or ActiveUniformType.Sampler2DShadow
            or ActiveUniformType.Sampler1DArray
            or ActiveUniformType.Sampler2DArray
            or ActiveUniformType.Sampler1DArrayShadow
            or ActiveUniformType.Sampler2DArrayShadow
            or ActiveUniformType.Sampler2DMultisample
            or ActiveUniformType.Sampler2DMultisampleArray
            or ActiveUniformType.SamplerCubeShadow
            or ActiveUniformType.SamplerBuffer
            or ActiveUniformType.Sampler2DRect
            or ActiveUniformType.Sampler2DRectShadow
            or ActiveUniformType.IntSampler1D
            or ActiveUniformType.IntSampler2D
            or ActiveUniformType.IntSampler3D
            or ActiveUniformType.IntSamplerCube
            or ActiveUniformType.IntSampler1DArray
            or ActiveUniformType.IntSampler2DArray
            or ActiveUniformType.IntSampler2DMultisample
            or ActiveUniformType.IntSampler2DMultisampleArray
            or ActiveUniformType.IntSamplerBuffer
            or ActiveUniformType.IntSampler2DRect
            or ActiveUniformType.UnsignedIntSampler1D
            or ActiveUniformType.UnsignedIntSampler2D
            or ActiveUniformType.UnsignedIntSampler3D
            or ActiveUniformType.UnsignedIntSamplerCube
            or ActiveUniformType.UnsignedIntSampler1DArray
            or ActiveUniformType.UnsignedIntSampler2DArray
            or ActiveUniformType.UnsignedIntSampler2DMultisample
            or ActiveUniformType.UnsignedIntSampler2DMultisampleArray
            or ActiveUniformType.UnsignedIntSamplerBuffer
            or ActiveUniformType.UnsignedIntSampler2DRect
            or ActiveUniformType.SamplerCubeMapArray
            or ActiveUniformType.SamplerCubeMapArrayShadow
            or ActiveUniformType.IntSamplerCubeMapArray
            or ActiveUniformType.UnsignedIntSamplerCubeMapArray;
    }

    private static bool IsImageType(ActiveUniformType type)
    {
        return type is
            ActiveUniformType.Image1D
            or ActiveUniformType.Image2D
            or ActiveUniformType.Image3D
            or ActiveUniformType.Image2DRect
            or ActiveUniformType.ImageCube
            or ActiveUniformType.ImageBuffer
            or ActiveUniformType.Image1DArray
            or ActiveUniformType.Image2DArray
            or ActiveUniformType.Image2DMultisample
            or ActiveUniformType.Image2DMultisampleArray
            or ActiveUniformType.IntImage1D
            or ActiveUniformType.IntImage2D
            or ActiveUniformType.IntImage3D
            or ActiveUniformType.IntImage2DRect
            or ActiveUniformType.IntImageCube
            or ActiveUniformType.IntImageBuffer
            or ActiveUniformType.IntImage1DArray
            or ActiveUniformType.IntImage2DArray
            or ActiveUniformType.IntImage2DMultisample
            or ActiveUniformType.IntImage2DMultisampleArray
            or ActiveUniformType.UnsignedIntImage1D
            or ActiveUniformType.UnsignedIntImage2D
            or ActiveUniformType.UnsignedIntImage3D
            or ActiveUniformType.UnsignedIntImage2DRect
            or ActiveUniformType.UnsignedIntImageCube
            or ActiveUniformType.UnsignedIntImageBuffer
            or ActiveUniformType.UnsignedIntImage1DArray
            or ActiveUniformType.UnsignedIntImage2DArray
            or ActiveUniformType.UnsignedIntImage2DMultisample
            or ActiveUniformType.UnsignedIntImage2DMultisampleArray
            or ActiveUniformType.ImageCubeMapArray
            or ActiveUniformType.IntImageCubeMapArray
            or ActiveUniformType.UnsignedIntImageCubeMapArray;
    }

    /// <summary>
    /// Attempts to get the UBO binding point for a uniform block by name.
    /// </summary>
    public bool TryGetUniformBlockBinding(string blockName, out int bindingIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blockName);
        return UniformBlockBindings.TryGetValue(blockName, out bindingIndex);
    }

    /// <summary>
    /// Attempts to get the SSBO binding point for a shader storage block by name.
    /// </summary>
    public bool TryGetShaderStorageBlockBinding(string blockName, out int bindingIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blockName);
        return ShaderStorageBlockBindings.TryGetValue(blockName, out bindingIndex);
    }

    /// <summary>
    /// Attempts to get the texture unit for a sampler uniform by name.
    /// </summary>
    public bool TryGetSamplerUnit(string samplerUniformName, out int unit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(samplerUniformName);
        return SamplerBindings.TryGetValue(samplerUniformName, out unit);
    }

    /// <summary>
    /// Attempts to get the image unit for an image uniform by name.
    /// </summary>
    public bool TryGetImageUnit(string imageUniformName, out int unit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUniformName);
        return ImageBindings.TryGetValue(imageUniformName, out unit);
    }

    /// <summary>
    /// Binds a UBO to the binding point for the named uniform block.
    /// </summary>
    public bool TryBindUniformBlock(string blockName, GpuUniformBuffer buffer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blockName);
        ArgumentNullException.ThrowIfNull(buffer);

        if (!TryGetUniformBlockBinding(blockName, out int binding))
        {
            return false;
        }

        buffer.BindBase(binding);
        return true;
    }

    /// <summary>
    /// Binds an SSBO to the binding point for the named shader storage block.
    /// </summary>
    public bool TryBindShaderStorageBlock(string blockName, GpuShaderStorageBuffer buffer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blockName);
        ArgumentNullException.ThrowIfNull(buffer);

        if (!TryGetShaderStorageBlockBinding(blockName, out int binding))
        {
            return false;
        }

        buffer.BindBase(binding);
        return true;
    }

    /// <summary>
    /// Binds a texture (and optional sampler object) to the texture unit used by the named sampler uniform.
    /// </summary>
    /// <remarks>
    /// The sampler unit is read from the linked program at cache-build time; if the program later changes
    /// sampler uniforms via <c>glUniform1i</c>, the cache can become stale.
    /// </remarks>
    public bool TryBindSamplerTexture(string samplerUniformName, TextureTarget target, int textureId, int samplerId = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(samplerUniformName);

        if (textureId == 0)
        {
            return false;
        }

        if (!TryGetSamplerUnit(samplerUniformName, out int unit))
        {
            return false;
        }

        try
        {
            GL.BindTextureUnit(unit, textureId);
        }
        catch
        {
            GL.ActiveTexture(TextureUnit.Texture0 + unit);
            GL.BindTexture(target, textureId);
        }

        if (samplerId != 0)
        {
            try { GL.BindSampler(unit, samplerId); } catch { }
        }

        return true;
    }
}

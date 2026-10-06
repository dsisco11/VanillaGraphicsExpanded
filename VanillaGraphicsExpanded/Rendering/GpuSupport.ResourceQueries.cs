using System;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Queries resource and compute limits for capability initialization.</summary>
public static partial class GpuSupport
{
    #region Private
    /// <summary>Captures implementation limits for the current capability lifetime.</summary>
    private static GraphicsCapabilities CaptureLimits(GraphicsCapabilities capabilities)
    {
        int maxTextureSize = default;
        int max3DTextureSize = default;
        int maxCubeMapTextureSize = default;
        int maxArrayTextureLayers = default;
        int maxTextureImageUnits = default;
        int maxCombinedTextureImageUnits = default;
        int maxVertexTextureImageUnits = default;
        int maxUniformBufferBindings = default;
        int maxUniformBlockSize = default;
        int maxShaderStorageBufferBindings = default;
        long maxShaderStorageBlockSize = default;
        int maxAtomicCounterBufferBindings = default;
        int maxImageUnits = default;
        int maxCombinedImageUnits = default;
        int maxColorAttachments = default;
        int maxViewportWidth = default;
        int maxViewportHeight = default;
        int shaderStorageBufferOffsetAlignment = default;
        int maxTessGenLevel = default;
        int maxTessControlTextureImageUnits = default;
        int maxTessEvaluationTextureImageUnits = default;
        int maxUniformLocations = default;
        ImmutableArray<int> maxComputeWorkGroupCount = ImmutableArray<int>.Empty;
        ImmutableArray<int> maxComputeWorkGroupSize = ImmutableArray<int>.Empty;
        int maxComputeWorkGroupInvocations = default;
        int maxComputeSharedMemorySize = default;

        GlDebug.ClearErrors();
        int[] viewport = new int[2];
        GL.GetInteger(GetPName.MaxViewportDims, viewport);
        maxViewportWidth = viewport[0];
        maxViewportHeight = viewport[1];
        shaderStorageBufferOffsetAlignment = capabilities.SupportsArbShaderStorageBufferObject
            ? SafeGetInt(GetPName.ShaderStorageBufferOffsetAlignment) : 0;
        maxTextureSize = SafeGetInt(GetPName.MaxTextureSize);
        max3DTextureSize = SafeGetInt(GetPName.Max3DTextureSize);
        maxCubeMapTextureSize = SafeGetInt(GetPName.MaxCubeMapTextureSize);
        maxArrayTextureLayers = IsAtLeast(capabilities.ApiVersion, 3, 0) ? SafeGetInt(GetPName.MaxArrayTextureLayers) : 0;

        maxTextureImageUnits = SafeGetInt(GetPName.MaxTextureImageUnits);
        maxCombinedTextureImageUnits = SafeGetInt(GetPName.MaxCombinedTextureImageUnits);
        maxVertexTextureImageUnits = SafeGetInt(GetPName.MaxVertexTextureImageUnits);

        if (IsAtLeast(capabilities.ApiVersion, 3, 1))
        {
            maxUniformBufferBindings = SafeGetInt(GetPName.MaxUniformBufferBindings);
            maxUniformBlockSize = SafeGetInt(GetPName.MaxUniformBlockSize);
        }
        else
        {
            maxUniformBufferBindings = 0;
            maxUniformBlockSize = 0;
        }

        if (capabilities.SupportsArbShaderStorageBufferObject)
        {
            maxShaderStorageBufferBindings = SafeGetInt((GetPName)All.MaxShaderStorageBufferBindings);
            maxShaderStorageBlockSize = SafeGetLong((GetPName)All.MaxShaderStorageBlockSize);
        }
        else
        {
            maxShaderStorageBufferBindings = 0;
            maxShaderStorageBlockSize = 0;
        }

        if (capabilities.SupportsArbShaderAtomicCounters)
        {
            maxAtomicCounterBufferBindings = SafeGetInt((GetPName)All.MaxAtomicCounterBufferBindings);
        }
        else
        {
            maxAtomicCounterBufferBindings = 0;
        }

        if (capabilities.SupportsArbShaderImageLoadStore)
        {
            // Image uniforms have distinct limits from sampler texture units.
            maxImageUnits = SafeGetInt((GetPName)All.MaxImageUnits);
            maxCombinedImageUnits = SafeGetInt((GetPName)All.MaxCombinedImageUniforms);
        }
        else
        {
            maxImageUnits = 0;
            maxCombinedImageUnits = 0;
        }

        maxColorAttachments = IsAtLeast(capabilities.ApiVersion, 3, 0) ? SafeGetInt(GetPName.MaxColorAttachments) : 0;

        bool tessellation = IsAtLeast(capabilities.ApiVersion, 4, 0) || GlExtensions.Supports("GL_ARB_tessellation_shader");
        maxTessGenLevel = tessellation ? SafeGetInt(GetPName.MaxTessGenLevel) : 0;
        maxTessControlTextureImageUnits = tessellation ? SafeGetInt(GetPName.MaxTessControlTextureImageUnits) : 0;
        maxTessEvaluationTextureImageUnits = tessellation ? SafeGetInt(GetPName.MaxTessEvaluationTextureImageUnits) : 0;
        maxUniformLocations = capabilities.SupportsArbExplicitUniformLocation
            ? SafeGetInt((GetPName)All.MaxUniformLocations)
            : 0;

        if (capabilities.SupportsArbComputeShader)
        {
            maxComputeWorkGroupCount = SafeGetInt3((GetIndexedPName)GetPName.MaxComputeWorkGroupCount);
            maxComputeWorkGroupSize = SafeGetInt3((GetIndexedPName)GetPName.MaxComputeWorkGroupSize);
            maxComputeWorkGroupInvocations = SafeGetInt(GetPName.MaxComputeWorkGroupInvocations);
            maxComputeSharedMemorySize = SafeGetInt((GetPName)All.MaxComputeSharedMemorySize);
        }
        else
        {
            maxComputeWorkGroupCount = ImmutableArray<int>.Empty;
            maxComputeWorkGroupSize = ImmutableArray<int>.Empty;
            maxComputeWorkGroupInvocations = 0;
            maxComputeSharedMemorySize = 0;
        }
        GlDebug.ThrowIfErrors();

        // Binary formats are driver capabilities, not program-specific reflection data.
        int binaryCount = IsAtLeast(capabilities.ApiVersion, 4, 1) || GlExtensions.Supports("GL_ARB_get_program_binary")
            ? SafeGetInt(GetPName.NumProgramBinaryFormats) : 0;
        int[] binaryFormats = new int[binaryCount];
        if (binaryCount > 0) GL.GetInteger(GetPName.ProgramBinaryFormats, binaryFormats);
        return capabilities with
        {
            UniformBufferOffsetAlignment = IsAtLeast(capabilities.ApiVersion, 3, 1) ? SafeGetInt(GetPName.UniformBufferOffsetAlignment) : 0,
            TextureBufferOffsetAlignment = capabilities.SupportsTextureBufferRange ? SafeGetInt((GetPName)All.TextureBufferOffsetAlignment) : 0,
            MaxTextureBufferSize = IsAtLeast(capabilities.ApiVersion, 3, 1) ? SafeGetInt(GetPName.MaxTextureBufferSize) : 0,
            MaxLabelLength = capabilities.SupportsKhrDebug ? SafeGetInt(GetPName.MaxLabelLength) : 0,
            ProgramBinaryFormats = [.. binaryFormats],
            MaxTextureSize = maxTextureSize,
            Max3DTextureSize = max3DTextureSize,
            MaxCubeMapTextureSize = maxCubeMapTextureSize,
            MaxArrayTextureLayers = maxArrayTextureLayers,
            MaxTextureImageUnits = maxTextureImageUnits,
            MaxCombinedTextureImageUnits = maxCombinedTextureImageUnits,
            MaxVertexTextureImageUnits = maxVertexTextureImageUnits,
            MaxUniformBufferBindings = maxUniformBufferBindings,
            MaxUniformBlockSize = maxUniformBlockSize,
            MaxShaderStorageBufferBindings = maxShaderStorageBufferBindings,
            MaxShaderStorageBlockSize = maxShaderStorageBlockSize,
            MaxAtomicCounterBufferBindings = maxAtomicCounterBufferBindings,
            MaxImageUnits = maxImageUnits,
            MaxCombinedImageUnits = maxCombinedImageUnits,
            MaxColorAttachments = maxColorAttachments,
            MaxViewportWidth = maxViewportWidth,
            MaxViewportHeight = maxViewportHeight,
            ShaderStorageBufferOffsetAlignment = shaderStorageBufferOffsetAlignment,
            MaxTessGenLevel = maxTessGenLevel,
            MaxTessControlTextureImageUnits = maxTessControlTextureImageUnits,
            MaxTessEvaluationTextureImageUnits = maxTessEvaluationTextureImageUnits,
            MaxUniformLocations = maxUniformLocations,
            MaxComputeWorkGroupCount = maxComputeWorkGroupCount,
            MaxComputeWorkGroupSize = maxComputeWorkGroupSize,
            MaxComputeWorkGroupInvocations = maxComputeWorkGroupInvocations,
            MaxComputeSharedMemorySize = maxComputeSharedMemorySize
        };
    }

    /// <summary>Reads an integer limit with a zero fallback when unavailable.</summary>
    private static int SafeGetInt(GetPName pname)
    {
        try
        {
            return GL.GetInteger(pname);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Reads a wide limit and falls back to the integer query.</summary>
    private static long SafeGetLong(GetPName pname)
    {
        try
        {
            // Prefer the 64-bit query when available.
            GL.GetInteger64(pname, out long value);
            return value;
        }
        catch
        {
            try
            {
                return SafeGetInt(pname);
            }
            catch
            {
                return 0;
            }
        }
    }

    /// <summary>Captures the three independent axes of an indexed compute limit.</summary>
    private static ImmutableArray<int> SafeGetInt3(GetIndexedPName pname)
    {
        try
        {
            int[] values = new int[3];
            // Compute axis limits require indexed queries; the unindexed overload generates InvalidEnum.
            for (int axis = 0; axis < values.Length; axis++)
                GL.GetInteger(pname, axis, out values[axis]);
            return [values[0], values[1], values[2]];
        }
        catch
        {
            return [];
        }
    }
    #endregion
}

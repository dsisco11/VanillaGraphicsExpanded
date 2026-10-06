using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Resolves feature availability from extensions and API versions.</summary>
public static partial class GpuSupport
{
    #region Private
    /// <summary>Resolves extension and core-version feature support.</summary>
    private static GraphicsCapabilities CaptureExtensionFlags(GraphicsCapabilities capabilities)
    {
        bool supportsKhrDebug = default;
        bool supportsArbDirectStateAccess = default;
        bool supportsArbMultiBind = default;
        bool supportsArbBindlessTexture = default;
        bool supportsArbComputeShader = default;
        bool supportsArbShaderStorageBufferObject = default;
        bool supportsArbShaderImageLoadStore = default;
        bool supportsArbShaderAtomicCounters = default;
        bool supportsArbExplicitUniformLocation = default;
        bool supportsArbBufferStorage = default;
        bool supportsArbShadingLanguage420Pack = default;
        bool supportsArbProgramInterfaceQuery = default;
        bool supportsArbGlSpirv = default;
        bool supportsExtSemaphore = default;
        bool supportsExtSemaphoreFd = default;
        bool supportsExtMemoryObject = default;
        bool supportsExtMemoryObjectFd = default;

        GlDebug.ClearErrors();
        supportsKhrDebug = GlExtensions.Supports("GL_KHR_debug") || IsAtLeast(capabilities.ApiVersion, 4, 3);
        supportsArbDirectStateAccess = GlExtensions.Supports("GL_ARB_direct_state_access") || IsAtLeast(capabilities.ApiVersion, 4, 5);
        supportsArbMultiBind = GlExtensions.Supports("GL_ARB_multi_bind") || IsAtLeast(capabilities.ApiVersion, 4, 4);
        supportsArbBindlessTexture = GlExtensions.Supports("GL_ARB_bindless_texture");

        supportsArbComputeShader = GlExtensions.Supports("GL_ARB_compute_shader") || IsAtLeast(capabilities.ApiVersion, 4, 3);
        supportsArbShaderStorageBufferObject = GlExtensions.Supports("GL_ARB_shader_storage_buffer_object") || IsAtLeast(capabilities.ApiVersion, 4, 3);
        supportsArbShaderImageLoadStore = GlExtensions.Supports("GL_ARB_shader_image_load_store") || IsAtLeast(capabilities.ApiVersion, 4, 2);
        supportsArbShaderAtomicCounters = GlExtensions.Supports("GL_ARB_shader_atomic_counters") || IsAtLeast(capabilities.ApiVersion, 4, 2);
        supportsArbExplicitUniformLocation = GlExtensions.Supports("GL_ARB_explicit_uniform_location") || IsAtLeast(capabilities.ApiVersion, 4, 3);
        supportsArbBufferStorage = GlExtensions.Supports("GL_ARB_buffer_storage") || IsAtLeast(capabilities.ApiVersion, 4, 4);

        // Enables layout(binding=...) in older GLSL (e.g., #version 330) when supported by the driver.
        supportsArbShadingLanguage420Pack = GlExtensions.Supports("GL_ARB_shading_language_420pack") || IsAtLeast(capabilities.ApiVersion, 4, 2);

        // Required for glGetProgramResource* and friends.
        supportsArbProgramInterfaceQuery = GlExtensions.Supports("GL_ARB_program_interface_query") || IsAtLeast(capabilities.ApiVersion, 4, 3);

        supportsArbGlSpirv = GlExtensions.Supports("GL_ARB_gl_spirv") || IsAtLeast(capabilities.ApiVersion, 4, 6);

        supportsExtSemaphore = GlExtensions.Supports("GL_EXT_semaphore");
        supportsExtSemaphoreFd = GlExtensions.Supports("GL_EXT_semaphore_fd");
        supportsExtMemoryObject = GlExtensions.Supports("GL_EXT_memory_object");
        supportsExtMemoryObjectFd = GlExtensions.Supports("GL_EXT_memory_object_fd");
        GlDebug.ThrowIfErrors();

        return capabilities with
        {
            SupportsTextureBufferRange = IsAtLeast(capabilities.ApiVersion, 4, 3) || GlExtensions.Supports("GL_ARB_texture_buffer_range"),
            SupportsClearTexture = IsAtLeast(capabilities.ApiVersion, 4, 4) || GlExtensions.Supports("GL_ARB_clear_texture"),
            SupportsSparseTexture = GlExtensions.Supports("GL_ARB_sparse_texture"),
            SupportsSparseBuffer = GlExtensions.Supports("GL_ARB_sparse_buffer"),
            SupportsArbParallelShaderCompile = GlExtensions.Supports("GL_ARB_parallel_shader_compile"),
            SupportsKhrParallelShaderCompile = GlExtensions.Supports("GL_KHR_parallel_shader_compile"),
            SupportsInternalFormatQuery2 = IsAtLeast(capabilities.ApiVersion, 4, 3) || GlExtensions.Supports("GL_ARB_internalformat_query2"),
            SupportsKhrDebug = supportsKhrDebug,
            SupportsArbDirectStateAccess = supportsArbDirectStateAccess,
            SupportsArbMultiBind = supportsArbMultiBind,
            SupportsArbBindlessTexture = supportsArbBindlessTexture,
            SupportsArbComputeShader = supportsArbComputeShader,
            SupportsArbShaderStorageBufferObject = supportsArbShaderStorageBufferObject,
            SupportsArbShaderImageLoadStore = supportsArbShaderImageLoadStore,
            SupportsArbShaderAtomicCounters = supportsArbShaderAtomicCounters,
            SupportsArbExplicitUniformLocation = supportsArbExplicitUniformLocation,
            SupportsArbBufferStorage = supportsArbBufferStorage,
            SupportsArbShadingLanguage420Pack = supportsArbShadingLanguage420Pack,
            SupportsArbProgramInterfaceQuery = supportsArbProgramInterfaceQuery,
            SupportsArbGlSpirv = supportsArbGlSpirv,
            SupportsExtSemaphore = supportsExtSemaphore,
            SupportsExtSemaphoreFd = supportsExtSemaphoreFd,
            SupportsExtMemoryObject = supportsExtMemoryObject,
            SupportsExtMemoryObjectFd = supportsExtMemoryObjectFd
        };
    }

    /// <summary>Checks whether a parsed API version meets the requested minimum.</summary>
    private static bool IsAtLeast(Version? version, int major, int minor)
    {
        if (version is null)
        {
            return false;
        }

        if (version.Major != major)
        {
            return version.Major > major;
        }

        return version.Minor >= minor;
    }
    #endregion
}

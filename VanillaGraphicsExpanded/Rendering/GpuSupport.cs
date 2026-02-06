using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Threading;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Global cache of GPU/context capability information.
/// Call <see cref="Initialize"/> (or <see cref="TryInitialize"/>) once on a thread with a current GL context,
/// then read cached properties without incurring additional GL queries.
/// </summary>
public static class GpuSupport
{
    private static readonly object Sync = new();

    private static bool isInitialized;
    private static string? cachedContextKey;

    #region Initialization

    /// <summary>
    /// Returns <c>true</c> when <see cref="Initialize"/> has successfully run at least once.
    /// </summary>
    public static bool IsInitialized => isInitialized;

    /// <summary>
    /// Returns <c>true</c> when the cached values were captured from the current GL context.
    /// </summary>
    public static bool IsInitializedForCurrentContext
    {
        get
        {
            if (!isInitialized)
            {
                return false;
            }

            if (!GlExtensions.TryGetContextKey(out string contextKey))
            {
                return false;
            }

            string? snapshot = Volatile.Read(ref cachedContextKey);
            return string.Equals(snapshot, contextKey, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Initializes (or refreshes) the cached values from the current OpenGL context.
    /// </summary>
    /// <param name="force">When true, always refreshes cached values even if the context matches.</param>
    /// <exception cref="InvalidOperationException">Thrown when no current GL context is available.</exception>
    public static void Initialize(bool force = false)
    {
        if (!GlExtensions.TryGetContextKey(out string contextKey))
        {
            throw new InvalidOperationException("No current OpenGL context available to query GPU support.");
        }

        lock (Sync)
        {
            if (!force && isInitialized && string.Equals(cachedContextKey, contextKey, StringComparison.Ordinal))
            {
                return;
            }

            // Ensure the shared extension cache is populated for this context.
            GlExtensions.TryLoadExtensions();

            CaptureContextStrings();
            CaptureExtensionFlags();
            CaptureContextFlagsAndProfile();
            CaptureLimits();

            Volatile.Write(ref cachedContextKey, contextKey);
            Volatile.Write(ref isInitialized, true);
        }
    }

    /// <summary>
    /// Best-effort initializer that never throws.
    /// </summary>
    public static bool TryInitialize(bool force = false)
    {
        try
        {
            Initialize(force);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ThrowIfNotInitialized()
    {
        if (!isInitialized)
        {
            throw new InvalidOperationException("GpuSupport.Initialize() must be called on a thread with a current GL context before reading cached values.");
        }
    }

    #endregion

    #region Context Strings

    public static string ContextKey
    {
        get
        {
            ThrowIfNotInitialized();
            return cachedContextKey ?? string.Empty;
        }
    }

    public static string VersionString { get; private set; } = string.Empty;
    public static string VendorString { get; private set; } = string.Empty;
    public static string RendererString { get; private set; } = string.Empty;
    public static string ShadingLanguageVersionString { get; private set; } = string.Empty;

    public static Version? ApiVersion { get; private set; }
    public static Version? ShadingLanguageVersion { get; private set; }

    public static bool IsOpenGles { get; private set; }

    public static bool? IsSharedContext { get; private set; }

    private static void CaptureContextStrings()
    {
        GlDebug.ClearErrors();
        VersionString = SafeGetString(StringName.Version);
        VendorString = SafeGetString(StringName.Vendor);
        RendererString = SafeGetString(StringName.Renderer);
        ShadingLanguageVersionString = SafeGetString(StringName.ShadingLanguageVersion);

        IsOpenGles = VersionString.Contains("OpenGL ES", StringComparison.OrdinalIgnoreCase)
            || RendererString.Contains("OpenGL ES", StringComparison.OrdinalIgnoreCase);

        ApiVersion = TryParseLeadingVersion(VersionString);
        ShadingLanguageVersion = TryParseLeadingVersion(ShadingLanguageVersionString);

        IsSharedContext = TryGetSharedContextFlag();
        GlDebug.ThrowIfErrors();
    }

    private static string SafeGetString(StringName name)
    {
        try
        {
            return GL.GetString(name) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static Version? TryParseLeadingVersion(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Typical GL strings: "4.6.0 NVIDIA 552.25", "3.3.0", "OpenGL ES 3.2 ..."
        ReadOnlySpan<char> span = text.AsSpan().TrimStart();

        if (span.StartsWith("OpenGL ES".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            int idx = span.IndexOf(' ');
            if (idx >= 0)
            {
                span = span.Slice(idx).TrimStart();
                idx = span.IndexOf(' ');
                if (idx >= 0)
                {
                    span = span.Slice(idx).TrimStart();
                }
            }
        }

        int end = 0;
        while (end < span.Length)
        {
            char c = span[end];
            if ((uint)(c - '0') <= 9u || c == '.')
            {
                end++;
                continue;
            }

            break;
        }

        if (end == 0)
        {
            return null;
        }

        string versionText = span.Slice(0, end).ToString();
        string[] parts = versionText.Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
        {
            return null;
        }

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int major))
        {
            return null;
        }

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int minor))
        {
            return null;
        }

        int build = 0;
        if (parts.Length >= 3)
        {
            _ = int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out build);
        }

        return new Version(major, minor, build);
    }

    private static bool? TryGetSharedContextFlag()
    {
        // OpenGL has no standard "shared context" query.
        // Vintage Story ships OpenTK 4 split assemblies (OpenTK.Graphics + OpenTK.Windowing.*).
        // The public OpenTK context interfaces available to mods do not expose a shared-context flag.
        // Keep this as "unknown" rather than guessing.
        return null;
    }

    #endregion

    #region Context Flags / Profile

    public static int ContextFlags { get; private set; }
    public static int ContextProfileMaskValue { get; private set; }

    public static bool IsDebugContext { get; private set; }
    public static bool IsForwardCompatibleContext { get; private set; }
    public static bool IsRobustAccessContext { get; private set; }

    public static bool IsCoreProfile { get; private set; }
    public static bool IsCompatibilityProfile { get; private set; }

    private static void CaptureContextFlagsAndProfile()
    {
        GlDebug.ClearErrors();
        // Many context queries were introduced in GL 3.x; avoid emitting GL errors on older contexts.
        if (!IsAtLeast(ApiVersion, 3, 0))
        {
            ContextFlags = 0;
            ContextProfileMaskValue = 0;
            IsDebugContext = false;
            IsForwardCompatibleContext = false;
            IsRobustAccessContext = false;
            IsCoreProfile = false;
            IsCompatibilityProfile = false;
            return;
        }

        ContextFlags = SafeGetInt(GetPName.ContextFlags);

        // GL_CONTEXT_PROFILE_MASK is GL 3.2+.
        ContextProfileMaskValue = IsAtLeast(ApiVersion, 3, 2)
            ? SafeGetInt(GetPName.ContextProfileMask)
            : 0;

        IsDebugContext = (((ContextFlagMask)ContextFlags) & ContextFlagMask.ContextFlagDebugBit) != 0;
        IsForwardCompatibleContext = (((ContextFlagMask)ContextFlags) & ContextFlagMask.ContextFlagForwardCompatibleBit) != 0;

        // Some drivers report robust access via ARB enum; keep this best-effort.
        IsRobustAccessContext = (((ContextFlagMask)ContextFlags) & (ContextFlagMask)0x00000004) != 0;

        if (ContextProfileMaskValue != 0)
        {
            IsCoreProfile = (((ContextProfileMask)ContextProfileMaskValue) & ContextProfileMask.ContextCoreProfileBit) != 0;
            IsCompatibilityProfile = (((ContextProfileMask)ContextProfileMaskValue) & ContextProfileMask.ContextCompatibilityProfileBit) != 0;
        }
        else
        {
            IsCoreProfile = false;
            IsCompatibilityProfile = false;
        }
        GlDebug.ThrowIfErrors();
    }

    #endregion

    #region Limits

    public static int MaxTextureSize { get; private set; }
    public static int Max3DTextureSize { get; private set; }
    public static int MaxCubeMapTextureSize { get; private set; }
    public static int MaxArrayTextureLayers { get; private set; }

    public static int MaxTextureImageUnits { get; private set; }
    public static int MaxCombinedTextureImageUnits { get; private set; }
    public static int MaxVertexTextureImageUnits { get; private set; }

    public static int MaxUniformBufferBindings { get; private set; }
    public static int MaxUniformBlockSize { get; private set; }

    public static int MaxShaderStorageBufferBindings { get; private set; }
    public static long MaxShaderStorageBlockSize { get; private set; }

    public static int MaxAtomicCounterBufferBindings { get; private set; }

    public static int MaxImageUnits { get; private set; }
    public static int MaxCombinedImageUnits { get; private set; }

    public static int MaxColorAttachments { get; private set; }
    public static int MaxDrawBuffers { get; private set; }
    public static int MaxSamples { get; private set; }

    public static int MaxVertexAttribs { get; private set; }

    public static int MaxUniformLocations { get; private set; }

    public static ImmutableArray<int> MaxComputeWorkGroupCount { get; private set; } = ImmutableArray<int>.Empty;
    public static ImmutableArray<int> MaxComputeWorkGroupSize { get; private set; } = ImmutableArray<int>.Empty;
    public static int MaxComputeWorkGroupInvocations { get; private set; }
    public static int MaxComputeSharedMemorySize { get; private set; }

    private static void CaptureLimits()
    {
        GlDebug.ClearErrors();
        MaxTextureSize = SafeGetInt(GetPName.MaxTextureSize);
        Max3DTextureSize = SafeGetInt(GetPName.Max3DTextureSize);
        MaxCubeMapTextureSize = SafeGetInt(GetPName.MaxCubeMapTextureSize);
        MaxArrayTextureLayers = IsAtLeast(ApiVersion, 3, 0) ? SafeGetInt(GetPName.MaxArrayTextureLayers) : 0;

        MaxTextureImageUnits = SafeGetInt(GetPName.MaxTextureImageUnits);
        MaxCombinedTextureImageUnits = SafeGetInt(GetPName.MaxCombinedTextureImageUnits);
        MaxVertexTextureImageUnits = SafeGetInt(GetPName.MaxVertexTextureImageUnits);

        if (IsAtLeast(ApiVersion, 3, 1))
        {
            MaxUniformBufferBindings = SafeGetInt(GetPName.MaxUniformBufferBindings);
            MaxUniformBlockSize = SafeGetInt(GetPName.MaxUniformBlockSize);
        }
        else
        {
            MaxUniformBufferBindings = 0;
            MaxUniformBlockSize = 0;
        }

        if (SupportsArbShaderStorageBufferObject)
        {
            MaxShaderStorageBufferBindings = SafeGetInt((GetPName)All.MaxShaderStorageBufferBindings);
            MaxShaderStorageBlockSize = SafeGetLong((GetPName)All.MaxShaderStorageBlockSize);
        }
        else
        {
            MaxShaderStorageBufferBindings = 0;
            MaxShaderStorageBlockSize = 0;
        }

        if (SupportsArbShaderAtomicCounters)
        {
            MaxAtomicCounterBufferBindings = SafeGetInt((GetPName)All.MaxAtomicCounterBufferBindings);
        }
        else
        {
            MaxAtomicCounterBufferBindings = 0;
        }

        if (SupportsArbShaderImageLoadStore)
        {
            // Some OpenTK builds used by VS don't expose these enums; keep numeric fallbacks.
            MaxImageUnits = SafeGetInt(GetPName.MaxTextureImageUnits /* (GetPName)0x8F38 GL_MAX_IMAGE_UNITS */);
            MaxCombinedImageUnits = SafeGetInt(GetPName.MaxCombinedTextureImageUnits /* (GetPName)0x8F39 GL_MAX_COMBINED_IMAGE_UNITS */);
        }
        else
        {
            MaxImageUnits = 0;
            MaxCombinedImageUnits = 0;
        }

        MaxColorAttachments = IsAtLeast(ApiVersion, 3, 0) ? SafeGetInt(GetPName.MaxColorAttachments) : 0;
        MaxDrawBuffers = IsAtLeast(ApiVersion, 2, 0) ? SafeGetInt(GetPName.MaxDrawBuffers) : 0;
        MaxSamples = IsAtLeast(ApiVersion, 3, 0) ? SafeGetInt(GetPName.MaxSamples) : 0;

        MaxVertexAttribs = SafeGetInt(GetPName.MaxVertexAttribs);
        MaxUniformLocations = SupportsArbExplicitUniformLocation
            ? SafeGetInt((GetPName)All.MaxUniformLocations)
            : 0;

        if (SupportsArbComputeShader)
        {
            MaxComputeWorkGroupCount = SafeGetInt3(GetPName.MaxComputeWorkGroupCount);
            MaxComputeWorkGroupSize = SafeGetInt3(GetPName.MaxComputeWorkGroupSize);
            MaxComputeWorkGroupInvocations = SafeGetInt(GetPName.MaxComputeWorkGroupInvocations);
            // MaxComputeSharedMemorySize = SafeGetInt((GetPName)0x8262 /* GL_MAX_COMPUTE_SHARED_MEMORY_SIZE */);
        }
        else
        {
            MaxComputeWorkGroupCount = ImmutableArray<int>.Empty;
            MaxComputeWorkGroupSize = ImmutableArray<int>.Empty;
            MaxComputeWorkGroupInvocations = 0;
            MaxComputeSharedMemorySize = 0;
        }
        GlDebug.ThrowIfErrors();
    }

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

    private static ImmutableArray<int> SafeGetInt3(GetPName pname)
    {
        try
        {
            int[] values = new int[3];
            GL.GetInteger(pname, values);
            return ImmutableArray.Create(values[0], values[1], values[2]);
        }
        catch
        {
            return ImmutableArray<int>.Empty;
        }
    }

    #endregion

    #region Extension Flags

    public static bool SupportsKhrDebug { get; private set; }
    public static bool SupportsArbDirectStateAccess { get; private set; }
    public static bool SupportsArbMultiBind { get; private set; }
    public static bool SupportsArbBindlessTexture { get; private set; }

    public static bool SupportsArbComputeShader { get; private set; }
    public static bool SupportsArbShaderStorageBufferObject { get; private set; }
    public static bool SupportsArbShaderImageLoadStore { get; private set; }
    public static bool SupportsArbShaderAtomicCounters { get; private set; }
    public static bool SupportsArbExplicitUniformLocation { get; private set; }
    public static bool SupportsArbBufferStorage { get; private set; }

    public static bool SupportsArbShadingLanguage420Pack { get; private set; }
    public static bool SupportsArbProgramInterfaceQuery { get; private set; }

    public static bool SupportsArbGlSpirv { get; private set; }

    public static bool SupportsExtSemaphore { get; private set; }
    public static bool SupportsExtSemaphoreFd { get; private set; }
    public static bool SupportsExtMemoryObject { get; private set; }
    public static bool SupportsExtMemoryObjectFd { get; private set; }

    /// <summary>
    /// Delegates to <see cref="GlExtensions.Supports"/> for ad-hoc checks.
    /// </summary>
    public static bool Supports(string extension)
    {
        ThrowIfNotInitialized();
        return GlExtensions.Supports(extension);
    }

    private static void CaptureExtensionFlags()
    {
        GlDebug.ClearErrors();
        SupportsKhrDebug = GlExtensions.Supports("GL_KHR_debug");
        SupportsArbDirectStateAccess = GlExtensions.Supports("GL_ARB_direct_state_access");
        SupportsArbMultiBind = GlExtensions.Supports("GL_ARB_multi_bind");
        SupportsArbBindlessTexture = GlExtensions.Supports("GL_ARB_bindless_texture");

        SupportsArbComputeShader = GlExtensions.Supports("GL_ARB_compute_shader") || IsAtLeast(ApiVersion, 4, 3);
        SupportsArbShaderStorageBufferObject = GlExtensions.Supports("GL_ARB_shader_storage_buffer_object") || IsAtLeast(ApiVersion, 4, 3);
        SupportsArbShaderImageLoadStore = GlExtensions.Supports("GL_ARB_shader_image_load_store") || IsAtLeast(ApiVersion, 4, 2);
        SupportsArbShaderAtomicCounters = GlExtensions.Supports("GL_ARB_shader_atomic_counters") || IsAtLeast(ApiVersion, 4, 2);
        SupportsArbExplicitUniformLocation = GlExtensions.Supports("GL_ARB_explicit_uniform_location") || IsAtLeast(ApiVersion, 4, 3);
        SupportsArbBufferStorage = GlExtensions.Supports("GL_ARB_buffer_storage") || IsAtLeast(ApiVersion, 4, 4);

        // Enables layout(binding=...) in older GLSL (e.g., #version 330) when supported by the driver.
        SupportsArbShadingLanguage420Pack = GlExtensions.Supports("GL_ARB_shading_language_420pack") || IsAtLeast(ApiVersion, 4, 2);

        // Required for glGetProgramResource* and friends.
        SupportsArbProgramInterfaceQuery = GlExtensions.Supports("GL_ARB_program_interface_query") || IsAtLeast(ApiVersion, 4, 3);

        SupportsArbGlSpirv = GlExtensions.Supports("GL_ARB_gl_spirv") || IsAtLeast(ApiVersion, 4, 6);

        SupportsExtSemaphore = GlExtensions.Supports("GL_EXT_semaphore");
        SupportsExtSemaphoreFd = GlExtensions.Supports("GL_EXT_semaphore_fd");
        SupportsExtMemoryObject = GlExtensions.Supports("GL_EXT_memory_object");
        SupportsExtMemoryObjectFd = GlExtensions.Supports("GL_EXT_memory_object_fd");
        GlDebug.ThrowIfErrors();
    }

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

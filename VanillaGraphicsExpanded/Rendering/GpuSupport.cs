using System;
using System.Collections.Immutable;
using System.Threading;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Global cache of GPU/context capability information.
/// Call <see cref="Initialize"/> (or <see cref="TryInitialize"/>) once on a thread with a current GL context,
/// then read cached properties without incurring additional GL queries.
/// </summary>
public static partial class GpuSupport
{
    private static readonly object Sync = new();

    private static bool isInitialized;
    private static string? cachedContextKey;
    private static (nint Handle, long Generation) cachedRegistration;

    #region Public API
    /// <summary>Counts complete capability captures independently of mutable state reads.</summary>
    internal static long CaptureCount { get; private set; }

    /// <summary>Reuses registered context capabilities without polling native strings on the draw path.</summary>
    internal static void EnsureCurrentContext()
    {
        var current = Integration.RenderContextRegistry.Current();
        if (current.Generation == 0) throw new InvalidOperationException("A registered current context is required.");
        if (!isInitialized || cachedRegistration != current)
        {
            // Do not erase an outstanding native failure while refreshing boundary capabilities.
            if (GL.GetError() != ErrorCode.NoError)
                throw new InvalidOperationException("Native error before capability capture.");
            Initialize();
        }
    }

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
            return cachedRegistration == Integration.RenderContextRegistry.Current()
                && string.Equals(snapshot, contextKey, StringComparison.Ordinal);
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

        var registration = Integration.RenderContextRegistry.Current();
        lock (Sync)
        {
            if (!force && isInitialized && cachedRegistration == registration && string.Equals(cachedContextKey, contextKey, StringComparison.Ordinal))
            {
                return;
            }

            // A failed refresh must not leave a partially replaced snapshot marked usable.
            Volatile.Write(ref isInitialized, false);
            // Ensure the shared extension cache is populated for this context.
            GlExtensions.TryLoadExtensions();

            // Build privately so failed queries cannot expose partially initialized capabilities.
            var capabilities = CaptureContextStrings(new GraphicsCapabilities());
            capabilities = CaptureExtensionFlags(capabilities);
            capabilities = CaptureContextFlagsAndProfile(capabilities);
            capabilities = CaptureLimits(capabilities);
            capabilities = CaptureGraphicsLimits(capabilities);
            GlDebug.ThrowIfErrors("Capability initialization");
            Volatile.Write(ref graphics, capabilities);
            formatCapabilities.Clear();

            cachedRegistration = registration;
            CaptureCount++;
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

    public static string VersionString => graphics.VersionString;
    public static string VendorString => graphics.VendorString;
    public static string RendererString => graphics.RendererString;
    public static string ShadingLanguageVersionString => graphics.ShadingLanguageVersionString;

    public static Version? ApiVersion => graphics.ApiVersion;
    public static Version? ShadingLanguageVersion => graphics.ShadingLanguageVersion;

    public static bool IsOpenGles => graphics.IsOpenGles;

    public static bool? IsSharedContext => graphics.IsSharedContext;

    #endregion

    #region Context Flags / Profile

    public static int ContextFlags => graphics.ContextFlags;
    public static int ContextProfileMaskValue => graphics.ContextProfileMaskValue;

    public static bool IsDebugContext => graphics.IsDebugContext;
    public static bool IsForwardCompatibleContext => graphics.IsForwardCompatibleContext;
    public static bool IsRobustAccessContext => graphics.IsRobustAccessContext;

    public static bool IsCoreProfile => graphics.CoreProfile;
    public static bool IsCompatibilityProfile => graphics.IsCompatibilityProfile;

    #endregion

    #region Limits

    public static int MaxTextureSize => graphics.MaxTextureSize;
    public static int Max3DTextureSize => graphics.Max3DTextureSize;
    public static int MaxCubeMapTextureSize => graphics.MaxCubeMapTextureSize;
    public static int MaxArrayTextureLayers => graphics.MaxArrayTextureLayers;

    public static int MaxTextureImageUnits => graphics.MaxTextureImageUnits;
    public static int MaxCombinedTextureImageUnits => graphics.MaxCombinedTextureImageUnits;
    public static int MaxVertexTextureImageUnits => graphics.MaxVertexTextureImageUnits;

    public static int MaxUniformBufferBindings => graphics.MaxUniformBufferBindings;
    public static int MaxUniformBlockSize => graphics.MaxUniformBlockSize;

    public static int MaxShaderStorageBufferBindings => graphics.MaxShaderStorageBufferBindings;
    public static long MaxShaderStorageBlockSize => graphics.MaxShaderStorageBlockSize;

    public static int MaxAtomicCounterBufferBindings => graphics.MaxAtomicCounterBufferBindings;

    public static int MaxImageUnits => graphics.MaxImageUnits;
    /// <summary>Maximum combined image uniforms across shader stages (GL_MAX_COMBINED_IMAGE_UNIFORMS).</summary>
    public static int MaxCombinedImageUnits => graphics.MaxCombinedImageUnits;

    public static int MaxColorAttachments => graphics.MaxColorAttachments;
    public static int MaxDrawBuffers => graphics.MaxDrawBuffers;
    /// <summary>Maximum effective viewport width for the current context.</summary>
    public static int MaxViewportWidth => graphics.MaxViewportWidth;
    /// <summary>Maximum effective viewport height for the current context.</summary>
    public static int MaxViewportHeight => graphics.MaxViewportHeight;
    /// <summary>Required shader-storage range alignment, or zero when unsupported.</summary>
    public static int ShaderStorageBufferOffsetAlignment => graphics.ShaderStorageBufferOffsetAlignment;
    public static int MaxSamples => graphics.MaxSamples;

    public static int MaxVertexAttribs => graphics.MaxVertexAttributes;

    /// <summary>Maximum input vertices per tessellation patch, or zero when unsupported.</summary>
    public static int MaxPatchVertices => graphics.MaxPatchVertices;
    /// <summary>Maximum tessellation subdivision level, or zero when unsupported.</summary>
    public static int MaxTessGenLevel => graphics.MaxTessGenLevel;
    public static int MaxTessControlTextureImageUnits => graphics.MaxTessControlTextureImageUnits;
    public static int MaxTessEvaluationTextureImageUnits => graphics.MaxTessEvaluationTextureImageUnits;

    public static int MaxUniformLocations => graphics.MaxUniformLocations;

    public static ImmutableArray<int> MaxComputeWorkGroupCount => graphics.MaxComputeWorkGroupCount;
    public static ImmutableArray<int> MaxComputeWorkGroupSize => graphics.MaxComputeWorkGroupSize;
    public static int MaxComputeWorkGroupInvocations => graphics.MaxComputeWorkGroupInvocations;
    public static int MaxComputeSharedMemorySize => graphics.MaxComputeSharedMemorySize;

    #endregion

    #region Extension Flags

    public static bool SupportsKhrDebug => graphics.SupportsKhrDebug;
    public static bool SupportsArbDirectStateAccess => graphics.SupportsArbDirectStateAccess;
    public static bool SupportsArbMultiBind => graphics.SupportsArbMultiBind;
    public static bool SupportsArbBindlessTexture => graphics.SupportsArbBindlessTexture;

    public static bool SupportsArbComputeShader => graphics.SupportsArbComputeShader;
    public static bool SupportsArbShaderStorageBufferObject => graphics.SupportsArbShaderStorageBufferObject;
    public static bool SupportsArbShaderImageLoadStore => graphics.SupportsArbShaderImageLoadStore;
    public static bool SupportsArbShaderAtomicCounters => graphics.SupportsArbShaderAtomicCounters;
    public static bool SupportsArbExplicitUniformLocation => graphics.SupportsArbExplicitUniformLocation;
    public static bool SupportsArbBufferStorage => graphics.SupportsArbBufferStorage;

    public static bool SupportsArbShadingLanguage420Pack => graphics.SupportsArbShadingLanguage420Pack;
    public static bool SupportsArbProgramInterfaceQuery => graphics.SupportsArbProgramInterfaceQuery;

    public static bool SupportsArbGlSpirv => graphics.SupportsArbGlSpirv;

    public static bool SupportsExtSemaphore => graphics.SupportsExtSemaphore;
    public static bool SupportsExtSemaphoreFd => graphics.SupportsExtSemaphoreFd;
    public static bool SupportsExtMemoryObject => graphics.SupportsExtMemoryObject;
    public static bool SupportsExtMemoryObjectFd => graphics.SupportsExtMemoryObjectFd;

    /// <summary>
    /// Delegates to <see cref="GlExtensions.Supports"/> for ad-hoc checks.
    /// </summary>
    public static bool Supports(string extension)
    {
        ThrowIfNotInitialized();
        return GlExtensions.Supports(extension);
    }

    #endregion
    #endregion

    #region Private
    /// <summary>Rejects capability access before successful initialization.</summary>
    private static void ThrowIfNotInitialized()
    {
        if (!isInitialized)
        {
            throw new InvalidOperationException("GpuSupport.Initialize() must be called on a thread with a current GL context before reading cached values.");
        }
    }
    #endregion
}

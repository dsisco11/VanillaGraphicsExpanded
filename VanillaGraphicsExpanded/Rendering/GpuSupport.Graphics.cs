using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns shared graphics feature flags and limits alongside the existing resource capabilities.</summary>
public static partial class GpuSupport
{
    private static GraphicsCapabilities graphics = new();

    #region Public API
    #region Features and limits
    public static bool SupportsIndependentBlend => graphics.IndependentBlend;
    public static bool SupportsSampleShading => graphics.SampleShading;
    public static bool SupportsFixedIndexRestart => graphics.FixedIndexRestart;
    public static bool SupportsDoubleAttributes => graphics.DoubleAttributes;
    public static bool SupportsDepthClamp => graphics.DepthClamp;
    public static int MaxVertexBindings => graphics.MaxVertexBindings;
    public static int MaxVertexRelativeOffset => graphics.MaxVertexRelativeOffset;
    public static int MaxVertexStride => graphics.MaxVertexStride;
    public static int MaxSampleMaskWords => graphics.MaxSampleMaskWords;
    public static float MinLineWidth => graphics.MinLineWidth;
    public static float MaxLineWidth => graphics.MaxLineWidth;
    public static float MinPointSize => graphics.MinPointSize;
    public static float MaxPointSize => graphics.MaxPointSize;
    #endregion

    /// <summary>Returns the shared immutable capabilities after ensuring initialization.</summary>
    public static GraphicsCapabilities Graphics
    {
        get
        {
            EnsureCurrentContext();
            return graphics;
        }
    }
    #endregion

    #region Private
    /// <summary>Extends the shared capability capture, querying optional limits only where supported.</summary>
    private static GraphicsCapabilities CaptureGraphicsLimits(GraphicsCapabilities capabilities)
    {
        bool bindings = IsAtLeast(capabilities.ApiVersion, 4, 3) || GlExtensions.Supports("GL_ARB_vertex_attrib_binding");
        int maxVertexAttributes = SafeGetInt(GetPName.MaxVertexAttribs);
        bool tessellation = IsAtLeast(capabilities.ApiVersion, 4, 0) || GlExtensions.Supports("GL_ARB_tessellation_shader");
        float[] line = new float[2], point = new float[2];
        GL.GetFloat(GetPName.AliasedLineWidthRange, line);
        GL.GetFloat(GetPName.AliasedPointSizeRange, point);
        // Extend the unpublished value; initialization publishes only after all queries succeed.
        return capabilities with
        {
            MaxClipDistances = IsAtLeast(capabilities.ApiVersion, 3, 0) ? SafeGetInt(GetPName.MaxClipDistances) : 0,
            ClipControl = IsAtLeast(capabilities.ApiVersion, 4, 5) || GlExtensions.Supports("GL_ARB_clip_control"),
            Graphics33 = IsAtLeast(capabilities.ApiVersion, 3, 3),
            CoreProfile = ((ContextProfileMask)capabilities.ContextProfileMaskValue).HasFlag(ContextProfileMask.ContextCoreProfileBit),
            Tessellation = tessellation,
            MaxDrawBuffers = IsAtLeast(capabilities.ApiVersion, 2, 0) ? SafeGetInt(GetPName.MaxDrawBuffers) : 0,
            MaxSamples = IsAtLeast(capabilities.ApiVersion, 3, 0) ? SafeGetInt(GetPName.MaxSamples) : 0,
            MaxVertexAttributes = maxVertexAttributes,
            MaxPatchVertices = tessellation ? SafeGetInt(GetPName.MaxPatchVertices) : 0,
            IndependentBlend = IsAtLeast(capabilities.ApiVersion, 4, 0) || GlExtensions.Supports("GL_ARB_draw_buffers_blend"),
            SampleShading = IsAtLeast(capabilities.ApiVersion, 4, 0) || GlExtensions.Supports("GL_ARB_sample_shading"),
            FixedIndexRestart = IsAtLeast(capabilities.ApiVersion, 4, 3) || GlExtensions.Supports("GL_ARB_ES3_compatibility"),
            DoubleAttributes = IsAtLeast(capabilities.ApiVersion, 4, 1) || GlExtensions.Supports("GL_ARB_vertex_attrib_64bit"),
            DepthClamp = IsAtLeast(capabilities.ApiVersion, 3, 2) || GlExtensions.Supports("GL_ARB_depth_clamp"),
            MaxVertexBindings = bindings ? SafeGetInt(GetPName.MaxVertexAttribBindings) : maxVertexAttributes,
            MaxVertexRelativeOffset = bindings ? SafeGetInt(GetPName.MaxVertexAttribRelativeOffset) : int.MaxValue,
            MaxVertexStride = IsAtLeast(capabilities.ApiVersion, 4, 4) ? SafeGetInt((GetPName)All.MaxVertexAttribStride) : int.MaxValue,
            MaxSampleMaskWords = IsAtLeast(capabilities.ApiVersion, 3, 2) ? SafeGetInt(GetPName.MaxSampleMaskWords) : 0,

            MinLineWidth = line[0], MaxLineWidth = line[1], MinPointSize = point[0], MaxPointSize = point[1]
        };
    }
    #endregion
}

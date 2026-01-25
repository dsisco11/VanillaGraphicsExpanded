using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for LumOn Probe Anchor pass.
/// Determines probe positions from G-buffer depth/normals.
/// 
/// Output is in WORLD-SPACE (matching UE5 Lumen's design) for temporal stability:
/// - World-space directions remain valid across camera rotations
/// - Radiance stored per world-space direction can be directly blended
/// - No SH rotation or coordinate transforms needed in temporal pass
///
/// Implements validation criteria from LumOn.02-Probe-Grid.md:
/// - Sky rejection (depth >= 0.9999)
/// - Edge detection via depth discontinuity (reduces temporal weight)
/// - Invalid normal rejection
/// </summary>
public class LumOnProbeAnchorShaderProgram : GpuProgram
{
    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnProbeAnchorShaderProgram
        {
            PassName = "lumon_probe_anchor",
            AssetDomain = "vanillagraphicsexpanded"
        };
        instance.Initialize(api);
        instance.CompileAndLink();
        api.Shader.RegisterMemoryShaderProgram("lumon_probe_anchor", instance);
    }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// Primary depth texture for position reconstruction.
    /// </summary>
    public int PrimaryDepth { set => BindTexture2D("primaryDepth", value, 0); }

    /// <summary>
    /// G-buffer world-space normals.
    /// </summary>
    public int GBufferNormal { set => BindTexture2D("gBufferNormal", value, 1); }

    /// <summary>
    /// PMJ jitter sequence texture (RG16_UNorm, width=cycleLength, height=1).
    /// </summary>
    public int PmjJitter { set => BindTexture2D("pmjJitter", value, 2); }

    #endregion

    #region Matrix Uniforms

    /// <summary>
    /// Inverse projection matrix for view-space position reconstruction.
    /// </summary>
    public float[] InvProjectionMatrix { set => UniformMatrix("invProjectionMatrix", value); }

    /// <summary>
    /// Inverse view matrix for transforming view-space positions to world-space.
    /// Required for world-space probe output (temporal stability across camera rotations).
    /// </summary>
    public float[] InvViewMatrix { set => UniformMatrix("invViewMatrix", value); }

    #endregion

    #region Probe Grid Uniforms

    /// <summary>
    /// Spacing between probes in pixels.
    /// </summary>
    public int ProbeSpacing { set => Uniform("probeSpacing", value); }

    /// <summary>
    /// Probe grid dimensions (probeCountX, probeCountY).
    /// </summary>
    public Vec2i ProbeGridSize { set => Uniform("probeGridSize", new Vec2f(value.X, value.Y)); }

    /// <summary>
    /// Screen dimensions in pixels.
    /// </summary>
    public Vec2f ScreenSize { set => Uniform("screenSize", value); }

    /// <summary>
    /// Current frame index for deterministic jittering.
    /// </summary>
    public int FrameIndex { set => Uniform("frameIndex", value); }

    /// <summary>
    /// Toggle deterministic probe anchor jitter.
    /// </summary>
    public int AnchorJitterEnabled { set => Uniform("anchorJitterEnabled", value); }

    /// <summary>
    /// Jitter scale as a fraction of probe spacing.
    /// </summary>
    public float AnchorJitterScale { set => Uniform("anchorJitterScale", value); }

    /// <summary>
    /// PMJ jitter cycle length (texture width).
    /// </summary>
    public int PmjCycleLength { set => Uniform("pmjCycleLength", value); }

    #endregion

    #region Z-Plane Uniforms

    /// <summary>
    /// Near clipping plane distance.
    /// </summary>
    public float ZNear { set => Uniform("zNear", value); }

    /// <summary>
    /// Far clipping plane distance.
    /// </summary>
    public float ZFar { set => Uniform("zFar", value); }

    #endregion

    #region Edge Detection Uniforms

    /// <summary>
    /// Threshold for depth discontinuity detection.
    /// Probes at edges (depth discontinuities) are marked with partial validity (0.5)
    /// for reduced temporal accumulation weight.
    /// Recommended value: 0.1
    /// </summary>
    public float DepthDiscontinuityThreshold { set => Uniform("depthDiscontinuityThreshold", value); }

    #endregion
}
